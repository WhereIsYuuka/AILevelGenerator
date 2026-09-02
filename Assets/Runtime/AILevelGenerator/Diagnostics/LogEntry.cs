using System;
using AILevelGenerator.Runtime.Interfaces;

namespace AILevelGenerator.Runtime.Diagnostics
{
    /// <summary>
    /// 结构化日志条目（第四周-Day5）：在纯文本日志之上附加 错误码/字段定位/解决建议/管线阶段，
    /// 供日志面板分级筛选、错误码高亮与详情提示渲染。
    /// - 窗口实现 ILogger.Log(LogEntry) 结构化渲染；
    /// - 未实现结构化宿主的日志器走 ILogger 默认分发（按级别降级为纯文本，零破坏兼容）。
    /// </summary>
    public struct LogEntry
    {
        /// <summary> 日志级别（信息/警告/错误/成功） </summary>
        public LogLevel Level;

        /// <summary> 时间戳（构造时取当前时间） </summary>
        public DateTime Timestamp;

        /// <summary> 日志正文 </summary>
        public string Message;

        /// <summary> 关联错误码（ErrorCodes 常量，可为空） </summary>
        public string Code;

        /// <summary> 字段定位（props[0].position 等 JSON 风格路径，可为空） </summary>
        public string DataPath;

        /// <summary> 解决建议（来自错误码目录，可为空） </summary>
        public string Hint;

        /// <summary> 管线阶段（请求/校验/生成/构建/回滚…） </summary>
        public LogStage Stage;

        public static LogEntry Create(LogLevel level, string message,
            string code = null, string dataPath = null, string hint = null, LogStage stage = LogStage.None)
        {
            return new LogEntry
            {
                Level = level,
                Timestamp = DateTime.Now,
                Message = message ?? string.Empty,
                Code = code,
                DataPath = dataPath,
                Hint = hint,
                Stage = stage
            };
        }

        /// <summary>
        /// 便捷：按错误码从目录自动补全解决建议（校验错误日志入口统一走这里，
        /// 保证「错误码 → 建议」在任意宿主一致）。
        /// </summary>
        public static LogEntry FromIssue(LogLevel level, string code, string message,
            string dataPath = null, LogStage stage = LogStage.None) =>
            Create(level, message, code, dataPath, ErrorFormatter.GetHint(code), stage);
    }
}
