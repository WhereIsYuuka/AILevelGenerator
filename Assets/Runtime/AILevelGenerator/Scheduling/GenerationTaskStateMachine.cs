using System;

namespace AILevelGenerator.Runtime.Scheduling
{
    /// <summary>
    /// 生成任务状态机（纯逻辑，不依赖日志/UI，可独立单元测试）。
    /// 流转表：Ready→Generating→Success|Failed→Ready，其余流转一律拒绝。
    /// </summary>
    public class GenerationTaskStateMachine
    {
        /// <summary> 当前状态（初始为准备） </summary>
        public GenerationTaskState CurrentState { get; private set; } = GenerationTaskState.Ready;

        /// <summary> 状态变更事件（参数为新状态） </summary>
        public event Action<GenerationTaskState> StateChanged;

        /// <summary>
        /// 尝试流转到目标状态。
        /// 合法 → 置位 + 触发事件 + 返回 true；非法 → 状态不变、不发事件、返回 false。
        /// </summary>
        public bool TryTransit(GenerationTaskState next)
        {
            if (!IsValidTransition(CurrentState, next)) return false;

            CurrentState = next;
            StateChanged?.Invoke(next);
            return true;
        }

        private static bool IsValidTransition(GenerationTaskState from, GenerationTaskState to)
        {
            switch (from)
            {
                case GenerationTaskState.Ready:
                    return to == GenerationTaskState.Generating;
                case GenerationTaskState.Generating:
                    return to is GenerationTaskState.Success or GenerationTaskState.Failed;
                case GenerationTaskState.Success:
                case GenerationTaskState.Failed:
                    return to == GenerationTaskState.Ready; // 新一轮生成前重置
                default:
                    return false;
            }
        }
    }
}
