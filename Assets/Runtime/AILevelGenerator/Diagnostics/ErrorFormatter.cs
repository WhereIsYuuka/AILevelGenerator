namespace AILevelGenerator.Runtime.Diagnostics
{
    /// <summary>
    /// 错误信息统一格式化（第四周-Day5「错误信息规范」）：
    /// - Format：`CODE：message（dataPath）` —— 与既有日志格式逐字兼容（调度器与测试断言依赖）；
    /// - FormatDetailed：追加解决建议 `CODE：message（dataPath）。建议：hint`（报告与窗口详情用）；
    /// - 未注册错误码不抛异常：保留原始内容（日志链路永不中断）。
    /// </summary>
    public static class ErrorFormatter
    {
        /// <summary> 统一格式：`CODE：message（dataPath）`；dataPath 为空时省略括号段 </summary>
        public static string Format(string code, string message, string dataPath = null)
        {
            var codeText = string.IsNullOrEmpty(code) ? "UNKNOWN" : code;
            var messageText = string.IsNullOrEmpty(message) ? "无错误信息" : message;
            var path = string.IsNullOrEmpty(dataPath) ? "" : $"（{dataPath}）";
            return $"{codeText}：{messageText}{path}";
        }

        /// <summary> 详细格式：统一格式 + 解决建议（错误码目录未注册时无建议段） </summary>
        public static string FormatDetailed(string code, string message, string dataPath = null)
        {
            var formatted = Format(code, message, dataPath);
            var hint = GetHint(code);
            return string.IsNullOrEmpty(hint) ? formatted : $"{formatted}。建议：{hint}";
        }

        /// <summary> 按错误码取解决建议（未注册返回 null） </summary>
        public static string GetHint(string code) =>
            code != null && ErrorCatalog.TryGet(code, out var def) ? def.Hint : null;
    }
}
