using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces.Templates;
using AILevelGenerator.Runtime.Templates;

namespace AILevelGenerator.Runtime.Prompting
{
    /// <summary>
    /// 占位符插值上下文：收集构建 Prompt 所需的全部可注入变量，
    /// 便于测试与未来扩展（如 {taskTemplates} 任务模板清单，扩展只改 CreateContext 与 GetValue）。
    /// </summary>
    [Serializable]
    public class PromptContext
    {
        public string UserPrompt;         // {userPrompt}        用户自然语言描述
        public string TemplateName;       // {templateName}      模板显示名
        public string TemplateGuideline;  // {templateGuideline} 模板指南
        public string ResourceList;       // {resourceList}      可用逻辑名清单（顿号分隔）
        public string Seed;               // {seed}              随机种子
        public string TerrainEnabled;     // {terrainEnabled}    生成地形/不生成地形
        public string PropsEnabled;       // {propsEnabled}      生成道具/不生成道具
        public string TasksEnabled;       // {tasksEnabled}      生成任务/不生成任务
    }

    /// <summary> 构建结果：System/User 提示词 + 未识别占位符列表（供调用方告警，容错不中断链路） </summary>
    public class PromptBuildResult
    {
        public string SystemPrompt;
        public string UserPrompt;
        public List<string> UnresolvedPlaceholders = new();
    }

    /// <summary>
    /// 提示词构建器：纯逻辑（Runtime 程序集，可单测）。
    /// 输入 PromptTemplate + PromptContext → 完整提示词。
    /// 未知占位符保留原文并记入 UnresolvedPlaceholders（模板文案写错时可定位，不中断生成）。
    /// </summary>
    public class PromptBuilder
    {
        /// <summary> 占位符匹配模式：{单词}，如 {userPrompt} </summary>
        private static readonly Regex PlaceholderRegex = new(@"\{(\w+)\}", RegexOptions.Compiled);

        /// <summary> 构建 System + User 提示词（对外主入口，Day7 生成器调用） </summary>
        public PromptBuildResult Build(PromptTemplate template, PromptContext context)
        {
            var result = new PromptBuildResult();
            if (template == null || context == null) return result; // 容错：空输入返回空结果

            result.SystemPrompt = ReplacePlaceholders(template.SystemPromptTemplate ?? string.Empty, context, result.UnresolvedPlaceholders);
            result.UserPrompt = ReplacePlaceholders(template.UserPromptTemplate ?? string.Empty, context, result.UnresolvedPlaceholders);
            return result;
        }

        /// <summary> 单文本插值：替换 context 中存在的键，未知占位符保留原样（单测直接测此方法） </summary>
        public string ReplacePlaceholders(string raw, PromptContext context)
        {
            return ReplacePlaceholders(raw, context, null);
        }

        /// <summary> 插值实现：未知键保留原文并记录（不递归插值，值中的花括号不二次处理） </summary>
        private static string ReplacePlaceholders(string raw, PromptContext context, List<string> unresolved)
        {
            if (string.IsNullOrEmpty(raw) || context == null) return raw;

            return PlaceholderRegex.Replace(raw, match =>
            {
                var key = match.Groups[1].Value;
                var value = GetValue(context, key);
                if (value == null)
                {
                    if (unresolved != null && !unresolved.Contains(key)) unresolved.Add(key);
                    return match.Value; // 未知占位符保留原样（容错）
                }
                return value;
            });
        }

        /// <summary> 按键取上下文值；未定义的键返回 null </summary>
        private static string GetValue(PromptContext context, string key)
        {
            switch (key)
            {
                case "userPrompt": return context.UserPrompt;
                case "templateName": return context.TemplateName;
                case "templateGuideline": return context.TemplateGuideline;
                case "resourceList": return context.ResourceList;
                case "seed": return context.Seed;
                case "terrainEnabled": return context.TerrainEnabled;
                case "propsEnabled": return context.PropsEnabled;
                case "tasksEnabled": return context.TasksEnabled;
                default: return null;
            }
        }

        /// <summary> 布尔开关 → 中文指令（LLM 更易理解，如 "生成地形" / "不生成地形"） </summary>
        private static string ToEnabledText(bool enabled) => enabled ? "生成" : "不生成";

        /// <summary>
        /// 从请求/模板/资源清单构造插值上下文（静态工厂，集中变量来源）。
        /// 各输入均可为 null（容错）；resourceNames 为 null 时资源清单留空。
        /// </summary>
        public static PromptContext CreateContext(GenerationRequest request, LevelTemplate levelTemplate, IReadOnlyList<string> resourceNames)
        {
            return new PromptContext
            {
                UserPrompt = request?.Prompt ?? string.Empty,
                TemplateName = levelTemplate != null && !string.IsNullOrEmpty(levelTemplate.DisplayName)
                    ? levelTemplate.DisplayName
                    : (request?.TemplateId ?? string.Empty),
                TemplateGuideline = levelTemplate?.GetGuideline() ?? string.Empty,
                ResourceList = resourceNames != null && resourceNames.Count > 0
                    ? string.Join("、", resourceNames)
                    : string.Empty,
                Seed = request?.RandomSeed.ToString() ?? "0",
                // 布尔开关映射完整中文指令：如 "生成地形" / "不生成地形"
                TerrainEnabled = ToEnabledText(request?.GenerateTerrain ?? true) + "地形",
                PropsEnabled = ToEnabledText(request?.GenerateProps ?? true) + "道具",
                TasksEnabled = ToEnabledText(request?.GenerateTasks ?? true) + "任务"
            };
        }
    }
}
