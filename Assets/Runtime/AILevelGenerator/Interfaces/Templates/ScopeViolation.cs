namespace AILevelGenerator.Runtime.Interfaces.Templates
{
    /// <summary>
    /// 模板规模自检条目（第五周-Day4）：模板把"生成结果不符合自身约束"逐条写入此结构，
    /// 由核心框架统一转译消费 —— LLM 生成器转 Warning（生成期提示）、数据级范围校验转 Error（拦截）。
    /// 模板只负责描述规则，不感知消费方的严重级别与调度路径（开闭原则）。
    /// </summary>
    public sealed class ScopeViolation
    {
        /// <summary> 错误码（复用 ErrorCodes 常量，与既有校验体系同码，保证日志/报告归并一致） </summary>
        public string Code;

        /// <summary> 中文提示（含实际值与期望值，与字段语义对齐；需定位时配合 DataPath） </summary>
        public string Message;

        /// <summary> 数据路径（LevelData 字段级定位，如 props/tasks/terrain.width；全局类约束可为空） </summary>
        public string DataPath;
    }
}
