namespace AILevelGenerator.Runtime.Diagnostics
{
    /// <summary>
    /// 错误码定义（目录条目）：Code 为唯一键，携带分类/严重级/含义摘要/解决建议。
    /// - Summary：一句话说明错误码含义（报告概览与文档用）；
    /// - Hint：解决建议——「所有错误有明确提示与定位」验收的核心，
    ///   错误发生时除消息与字段路径外，还给出可执行的解决建议。
    /// </summary>
    public class ErrorDefinition
    {
        /// <summary> 错误码（ErrorCodes 常量，目录唯一键） </summary>
        public string Code;

        /// <summary> 错误分类（分组展示/报告统计维度） </summary>
        public ErrorCategory Category;

        /// <summary> 严重级：Error 阻断 / Warning 仅提示 </summary>
        public ErrorSeverity Severity;

        /// <summary> 含义摘要（一句话说明触发条件） </summary>
        public string Summary;

        /// <summary> 解决建议（错误出现后怎么办） </summary>
        public string Hint;
    }
}
