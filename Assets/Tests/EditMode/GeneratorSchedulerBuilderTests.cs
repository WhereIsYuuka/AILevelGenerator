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
    /// 调度器 × 构建器接入单元测试（Day1）：
    /// 生成成功后自动分帧构建 → 成功/失败/取消/异常各分支；生成失败时不得调用构建器。
    /// 全部使用已完成 Task 的假构建器，await 同步内联完成，零等待、完全确定。
    /// </summary>
    public class GeneratorSchedulerBuilderTests
    {
        private class FakeGenerator : IGenerator
        {
            public Func<GenerationRequest, Task<GenerationResult>> Handler;
            public Task<GenerationResult> GenerateAsync(GenerationRequest request) => Handler(request);
        }

        /// <summary> 假构建器：按配置返回已完成 Task，并记录是否被调用 </summary>
        private class FakeBuilder : ILevelBuilder
        {
            public Func<LevelData, LevelBuildOptions, Task<LevelBuildResult>> Handler;
            public bool BuildCalled;

            public bool IsBuilding => false;

            public event Action<float> ProgressChanged { add { } remove { } }

            public Task<LevelBuildResult> BuildAsync(LevelData levelData, LevelBuildOptions options = null)
            {
                BuildCalled = true;
                return Handler(levelData, options);
            }

            public void Cancel() { }
        }

        private static GenerationRequest CreateRequest() => new() { Prompt = "测试关卡" };

        private static GenerationResult CreateSuccessResult() => new()
        {
            Success = true,
            LevelData = new LevelData { LevelName = "测试关卡", Props = new List<PropPlacement>() },
            GenerationTime = 1f
        };

        private static GeneratorScheduler CreateScheduler(FakeBuilder builder, FakeGenerator generator, TestLogger logger = null)
        {
            var scheduler = new GeneratorScheduler(generator);
            scheduler.SetBuilder(builder);
            scheduler.SetLogger(logger ?? new TestLogger());
            return scheduler;
        }

        [Test]
        public async Task 注入构建器_构建成功_状态流转成功并输出构建日志()
        {
            var builder = new FakeBuilder { Handler = (_, _) => Task.FromResult(LevelBuildResult.Succeeded(5, 0, 2f)) };
            var logger = new TestLogger();
            var scheduler = CreateScheduler(builder, new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) }, logger);
            var states = new List<GenerationTaskState>();
            scheduler.StateChanged += s => states.Add(s);

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.IsTrue(builder.BuildCalled, "生成成功后应调用构建器");
            Assert.AreEqual(GenerationTaskState.Success, scheduler.CurrentState);
            Assert.AreEqual(new[] { GenerationTaskState.Generating, GenerationTaskState.Success }, states,
                "状态序列应为 生成中（含构建）→ 成功");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[SUCCESS]") && m.Contains("构建 5 个实体")),
                "应输出包含构建数量的成功日志");
        }

        [Test]
        public async Task 构建失败_状态流转失败并输出失败日志()
        {
            var builder = new FakeBuilder { Handler = (_, _) => Task.FromResult(LevelBuildResult.Failed("找不到预制体")) };
            var logger = new TestLogger();
            var scheduler = CreateScheduler(builder, new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) }, logger);

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.IsTrue(builder.BuildCalled);
            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState);
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[ERROR]") && m.Contains("场景构建失败") && m.Contains("找不到预制体")),
                "应输出包含错误明细的失败日志");
        }

        [Test]
        public async Task 构建被取消_状态流转失败并输出取消日志()
        {
            var builder = new FakeBuilder { Handler = (_, _) => Task.FromResult(LevelBuildResult.Cancelled(2)) };
            var logger = new TestLogger();
            var scheduler = CreateScheduler(builder, new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) }, logger);

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState);
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[WARN]") && m.Contains("生成取消") && m.Contains("已清理")),
                "取消应输出清理提示（非错误）");
        }

        [Test]
        public async Task 构建器抛异常_状态流转失败并输出异常日志()
        {
            var builder = new FakeBuilder { Handler = (_, _) => Task.FromException<LevelBuildResult>(new Exception("build boom")) };
            var logger = new TestLogger();
            var scheduler = CreateScheduler(builder, new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) }, logger);

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState);
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[ERROR]") && m.Contains("构建异常") && m.Contains("build boom")),
                "应输出构建异常日志");
        }

        [Test]
        public async Task 构建结果null_判定为失败()
        {
            var builder = new FakeBuilder { Handler = (_, _) => Task.FromResult<LevelBuildResult>(null) };
            var logger = new TestLogger();
            var scheduler = CreateScheduler(builder, new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) }, logger);

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState);
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("场景构建失败") && m.Contains("未知错误")),
                "null 结果应提示未知错误");
        }

        [Test]
        public async Task 生成结果失败_不调用构建器()
        {
            var builder = new FakeBuilder { Handler = (_, _) => Task.FromResult(LevelBuildResult.Succeeded(1, 0, 1f)) };
            var failedResult = new GenerationResult
            {
                Success = false,
                Errors = new List<ValidationError> { new() { Code = "LLM_FAIL", Message = "LLM 生成失败" } }
            };
            var logger = new TestLogger();
            var scheduler = CreateScheduler(builder, new FakeGenerator { Handler = _ => Task.FromResult(failedResult) }, logger);

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.IsFalse(builder.BuildCalled, "生成失败时不应触发构建");
            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState);
        }

        [Test]
        public async Task 构建器挂起_处于生成中禁止重复请求()
        {
            // 挂起的 TCS：模拟分帧构建进行中
            var tcs = new TaskCompletionSource<LevelBuildResult>();
            var builder = new FakeBuilder { Handler = (_, _) => tcs.Task };
            var logger = new TestLogger();
            var scheduler = CreateScheduler(builder, new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) }, logger);

            var firstTask = scheduler.StartGenerationAsync(CreateRequest());
            Assert.IsTrue(scheduler.IsBusy, "构建进行中应视为忙碌（Generating）");

            await scheduler.StartGenerationAsync(CreateRequest()); // 忙碌中被拒，同步返回
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[WARN]") && m.Contains("已有生成任务进行中")), "应输出忙碌警告");

            tcs.SetResult(LevelBuildResult.Succeeded(5, 0, 2f));
            await firstTask; // 收尾：驱动首次请求完成
            Assert.AreEqual(GenerationTaskState.Success, scheduler.CurrentState);
        }
    }
}
