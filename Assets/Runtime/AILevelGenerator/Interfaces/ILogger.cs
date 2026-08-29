using System;

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
    }

    public enum LogLevel { Info, Warning, Error, Success }
}