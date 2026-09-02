using System.Linq;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Scheduling;

namespace AILevelGenerator.Runtime.Diagnostics
{
    /// <summary>
    /// 生成报告构建器（纯逻辑，可单测）：由 请求/生成结果/构建结果/终态/回滚信息 汇总为 GenerationReport。
    /// - 错误与警告统一转 ReportIssue，并依据错误码目录补全分类与解决建议（未注册码降级为未知分类）；
    /// - Prompt 截断防报告过长；耗时汇总 LLM/构建/总耗时；空输入全部安全降级（报告链路永不抛异常）。
    /// </summary>
    public class GenerationReportBuilder
    {
        private const int MaxPromptLength = 120;

        /// <summary>
        /// 构建报告。各入参均可为 null（异常/取消路径只有部分数据），缺失字段安全降级。
        /// </summary>
        /// <param name="request">本次生成请求（可为 null）</param>
        /// <param name="result">生成器结果（可为 null：生成器抛异常路径）</param>
        /// <param name="buildResult">构建结果（可为 null：未构建/构建异常路径）</param>
        /// <param name="finalState">任务终态</param>
        /// <param name="rollbackTriggered">是否触发过全量回滚</param>
        /// <param name="rollbackSucceeded">回滚是否成功</param>
        /// <param name="totalTimeSeconds">任务总耗时（调度器计时）</param>
        /// <param name="statusTextOverride">状态文案覆盖（如「已取消」）；null 时按终态推导</param>
        public GenerationReport Build(GenerationRequest request, GenerationResult result,
            LevelBuildResult buildResult, GenerationTaskState finalState,
            bool rollbackTriggered = false, bool rollbackSucceeded = false,
            float totalTimeSeconds = 0f, string statusTextOverride = null)
        {
            var report = new GenerationReport
            {
                FinalState = finalState,
                StatusText = statusTextOverride ?? ToStatusText(finalState),
                TemplateId = request?.TemplateId ?? string.Empty,
                RandomSeed = request?.RandomSeed ?? 0,
                Prompt = Truncate(request?.Prompt),
                TotalTimeSeconds = totalTimeSeconds,
                LevelName = result?.LevelData?.LevelName ?? string.Empty,
                PropCount = result?.LevelData?.Props?.Count ?? 0,
                TaskCount = result?.LevelData?.Tasks?.Count ?? 0,
                MainTaskCount = result?.LevelData?.Tasks?.Count(t => t != null && t.IsMainTask) ?? 0,
                HasTerrain = result?.LevelData?.Terrain != null,
                LlmTimeSeconds = result?.GenerationTime ?? 0f,
                RawLlmResponse = result?.RawLLMResponse ?? string.Empty
            };

            if (buildResult != null)
            {
                report.BuildTimeSeconds = buildResult.BuildTime;
                report.InstantiatedCount = buildResult.InstantiatedCount;
                report.BoundComponents = buildResult.BoundComponents;
                report.BindFailedComponents = buildResult.BindFailedComponents;
                report.ResolvedOverlapPairs = buildResult.ResolvedOverlapPairs;
                report.OverlapRatio = buildResult.OverlapRatio;
            }

            report.RollbackTriggered = rollbackTriggered;
            report.RollbackSucceeded = rollbackSucceeded;
            report.RollbackNote = BuildRollbackNote(rollbackTriggered, rollbackSucceeded);

            // 错误/警告 → 报告条目（错误在前，按目录补全分类与解决建议）
            if (result != null)
            {
                if (result.Errors != null)
                    foreach (var e in result.Errors)
                        report.Issues.Add(ToIssue(e.Code, e.Message, e.DataPath, ErrorSeverity.Error));
                if (result.Warnings != null)
                    foreach (var w in result.Warnings)
                        report.Issues.Add(ToIssue(w.Code, w.Message, w.DataPath, ErrorSeverity.Warning));
            }
            report.Issues = report.Issues.OrderBy(i => i.Severity).ToList(); // Error(0) 在 Warning(1) 前
            report.ErrorCount = report.Issues.Count(i => i.Severity == ErrorSeverity.Error);
            report.WarningCount = report.Issues.Count - report.ErrorCount;

            return report;
        }

        private static ReportIssue ToIssue(string code, string message, string dataPath, ErrorSeverity severity)
        {
            var def = ErrorCatalog.Get(code);
            return new ReportIssue
            {
                Code = code ?? string.Empty,
                Message = message ?? string.Empty,
                DataPath = dataPath ?? string.Empty,
                Category = def?.Category ?? ErrorCategory.Pipeline, // 未注册码降级：不中断报告
                Severity = severity,
                Hint = def?.Hint ?? string.Empty
            };
        }

        private static string BuildRollbackNote(bool triggered, bool succeeded)
        {
            if (!triggered) return "未触发";
            return succeeded ? "已自动回滚成功（场景已恢复至生成前快照）" : "自动回滚失败（场景可能残留本次生成物体，请检查 Console 或手动回滚）";
        }

        private static string ToStatusText(GenerationTaskState state) => state switch
        {
            GenerationTaskState.Success => "成功",
            GenerationTaskState.Failed => "失败",
            _ => state.ToString()
        };

        private static string Truncate(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= MaxPromptLength ? text : text.Substring(0, MaxPromptLength) + "…";
        }
    }
}
