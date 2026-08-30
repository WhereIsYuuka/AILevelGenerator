namespace AILevelGenerator.Runtime.Utilities
{
    /// <summary>
    /// NavMesh 烘焙状态（Week3-Day5）：Ready → Baking → Completed / Failed。
    /// 纯逻辑状态记录（可单测）；实际烘焙执行在编辑器侧 NavMeshBaker。
    /// 状态文案同时用于「烘焙中」提示（DisplayProgressBar）与日志输出——用户可感知，不误以为卡死。
    /// </summary>
    public enum NavMeshBakeState
    {
        /// <summary> 初始状态：尚未发起烘焙 </summary>
        Ready,

        /// <summary> 烘焙中（同步阻塞执行前的提示状态） </summary>
        Baking,

        /// <summary> 烘焙完成（含成功注册到运行时 NavMesh 数据） </summary>
        Completed,

        /// <summary> 烘焙失败（未收集到几何/范围无效/异常），不阻塞生成流程 </summary>
        Failed
    }

    /// <summary>
    /// 烘焙状态追踪器（纯逻辑，可单测）：记录状态流转与当前状态文案。
    /// 构建器在烘焙前后调用 BeginBaking/Complete/Fail，日志与进度条文案统一取自这里。
    /// </summary>
    public class NavMeshBakeTracker
    {
        public NavMeshBakeState State { get; private set; } = NavMeshBakeState.Ready;

        /// <summary> 当前状态的用户可见文案（中文，直接进日志/进度条） </summary>
        public string Message { get; private set; } = "NavMesh 未烘焙";

        /// <summary> 烘焙成功的几何源数量（Complete 时写入，0 = 本次未烘焙出数据） </summary>
        public int SourceCount { get; private set; }

        /// <summary> 进入「烘焙中」：同步阻塞执行前必须调用（用户可感知的关键提示点） </summary>
        public void BeginBaking()
        {
            State = NavMeshBakeState.Baking;
            Message = "正在烘焙全局 NavMesh…（请稍候，不会卡死）";
        }

        /// <summary> 烘焙完成（成功注册运行时 NavMesh 数据） </summary>
        public void Complete(int sourceCount)
        {
            State = NavMeshBakeState.Completed;
            SourceCount = sourceCount;
            Message = $"NavMesh 烘焙完成（{sourceCount} 个几何源）";
        }

        /// <summary> 烘焙失败：记录失败原因，生成流程不中断（仅告警） </summary>
        public void Fail(string error)
        {
            State = NavMeshBakeState.Failed;
            SourceCount = 0;
            Message = $"NavMesh 烘焙失败：{error}";
        }
    }
}
