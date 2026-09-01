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
    /// 调度器 × 后置校验（第四周-Day3）集成测试：
    /// Post 校验失败（场景已污染）→ 转 Failed + 自动全量回滚 + 复位 Ready；
    /// 无快照 → 保持 Failed 不回滚；Post 通过 → Success 且快照保留（成功路径不丢弃，快照由后续生命周期管理）；
    /// 未注册 Post 校验器 → 行为不变（回归保护）；校验器异常 → 单点转 VALIDATOR_ERROR 不打断失败链路。
    /// </summary>
    public class GeneratorSchedulerPostValidationTests
    {
        private readonly List<GameObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _created.Clear();
        }

        private GameObject CreateEntity(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go;
        }

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

        /// <summary> 假快照管理器：记录 Create/Discard/Rollback 调用次数 + 可控结果 </summary>
        private class FakeSnapshotManager : ISceneSnapshotManager
        {
            public bool HasSnapshotResult = true;
            public bool RollbackResult = true;
            public int CreateCount;
            public int DiscardCount;
            public int RollbackCount;

            public bool HasSnapshot => HasSnapshotResult;
            public string SnapshotPath => "Temp/TestSnapshot.unity";
            public string OriginalScenePath => "";

            public bool CreateSnapshot() { CreateCount++; return true; }
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

        /// <summary> 可控后置校验器：测试注入期望行为（真校验器行为由 PostBuildValidatorTests 覆盖） </summary>
        private class FakePostValidator : BaseValidator<PostBuildData>
        {
            public Func<PostBuildData, ValidationResult> Handler;
            public override ValidationResult Validate(PostBuildData data, ValidationContext context) => Handler(data);
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

        /// <summary> 构建成功结果（带 BuiltObjects，供 Post 校验消费） </summary>
        private static LevelBuildResult CreateBuiltResult(int count)
        {
            var result = LevelBuildResult.Succeeded(count, 0, 1f);
            var entities = new List<GameObject>();
            for (var i = 0; i < count; i++) entities.Add(new GameObject("宝箱"));
            result.BuiltObjects = entities;
            return result;
        }

        /// <summary> 装配完整校验体系（Pre + Post）+ 快照 + 构建器 </summary>
        private static (GeneratorScheduler Scheduler, FakeSnapshotManager Snapshots, FakeBuilder Builder, TestLogger Logger)
            CreateRig(FakeGenerator generator, Func<PostBuildData, ValidationResult> postHandler = null, int builtCount = 1)
        {
            var registry = new ValidatorRegistry();
            var mapper = new FakeResourceMapper { Available = { "宝箱", "敌人-弓箭手" } };
            registry.SetServices(mapper, null);
            registry.Register(ValidationStage.Pre, new RequestValidator());
            registry.Register(ValidationStage.Pre, new ResourceValidator());
            registry.Register(ValidationStage.Pre, new DataBoundsValidator());
            if (postHandler != null)
                registry.Register(ValidationStage.Post, new FakePostValidator { Handler = postHandler });

            var snapshots = new FakeSnapshotManager();
            var builder = new FakeBuilder
            {
                Handler = (_, _) => Task.FromResult(CreateBuiltResult(builtCount))
            };
            var logger = new TestLogger();
            var scheduler = new GeneratorScheduler(generator, registry);
            scheduler.SetBuilder(builder);
            scheduler.SetSnapshotManager(snapshots);
            scheduler.SetLogger(logger);
            return (scheduler, snapshots, builder, logger);
        }

        private static ValidationResult FailResult(string code) => new()
        {
            Errors = { new ValidationError { Code = code, Message = "测试注入失败", DataPath = "entities[0]" } }
        };

        [Test]
        public async Task 后置校验失败_有快照_自动全量回滚并复位就绪()
        {
            var (scheduler, snapshots, _, logger) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) },
                data => FailResult("POST_COUNT_MISMATCH"));
            var states = new List<GenerationTaskState>();
            scheduler.StateChanged += s => states.Add(s);

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(1, snapshots.RollbackCount, "Post 失败（场景已污染）应自动全量回滚");
            Assert.AreEqual(GenerationTaskState.Ready, scheduler.CurrentState, "回滚成功后状态机复位");
            Assert.AreEqual(
                new[] { GenerationTaskState.Generating, GenerationTaskState.Failed, GenerationTaskState.Ready },
                states, "状态序列：生成中 → 失败 → （自动回滚后）就绪");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[ERROR]") && m.Contains("后置校验失败")),
                "应输出后置校验失败日志");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[WARN]") && m.Contains("已自动回滚")),
                "应输出自动回滚提示");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("POST_COUNT_MISMATCH") && m.Contains("entities[0]")),
                "错误日志应含错误码与字段定位");
        }

        [Test]
        public async Task 后置校验失败_无快照_保持失败不调回滚()
        {
            var (scheduler, snapshots, _, logger) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) },
                data => FailResult("POST_ENTITY_NULL"));
            snapshots.HasSnapshotResult = false; // 无快照：增量清理兜底，不阻塞失败流转

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState);
            Assert.AreEqual(0, snapshots.RollbackCount, "无快照时不得调用回滚");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("后置校验失败")), "仍应输出失败日志");
        }

        [Test]
        public async Task 后置校验失败_回滚失败_保持失败并输出错误日志()
        {
            var (scheduler, snapshots, _, logger) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) },
                data => FailResult("POST_FLOAT_UNSUPPORTED"));
            snapshots.RollbackResult = false; // 回滚失败：保持 Failed 提示人工处理

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(1, snapshots.RollbackCount, "应尝试回滚");
            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState, "回滚失败不得复位状态机");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[ERROR]") && m.Contains("自动回滚失败")),
                "应提示人工处理");
        }

        [Test]
        public async Task 后置校验通过_转成功且快照保留()
        {
            var (scheduler, snapshots, builder, _) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) },
                data => new ValidationResult());

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Success, scheduler.CurrentState, "Post 通过应正常转成功");
            Assert.IsTrue(builder.BuildCalled);
            Assert.AreEqual(0, snapshots.RollbackCount, "通过路径不得回滚");
            Assert.AreEqual(0, snapshots.DiscardCount, "成功路径不得丢弃快照（快照由 Day4 成功清理语义管理）");
        }

        [Test]
        public async Task 未注册后置校验器_行为不变()
        {
            // 回归保护：不注册 Post 校验器时调度器保持 Day2 行为（直接转 Success）
            var (scheduler, snapshots, _, logger) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Success, scheduler.CurrentState);
            Assert.AreEqual(0, snapshots.RollbackCount);
        }

        [Test]
        public async Task 后置校验器抛异常_转校验器错误并失败回滚()
        {
            var (scheduler, snapshots, _, logger) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) },
                data => throw new InvalidOperationException("后置校验器内部爆炸"));

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(1, snapshots.RollbackCount, "校验器异常经单点保护转 VALIDATOR_ERROR，仍按失败回滚处理");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("后置校验失败") && m.Contains("VALIDATOR_ERROR")),
                "单点异常应转 VALIDATOR_ERROR 并入失败日志");
        }

        [Test]
        public async Task 构建取消_不触发后置校验()
        {
            var (scheduler, snapshots, builder, logger) = CreateRig(
                new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) },
                data => FailResult("POST_ENTITY_NULL"), // 即使注册了必失败校验器
                builtCount: 0);
            // 覆盖为取消结果：取消路径不应执行 Post 校验（Cancelled 分支直接返回）
            builder.Handler = (_, _) => Task.FromResult(LevelBuildResult.Cancelled(2));

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(0, snapshots.RollbackCount, "用户主动取消 ≠ 失败，不得触发回滚");
            Assert.AreEqual(1, snapshots.DiscardCount, "取消应丢弃快照（与 Day2 语义一致）");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[WARN]") && m.Contains("生成取消")),
                "取消应输出提示（非错误）");
            Assert.IsFalse(logger.Messages.Exists(m => m.Contains("后置校验失败")), "取消路径不得执行 Post 校验");
        }
    }
}
