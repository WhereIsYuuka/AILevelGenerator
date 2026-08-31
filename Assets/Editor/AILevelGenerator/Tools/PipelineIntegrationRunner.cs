using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AILevelGenerator.Editor.Builders;
using AILevelGenerator.Runtime.Components;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Mappings;
using AILevelGenerator.Runtime.Scheduling;
using AILevelGenerator.Runtime.Utilities;
using UnityEditor;
using UnityEngine;
// Debug 歧义：System.Diagnostics.Debug（Stopwatch 所需）与 UnityEngine.Debug 同名，统一指向引擎日志
using Debug = UnityEngine.Debug;

namespace AILevelGenerator.Editor.Tools
{
    /// <summary>
    /// 全链路联调驱动器（Week3-Day6/7）：输入 → 生成 → 构建（分帧+自适应+组件绑定）→ NavMesh 烘焙 的完整链路验收。
    /// 为隔离性与可复现性，绕过 ServiceLocator 自建链路：MockGenerator（0ms 延迟，确定性数据）
    /// → GeneratorScheduler → SceneLevelBuilder（真实资源映射/回滚/组件绑定/NavMesh 烘焙）。
    /// 提供三项验收能力：
    ///   1. 10 次场景生成测试：连续 N 轮完整场景生成，每轮间清理场景，统计成功率与轮均耗时；
    ///   2. 30 实体性能基准：完整生成+构建 30 实体，断言总耗时 ≤3s，并采集构建期间帧间隔（avg/max，无卡顿证据）；
    ///   3. 快照回滚验收（第四周-Day1）：场景指纹驱动的"快照→生成→回滚→100% 恢复"自动化断言。
    /// 异步驱动：方法为 async（UnitySynchronizationContext 上 await），协程经 EditorApplication.update 推进，
    /// 主线程永不阻塞（禁止 GetResult/Wait）。MCP/脚本调用：fire-and-forget 启动后轮询 IsRunning/LastResult。
    /// 菜单路径挂 "Tools/AI Level Generator Tests/"（兄弟菜单，不与窗口叶子项冲突——见 AccuracyTestRunner 注释）。
    /// </summary>
    public static class PipelineIntegrationRunner
    {
        /// <summary> 是否正在执行（防止菜单重复点击/并发启动） </summary>
        public static bool IsRunning { get; private set; }

        /// <summary> 最近一次执行的汇总文本（MCP/测试轮询读取） </summary>
        public static string LastResult { get; private set; }

        [MenuItem("Tools/AI Level Generator Tests/10 次场景生成测试")]
        public static async void RunSceneGenerationTest10()
        {
            await RunSceneGenerationTestAsync(10); // 核心方法自带守卫与异常兜底，async void 安全
        }

        [MenuItem("Tools/AI Level Generator Tests/30 实体性能基准（≤3s）")]
        public static async void RunPerformanceBenchmark30()
        {
            await RunPerformanceBenchmarkAsync(30);
        }

        [MenuItem("Tools/AI Level Generator Tests/快照回滚验收（场景级）")]
        public static async void RunSnapshotRollbackTest()
        {
            await RunSnapshotRollbackTestAsync();
        }

        /// <summary>
        /// 连续 rounds 轮完整场景生成，返回汇总文本（每轮间清理场景）。
        /// 入口守卫（菜单与 MCP 共用）：并发保护 + 播放模式禁止；异常兜底（永不抛出，结果写入 LastResult）。
        /// </summary>
        public static async Task<string> RunSceneGenerationTestAsync(int rounds)
        {
            if (IsRunning)
            {
                Debug.LogWarning("[联调] 已有联调任务执行中，忽略本次触发");
                return "忽略：已有联调任务执行中";
            }
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[联调] 播放模式中禁止执行联调测试，请先停止播放");
                return "忽略：播放模式中禁止执行";
            }
            IsRunning = true;
            try
            {
                LastResult = await RunSceneGenerationTestCore(rounds);
                Debug.Log($"[联调] {LastResult}");
                return LastResult;
            }
            catch (Exception ex)
            {
                LastResult = $"执行异常：{ex.Message}";
                Debug.LogError($"[联调] {LastResult}\n{ex.StackTrace}");
                return LastResult;
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary> 10 次场景生成测试核心循环（无守卫，仅被守卫入口调用） </summary>
        private static async Task<string> RunSceneGenerationTestCore(int rounds)
        {
            var swTotal = Stopwatch.StartNew();
            var passed = 0;
            var totalEntities = 0;
            var buildTimes = new List<double>();
            var log = new CollectingLogger();

            for (var i = 1; i <= rounds; i++)
            {
                CleanupGeneratedRoots();
                var chain = CreateIsolatedChain(delayMs: 0, propCount: 5);
                chain.scheduler.SetLogger(log);

                var roundSw = Stopwatch.StartNew();
                await chain.scheduler.StartGenerationAsync(new GenerationRequest
                {
                    Prompt = $"联调测试第 {i}/{rounds} 轮：森林营地，敌人巡逻与宝箱分布",
                    TemplateId = "战斗关卡",
                    RandomSeed = i * 100 + 7
                });
                roundSw.Stop();

                var ok = chain.scheduler.CurrentState == GenerationTaskState.Success;
                var entities = CountGeneratedEntities();
                if (ok && entities > 0) passed++;
                totalEntities += entities;
                buildTimes.Add(roundSw.Elapsed.TotalSeconds);

                var status = ok ? "成功" : $"失败（状态 {chain.scheduler.CurrentState}）";
                Debug.Log($"[联调] 第 {i}/{rounds} 轮：{status}，实体 {entities} 个，耗时 {roundSw.Elapsed.TotalSeconds:F2}s");
                CleanupGeneratedRoots();
            }

            swTotal.Stop();
            var avgBuild = buildTimes.Count > 0 ? buildTimes.Average() : 0;
            var summary = $"10 次场景生成测试：{passed}/{rounds} 轮成功，累计实体 {totalEntities} 个，" +
                          $"总耗时 {swTotal.Elapsed.TotalSeconds:F1}s（轮均 {avgBuild:F2}s）→ {(passed == rounds ? "PASS" : "FAIL")}";
            return summary;
        }

        /// <summary>
        /// 单轮 entityCount 实体完整生成+构建，断言 ≤3s 并采集帧间隔统计，返回汇总文本。
        /// 入口守卫与异常兜底同 RunSceneGenerationTestAsync（菜单与 MCP 共用）。
        /// </summary>
        public static async Task<string> RunPerformanceBenchmarkAsync(int entityCount)
        {
            if (IsRunning)
            {
                Debug.LogWarning("[联调] 已有联调任务执行中，忽略本次触发");
                return "忽略：已有联调任务执行中";
            }
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[联调] 播放模式中禁止执行联调测试，请先停止播放");
                return "忽略：播放模式中禁止执行";
            }
            IsRunning = true;
            try
            {
                LastResult = await RunPerformanceBenchmarkCore(entityCount);
                Debug.Log($"[联调] {LastResult}");
                return LastResult;
            }
            catch (Exception ex)
            {
                LastResult = $"执行异常：{ex.Message}";
                Debug.LogError($"[联调] {LastResult}\n{ex.StackTrace}");
                return LastResult;
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary> 性能基准核心（无守卫，仅被守卫入口调用） </summary>
        private static async Task<string> RunPerformanceBenchmarkCore(int entityCount)
        {
            CleanupGeneratedRoots();
            var chain = CreateIsolatedChain(delayMs: 0, propCount: entityCount);
            var log = new CollectingLogger();
            chain.scheduler.SetLogger(log);

            // 帧间隔采样：构建全程（含实例化/布局/烘焙）采集，无卡顿证据（avg/max 毫秒）
            var frameStats = new FrameStats();
            frameStats.Start();

            var sw = Stopwatch.StartNew();
            await chain.scheduler.StartGenerationAsync(new GenerationRequest
            {
                Prompt = $"性能基准：{entityCount} 个实体联调",
                TemplateId = "战斗关卡",
                RandomSeed = 20260830
            });
            sw.Stop();
            frameStats.Stop();

            var ok = chain.scheduler.CurrentState == GenerationTaskState.Success;
            var entities = CountGeneratedEntities();
            var totalSec = sw.Elapsed.TotalSeconds;
            var withinBudget = totalSec <= 3.0;
            var passed = ok && entities >= entityCount && withinBudget;

            var summary = $"30 实体性能基准：实体 {entities}/{entityCount} 个，总耗时 {totalSec:F2}s（≤3s：{(withinBudget ? "达标" : "超标")}）" +
                          $"→ {(passed ? "PASS" : "FAIL")}；帧间隔 avg {frameStats.AverageMs:F1}ms / max {frameStats.MaxMs:F1}ms" +
                          "（max 预期来自 NavMesh 同步烘焙帧，已带「烘焙中」模态提示）";
            CleanupGeneratedRoots();
            return summary;
        }

        /// <summary>
        /// 快照回滚验收（第四周-Day1）：场景级快照 + 全量回滚的自动化验收。
        /// 入口守卫与异常兜底同 RunSceneGenerationTestAsync（菜单与 MCP 共用）。
        /// 验收逻辑：环境参照物（模拟生成前场景内容）→ 指纹 A → 快照 → 生成 → 指纹 B（断言变化且含生成根）
        /// → 回滚 → 指纹 C（断言 == A，即"层级/父子/组件 100% 恢复、无残留"）。
        /// </summary>
        public static async Task<string> RunSnapshotRollbackTestAsync()
        {
            if (IsRunning)
            {
                Debug.LogWarning("[联调] 已有联调任务执行中，忽略本次触发");
                return "忽略：已有联调任务执行中";
            }
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[联调] 播放模式中禁止执行联调测试，请先停止播放");
                return "忽略：播放模式中禁止执行";
            }
            IsRunning = true;
            try
            {
                LastResult = await RunSnapshotRollbackTestCore();
                Debug.Log($"[联调] {LastResult}");
                return LastResult;
            }
            catch (Exception ex)
            {
                LastResult = $"执行异常：{ex.Message}";
                Debug.LogError($"[联调] {LastResult}\n{ex.StackTrace}");
                return LastResult;
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary> 快照回滚验收核心（无守卫，仅被守卫入口调用） </summary>
        private static async Task<string> RunSnapshotRollbackTestCore()
        {
            var snapshot = SceneSnapshotManager.Instance;
            var log = new CollectingLogger();

            // 1. 场景准备：清理历史生成根 + 摆放环境参照物（模拟生成前用户场景内容，回滚必须原样保留）
            CleanupGeneratedRoots();
            var env = new GameObject("Env_Reference_Tree");
            var rock = new GameObject("Env_Reference_Rock");
            rock.transform.SetParent(env.transform);
            rock.AddComponent<Rigidbody>(); // 组件状态差异点：回滚后必须保留
            var fpBefore = SceneFingerprint.Compute();

            // 2. 创建快照（生成前）
            var created = snapshot.CreateSnapshot();

            // 3. 生成（隔离链路：0ms 确定性数据 + 真实构建）
            var chain = CreateIsolatedChain(delayMs: 0, propCount: 5);
            chain.scheduler.SetLogger(log);
            await chain.scheduler.StartGenerationAsync(new GenerationRequest
            {
                Prompt = "快照回滚验收：5 个实体生成后应整体回滚",
                TemplateId = "战斗关卡",
                RandomSeed = 20260831
            });
            var genOk = chain.scheduler.CurrentState == GenerationTaskState.Success;

            // 4. 生成后场景必须变化（指纹不同 + 出现 [AI Generated] 生成根）
            var fpAfterGen = SceneFingerprint.Compute();
            var changedByGen = fpAfterGen != fpBefore;
            var hasGenRoot = fpAfterGen.Contains("[AI Generated]");

            // 5. 全量回滚（OpenScene 原子还原 + NavMesh 重烘焙 + 临时文件清理）
            var rolled = snapshot.RollbackToSnapshot();

            // 6. 回滚后场景必须 100% 恢复（指纹一致 = 层级/父子关系/组件状态完全还原，无残留）
            var fpAfterRollback = SceneFingerprint.Compute();
            var restored = fpAfterRollback == fpBefore;

            // 7. 清理参照物（回滚后是新实例，按名字重新查找销毁）
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                if (go.name == "Env_Reference_Tree" || go.name == "Env_Reference_Rock")
                    UnityEngine.Object.DestroyImmediate(go);
            }

            var passed = created && genOk && changedByGen && hasGenRoot && rolled && restored;
            var summary = $"快照回滚验收：快照创建 {(created ? "✓" : "✗")} | 生成成功 {(genOk ? "✓" : "✗")} | " +
                          $"生成改变场景 {(changedByGen ? "✓" : "✗")} | 生成根出现 {(hasGenRoot ? "✓" : "✗")} | " +
                          $"回滚执行 {(rolled ? "✓" : "✗")} | 场景100%恢复(指纹一致) {(restored ? "✓" : "✗")} → {(passed ? "PASS" : "FAIL")}";
            return summary;
        }

        /// <summary>
        /// 构造隔离链路：MockGenerator + 调度器 + 真实场景构建器（资源映射/回滚/组件绑定/NavMesh 烘焙均从资产加载）。
        /// 配置资产缺失时相应环节降级（与 GeneratorServiceInitializer 语义一致），链路本身不失败。
        /// </summary>
        private static (GeneratorScheduler scheduler, RollbackManager rollback) CreateIsolatedChain(int delayMs, int propCount)
        {
            var mappingConfig = AssetDatabase.LoadAssetAtPath<PrefabMappingConfig>("Assets/Settings/Mappings/PrefabMapping_Default.asset");
            IResourceMapper mapper = mappingConfig != null ? new ResourceMappingManager(mappingConfig) : null;

            var bindingConfig = AssetDatabase.LoadAssetAtPath<ComponentBindingConfig>("Assets/Settings/Bindings/ComponentBinding_Default.asset");
            var binder = new ComponentBinder(bindingConfig);

            var rollback = new RollbackManager();
            var builder = new SceneLevelBuilder(mapper, rollback, binder, new NavMeshBaker());

            var scheduler = new GeneratorScheduler(new MockGenerator(delayMs, propCount));
            scheduler.SetBuilder(builder);
            return (scheduler, rollback);
        }

        /// <summary> 清理场景中全部 "[AI Generated]" 根物体（上一轮残留/历史遗留，保证每轮干净起点） </summary>
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

        /// <summary> 构建期间帧间隔采样器：EditorApplication.update 逐帧记录间隔，停止后输出统计 </summary>
        private class FrameStats
        {
            private readonly List<float> _deltas = new();
            private float _lastTime;
            private EditorApplication.CallbackFunction _callback;

            public float AverageMs => _deltas.Count == 0 ? 0f : _deltas.Average() * 1000f;
            public float MaxMs => _deltas.Count == 0 ? 0f : _deltas.Max() * 1000f;

            public void Start()
            {
                _lastTime = (float)EditorApplication.timeSinceStartup;
                _deltas.Clear();
                _callback = () =>
                {
                    var now = (float)EditorApplication.timeSinceStartup;
                    _deltas.Add(now - _lastTime);
                    _lastTime = now;
                };
                EditorApplication.update += _callback;
            }

            public void Stop()
            {
                if (_callback != null)
                {
                    EditorApplication.update -= _callback;
                    _callback = null;
                }
            }
        }

        /// <summary> 链路内日志收集器（调度器/构建器日志入库，供失败轮次溯源）。
        /// 注意：必须全限定名——Runtime.Interfaces.ILogger 与 UnityEngine.ILogger 同名歧义（CLAUDE.md 已知坑） </summary>
        private class CollectingLogger : AILevelGenerator.Runtime.Interfaces.ILogger
        {
            public readonly List<string> Messages = new();

            public void Log(string message) => Messages.Add("[INFO] " + message);
            public void LogWarning(string message) => Messages.Add("[WARN] " + message);
            public void LogError(string message) => Messages.Add("[ERROR] " + message);
            public void LogSuccess(string message) => Messages.Add("[SUCCESS] " + message);
            public void Clear() => Messages.Clear();
            public event Action<string, LogLevel> OnLogReceived;
        }
    }
}
