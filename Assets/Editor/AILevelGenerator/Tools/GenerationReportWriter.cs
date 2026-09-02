using System;
using System.IO;
using System.Text;
using AILevelGenerator.Runtime.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace AILevelGenerator.Editor.Tools
{
    /// <summary>
    /// 生成报告落盘（第四周-Day5）：把 GenerationReport 渲染为 Markdown 归档到
    /// Assets/Temp/GenerateReports/（Assets/Temp 已被 .gitignore，生成物不入版本库）。
    /// - 由 GeneratorServiceInitializer 订阅调度器 GenerationCompleted 自动调用——无窗口打开也归档；
    /// - 只做 IO 与刷新，内容格式化委托 GenerationReportFormatter（纯逻辑，可单测）；
    /// - 失败降级为 Debug.LogWarning，绝不打断生成链路。
    /// </summary>
    public static class GenerationReportWriter
    {
        /// <summary> 归档目录（Assets 相对路径，供日志/文档展示） </summary>
        public const string ReportDirectory = "Assets/Temp/GenerateReports";

        /// <summary>
        /// 写 Markdown 报告到归档目录。
        /// </summary>
        /// <returns>Assets 相对路径（如 Assets/Temp/GenerateReports/生成报告_20260901_103000.md）；失败/入参为空返回 null</returns>
        public static string Write(GenerationReport report)
        {
            if (report == null) return null;
            try
            {
                // Application.dataPath = Project/Assets，向上取 Temp 与 Temp 同级的 Assets/Temp 保持一致
                var dir = Path.Combine(Application.dataPath, "Temp", "GenerateReports");
                Directory.CreateDirectory(dir);

                var fileName = $"生成报告_{report.Timestamp:yyyyMMdd_HHmmss}.md";
                var fullPath = Path.Combine(dir, fileName);
                File.WriteAllText(fullPath, GenerationReportFormatter.FormatMarkdown(report), new UTF8Encoding(false));

                // 刷新 AssetDatabase，让归档文件出现在项目视图（Assets/Temp 已被 gitignore，不影响版本库）
                AssetDatabase.Refresh();
                return $"{ReportDirectory}/{fileName}";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Generator] 生成报告落盘失败（不影响生成结果）：{e.Message}");
                return null;
            }
        }
    }
}
