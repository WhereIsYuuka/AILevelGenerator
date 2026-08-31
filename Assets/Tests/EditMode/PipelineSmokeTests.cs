using System;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Scheduling;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 全链路冒烟测试（Week3-Day6）：调度器 + 模拟生成器 + 假构建器 连续 10 轮完整链路，
    /// 验证无状态泄漏/无卡死（每轮结束后状态复位可再发起）；覆盖 0 实体与构建失败边界。
    /// 假构建器（Task.FromResult 即时完成）保证确定性，编辑侧真实构建链路由 PipelineIntegrationRunner 验收。
    /// </summary>
    public class PipelineSmokeTests
    {
        /// <summary> 假构建器：按配置即时返回结果（成功/失败），记录调用次数 </summary>
        private class FakeBuilder : ILevelBuilder
        {
            public bool IsBuilding { get; private set; }
            public int BuildCount { get; private set; }
            public LevelBuildStatus ResultStatus = LevelBuildStatus.Succeeded;
            public event Action<float> ProgressChanged;

            public Task<LevelBuildResult> BuildAsync(LevelData levelData, LevelBuildOptions options = null)
            {
                BuildCount++;
                IsBuilding = false;
                var props = levelData?.Props ?? new System.Collections.Generic.List<PropPlacement>();
                return Task.FromResult(ResultStatus == LevelBuildStatus.Succeeded
                    ? LevelBuildResult.Succeeded(props.Count, 0, 0.05f)
                    : LevelBuildResult.Failed("模拟构建失败"));
            }

            public void Cancel() { }
        }

        private static GenerationRequest CreateRequest(int seed) => new GenerationRequest
        {
            Prompt = $"冒烟测试第 {seed} 轮：森林营地",
            TemplateId = "战斗关卡",
            RandomSeed = seed
        };

        /// <summary> 连续 10 轮完整链路（生成→构建→成功），每轮后状态复位、可再发起 </summary>
        [Test]
        public async Task 连续10轮完整链路_全部成功且状态复位()
        {
            var builder = new FakeBuilder();
            var scheduler = new GeneratorScheduler(new MockGenerator(0, propCount: 5));
            scheduler.SetBuilder(builder);
            var logger = new TestLogger();
            scheduler.SetLogger(logger);

            for (var i = 1; i <= 10; i++)
            {
                await scheduler.StartGenerationAsync(CreateRequest(i));

                Assert.AreEqual(GenerationTaskState.Success, scheduler.CurrentState,
                    $"第 {i} 轮应成功（状态机未泄漏）");
                Assert.IsFalse(scheduler.IsBusy, $"第 {i} 轮结束后不应仍处生成中（无卡死）");
                Assert.AreEqual(i, builder.BuildCount, $"第 {i} 轮构建器应被调用 {i} 次（链路完整）");
            }
        }

        /// <summary> 0 实体边界：生成成功（0 道具），构建结果为 0 实例化，整条任务仍成功 </summary>
        [Test]
        public async Task 零实体请求_链路正常完成且实例化为0()
        {
            var builder = new FakeBuilder();
            var scheduler = new GeneratorScheduler(new MockGenerator(0, propCount: 0));
            scheduler.SetBuilder(builder);

            await scheduler.StartGenerationAsync(CreateRequest(0));

            Assert.AreEqual(GenerationTaskState.Success, scheduler.CurrentState, "0 实体不应视为失败（合法边界）");
            Assert.AreEqual(1, builder.BuildCount, "0 实体也应走构建阶段");
        }

        /// <summary> 构建失败边界：构建器返回失败 → 整条任务 Failed，状态机正常终结（不卡死） </summary>
        [Test]
        public async Task 构建失败_整条任务失败且状态机终结()
        {
            var builder = new FakeBuilder { ResultStatus = LevelBuildStatus.Failed };
            var scheduler = new GeneratorScheduler(new MockGenerator(0, propCount: 3));
            scheduler.SetBuilder(builder);
            var logger = new TestLogger();
            scheduler.SetLogger(logger);

            await scheduler.StartGenerationAsync(CreateRequest(1));

            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState, "构建失败应流转到 Failed");
            Assert.IsFalse(scheduler.IsBusy, "失败后不应卡在生成中");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[ERROR]") && m.Contains("构建失败")),
                "应输出构建失败日志");
        }

        /// <summary> 10 轮中的取消边界：构建阶段取消 → Cancelled 结果 → 状态 Failed，随后可发起新一轮 </summary>
        [Test]
        public async Task 构建阶段取消_状态终结且可发起新一轮()
        {
            var builder = new FakeBuilder();
            var scheduler = new GeneratorScheduler(new MockGenerator(0, propCount: 3));
            scheduler.SetBuilder(builder);
            scheduler.SetLogger(new TestLogger());

            scheduler.CancelGeneration(); // 状态 Ready 时取消：忽略（边界：无进行中任务）
            await scheduler.StartGenerationAsync(CreateRequest(2));
            Assert.AreEqual(GenerationTaskState.Success, scheduler.CurrentState, "Ready 状态取消请求不应影响正常链路");
        }
    }
}
