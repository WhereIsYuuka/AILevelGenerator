using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Diagnostics;
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
    /// 调度器生成报告事件测试（第四周-Day5）：
    /// 每个终态路径（成功/业务失败/异常/取消/构建失败回滚）GenerationCompleted 恰好触发一次；
    /// 请求被前置校验拦截（未进入任务）不触发；
    /// 报告内容与构建结果/回滚信息一致（回滚成功/失败均进入报告）。
    /// </summary>
    public class GeneratorSchedulerReportTests
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

        /// <summary> 假快照管理器：记录调用次数 + 可控结果（验证回滚信息进入报告） </summary>
        private class FakeSnapshotManager : ISceneSnapshotManager
        {
            public bool HasSnapshotResult = true;
            public bool RollbackResult = true;
            public int CreateCount;
            public int RollbackCount;

            public bool HasSnapshot => HasSnapshotResult;
            public string SnapshotPath => "Temp/TestSnapshot.unity";
            public string OriginalScenePath => "";

            public bool CreateSnapshot() { CreateCount++; return true; }
            public bool RollbackToSnapshot(bool rebakeNavMesh = true) { RollbackCount++; return RollbackResult; }
            public bool DiscardSnapshot() => true;
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

        private static GenerationRequest CreateRequest() =>
            new GenerationRequest { Prompt = "森林营地，1个宝箱", TemplateId = "forest", RandomSeed = 7 };

        private static GenerationResult CreateSuccessResult() => new GenerationResult
        {
            Success = true,
            GenerationTime = 1f,
            LevelData = new LevelData
            {
                LevelName = "测试关卡",
                Props = new List<PropPlacement> { new() { PrefabLogicalName = "宝箱", Position = Vector3.zero, Scale = Vector3.one } },
                Terrain = new TerrainData { Width = 100, Length = 100, HeightScale = 10f },
                Tasks = new List<TaskData> { new() { TaskID = "main_1", IsMainTask = true } }
            }
        };

        /// <summary> 装配：完整 Pre 校验 + 快照 + 构建器 + 报告订阅，返回 rig </summary>
        private static (GeneratorScheduler Scheduler, FakeSnapshotManager Snapshots, FakeBuilder Builder, List<GenerationReport> Reports)
            CreateRig(FakeGenerator generator, bool withBuilder = true)
        {
            var registry = new ValidatorRegistry();
            var mapper = new FakeResourceMapper { Available = { "宝箱", "敌人-弓箭手" } };
            registry.SetServices(mapper, null);
            registry.Register(ValidationStage.Pre, new RequestValidator());
            registry.Register(ValidationStage.Pre, new ResourceValidator());
            registry.Register(ValidationStage.Pre, new DataBoundsValidator());

            var snapshots = new FakeSnapshotManager();
            var builder = new FakeBuilder();
            var scheduler = new GeneratorScheduler(generator, registry);
            if (withBuilder) scheduler.SetBuilder(builder);
            scheduler.SetSnapshotManager(snapshots);
            scheduler.SetLogger(new TestLogger());

            var reports = new List<GenerationReport>();
            scheduler.GenerationCompleted += reports.Add;
            return (scheduler, snapshots, builder, reports);
        }

        [Test]
        public async Task 成功路径_事件恰好触发一次且报告为成功()
        {
            var generator = new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) };
            var (scheduler, _, _, reports) = CreateRig(generator, withBuilder: false); // 纯生成链路

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.That(reports.Count, Is.EqualTo(1), "成功路径必须恰好触发一次报告");
            var report = reports[0];
            Assert.That(report.FinalState, Is.EqualTo(GenerationTaskState.Success));
            Assert.That(report.StatusText, Is.EqualTo("成功"));
            Assert.That(report.ErrorCount, Is.EqualTo(0));
            Assert.That(report.TemplateId, Is.EqualTo("forest"));
            Assert.That(report.PropCount, Is.EqualTo(1));
            Assert.That(report.MainTaskCount, Is.EqualTo(1));
            Assert.That(report.HasTerrain, Is.True);
            Assert.That(report.LlmTimeSeconds, Is.EqualTo(1f));
            Assert.That(report.RollbackTriggered, Is.False);
        }

        [Test]
        public async Task 业务失败路径_事件触发一次且报告含错误()
        {
            var generator = new FakeGenerator
            {
                Handler = _ => Task.FromResult(new GenerationResult
                {
                    Success = false,
                    Errors = new List<ValidationError>
                    {
                        new() { Code = ErrorCodes.DEMO_FAIL, Message = "演示失败", DataPath = "props[0]" }
                    }
                })
            };
            var (scheduler, _, _, reports) = CreateRig(generator);

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.That(reports.Count, Is.EqualTo(1));
            Assert.That(reports[0].FinalState, Is.EqualTo(GenerationTaskState.Failed));
            Assert.That(reports[0].ErrorCount, Is.EqualTo(1));
            Assert.That(reports[0].Issues[0].Code, Is.EqualTo(ErrorCodes.DEMO_FAIL));
            Assert.That(reports[0].Issues[0].Hint, Is.Not.Empty, "目录命中应补全建议");
        }

        [Test]
        public async Task 生成异常路径_事件触发一次且报告归入LLM_ERROR()
        {
            var generator = new FakeGenerator { Handler = _ => throw new InvalidOperationException("模拟生成器崩溃") };
            var (scheduler, _, _, reports) = CreateRig(generator, withBuilder: false);

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.That(reports.Count, Is.EqualTo(1));
            var report = reports[0];
            Assert.That(report.FinalState, Is.EqualTo(GenerationTaskState.Failed));
            Assert.That(report.ErrorCount, Is.EqualTo(1));
            Assert.That(report.Issues[0].Code, Is.EqualTo(ErrorCodes.LLM_ERROR));
            Assert.That(report.Issues[0].Message, Does.Contain("模拟生成器崩溃"));
        }

        [Test]
        public async Task 生成阶段取消_事件触发一次且状态文案为已取消()
        {
            var generator = new FakeGenerator();
            var (scheduler, _, _, reports) = CreateRig(generator);

            var gate = new TaskCompletionSource<GenerationResult>();
            generator.Handler = _ => gate.Task; // 挂起生成等待取消信号
            var task = scheduler.StartGenerationAsync(CreateRequest()); // 同步执行到 await 挂起点

            scheduler.CancelGeneration(); // 生成阶段取消
            gate.SetResult(CreateSuccessResult()); // 结果返回后被丢弃
            await task;

            Assert.That(reports.Count, Is.EqualTo(1));
            var report = reports[0];
            Assert.That(report.FinalState, Is.EqualTo(GenerationTaskState.Failed));
            Assert.That(report.StatusText, Is.EqualTo("已取消"));
            Assert.That(report.ErrorCount, Is.EqualTo(0), "取消路径不产生校验错误");
        }

        [Test]
        public async Task 请求被前置校验拦截_事件不触发()
        {
            var generator = new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) };
            var (scheduler, snapshots, builder, reports) = CreateRig(generator);

            await scheduler.StartGenerationAsync(new GenerationRequest { Prompt = "  " }); // 空描述 → 拦截

            Assert.That(reports.Count, Is.EqualTo(0), "被拦截请求不是任务，不产生报告");
            Assert.That(snapshots.CreateCount, Is.EqualTo(0), "拦截发生在快照创建之前");
            Assert.That(builder.BuildCalled, Is.False);
        }

        [Test]
        public async Task 构建成功路径_报告含构建统计()
        {
            var generator = new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) };
            var (scheduler, _, builder, reports) = CreateRig(generator);
            builder.Handler = (_, _) => Task.FromResult(LevelBuildResult.Succeeded(1, 0, 0.5f,
                overlapRatio: 0.05f, resolvedPairs: 2, boundComponents: 3));

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.That(reports.Count, Is.EqualTo(1));
            var report = reports[0];
            Assert.That(report.FinalState, Is.EqualTo(GenerationTaskState.Success));
            Assert.That(report.InstantiatedCount, Is.EqualTo(1));
            Assert.That(report.BoundComponents, Is.EqualTo(3));
            Assert.That(report.ResolvedOverlapPairs, Is.EqualTo(2));
            Assert.That(report.OverlapRatio, Is.EqualTo(0.05f));
            Assert.That(report.BuildTimeSeconds, Is.EqualTo(0.5f));
        }

        [Test]
        public async Task 构建失败自动回滚_报告含回滚成功信息()
        {
            var generator = new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) };
            var (scheduler, snapshots, builder, reports) = CreateRig(generator);
            builder.Handler = (_, _) => Task.FromResult(LevelBuildResult.Failed("预制体映射失败"));

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.That(snapshots.RollbackCount, Is.EqualTo(1), "构建失败应触发全量回滚");
            Assert.That(reports.Count, Is.EqualTo(1));
            var report = reports[0];
            Assert.That(report.FinalState, Is.EqualTo(GenerationTaskState.Failed));
            Assert.That(report.RollbackTriggered, Is.True);
            Assert.That(report.RollbackSucceeded, Is.True);
            Assert.That(report.RollbackNote, Does.Contain("已自动回滚成功"));
        }

        [Test]
        public async Task 构建失败回滚失败_报告标记回滚失败()
        {
            var generator = new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) };
            var (scheduler, snapshots, builder, reports) = CreateRig(generator);
            snapshots.RollbackResult = false; // 回滚失败 → 报告须如实记录
            builder.Handler = (_, _) => Task.FromResult(LevelBuildResult.Failed("构建异常"));

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.That(reports.Count, Is.EqualTo(1));
            var report = reports[0];
            Assert.That(report.RollbackTriggered, Is.True);
            Assert.That(report.RollbackSucceeded, Is.False);
            Assert.That(report.RollbackNote, Does.Contain("自动回滚失败"));
        }
    }
}
