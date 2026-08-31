using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Components;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Utilities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AILevelGenerator.Editor.Builders
{
    /// <summary>
    /// 场景构建器：把生成结果 LevelData 分帧实例化到当前场景。
    /// 生命周期：Idle → Preparing(建根) → Instantiating(分帧) → Succeeded / Cancelled / Failed → Idle
    /// 分帧底层：EditorCoroutine（EditorApplication.update 驱动）+ FrameBudgetCalculator（帧率自适应每帧预算）。
    /// 根物体管理：全部实例挂在 "[AI Generated] &lt;LevelName&gt;" 根下，便于增量取消（Day3 起经 IRollbackManager
    /// 统一执行删除）与场景层级识别。
    /// 资源解耦：构造注入 IResourceMapper（逻辑名 → 预制体）；未命中映射的 Prop 记日志跳过，不中断整轮构建。
    /// 组件绑定（Day4）：实例化后同帧经 ComponentBinder 按逻辑名挂载逻辑组件（分帧节奏内完成，不额外占帧）。
    /// </summary>
    public class SceneLevelBuilder : ILevelBuilder
    {
        private readonly IResourceMapper _resourceMapper;
        private readonly IRollbackManager _rollbackManager; // Day3：取消清理统一经回滚管理器（分帧删除），null 退回自删
        private readonly ComponentBinder _componentBinder; // Day4：实例化后自动挂载逻辑组件，null = 不绑定（向后兼容）
        private readonly NavMeshBaker _navMeshBaker; // Day5：收尾同步烘焙全局 NavMesh，null = 不烘焙（向后兼容）
        private readonly NavMeshBakeTracker _bakeTracker = new(); // Day5：烘焙状态与文案（日志/进度提示）

        private LevelBuildOptions _options;
        private FrameBudgetCalculator _budget;
        private TaskCompletionSource<LevelBuildResult> _tcs;
        private EditorCoroutine _coroutine;
        private GameObject _root;
        private bool _cancelRequested;
        private int _instantiatedCount;
        private int _skippedCount;
        private int _groundFittedCount;
        private int _resolvedOverlapPairs;
        private float _overlapRatio;
        private int _boundComponents;      // Day4：成功挂载的组件数
        private int _bindFailedComponents; // Day4：绑定失败数（类型找不到/添加/装配异常）
        private float _startTime;

        /// <summary> 已实例化物体（供 Day2 布局阶段重叠检测/分离） </summary>
        private readonly List<GameObject> _instances = new();

        /// <summary> 布局修正最大迭代轮数（每轮一帧），防止全重叠极端布局无限循环 </summary>
        private const int MaxLayoutRounds = 10;

        /// <param name="rollbackManager">回滚管理器（可选）：取消/失败时经其分帧删除本次生成根；null 退回同步自删（向后兼容）</param>
        /// <param name="componentBinder">组件绑定器（可选，Day4）：实例化后按逻辑名挂载逻辑组件；null = 不绑定（向后兼容）</param>
        /// <param name="navMeshBaker">NavMesh 烘焙器（可选，Day5）：构建收尾同步烘焙全局 NavMesh；null = 不烘焙（向后兼容）</param>
        public SceneLevelBuilder(IResourceMapper resourceMapper, IRollbackManager rollbackManager = null,
            ComponentBinder componentBinder = null, NavMeshBaker navMeshBaker = null)
        {
            _resourceMapper = resourceMapper;
            _rollbackManager = rollbackManager;
            _componentBinder = componentBinder;
            _navMeshBaker = navMeshBaker;
        }

        public bool IsBuilding => _tcs != null && !_tcs.Task.IsCompleted;

        /// <summary> 构建进度事件（0~1），UI 层订阅用于进度条显示（Day3） </summary>
        public event Action<float> ProgressChanged;

        public Task<LevelBuildResult> BuildAsync(LevelData levelData, LevelBuildOptions options = null)
        {
            // 同步前缀：非抛出式校验（async 方法首个 await 前的代码在调用线程同步执行，无竞态）
            if (IsBuilding)
                return Task.FromResult(LevelBuildResult.Failed("已有构建任务进行中"));
            if (levelData == null)
                return Task.FromResult(LevelBuildResult.Failed("关卡数据为空"));

            _options = options ?? new LevelBuildOptions();
            _budget = new FrameBudgetCalculator(_options.BudgetWindowSize, _options.TargetFrameTimeMs,
                _options.BasePerFrame, _options.MinPerFrame, _options.MaxPerFrame);
            _tcs = new TaskCompletionSource<LevelBuildResult>();
            _cancelRequested = false;
            _instantiatedCount = 0;
            _skippedCount = 0;
            _groundFittedCount = 0;
            _resolvedOverlapPairs = 0;
            _overlapRatio = 0f;
            _boundComponents = 0;
            _bindFailedComponents = 0;
            _instances.Clear();
            _startTime = (float)EditorApplication.timeSinceStartup;

            _coroutine = EditorCoroutine.Start(BuildRoutine(levelData));
            if (_coroutine == null)
            {
                _tcs = null;
                return Task.FromResult(LevelBuildResult.Failed("EditorCoroutine 启动失败"));
            }
            return _tcs.Task;
        }

        /// <summary> 请求取消当前构建：帧头检查后删除本次根物体并返回 Cancelled 结果（增量取消雏形） </summary>
        public void Cancel()
        {
            _cancelRequested = true;
        }

        /// <summary>
        /// 构建主协程（Day6 异常兜底）。C# 限制：try+catch 块内不能 yield（CS1626），
        /// 故采用"驱动循环"模式——手动推进子协程，异常在驱动层捕获（catch 不包裹任何 yield）。
        /// 兜底必要性：EditorCoroutine 捕获协程异常只 LogError 并停止，不会调用 Finish——
        /// 若不在此收尾，_tcs 永不清零，调度器 await 永不完成，状态机卡死在 Generating（按钮永久禁用）。
        /// </summary>
        private IEnumerator BuildRoutine(LevelData levelData)
        {
            var routine = BuildRoutineCore(levelData);
            while (true)
            {
                bool moveNext;
                try
                {
                    moveNext = routine.MoveNext();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AI Generator] 构建阶段异常：{ex.Message}\n{ex.StackTrace}");
                    // 异常时同样执行增量清理（本次生成根），结果按 Failed 返回，任务正常终结
                    try { RollbackCurrentRoot(); }
                    catch (Exception rollbackEx) { Debug.LogWarning($"[AI Generator] 异常后清理失败：{rollbackEx.Message}"); }
                    Finish(LevelBuildResult.Failed($"构建阶段异常：{ex.Message}", _instantiatedCount));
                    yield break;
                }
                if (!moveNext) yield break;
                yield return routine.Current;
            }
        }

        private IEnumerator BuildRoutineCore(LevelData levelData)
        {
            // —— 阶段 1：Prepare（创建生成根物体） ——
            var rootName = _options.RootNamePrefix + (string.IsNullOrEmpty(levelData.LevelName) ? "Level" : levelData.LevelName);
            _root = new GameObject(rootName);
            _rollbackManager?.TrackRoot(_root); // 登记本次生成根：取消/失败时经回滚管理器增量删除
            ProgressChanged?.Invoke(0f);

            // —— 阶段 2：Instantiating（分帧 + 帧率自适应） ——
            var props = levelData.Props ?? new List<PropPlacement>();
            var total = props.Count;
            var index = 0;
            var lastFrameTime = EditorApplication.timeSinceStartup;
            while (index < total)
            {
                // 帧头：实测本帧间隔 → 滑动平均 → 预算（帧率自适应核心）
                var now = EditorApplication.timeSinceStartup;
                _budget.RecordDeltaTime((float)(now - lastFrameTime));
                lastFrameTime = now;
                var budget = _budget.GetBudgetPerFrame();

                var frameDone = 0;
                while (index < total && frameDone < budget)
                {
                    InstantiateOne(props[index]);
                    index++;
                    frameDone++;
                    if (_cancelRequested) break;
                }

                // 帧末：进度事件（每帧最多一次，避免逐物体刷新 UI；实例化占整体 0~80%）。
                // Day6 边界：total==0（空实体请求）时 0/0 为 NaN，进度条/日志会收到非法值——跳过
                if (total > 0)
                    ProgressChanged?.Invoke(index / (float)total * 0.8f);

                if (_cancelRequested) break;
                if (index < total) yield return null; // 未完成 → 下一帧继续
            }

            if (_cancelRequested)
            {
                RollbackCurrentRoot(); // 增量取消：经 IRollbackManager 分帧删除本次生成的根物体
                Finish(LevelBuildResult.Cancelled(_instantiatedCount));
                yield break;
            }

            // —— 阶段 3（Day2）：Layout —— 分帧重叠检测与自动修正 ——
            // 需要全部物体在场才能正确检测（分帧实例化时后面的物体尚不存在），
            // 故作为独立阶段在实例化完成后执行；每轮一帧（yield null），不阻塞编辑器。
            // 顺序：先水平分离重叠（此时物体可能仍浮空，但分离只动 x/z），再统一地面贴合——
            // 若先贴合，重叠的物体互相挡住地面射线会"叠罗汉"（射线排除逻辑见 SceneLayoutProcessor）。
            // 注：EditMode 下动态创建且未被任何物理查询触碰过的 Collider 未注册进物理场景，
            // OverlapSphere 会查不到（全 miss）——先 SyncTransforms 触发注册（粗筛仍带纯几何兜底）。
            Physics.SyncTransforms();
            var rounds = 0;
            while (rounds < MaxLayoutRounds)
            {
                if (_cancelRequested)
                {
                    RollbackCurrentRoot();
                    Finish(LevelBuildResult.Cancelled(_instantiatedCount));
                    yield break;
                }
                var fixedPairs = SceneLayoutProcessor.ResolveRound(_instances);
                _resolvedOverlapPairs += fixedPairs;
                if (fixedPairs == 0) break; // 已无重叠，收敛
                rounds++;
                ProgressChanged?.Invoke(0.8f + 0.1f * rounds / MaxLayoutRounds); // 布局阶段占 80%~90%
                yield return null; // 每轮一帧：布局修正分帧推进
            }
            _overlapRatio = SceneLayoutProcessor.GetOverlapRatio(_instances);

            // 统一地面贴合（射线级开销，一次完成；无地面/未命中的物体保持原坐标）——占 90%~100%
            for (var k = 0; k < _instances.Count; k++)
            {
                if (SceneLayoutProcessor.FitToGround(_instances[k], _root.transform)) _groundFittedCount++;
                if (k % 10 == 0)
                    ProgressChanged?.Invoke(0.9f + 0.1f * (k + 1) / _instances.Count); // 每 10 个刷新一次，避免逐物体刷 UI
            }
            ProgressChanged?.Invoke(1f);

            // —— 阶段 3.5（Day5）：环境适配 —— 同步烘焙全局 NavMesh（最后一帧，用户可感知）——
            BakeEnvironment();

            // —— 阶段 4：收尾 ——
            var buildTime = (float)(EditorApplication.timeSinceStartup - _startTime);
            Finish(LevelBuildResult.Succeeded(_instantiatedCount, _skippedCount, buildTime,
                _overlapRatio, _resolvedOverlapPairs, _groundFittedCount,
                _boundComponents, _bindFailedComponents));
        }

        /// <summary>
        /// 实例化单个 Prop：未命中资源映射/实例化失败的跳过（记数不中断整轮构建），
        /// 全部实例挂到生成根下，坐标取 PropPlacement 世界坐标。
        /// </summary>
        private void InstantiateOne(PropPlacement prop)
        {
            try
            {
                if (prop == null || string.IsNullOrWhiteSpace(prop.PrefabLogicalName))
                {
                    _skippedCount++;
                    return;
                }
                // Day6 边界：非有限坐标（NaN/Infinity）会让 Transform 进入非法状态，
                // 后续布局/烘焙阶段产生 NaN 传染——直接跳过并记日志，不中断整轮构建
                if (!IsFinite(prop.Position) || !IsFinite(prop.Rotation) || !IsFinite(prop.Scale))
                {
                    _skippedCount++;
                    Debug.LogWarning($"[AI Generator] 位置/旋转/缩放含非有限值，跳过：{prop.PrefabLogicalName}");
                    return;
                }
                if (_resourceMapper == null || !_resourceMapper.TryGetPrefab(prop.PrefabLogicalName, out var prefab) || prefab == null)
                {
                    _skippedCount++;
                    Debug.LogWarning($"[AI Generator] 未命中资源映射，跳过：{prop.PrefabLogicalName}");
                    return;
                }

                var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance == null)
                {
                    _skippedCount++;
                    Debug.LogWarning($"[AI Generator] 预制体实例化失败：{prop.PrefabLogicalName}");
                    return;
                }

                instance.transform.SetParent(_root.transform, true); // worldPositionStays：保持世界坐标
                instance.transform.position = prop.Position;
                instance.transform.rotation = Quaternion.Euler(prop.Rotation);
                instance.transform.localScale = prop.Scale;
                instance.name = prop.PrefabLogicalName; // 层级按逻辑名显示，便于策划识别与后续绑定

                _instances.Add(instance); // 供布局阶段重叠检测/分离
                _instantiatedCount++;

                // Day4：实例化后同帧绑定逻辑组件（随分帧节奏推进，不额外占帧；单个失败仅日志不阻塞）
                if (_componentBinder != null)
                {
                    var bindResult = _componentBinder.BindTo(prop.PrefabLogicalName, instance);
                    _boundComponents += bindResult.BoundCount;
                    _bindFailedComponents += bindResult.FailedCount;
                }
            }
            catch (Exception ex)
            {
                _skippedCount++;
                Debug.LogWarning($"[AI Generator] 实例化异常，跳过：{prop?.PrefabLogicalName} - {ex.Message}");
            }
        }

        /// <summary>
        /// 环境自动适配（Day5）：收尾同步烘焙全局 NavMesh。
        /// 烘焙为同步阻塞操作（需求指定），阻塞前必须给出「烘焙中」用户提示（模态进度条立即绘制 + 日志），
        /// 避免用户误以为卡死；完成后清理进度条、登记场景脏标记（场景状态同步），并输出结果日志。
        /// 未注入烘焙器时整段跳过（向后兼容）；烘焙失败仅告警，不中断生成流程。
        /// </summary>
        private void BakeEnvironment()
        {
            if (_navMeshBaker == null) return;

            // 「烘焙中」提示：DisplayProgressBar 为立即绘制的模态进度条，同步阻塞期间依然可见；日志同步输出
            _bakeTracker.BeginBaking();
            EditorUtility.DisplayProgressBar("AI 关卡生成", _bakeTracker.Message, 0.97f);
            Debug.Log($"[AI Generator] {_bakeTracker.Message}");

            // 排除本次生成实体（角色不作为 NavMesh 障碍物，保证其 NavMeshAgent 脚下有数据可落地寻路）
            var ok = _navMeshBaker.BakeGlobal(_bakeTracker, _root != null ? _root.transform : null);
            EditorUtility.ClearProgressBar(); // 无论成败都清理模态进度条

            if (ok)
            {
                Debug.Log($"[AI Generator] {_bakeTracker.Message}（同步完成，怪物可被 NavMeshAgent 识别）");
            }
            else
            {
                Debug.LogWarning($"[AI Generator] {_bakeTracker.Message}（不影响场景实例化，仅寻路不可用）");
            }

            // 场景状态同步：烘焙改了场景数据（NavMesh 覆盖范围），标记场景为需要保存
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        /// <summary>
        /// 取消清理：优先经 IRollbackManager 分帧删除本次根（无卡顿）；
        /// 未注入时退回同步删除（旧行为，兼容测试/降级场景）。
        /// </summary>
        private void RollbackCurrentRoot()
        {
            if (_rollbackManager != null)
            {
                _rollbackManager.RollbackLastGeneration(); // 删除最近一次登记的根 = 本次 _root
            }
            else
            {
                CleanupRoot();
            }
            _root = null;
        }

        /// <summary> 坐标/旋转/缩放有限性检查（NaN/Infinity 拒绝）：float.IsFinite 需 .NET Standard 2.1+，Unity 2022+ 可用 </summary>
        private static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        /// <summary> 同步删除本次生成的根物体（含全部子实例）；仅作为未注入回滚管理器时的兜底 </summary>
        private void CleanupRoot()
        {
            if (_root == null) return;
            UnityEngine.Object.DestroyImmediate(_root);
            _root = null;
        }

        private void Finish(LevelBuildResult result)
        {
            _coroutine?.Stop();
            _coroutine = null;
            _tcs.TrySetResult(result);
        }
    }
}
