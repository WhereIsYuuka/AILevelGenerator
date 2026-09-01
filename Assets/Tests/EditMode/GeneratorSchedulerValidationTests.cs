using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Scheduling;
using AILevelGenerator.Runtime.Validation;
using NUnit.Framework;
using UnityEngine;
// 数据层 TerrainData 与 UnityEngine.TerrainData 同名，别名消除歧义（见 CLAUDE.md 已知坑）
using TerrainData = AILevelGenerator.Runtime.Data.TerrainData;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 调度器 × 前置校验 × 场景快照集成测试（第四周-Day2，Day4 固化快照生命周期）：
    /// 请求校验失败 → 拦截停留原状态 + 零快照副作用（前置校验在快照创建之前）；
    /// 数据校验失败 → 转 Failed + 丢弃快照 + 构建器未调用；
    /// 构建失败/异常（场景已污染）→ 自动全量回滚 + 复位 Ready；取消 → 仅丢弃快照不回滚；
    /// 成功 → 事务提交删除快照；生成异常 → Failed + 丢弃快照（场景零变更零残留）。
    /// 快照生命周期一致性：所有任务终结路径统一清理快照（成功丢弃 / 失败回滚消费 / 取消与校验失败丢弃），
    /// 避免陈旧快照误导回滚按钮。
    /// </summary>
    public class GeneratorSchedulerValidationTests
    {
        private class FakeGenerator : IGenerator
        {
            public Func<GenerationRequest, Task<GenerationResult>> Handler;
            public Task<GenerationResult> GenerateAsync(GenerationRequest request) => Handler(request);
        }

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

        /// <summary> 假快照管理器：记录 Create/Discard/Rollback 调用次数 + 可控结果（验证调度器的快照生命周期语义） </summary>
        private class FakeSnapshotManager : ISceneSnapshotManager
        {
            public bool HasSnapshotResult = true;
            public bool RollbackResult = true;
            public bool CreateResult = true; // Day4：可控快照创建结果（验证失败降级）
            public int CreateCount;
            public int DiscardCount;
            public int RollbackCount;

            public bool HasSnapshot => HasSnapshotResult;
            public string SnapshotPath => "Temp/TestSnapshot.unity";
            public string OriginalScenePath => "";

            public bool CreateSnapshot() { CreateCount++; return CreateResult; }
            public bool RollbackToSnapshot(bool rebakeNavMesh = true) { RollbackCount++; return RollbackResult; }
            public bool DiscardSnapshot() { DiscardCount++; return true; }
        }

        private class FakeResourceMapper : IResourceMapper
        {
            public HashSet<string> Available = new();
            public GameObject GetPrefab(string logicalName) => Available.Contains(logicalName) ? new GameObject() : null;
            public bool TryGetPrefab(string logicalName, out GameObject prefab)
            {
                prefab = Available.Contains(logicalName) ? new GameObject() : null;
                return prefab != null;
            }
            public GameObject GetPrefabByFuzzy(string keyword) => null;
            public IReadOnlyList<string> GetAllLogicalNames() => Available.ToList();
        }

        private static GenerationRequest CreateRequest() => new GenerationRequest { Prompt = "森林营地，1个宝箱" };

        private static GenerationResult CreateSuccessResult() => new GenerationResult
        {
            Success = true,
            LevelData = new LevelData
            {
                LevelName = "测试关卡",
                Props = new List<PropPlacement> { new() { PrefabLogicalName = "宝箱", Position = Vector3.zero, Scale = Vector3.one } },
                Terrain = new TerrainData { Width = 100, Length = 100, HeightScale = 10f },
                Tasks = new List<TaskData> { new() { TaskID = "main_1", IsMainTask = true } }
            },
            GenerationTime = 1f
        };

        /// <summary> 装配完整校验体系（请求/资源/数值边界） + 快照 + 构建器 </summary>
        private static (GeneratorScheduler Scheduler, FakeSnapshotManager Snapshots, FakeBuilder Builder, TestLogger Logger, FakeResourceMapper Mapper)
            CreateRig(FakeGenerator generator)
        {
            var registry = new ValidatorRegistry();
            var mapper = new FakeResourceMapper { Available = { "宝箱", "敌人-弓箭手" } };
            registry.SetServices(mapper, null);
            registry.Register(ValidationStage.Pre, new RequestValidator());
            registry.Register(ValidationStage.Pre, new ResourceValidator());
            registry.Register(ValidationStage.Pre, new DataBoundsValidator());

            var snapshots = new FakeSnapshotManager();
            var builder = new FakeBuilder();
            var logger = new TestLogger();
            var scheduler = new GeneratorScheduler(generator, registry);
            scheduler.SetBuilder(builder);
            scheduler.SetSnapshotManager(snapshots);
            scheduler.SetLogger(logger);
            return (scheduler, snapshots, builder, logger, mapper);
        }

        [Test]
        public async Task 请求校验失败_拦截停留原状态且零快照副作用()
        {
            var (scheduler, snapshots, _, logger, _) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            var request = CreateRequest();
            request.Prompt = "  "; // 非法输入：空白描述

            await scheduler.StartGenerationAsync(request);

            Assert.AreEqual(GenerationTaskState.Ready, scheduler.CurrentState, "非法输入必须 100% 拦截，停留原状态");
            Assert.AreEqual(0, snapshots.CreateCount, "Day4 固化顺序：前置校验在快照创建之前，校验失败零快照创建");
            Assert.AreEqual(0, snapshots.DiscardCount, "校验失败无需丢弃快照（未创建）");
            Assert.AreEqual(0, snapshots.RollbackCount, "构建前失败不得触发全量回滚");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[ERROR]") && m.Contains("前置校验失败")),
                "应输出统一格式的拦截日志");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("REQUEST_PROMPT_EMPTY") && m.Contains("prompt")),
                "错误日志应含错误码与字段定位");
        }

        [Test]
        public async Task 数据校验失败_转失败丢弃快照且构建器未调用()
        {
            var (scheduler, snapshots, builder, logger, mapper) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            mapper.Available.Remove("宝箱"); // 生成结果引用了不存在的资源

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState, "不存在资源必须被拦截为失败");
            Assert.IsFalse(builder.BuildCalled, "数据校验失败不得进入构建阶段");
            Assert.AreEqual(1, snapshots.CreateCount, "合法请求应已创建快照（数据级校验失败时快照已存在）");
            Assert.AreEqual(1, snapshots.DiscardCount, "构建前失败应丢弃快照");
            Assert.AreEqual(0, snapshots.RollbackCount);
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[ERROR]") && m.Contains("数据校验失败")),
                "应输出数据校验失败日志");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("RESOURCE_NOT_FOUND") && m.Contains("props[0].prefabLogicalName")),
                "错误日志应含错误码与字段定位");
        }

        [Test]
        public async Task 数据校验通过_构建成功正常走通()
        {
            var (scheduler, snapshots, builder, logger, _) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            builder.Handler = (_, _) => Task.FromResult(LevelBuildResult.Succeeded(1, 0, 1f));

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Success, scheduler.CurrentState, "合法数据应正常走通全链路");
            Assert.IsTrue(builder.BuildCalled);
            Assert.AreEqual(1, snapshots.CreateCount, "合法请求应已创建快照");
            Assert.AreEqual(1, snapshots.DiscardCount, "Day4：成功即事务提交，快照完成使命应被清理");
        }

        [Test]
        public async Task 构建失败_有快照_自动全量回滚并复位就绪()
        {
            var (scheduler, snapshots, builder, logger, _) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            builder.Handler = (_, _) => Task.FromResult(LevelBuildResult.Failed("实例化异常"));
            var states = new List<GenerationTaskState>();
            scheduler.StateChanged += s => states.Add(s);

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(1, snapshots.RollbackCount, "构建失败（场景已污染）应自动全量回滚");
            Assert.AreEqual(GenerationTaskState.Ready, scheduler.CurrentState, "回滚成功后状态机复位");
            Assert.AreEqual(
                new[] { GenerationTaskState.Generating, GenerationTaskState.Failed, GenerationTaskState.Ready },
                states, "状态序列：生成中 → 失败 → （自动回滚后）就绪");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[WARN]") && m.Contains("已自动回滚")),
                "应输出自动回滚提示");
        }

        [Test]
        public async Task 构建失败_无快照_保持失败不调回滚()
        {
            var (scheduler, snapshots, builder, logger, _) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            builder.Handler = (_, _) => Task.FromResult(LevelBuildResult.Failed("实例化异常"));
            snapshots.HasSnapshotResult = false; // 无快照：增量清理兜底，不阻塞失败流转

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState);
            Assert.AreEqual(0, snapshots.RollbackCount, "无快照时不得调用回滚");
        }

        [Test]
        public async Task 构建失败_回滚失败_保持失败并输出错误日志()
        {
            var (scheduler, snapshots, builder, logger, _) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            builder.Handler = (_, _) => Task.FromResult(LevelBuildResult.Failed("实例化异常"));
            snapshots.RollbackResult = false; // 回滚失败：保持 Failed 提示人工处理

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(1, snapshots.RollbackCount, "应尝试回滚");
            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState, "回滚失败不得复位状态机");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[ERROR]") && m.Contains("自动回滚失败")),
                "应提示人工处理");
        }

        [Test]
        public async Task 构建异常_同样自动全量回滚()
        {
            var (scheduler, snapshots, builder, logger, _) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            builder.Handler = (_, _) => throw new InvalidOperationException("构建器内部爆炸");

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(1, snapshots.RollbackCount, "构建异常同样视为场景已污染");
            Assert.AreEqual(GenerationTaskState.Ready, scheduler.CurrentState);
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("构建异常")), "应输出异常日志");
        }

        [Test]
        public async Task 构建取消_不触发全量回滚仅丢弃快照()
        {
            var (scheduler, snapshots, builder, logger, _) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            builder.Handler = (_, _) => Task.FromResult(LevelBuildResult.Cancelled(2));

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(0, snapshots.RollbackCount, "用户主动取消 ≠ 失败，不得全量回滚");
            Assert.AreEqual(1, snapshots.DiscardCount, "取消即快照作废（快照只存在于生成-构建生命周期）");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[WARN]") && m.Contains("生成取消")),
                "取消应输出提示（非错误）");
        }

        [Test]
        public async Task 生成器抛异常_转失败并丢弃快照()
        {
            var (scheduler, snapshots, builder, logger, _) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromException<GenerationResult>(new Exception("llm boom")) });

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState);
            Assert.IsFalse(builder.BuildCalled, "生成异常不得进入构建");
            Assert.AreEqual(1, snapshots.CreateCount, "生成异常前快照已创建（前置校验通过）");
            Assert.AreEqual(1, snapshots.DiscardCount, "生成异常 = 场景零变更，应丢弃快照避免陈旧快照误导回滚按钮");
            Assert.AreEqual(0, snapshots.RollbackCount, "场景零变更无需全量回滚");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[ERROR]") && m.Contains("生成异常") && m.Contains("llm boom")),
                "应输出生成异常日志");
        }

        [Test]
        public async Task 快照创建失败_降级继续生成并提示()
        {
            var (scheduler, snapshots, builder, logger, _) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            builder.Handler = (_, _) => Task.FromResult(LevelBuildResult.Succeeded(1, 0, 1f));
            snapshots.CreateResult = false; // 快照创建失败：降级为增量回滚兜底，不阻塞生成

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Success, scheduler.CurrentState, "快照创建失败不得阻塞生成链路");
            Assert.AreEqual(1, snapshots.CreateCount, "应尝试创建快照");
            Assert.AreEqual(0, snapshots.RollbackCount);
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[WARN]") && m.Contains("快照创建失败")),
                "应输出降级提示");
        }

        [Test]
        public async Task 第二轮生成_快照重新创建()
        {
            var (scheduler, snapshots, builder, _, _) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            builder.Handler = (_, _) => Task.FromResult(LevelBuildResult.Succeeded(1, 0, 1f));

            await scheduler.StartGenerationAsync(CreateRequest()); // 第一轮：创建 + 成功清理
            Assert.AreEqual(1, snapshots.CreateCount, "第一轮应创建快照");

            await scheduler.StartGenerationAsync(CreateRequest()); // 第二轮：重新创建（覆盖语义）

            Assert.AreEqual(2, snapshots.CreateCount, "新一轮任务应重新创建快照");
            Assert.AreEqual(GenerationTaskState.Success, scheduler.CurrentState);
        }

        [Test]
        public async Task 无注册表无快照_构建失败不抛异常保持旧行为()
        {
            // 回归保护：未注入校验体系/快照时，调度器保持 Day1 行为
            var builder = new FakeBuilder { Handler = (_, _) => Task.FromResult(LevelBuildResult.Failed("找不到预制体")) };
            var scheduler = new GeneratorScheduler(new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            scheduler.SetBuilder(builder);

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState, "无快照时保持旧行为（Failed）");
            Assert.IsTrue(builder.BuildCalled);
        }
    }
}
