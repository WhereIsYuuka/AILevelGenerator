using System;
using System.Linq;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;

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

        public GeneratorScheduler(IGenerator generator)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        }

        public GenerationTaskState CurrentState => _stateMachine.CurrentState;

        /// <summary> 生成中即为忙碌，此时拒绝新的生成请求 </summary>
        public bool IsBusy => CurrentState == GenerationTaskState.Generating;

        public event Action<GenerationTaskState> StateChanged
        {
            add => _stateMachine.StateChanged += value;
            remove => _stateMachine.StateChanged -= value;
        }

        public void SetLogger(ILogger logger) => _logger = logger;

        /// <summary>
        /// 注入场景构建器（可选）：注入后生成成功会自动分帧构建场景，构建成功才算整条任务成功；
        /// 未注入时保持纯生成链路行为（向后兼容，测试与降级场景）。
        /// </summary>
        public void SetBuilder(ILevelBuilder builder) => _builder = builder;

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
            if (request == null || string.IsNullOrWhiteSpace(request.Prompt))
            {
                _logger?.LogError("生成请求为空或缺少描述，已取消");
                return; // 停留在 Ready：调用方 bug 不走状态流转，保持流转表最小
            }
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
                    _logger?.LogWarning("生成取消：构建被中止，本次新增物体已清理");
                else
                    _logger?.LogError($"生成失败：场景构建失败 - {buildResult?.ErrorMessage ?? "未知错误"}");
            }
            catch (Exception ex)
            {
                if (CurrentState != GenerationTaskState.Generating) return;
                _stateMachine.TryTransit(GenerationTaskState.Failed);
                _logger?.LogError($"生成失败：场景构建异常 - {ex.Message}");
            }
        }
    }
}
