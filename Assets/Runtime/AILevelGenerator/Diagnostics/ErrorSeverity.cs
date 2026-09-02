namespace AILevelGenerator.Runtime.Diagnostics
{
    /// <summary>
    /// 错误严重级别（校验结果层语义）：Error 阻断流程（生成拦截/转失败），Warning 仅提示不阻断。
    /// 与日志级别 LogLevel 语义对齐但维度不同——此处描述「错误码」本身的性质，日志级别描述「本次输出」的性质。
    /// </summary>
    public enum ErrorSeverity
    {
        /// <summary> 错误：流程被拦截或任务转失败 </summary>
        Error,

        /// <summary> 警告：仅提示，不阻断流程（如降级跳过、默认值兜底） </summary>
        Warning
    }
}
