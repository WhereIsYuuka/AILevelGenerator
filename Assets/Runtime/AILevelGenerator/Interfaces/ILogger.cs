using System;
using AILevelGenerator.Runtime.Diagnostics;

namespace AILevelGenerator.Runtime.Interfaces
{
    /// <summary>
    /// 日志服务接口 窗口/控制台/文件均可实现
    /// </summary>
    public interface ILogger
    {
        void Log(string message);
        void LogWarning(string message);
        void LogError(string message);
        void LogSuccess(string message);
        void Clear();
        event Action<string, LogLevel> OnLogReceived;

        /// <summary>
        /// 结构化日志（第四周-Day5）：携带错误码/字段定位/解决建议/管线阶段，供分级展示与报告。
        /// 默认接口方法（C# 8+，Unity 6 支持）：宿主未显式实现时按级别分发到
        /// Log/LogWarning/LogError/LogSuccess——零破坏兼容（TestLogger 等简单宿主无需改动）；
        /// 窗口等 UI 宿主显式实现以获得富文本、错误码高亮与级别筛选能力。
        /// </summary>
        void Log(LogEntry entry)
        {
            switch (entry.Level)
            {
                case LogLevel.Warning:
                    LogWarning(entry.Message);
                    break;
                case LogLevel.Error:
                    LogError(entry.Message);
                    break;
                case LogLevel.Success:
                    LogSuccess(entry.Message);
                    break;
                default:
                    Log(entry.Message);
                    break;
            }
        }
    }

    public enum LogLevel { Info, Warning, Error, Success }
}
