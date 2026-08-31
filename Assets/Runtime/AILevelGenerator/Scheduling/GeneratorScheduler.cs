using System;
using System.Linq;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Validation;

namespace AILevelGenerator.Runtime.Scheduling
{
    /// <summary>
    /// 生成调度器：驱动 状态机 + 生成器 + 日志 的基础异步链路。
    /// 职责：校验请求 → 状态流转 → 异步调用生成器 → 结果判定 → 日志输出。
    /// 注意：所有异常路径均被捕获并转为 Failed 状态，返回的 Task 永不清零，
    /// 调用方（窗口）可安全 fire-and-forget；禁止在本类任何位置使用 .Result/.Wait()（Editor 同步上下文下必死锁）。
    /// </summary>
    public class GeneratorScheduler : IGeneratorScheduler
    {
        private readonly IGenerator _generator;
        private readonly GenerationTaskStateMachine _stateMachine = new();
        private ILogger _logger;
        private ILevelBuilder _builder; // Day1：生成成功后分帧构建场景（未注入时行为与纯生成链路一致）
        private ValidatorRegistry _validatorRegistry; // Day2：可插拔校验器注册表（未注入时保持旧链路行为）
        private ISceneSnapshotManager _snapshotManager; // Day2：场景级快照（校验失败清理 / 构建失败自动全量回滚）

        /// <summary> 取消标记（Day3）：LLM 生成阶段取消后置位，结果返回时据此丢弃不进入构建 </summary>
        private volatile bool _cancelRequested;

        public GeneratorScheduler(IGenerator generator, ValidatorRegistry validatorRegistry = null)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _validatorRegistry = validatorRegistry;
        }

        public GenerationTaskState CurrentState => _stateMachine.CurrentState;

        /// <summary> 生成中即为忙碌，此时拒绝新的生成请求 </summary>
        public bool IsBusy => CurrentState == GenerationTaskState.Generating;

        /// <summary>
        /// 强制复位到 Ready（第四周-Day1：场景级回滚后重置状态机，事件链驱动窗口 UI 复位）。
        /// 调用方须保证生成/构建协程已结束（回滚入口已做 IsBusy 校验）。
        /// </summary>
        public void ResetToReady() => _stateMachine.ForceReset();

        public event Action<GenerationTaskState> StateChanged
        {
            add => _stateMachine.StateChanged += value;
            remove => _stateMachine.StateChanged -= value;
        }

        public void SetLogger(ILogger logger)
        {
            _logger = logger;
            _validatorRegistry?.SetLogger(logger); // 日志宿主同步转发给已注册校验器（窗口即宿主）
        }

        /// <summary>
        /// 注入场景快照服务（Day2，可选）：前置校验失败自动清理快照；构建失败（场景已污染）自动全量回滚。
        /// 未注入时校验失败仅拦截不打快照，构建失败保持旧行为（仅增量清理 + Failed）。
        /// </summary>
        public void SetSnapshotManager(ISceneSnapshotManager snapshotManager) => _snapshotManager = snapshotManager;

        /// <summary>
        /// 注入场景构建器（可选）：注入后生成成功会自动分帧构建场景，构建成功才算整条任务成功；
        /// 未注入时保持纯生成链路行为（向后兼容，测试与降级场景）。
        /// </summary>
        public void SetBuilder(ILevelBuilder builder) => _builder = builder;

        /// <summary>
        /// 请求取消当前任务（Day3）：
        /// 构建阶段 → 转发构建器 Cancel（帧头检查后经 IRollbackManager 分帧清理本次物体）；
        /// 生成阶段 → 置标记，LLM 结果返回后被丢弃（不进入构建、场景无变更）。
        /// </summary>
        public void CancelGeneration()
        {
            if (CurrentState != GenerationTaskState.Generating)
            {
                _logger?.LogWarning("当前无进行中的生成任务，忽略取消请求");
                return;
            }
            _cancelRequested = true;
            _builder?.Cancel(); // 构建中：由构建器帧头检查；未构建时置标记无副作用
            _logger?.LogWarning("已请求取消：正在终止当前生成/构建...");
        }

        public async Task StartGenerationAsync(GenerationRequest request)
        {
            // —— 同步前缀：非抛出式校验（异常不入 catch，语义清晰）——
            // async 方法中首个 await 之前的代码在调用线程同步执行，
            // 因此 IsBusy 检查与 Generating 置位均发生在点击处理内，编辑器单线程下无竞态。
            if (IsBusy)
            {
                _logger?.LogWarning("已有生成任务进行中，忽略本次请求");
                return;
            }
            if (request == null)
            {
                _logger?.LogError("生成请求为空或缺少描述，已取消");
                return; // 停留在 Ready：调用方 bug 不走状态流转，保持流转表最小
            }
            // Day2 请求级前置校验（输入合法性）：非法输入 100% 拦截，不进入生成链路；
            // 窗口已创建场景快照（零变更），校验失败自动丢弃，避免陈旧快照误导「回滚到快照」按钮。
            // 无注册表时保留旧内联检查（行为等价，现有测试路径）。
            if (_validatorRegistry != null)
            {
                var validation = _validatorRegistry.Run(ValidationStage.Pre, request, request.TemplateId);
                if (!validation.IsValid)
                {
                    LogValidationErrors("前置校验失败，已拦截本次生成", validation);
                    _snapshotManager?.DiscardSnapshot();
                    return; // 不进入状态流转（停留当前状态，调用方可修改输入后重试）
                }
            }
            else if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                _logger?.LogError("生成请求为空或缺少描述，已取消");
                return; // 停留在 Ready：调用方 bug 不走状态流转，保持流转表最小
            }
            _cancelRequested = false; // 新一轮任务：重置取消标记（防止上轮取消污染本轮）
            if (CurrentState is GenerationTaskState.Success or GenerationTaskState.Failed)
                _stateMachine.TryTransit(GenerationTaskState.Ready); // 新一轮生成前重置

            // —— 异步主体：全部异常路径均捕获，返回的 Task 永不清零 ——
            GenerationResult result = null;
            try
            {
                _stateMachine.TryTransit(GenerationTaskState.Generating);
                _logger?.Log($"状态流转：{GenerationTaskState.Ready.ToDisplayName()} → {GenerationTaskState.Generating.ToDisplayName()}（{request.Prompt}）");
                result = await _generator.GenerateAsync(request); // 唯一真实的异步点
            }
            catch (Exception ex)
            {
                _logger?.LogError($"生成异常：{ex.Message}");
                // 先查状态再流转：防止 Ready→Failed 非法流转被状态机拒绝后状态卡死
                if (CurrentState == GenerationTaskState.Generating)
                    _stateMachine.TryTransit(GenerationTaskState.Failed);
                return;
            }

            // 生成阶段取消（Day3）：LLM 结果返回但用户已取消 → 丢弃结果，不进入构建，场景无任何变更
            if (_cancelRequested)
            {
                if (CurrentState == GenerationTaskState.Generating)
                    _stateMachine.TryTransit(GenerationTaskState.Failed);
                _snapshotManager?.DiscardSnapshot(); // Day2：快照只存在于生成-构建生命周期，取消即失效
                _logger?.LogWarning("生成已取消：LLM 结果已丢弃，场景未发生任何变更");
                return;
            }

            // Day2 数据级前置校验（LLM 返回后、构建前）：资源存在性 + 数值边界 + 模板范围。
            // 校验失败 → 错误合并进 result 自然落 else 分支转 Failed；场景零变更（未构建），仅丢弃快照不做 OpenScene 回滚。
            if (_validatorRegistry != null && result != null && result.Success && (result.Errors == null || result.Errors.Count == 0))
            {
                if (result.LevelData == null)
                {
                    result.Errors.Add(new ValidationError { Code = "DATA_NULL", Message = "生成结果缺少关卡数据（LevelData 为空）" });
                }
                else
                {
                    var validation = _validatorRegistry.Run(ValidationStage.Pre, result.LevelData, request.TemplateId);
                    if (!validation.IsValid)
                    {
                        result.Errors.AddRange(validation.Errors);
                        result.Warnings.AddRange(validation.Warnings);
                        LogValidationErrors("数据校验失败", validation);
                        _snapshotManager?.DiscardSnapshot();
                    }
                }
            }

            if (result != null && result.Success && (result.Errors == null || result.Errors.Count == 0))
            {
                if (_builder == null)
                {
                    // 未注入构建器：保持纯生成链路行为
                    _stateMachine.TryTransit(GenerationTaskState.Success);
                    _logger?.LogSuccess($"生成成功：{result.LevelData?.LevelName ?? "无名称"}，耗时 {result.GenerationTime:F1}s");
                }
                else
                {
                    await BuildSceneAndTransit(result); // 构建纳入 Generating 状态，完成才 Success
                }
            }
            else
            {
                _stateMachine.TryTransit(GenerationTaskState.Failed);
                var summary = result?.Errors != null && result.Errors.Count > 0
                    ? string.Join("；", result.Errors.Select(e => e.Message))
                    : "生成器未返回成功结果";
                _logger?.LogError($"生成失败：{summary}");
            }
        }

        /// <summary>
        /// 分帧构建场景（Day1，纳入 Generating 状态）：构建成功 → Success；
        /// 构建失败/取消/异常 → Failed（已实例化物体的增量清理由构建器负责，Day3 起经 IRollbackManager 统一执行）。
        /// </summary>
        private async Task BuildSceneAndTransit(GenerationResult result)
        {
            try
            {
                var buildResult = await _builder.BuildAsync(result.LevelData);
                if (buildResult != null && buildResult.IsSuccess)
                {
                    _stateMachine.TryTransit(GenerationTaskState.Success);
                    var layoutInfo = buildResult.OverlapRatio > 0f
                        ? $"，重叠修正 {buildResult.ResolvedOverlapPairs} 对（残留重叠率 {buildResult.OverlapRatio:P1}）"
                        : "";
                    _logger?.LogSuccess($"生成成功：{result.LevelData?.LevelName ?? "无名称"}（构建 {buildResult.InstantiatedCount} 个实体，生成 {result.GenerationTime:F1}s + 构建 {buildResult.BuildTime:F1}s{layoutInfo}）");
                    return;
                }

                // 先查状态再流转：防止非法流转被状态机拒绝后状态卡死
                if (CurrentState != GenerationTaskState.Generating) return;
                _stateMachine.TryTransit(GenerationTaskState.Failed);
                if (buildResult?.Status == LevelBuildStatus.Cancelled)
                {
                    _logger?.LogWarning("生成取消：构建被中止，本次新增物体已清理");
                    _snapshotManager?.DiscardSnapshot(); // Day2：用户主动取消 ≠ 失败，仅清理快照不触发全量回滚
                    return;
                }
                _logger?.LogError($"生成失败：场景构建失败 - {buildResult?.ErrorMessage ?? "未知错误"}");
                TryAutoRollback(); // Day2：构建已污染场景 → 自动全量回滚兜底
            }
            catch (Exception ex)
            {
                if (CurrentState != GenerationTaskState.Generating) return;
                _stateMachine.TryTransit(GenerationTaskState.Failed);
                _logger?.LogError($"生成失败：场景构建异常 - {ex.Message}");
                TryAutoRollback(); // Day2：构建异常同样视为场景已污染 → 自动全量回滚兜底
            }
        }

        /// <summary>
        /// 污染感知自动回滚（Day2，用户拍板策略）：
        /// 构建中/后失败（场景已被本次生成污染）→ 有快照则 RollbackToSnapshot 全量原子还原，
        /// 成功则 ResetToReady（状态机复位，事件链驱动窗口 UI），失败则保持 Failed 并提示人工处理。
        /// 构建前失败/取消路径不调用本方法（场景零变更，仅 DiscardSnapshot）。
        /// </summary>
        private void TryAutoRollback()
        {
            if (_snapshotManager == null || !_snapshotManager.HasSnapshot) return; // 无快照：保持 Failed，增量清理已兜底
            if (_snapshotManager.RollbackToSnapshot())
            {
                ResetToReady();
                _logger?.LogWarning("已自动回滚：构建失败后场景已恢复至生成前快照");
            }
            else
            {
                _logger?.LogError("自动回滚失败：场景可能残留本次生成物体，请检查 Console 日志或手动回滚");
            }
        }

        /// <summary>
        /// 统一校验错误日志格式（Day2）：`code：message（dataPath）` 逐条列出，多条以「；」连接，
        /// 与现有失败汇总风格一致，错误信息清晰明确、定位到具体字段（验收标准）。
        /// </summary>
        private void LogValidationErrors(string prefix, ValidationResult result)
        {
            if (result == null) return;
            var joined = string.Join("；", result.Errors.Select(e =>
                $"{e.Code}：{e.Message}{(string.IsNullOrEmpty(e.DataPath) ? "" : $"（{e.DataPath}）")}"));
            _logger?.LogError($"{prefix}（{result.Errors.Count} 项）：{joined}");
        }
    }
}
