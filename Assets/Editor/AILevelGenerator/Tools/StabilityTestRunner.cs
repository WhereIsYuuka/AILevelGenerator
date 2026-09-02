using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using AILevelGenerator.Editor.Builders;
using AILevelGenerator.Runtime.Components;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Diagnostics;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Mappings;
using AILevelGenerator.Runtime.Scheduling;
using AILevelGenerator.Runtime.Stability;
using AILevelGenerator.Runtime.Utilities;
using AILevelGenerator.Runtime.Validation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
// Debug 歧义：System.Diagnostics.Debug（Stopwatch 所需）与 UnityEngine.Debug 同名，统一指向引擎日志
using Debug = UnityEngine.Debug;

namespace AILevelGenerator.Editor.Tools
{
    /// <summary>
    /// 稳定性测试驱动器（第四周-Day6/7）：连续 20 次生成，统计成功率与回滚成功率，覆盖核心异常场景。
    /// 逐轮执行 ScenarioRotation 固定轮换表（成功/生成器异常/业务失败/请求拦截/资源缺失/Mid 失败/
    /// 构建失败/构建器异常/Post 失败/生成中取消/构建中取消/回滚失败/0 实体/NaN 坐标），
    /// 每轮 = 隔离链路（真实组件）执行 + 四重断言：
    ///   1. 终态符合轮换表期望（拦截轮停留 Ready）；
    ///   2. 报告事件计数符合预期（拦截轮 0 次，任务轮恰好 1 次）；
    ///   3. 回滚触发/结果与轮换表一致（注入的回滚失败轮如实标记失败）；
    ///   4. 场景断言（场景指纹）：失败轮回到生成前（零变更/全量回滚/增量清理），成功轮出现生成根 + 实体数合规。
    /// 统计口径（Runtime 纯逻辑，可单测）：成功率 = 通过轮/总轮；回滚成功率 = 回滚成功/回滚触发（0 除安全）。
    /// 前置条件：当前场景已保存（真实快照能力依赖已保存场景；未保存时快照创建失败 → 全量回滚轮会降级，测试失真）。
    /// 菜单路径挂 "Tools/AI Level Generator Tests/"（兄弟菜单约定）；MCP/脚本：fire-and-forget + 轮询 LastResult/LastTestResult。
    /// </summary>
    public static class StabilityTestRunner
    {
        /// <summary> 是否正在执行（防并发启动） </summary>
        public static bool IsRunning { get; private set; }

        /// <summary> 最近一次执行的汇总文本（MCP/脚本轮询读取） </summary>
        public static string LastResult { get; private set; }

        /// <summary> 最近一次执行的结构化结果（轮次明细 + 统计口径，脚本可深入分析） </summary>
        public static StabilityTestResult LastTestResult { get; private set; }

        [MenuItem("Tools/AI Level Generator Tests/稳定性测试（20 次连续生成）")]
        public static async void RunStabilityTest20()
        {
            await RunStabilityTestAsync(ScenarioRotation.RoundCount); // 入口自带守卫与异常兜底，async void 安全
        }

        /// <summary>
        /// 稳定性测试入口（菜单与 MCP 共用）：并发保护 + 播放模式禁止 + 场景已保存校验；异常兜底（永不抛出）。
        /// </summary>
        public static async Task<string> RunStabilityTestAsync(int rounds = ScenarioRotation.RoundCount)
        {
            if (IsRunning)
            {
                Debug.LogWarning("[稳定性] 已有稳定性测试执行中，忽略本次触发");
                return "忽略：已有稳定性测试执行中";
            }
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[稳定性] 播放模式中禁止执行稳定性测试，请先停止播放");
                return "忽略：播放模式中禁止执行";
            }
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            {
                Debug.LogWarning("[稳定性] 当前场景尚未保存过，无法创建快照（全量回滚轮依赖已保存场景），请先保存场景（如 ToolScene）");
                return "忽略：当前场景未保存（需已保存场景支撑快照能力）";
            }
            IsRunning = true;
            try
            {
                var result = await RunCore(rounds);
                LastTestResult = result;
                LastResult = result.ToSummaryText();
                Debug.Log($"[稳定性] {LastResult}");
                foreach (var r in result.Rounds)
                    Debug.Log($"[稳定性] {r.ToSummaryLine(result.Rounds.Count)}");
                return LastResult;
            }
            catch (Exception ex)
            {
                LastResult = $"执行异常：{ex.Message}";
                Debug.LogError($"[稳定性] {LastResult}\n{ex.StackTrace}");
                return LastResult;
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary> 核心循环（无守卫，仅被守卫入口调用）：逐轮执行轮换表并汇总统计 </summary>
        private static async Task<StabilityTestResult> RunCore(int rounds)
        {
            var result = new StabilityTestResult();
            var swTotal = Stopwatch.StartNew();
            var total = Math.Min(rounds, ScenarioRotation.Rounds.Count);

            for (var i = 0; i < total; i++)
                result.Rounds.Add(await RunRoundAsync(ScenarioRotation.Rounds[i], i + 1));

            swTotal.Stop();
            result.TotalTimeSeconds = swTotal.Elapsed.TotalSeconds;
            return result;
        }

        /// <summary> 单轮执行：干净起点 → 基线指纹 → 装配注入 → 执行（含取消时序）→ 四重断言 </summary>
        private static async Task<StabilityRoundResult> RunRoundAsync(StabilityRoundSpec spec, int index)
        {
            var round = new StabilityRoundResult { Index = index, Scenario = spec.Scenario, ExpectedState = spec.ExpectedState };
            var sw = Stopwatch.StartNew();

            // 干净起点（清除历史生成根）→ 基线指纹（该轮生成前场景状态）
            CleanupGeneratedRoots();
            var baseline = SceneFingerprint.Compute();

            // 装配隔离链路 + 订阅报告事件（计数 + 保留最后一份供回滚信息断言）
            var scheduler = CreateChain(spec.Scenario, out var realBuilder);
            var reportCount = 0;
            GenerationReport lastReport = null;
            scheduler.GenerationCompleted += r => { reportCount++; lastReport = r; };

            var request = BuildRequest(spec.Scenario, index);
            try
            {
                if (spec.Scenario == StabilityScenario.CancelDuringGenerate)
                {
                    // 生成阶段取消：Mock 300ms 挂起中取消 → 结果返回后丢弃（场景零变更）
                    var task = scheduler.StartGenerationAsync(request);
                    scheduler.CancelGeneration();
                    await task;
                }
                else if (spec.Scenario == StabilityScenario.CancelDuringBuild)
                {
                    // 构建阶段取消：等待真实构建器确认「构建进行中」后再取消——
                    // 固定延迟在 MCP 无头高帧率下不可靠（分帧预算上限 30/帧，构建可能已完成，取消失效）；
                    // IsBuilding=true 保证 BuildAsync 未返回 → Cancel 必在构建完成前生效。
                    var task = scheduler.StartGenerationAsync(request);
                    var timeout = Stopwatch.StartNew();
                    while (realBuilder == null || !realBuilder.IsBuilding)
                    {
                        if (timeout.ElapsedMilliseconds > 3000) break; // 超时兜底（理论不达：Mock 0ms 立即进入构建）
                        await Task.Delay(5);
                    }
                    await Task.Delay(16); // 让至少一帧分帧实例化执行（真实「构建中」取消，而非预中止）
                    scheduler.CancelGeneration();
                    await task;
                }
                else
                {
                    await scheduler.StartGenerationAsync(request);
                }
            }
            catch (Exception ex)
            {
                AddNote(round, $"执行异常：{ex.Message}");
            }

            // 构建中取消：增量删除是分帧协程（每帧销毁 4 个子物体），Finish 在删除完成前返回——
            // 必须等待删除收敛（无生成根）后再断言，否则指纹断言读到的是残留场景
            if (spec.Scenario == StabilityScenario.CancelDuringBuild)
            {
                var cleanupTimeout = Stopwatch.StartNew();
                while (HasGeneratedRoot() && cleanupTimeout.ElapsedMilliseconds < 5000)
                    await Task.Delay(16);
                if (HasGeneratedRoot())
                    AddNote(round, "增量清理超时（生成根残留）");
            }

            sw.Stop();
            round.RoundTimeSeconds = sw.Elapsed.TotalSeconds;

            // 断言 1：终态（取报告 FinalState = 任务语义终态；回滚成功轮状态机被复位到 Ready 是 UI 复位行为，
            // 不代表任务未失败——调度器 TryAutoRollback 成功路径会 ResetToReady，报告仍如实标记 Failed + 回滚成功）
            round.ActualState = lastReport != null ? lastReport.FinalState : scheduler.CurrentState;
            round.StateMatched = round.ActualState == spec.ExpectedState;
            if (!round.StateMatched)
                AddNote(round, $"终态 {round.ActualState.ToDisplayName()} ≠ 期望 {round.ExpectedState.ToDisplayName()}");

            // 断言 2：报告事件计数（拦截轮 0 次，任务轮恰好 1 次）
            var expectedReports = spec.ExpectedState == GenerationTaskState.Ready ? 0 : 1;
            round.ReportCountMatched = reportCount == expectedReports;
            if (!round.ReportCountMatched)
                AddNote(round, $"报告事件 {reportCount} 次 ≠ 期望 {expectedReports} 次");

            // 断言 3：回滚触发/结果（取自调度器写入的终态报告）
            round.RollbackTriggered = lastReport?.RollbackTriggered ?? false;
            round.RollbackSucceeded = lastReport?.RollbackSucceeded ?? false;
            if (round.RollbackTriggered != spec.ExpectRollbackTriggered)
                AddNote(round, $"回滚触发 {round.RollbackTriggered} ≠ 期望 {spec.ExpectRollbackTriggered}");
            else if (round.RollbackTriggered && round.RollbackSucceeded != spec.ExpectRollbackSucceeded)
                AddNote(round, $"回滚结果 {round.RollbackSucceeded} ≠ 期望 {spec.ExpectRollbackSucceeded}");

            // 断言 4：场景断言（指纹：失败轮回到生成前 / 成功轮出现生成根）
            round.AssertionPassed = CheckAssertion(spec, baseline);
            if (!round.AssertionPassed)
                AddNote(round, $"场景断言失败（{spec.Assertion}）");

            // 成功轮补充实体数断言（NormalSuccess > 0；ZeroEntities == 0）
            if (spec.Assertion == StabilityAssertion.GenRootCreated && round.AssertionPassed)
            {
                var entities = CountGeneratedEntities();
                var entitiesOk = spec.Scenario == StabilityScenario.ZeroEntities ? entities == 0 : entities > 0;
                if (!entitiesOk)
                {
                    round.AssertionPassed = false;
                    AddNote(round, $"实体数断言失败：{entities}");
                }
            }

            return round;
        }

        /// <summary> 场景断言：按断言类型对比当前指纹与基线（失败轮必须回到生成前状态） </summary>
        private static bool CheckAssertion(StabilityRoundSpec spec, string baseline)
        {
            var now = SceneFingerprint.Compute();
            switch (spec.Assertion)
            {
                case StabilityAssertion.NoSideEffect:   // 拦截轮：场景未动
                case StabilityAssertion.ZeroChange:     // 构建前失败/生成中取消：场景零变更
                case StabilityAssertion.RollbackRestored: // 全量回滚后：指纹回到生成前
                case StabilityAssertion.IncrementalClean: // 增量清理后：本次根已删，指纹回到生成前
                    return now == baseline;
                case StabilityAssertion.GenRootCreated: // 成功轮：出现 [AI Generated] 生成根
                    return now.Contains("[AI Generated]");
                default:
                    return false;
            }
        }

        /// <summary>
        /// 按场景装配隔离链路（真实组件 + 异常注入点），返回调度器。
        /// observableBuilder：真实构建器引用（构建中取消轮用于轮询 IsBuilding 确认取消时机）；桩轮为 null。
        /// 装配矩阵见各分支注释。
        /// </summary>
        private static GeneratorScheduler CreateChain(StabilityScenario scenario, out ILevelBuilder observableBuilder)
        {
            observableBuilder = null;
            // 配置资产（资产缺失时降级，与 GeneratorServiceInitializer 语义一致）
            var mappingConfig = AssetDatabase.LoadAssetAtPath<PrefabMappingConfig>("Assets/Settings/Mappings/PrefabMapping_Default.asset");
            var bindingConfig = AssetDatabase.LoadAssetAtPath<ComponentBindingConfig>("Assets/Settings/Bindings/ComponentBinding_Default.asset");

            // Mid 校验失败轮：校验器与构建器共用空映射表 → 首帧 RESOURCE_MAPPING_EMPTY（Pre 阶段不注册 Resource，
            // 否则空映射表会在构建前被数据校验提前拦截，Mid 校验就无从验证）
            var useEmptyMapper = scenario == StabilityScenario.MidValidationFail;
            IResourceMapper mapper = useEmptyMapper
                ? new ResourceMappingManager(ScriptableObject.CreateInstance<PrefabMappingConfig>())
                : mappingConfig != null ? new ResourceMappingManager(mappingConfig) : null;

            // Post 校验失败轮：绑定器用空配置（实体不挂组件）→ Post 组件完整性 POST_COMPONENT_MISSING
            var useEmptyBinder = scenario == StabilityScenario.PostValidationFail;
            var binder = useEmptyBinder
                ? new ComponentBinder(ScriptableObject.CreateInstance<ComponentBindingConfig>())
                : new ComponentBinder(bindingConfig); // null 配置 = 安全空结果（已知坑）

            // 校验注册表：Pre（拦截输入与数据）+ Mid（构建中）+ Post（组件完整性；可达性关闭沿用联调惯例）
            var registry = new ValidatorRegistry();
            if (mapper != null)
            {
                registry.SetServices(mapper, null);
                registry.Register(ValidationStage.Pre, new RequestValidator());
                registry.Register(ValidationStage.Pre, new DataBoundsValidator());
                if (!useEmptyMapper) registry.Register(ValidationStage.Pre, new ResourceValidator());
                registry.Register(ValidationStage.Mid, new ResourceValidator());
                registry.Register(ValidationStage.Mid, new DataBoundsValidator());
                registry.Register(ValidationStage.Post, new PostBuildValidator(bindingConfig, checkReachability: false));
            }

            // 生成器：统一包装（Mock 魔法词「异常/失败」透传 + 数据篡改按场景注入）
            var (delayMs, propCount) = scenario switch
            {
                StabilityScenario.CancelDuringGenerate => (300, 3), // 挂起足够取消窗口
                StabilityScenario.CancelDuringBuild => (0, 150),    // 大量实体（每帧预算上限 30）：保证取消轮询窗口内构建必然未完成
                StabilityScenario.ZeroEntities => (0, 0),           // 空关卡
                _ => (0, 5)
            };
            IGenerator generator = new ScenarioGenerator(new MockGenerator(delayMs, propCount), scenario);

            var scheduler = new GeneratorScheduler(generator, registry);
            scheduler.SetLogger(new CollectingLogger());

            // 构建器：失败/异常/回滚失败轮注入桩（不污染真实场景）；其余轮用真实构建器（分帧+布局+绑定+烘焙+Mid）
            if (scenario is StabilityScenario.BuildFail or StabilityScenario.BuilderThrows or StabilityScenario.RollbackFail)
            {
                var stub = new StubBuilder();
                // 三态明确：BuilderThrows 抛异常；BuildFail/RollbackFail 返回失败结果（回滚失败轮差异在快照桩——Rollback 恒 false）
                stub.Handler = scenario == StabilityScenario.BuilderThrows
                    ? () => throw new InvalidOperationException("稳定性注入：构建器异常")
                    : () => Task.FromResult(LevelBuildResult.Failed("稳定性注入：构建失败"));
                scheduler.SetBuilder(stub);
            }
            else
            {
                var rollback = new RollbackManager();
                var realBuilder = new SceneLevelBuilder(mapper, rollback, binder, new NavMeshBaker(), registry);
                observableBuilder = realBuilder;
                scheduler.SetBuilder(realBuilder);
            }

            // 快照管理器：回滚失败轮注入桩（Create 成功 / Rollback 恒 false）；其余轮真实快照（全量回滚真实验证）
            scheduler.SetSnapshotManager(scenario == StabilityScenario.RollbackFail
                ? new StubSnapshotManager()
                : SceneSnapshotManager.Instance);

            return scheduler;
        }

        /// <summary> 构造本轮请求（魔法词触发 Mock 失败/异常路径；空白描述触发请求拦截） </summary>
        private static GenerationRequest BuildRequest(StabilityScenario scenario, int index)
        {
            var prompt = scenario switch
            {
                StabilityScenario.GeneratorThrows => $"稳定性测试第 {index} 轮：异常",
                StabilityScenario.GeneratorBusinessFail => $"稳定性测试第 {index} 轮：失败",
                StabilityScenario.RequestBlocked => "  ",
                _ => $"稳定性测试第 {index} 轮：森林营地，敌人巡逻与宝箱分布"
            };
            return new GenerationRequest { Prompt = prompt, TemplateId = "战斗关卡", RandomSeed = 1000 + index };
        }

        /// <summary> 失败原因追加（多条用「；」连接） </summary>
        private static void AddNote(StabilityRoundResult round, string note)
        {
            round.Note = string.IsNullOrEmpty(round.Note) ? note : $"{round.Note}；{note}";
        }

        /// <summary>
        /// 生成器包装（异常注入）：Mock 结果返回后按场景篡改数据（资源缺失 / NaN 坐标）。
        /// Mock 的「异常」「失败」魔法词原样透传（await 抛出/业务失败结果不被包装拦截）。
        /// </summary>
        private class ScenarioGenerator : IGenerator
        {
            private readonly MockGenerator _inner;
            private readonly StabilityScenario _scenario;

            public ScenarioGenerator(MockGenerator inner, StabilityScenario scenario)
            {
                _inner = inner;
                _scenario = scenario;
            }

            public async Task<GenerationResult> GenerateAsync(GenerationRequest request)
            {
                var result = await _inner.GenerateAsync(request);
                if (result?.LevelData == null || result.LevelData.Props.Count == 0) return result;

                if (_scenario == StabilityScenario.ResourceMissing)
                    result.LevelData.Props[0].PrefabLogicalName = "稳定性注入：不存在的物体";
                else if (_scenario == StabilityScenario.NanCoordinate)
                    result.LevelData.Props[0].Position = new Vector3(float.NaN, 0f, 0f);
                return result;
            }
        }

        /// <summary> 构建器桩（失败/异常注入）：不实例化、不污染真实场景，仅按注入返回结果/抛出 </summary>
        private class StubBuilder : ILevelBuilder
        {
            public Func<Task<LevelBuildResult>> Handler;
            public bool IsBuilding => false;
            public event Action<float> ProgressChanged { add { } remove { } }

            public Task<LevelBuildResult> BuildAsync(LevelData levelData, LevelBuildOptions options = null) => Handler();

            public void Cancel() { }
        }

        /// <summary> 快照管理器桩（回滚失败注入）：Create 恒成功（快照"存在"），Rollback 恒失败 </summary>
        private class StubSnapshotManager : ISceneSnapshotManager
        {
            public bool HasSnapshot => true;
            public string SnapshotPath => "Assets/Temp/StubSnapshot.unity";
            public string OriginalScenePath => string.Empty;
            public bool CreateSnapshot() => true;
            public bool RollbackToSnapshot(bool rebakeNavMesh = true) => false;
            public bool DiscardSnapshot() => true;
        }

        /// <summary> 场景中是否存在 "[AI Generated]" 生成根（增量删除收敛信号） </summary>
        private static bool HasGeneratedRoot()
        {
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                if (go.transform.parent == null && go.name.StartsWith("[AI Generated]"))
                    return true;
            }
            return false;
        }

        /// <summary> 清理场景中全部 "[AI Generated]" 根物体（保证每轮干净起点） </summary>
        private static void CleanupGeneratedRoots()
        {
            var roots = new List<GameObject>();
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                if (go.transform.parent == null && go.name.StartsWith("[AI Generated]"))
                    roots.Add(go);
            }
            foreach (var root in roots)
                UnityEngine.Object.DestroyImmediate(root);
        }

        /// <summary> 统计场景中生成根下的实体数（直接子物体） </summary>
        private static int CountGeneratedEntities()
        {
            var count = 0;
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                if (go.transform.parent != null && go.transform.parent.name.StartsWith("[AI Generated]"))
                    count++;
            }
            return count;
        }

        /// <summary> 链路内日志收集器（失败轮溯源；全限定名规避 UnityEngine.ILogger 歧义） </summary>
        private class CollectingLogger : AILevelGenerator.Runtime.Interfaces.ILogger
        {
            public void Log(string message) => Debug.Log($"[稳定性/日志] {message}");
            public void LogWarning(string message) => Debug.LogWarning($"[稳定性/日志] {message}");
            public void LogError(string message) => Debug.LogError($"[稳定性/日志] {message}");
            public void LogSuccess(string message) => Debug.Log($"[稳定性/日志] {message}");
            public void Clear() { }
            public event Action<string, LogLevel> OnLogReceived;
        }
    }
}
