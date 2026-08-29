namespace AILevelGenerator.Runtime.Scheduling
{
    /// <summary>
    /// 生成任务状态机四态。
    /// 合法流转：Ready → Generating → Success|Failed → Ready（新一轮生成前重置）
    /// </summary>
    public enum GenerationTaskState
    {
        /// <summary> 准备（初始状态，可发起生成） </summary>
        Ready,

        /// <summary> 生成中（LLM 调用/数据组装阶段，禁止重复发起） </summary>
        Generating,

        /// <summary> 成功 </summary>
        Success,

        /// <summary> 失败 </summary>
        Failed
    }

    /// <summary> 状态显示名扩展（中文文案，用于日志与 UI） </summary>
    public static class GenerationTaskStateExtensions
    {
        public static string ToDisplayName(this GenerationTaskState state)
        {
            return state switch
            {
                GenerationTaskState.Ready => "准备",
                GenerationTaskState.Generating => "生成中",
                GenerationTaskState.Success => "成功",
                GenerationTaskState.Failed => "失败",
                _ => state.ToString()
            };
        }
    }
}
