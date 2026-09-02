namespace AILevelGenerator.Runtime.Diagnostics
{
    /// <summary>
    /// 管线阶段标记（结构化日志分组维度）：定位一条日志发生在生成链路的哪个环节，
    /// 窗口日志面板按阶段分组/检索，报告按阶段统计。
    /// </summary>
    public enum LogStage
    {
        /// <summary> 未指定（一般信息） </summary>
        None,

        /// <summary> 请求/参数阶段（窗口点击、参数收集） </summary>
        Request,

        /// <summary> 校验（前置/数据级/生成中/后置） </summary>
        Validation,

        /// <summary> LLM 生成 </summary>
        Generation,

        /// <summary> 场景构建（分帧实例化/布局/贴合） </summary>
        Build,

        /// <summary> 回滚（增量/全量） </summary>
        Rollback,

        /// <summary> 取消 </summary>
        Cancellation,

        /// <summary> 生成报告 </summary>
        Report
    }
}
