using System;
using System.Collections.Generic;
using AILevelGenerator.Runtime.Interfaces;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 测试辅助日志器：收集带级别前缀的消息列表供断言，模拟窗口/控制台等日志宿主
    /// </summary>
    public class TestLogger : ILogger
    {
        public readonly List<string> Messages = new();

        public void Log(string message) => Messages.Add($"[INFO] {message}");

        public void LogWarning(string message) => Messages.Add($"[WARN] {message}");

        public void LogError(string message) => Messages.Add($"[ERROR] {message}");

        public void LogSuccess(string message) => Messages.Add($"[SUCCESS] {message}");

        public void Clear() => Messages.Clear();

        public event Action<string, LogLevel> OnLogReceived;
    }
}
