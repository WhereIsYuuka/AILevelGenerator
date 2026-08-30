using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Scheduling;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 调度器取消链路单元测试（Day3）：
    /// CancelGeneration 在构建阶段转发构建器 Cancel（增量清理本次物体）；
    /// 生成（LLM）阶段置取消标记，结果返回后丢弃、不进入构建；无任务时安全空操作。
    /// </summary>
    public class GeneratorSchedulerCancelTests
    {
        private class FakeGenerator : IGenerator
        {
            public Func<GenerationRequest, Task<GenerationResult>> Handler;
            public Task<GenerationResult> GenerateAsync(GenerationRequest request) => Handler(request);
        }

        /// <summary> 假构建器：记录 Cancel 调用，按配置返回构建结果 </summary>
        private class FakeBuilder : ILevelBuilder
        {
            public Func<LevelData, LevelBuildOptions, Task<LevelBuildResult>> Handler;
            public bool BuildCalled;
            public bool CancelCalled;

            public bool IsBuilding => false;

            public event Action<float> ProgressChanged { add { } remove { } }

            public Task<LevelBuildResult> BuildAsync(LevelData levelData, LevelBuildOptions options = null)
            {
                BuildCalled = true;
                return Handler(levelData, options);
            }

            public void Cancel() => CancelCalled = true;
        }

        private static GenerationRequest CreateRequest() => new() { Prompt = "测试关卡" };

        private static GenerationResult CreateSuccessResult() => new()
        {
            Success = true,
            LevelData = new LevelData { LevelName = "测试关卡", Props = new List<PropPlacement>() },
            GenerationTime = 1f
        };

        [Test]
        public async Task 构建阶段取消_转发构建器Cancel_状态失败并输出清理日志()
        {
            // 构建挂起（TCS）：模拟分帧构建进行中
            var tcs = new TaskCompletionSource<LevelBuildResult>();
            var builder = new FakeBuilder { Handler = (_, _) => tcs.Task };
            var logger = new TestLogger();
            var scheduler = new GeneratorScheduler(new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            scheduler.SetBuilder(builder);
            scheduler.SetLogger(logger);

            var task = scheduler.StartGenerationAsync(CreateRequest());
            Assert.IsTrue(scheduler.IsBusy, "构建中应处于 Generating");

            scheduler.CancelGeneration(); // 构建阶段取消 → 转发构建器

            Assert.IsTrue(builder.CancelCalled, "CancelGeneration 应转发构建器 Cancel（增量清理本次物体）");
            tcs.SetResult(LevelBuildResult.Cancelled(5)); // 构建器取消后返回 Cancelled
            await task;

            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState, "取消后任务应判为失败（四态不变）");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[WARN]") && m.Contains("生成取消") && m.Contains("已清理")),
                "应输出增量清理提示日志");
        }

        [Test]
        public async Task 生成阶段取消_结果丢弃不进入构建()
        {
            var tcs = new TaskCompletionSource<GenerationResult>();
            var generator = new FakeGenerator { Handler = _ => tcs.Task };
            var builder = new FakeBuilder { Handler = (_, _) => Task.FromResult(LevelBuildResult.Succeeded(5, 0, 1f)) };
            var logger = new TestLogger();
            var scheduler = new GeneratorScheduler(generator);
            scheduler.SetBuilder(builder);
            scheduler.SetLogger(logger);

            var task = scheduler.StartGenerationAsync(CreateRequest());

            scheduler.CancelGeneration(); // LLM 阶段取消：结果返回后被丢弃
            tcs.SetResult(CreateSuccessResult());
            await task;

            Assert.IsFalse(builder.BuildCalled, "生成阶段取消后不得进入构建（场景无变更）");
            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState);
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[WARN]") && m.Contains("已取消") && m.Contains("已丢弃")),
                "应输出结果丢弃日志");
        }

        [Test]
        public void 无进行中任务_取消请求为安全空操作()
        {
            var logger = new TestLogger();
            var scheduler = new GeneratorScheduler(new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            scheduler.SetLogger(logger);

            scheduler.CancelGeneration(); // 不应抛异常

            Assert.AreEqual(GenerationTaskState.Ready, scheduler.CurrentState);
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[WARN]") && m.Contains("无进行中")),
                "应提示无进行中的生成任务");
        }

        [Test]
        public async Task 取消后新一轮生成_取消标记被重置()
        {
            // 第一轮：生成阶段取消
            var tcs = new TaskCompletionSource<GenerationResult>();
            var generator = new FakeGenerator { Handler = _ => tcs.Task };
            var builder = new FakeBuilder { Handler = (_, _) => Task.FromResult(LevelBuildResult.Succeeded(1, 0, 1f)) };
            var logger = new TestLogger();
            var scheduler = new GeneratorScheduler(generator);
            scheduler.SetBuilder(builder);
            scheduler.SetLogger(logger);

            var task = scheduler.StartGenerationAsync(CreateRequest());
            scheduler.CancelGeneration();
            tcs.SetResult(CreateSuccessResult());
            await task;
            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState);

            // 第二轮：正常生成，取消标记应已被重置（不误判为取消）
            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.IsTrue(builder.BuildCalled, "新一轮任务应正常进入构建（取消标记已重置）");
            Assert.AreEqual(GenerationTaskState.Success, scheduler.CurrentState);
        }
    }
}
