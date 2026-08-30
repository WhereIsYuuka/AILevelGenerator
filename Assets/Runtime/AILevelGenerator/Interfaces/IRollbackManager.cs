using UnityEngine;

namespace AILevelGenerator.Runtime.Interfaces
{
    /// <summary>
    /// 回滚管理器接口（Week3-Day3 定义，实现体第四周完善）：
    /// 统一"删除本次生成物体"的入口 —— 调度器/构建器只依赖本接口，不直接 DestroyImmediate。
    /// 第三周最小实现：增量删除（分帧销毁生成根物体，场景原有内容完全保留，不涉及快照）；
    /// 第四周扩展：场景快照保存/恢复作为全量回滚兜底（实现体在 RollbackManager 内补充）。
    /// </summary>
    public interface IRollbackManager
    {
        /// <summary> 分帧删除是否进行中（UI 可据此提示"正在清理"） </summary>
        bool IsRollingBack { get; }

        /// <summary> 登记一次生成产生的根物体（后续增量删除/回滚的追踪依据，自动去重） </summary>
        void TrackRoot(GameObject root);

        /// <summary>
        /// 增量取消：仅删除最近一次生成登记的根物体（分帧执行，不阻塞编辑器）。
        /// 无登记时为安全空操作；删除进行中重复调用被忽略。
        /// </summary>
        void RollbackLastGeneration();

        /// <summary> 清理全部登记（会话重置/生成器重载；第四周扩展为快照级回滚兜底） </summary>
        void ClearAllTracked();
    }
}
