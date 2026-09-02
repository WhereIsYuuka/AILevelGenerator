using System;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Diagnostics;
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

        /// <summary>
        /// 生成报告事件（第四周-Day5）：一次生成任务在终态（成功/失败/取消/异常）统一触发一次，
        /// 参数为本次任务的结构化报告（请求摘要/耗时/内容统计/构建摘要/校验问题清单/回滚信息）。
        /// 窗口订阅后渲染报告块并落盘归档；请求被前置校验拦截（未进入任务）时不触发。
        /// </summary>
        event Action<GenerationReport> GenerationCompleted;

        /// <summary> 注入日志宿主（窗口实现 ILogger）；未注入时调度器静默运行 </summary>
        void SetLogger(ILogger logger);

        /// <summary>
        /// 启动一次生成任务。
        /// 内部捕获全部异常并转为 Failed 状态，返回的 Task 永不清零（never fault），可安全 fire-and-forget。
        /// </summary>
        Task StartGenerationAsync(GenerationRequest request);

        /// <summary>
        /// 请求取消当前生成任务（Day3，UI 取消按钮入口）：
        /// 构建阶段 → 转发构建器 Cancel（分帧删除本次已生成物体，经 IRollbackManager）；
        /// LLM 生成阶段 → 置取消标记，结果返回后丢弃、不进入构建。
        /// 无进行中任务时为安全空操作（打提示日志）。
        /// </summary>
        void CancelGeneration();

        /// <summary>
        /// 强制复位到 Ready（第四周-Day1：场景级回滚后重置状态机，事件链驱动窗口 UI 复位）。
        /// 调用方须保证生成/构建协程已结束（回滚入口已做 IsBusy 校验）。
        /// </summary>
        void ResetToReady();
    }
}
