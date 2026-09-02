using System;
using System.Collections.Generic;
using AILevelGenerator.Runtime.Scheduling;

namespace AILevelGenerator.Runtime.Diagnostics
{
    /// <summary>
    /// 生成报告（第四周-Day5）：一次生成任务的全景记录（请求 → 结果 → 构建 → 校验 → 回滚）。
    /// 调度器在任务终态（成功/失败/取消/异常）统一构建并触发 GenerationCompleted 事件；
    /// 纯 DTO，渲染与落盘由宿主（窗口日志面板 / GenerationReportWriter）负责。
    /// </summary>
    public class GenerationReport
    {
        /// <summary> 报告生成时间 </summary>
        public DateTime Timestamp = DateTime.Now;

        /// <summary> 任务终态（Success/Failed） </summary>
        public GenerationTaskState FinalState;

        /// <summary> 中文状态文案（成功/失败/已取消） </summary>
        public string StatusText;

        // —— 请求摘要 ——
        public string TemplateId;
        public string TemplateName;
        public int RandomSeed;
        public string Prompt; // 截断 ≤ 120 字

        // —— 耗时 ——
        public float LlmTimeSeconds;
        public float BuildTimeSeconds;
        public float TotalTimeSeconds;

        // —— 内容统计 ——
        public string LevelName;
        public int PropCount;
        public int TaskCount;
        public int MainTaskCount;
        public bool HasTerrain;

        // —— 构建摘要 ——
        public int InstantiatedCount;
        public int BoundComponents;
        public int BindFailedComponents;
        public int ResolvedOverlapPairs;
        public float OverlapRatio;

        // —— 回滚信息 ——
        public bool RollbackTriggered;
        public bool RollbackSucceeded;
        public string RollbackNote;

        // —— 校验摘要 ——
        /// <summary> 错误 + 警告统一清单（按严重级排序：错误在前） </summary>
        public List<ReportIssue> Issues = new();
        public int ErrorCount;
        public int WarningCount;

        /// <summary> 原始 LLM 响应（可能为空：业务失败/异常路径） </summary>
        public string RawLlmResponse;
    }

    /// <summary>
    /// 报告条目：错误码 + 消息 + 字段定位 + 分类/严重级 + 解决建议（由错误码目录补全）。
    /// 「所有错误有明确提示与定位」在报告中的落点。
    /// </summary>
    public class ReportIssue
    {
        public string Code;
        public string Message;
        public string DataPath;
        public ErrorCategory Category;
        public ErrorSeverity Severity;
        public string Hint;
    }
}
