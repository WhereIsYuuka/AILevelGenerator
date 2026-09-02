using System.Globalization;
using System.Text;
using AILevelGenerator.Runtime.Scheduling;

namespace AILevelGenerator.Runtime.Diagnostics
{
    /// <summary>
    /// 生成报告 Markdown 格式化（第四周-Day5）：把 GenerationReport 渲染为 Markdown 文本。
    /// 纯逻辑（无 IO，可单测）：字符串/数字统一 InvariantCulture 输出，测试断言不随机器区域设置波动。
    /// 落盘（GenerationReportWriter，Editor 层）只做 IO，不重复格式化逻辑。
    /// </summary>
    public static class GenerationReportFormatter
    {
        public static string FormatMarkdown(GenerationReport report)
        {
            if (report == null) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine($"# 生成报告：{report.StatusText}");
            sb.AppendLine();
            sb.AppendLine($"> 生成时间：{report.Timestamp:yyyy-MM-dd HH:mm:ss} ｜ 终态：{report.FinalState} ｜ " +
                          $"总耗时：{F(report.TotalTimeSeconds)}s（LLM {F(report.LlmTimeSeconds)}s + 构建 {F(report.BuildTimeSeconds)}s）");

            sb.AppendLine();
            sb.AppendLine("## 请求摘要");
            sb.AppendLine($"- 模板：{report.TemplateName ?? report.TemplateId}（ID：{report.TemplateId}）");
            sb.AppendLine($"- 随机种子：{report.RandomSeed}");
            sb.AppendLine($"- 描述：{report.Prompt}");

            sb.AppendLine();
            sb.AppendLine("## 内容统计");
            sb.AppendLine($"- 关卡名：{report.LevelName}");
            sb.AppendLine($"- 道具：{report.PropCount} ｜ 任务：{report.TaskCount}（主线 {report.MainTaskCount}）｜ 地形：{(report.HasTerrain ? "有" : "无")}");

            sb.AppendLine();
            sb.AppendLine("## 构建摘要");
            sb.AppendLine($"- 实例化：{report.InstantiatedCount} ｜ 绑定组件：{report.BoundComponents}（失败 {report.BindFailedComponents}）");
            sb.AppendLine($"- 重叠修正：{report.ResolvedOverlapPairs} 对 ｜ 重叠率：{F(report.OverlapRatio * 100f)}%");

            sb.AppendLine();
            sb.AppendLine($"## 校验问题（错误 {report.ErrorCount} / 警告 {report.WarningCount}）");
            if (report.Issues == null || report.Issues.Count == 0)
            {
                sb.AppendLine("- 无");
            }
            else
            {
                foreach (var issue in report.Issues)
                {
                    var sev = issue.Severity == ErrorSeverity.Error ? "错误" : "警告";
                    var path = string.IsNullOrEmpty(issue.DataPath) ? string.Empty : $"（{issue.DataPath}）";
                    var hint = string.IsNullOrEmpty(issue.Hint) ? string.Empty : $" ｜ 建议：{issue.Hint}";
                    sb.AppendLine($"- [{sev}] {issue.Code}：{issue.Message}{path}{hint}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## 回滚");
            sb.AppendLine($"- {report.RollbackNote}");

            if (!string.IsNullOrEmpty(report.RawLlmResponse))
            {
                sb.AppendLine();
                sb.AppendLine("## 原始 LLM 响应");
                sb.AppendLine("```text");
                sb.AppendLine(report.RawLlmResponse);
                sb.AppendLine("```");
            }

            return sb.ToString();
        }

        /// <summary> 数字统一两位小数（InvariantCulture，避免区域设置干扰断言） </summary>
        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
