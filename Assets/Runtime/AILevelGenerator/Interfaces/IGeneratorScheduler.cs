using System;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Scheduling;

namespace AILevelGenerator.Runtime.Interfaces
{
    /// <summary>
    /// 生成调度器接口 对外暴露状态与状态变更事件，屏蔽内部状态机/生成器实现细节
    /// 窗口等 UI 层只依赖此接口，通过 ServiceLocator 获取实例
    /// </summary>
    public interface IGeneratorScheduler
    {
        /// <summary> 当前生成任务状态 </summary>
        GenerationTaskState CurrentState { get; }

        /// <summary> 是否有生成任务进行中（CurrentState == Generating），此时拒绝新的生成请求 </summary>
        bool IsBusy { get; }

        /// <summary> 状态变更事件（参数为新状态），UI 层订阅用于刷新展示 </summary>
        event Action<GenerationTaskState> StateChanged;

        /// <summary> 注入日志宿主（窗口实现 ILogger）；未注入时调度器静默运行 </summary>
        void SetLogger(ILogger logger);

        /// <summary>
        /// 启动一次生成任务。
        /// 内部捕获全部异常并转为 Failed 状态，返回的 Task 永不清零（never fault），可安全 fire-and-forget。
        /// </summary>
        Task StartGenerationAsync(GenerationRequest request);
    }
}
