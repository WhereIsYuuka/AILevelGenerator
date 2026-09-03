using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Interfaces.Templates;
using AILevelGenerator.Runtime.LLM;
using AILevelGenerator.Runtime.Parsing;
using AILevelGenerator.Runtime.Prompting;
using AILevelGenerator.Runtime.Templates;
using AILevelGenerator.Runtime.Utilities;
using UnityEditor;
using UnityEngine;

namespace AILevelGenerator.Editor.Tools
{
    /// <summary>
    /// Prompt 精简基准测试（第五周-Day5 验收工具）：3 个固定场景 × {优化前基线, 当前} = 6 次真实 API 调用，
    /// 直接经 DeepSeekClient 发送（**绕过 LLMGenerator 与两级缓存**，保证每次都真实请求、口径纯净），
    /// 统计 usage.prompt_tokens 旧/新各 3 次均值 → 下降比例，验收线 ≥20%（completion/total 仅参考）。
    /// - 优化前一侧：PromptBaselineV1 冻结文本（改造前 System/User/Schema 逐字重放）；
    /// - 当前一侧：默认 Prompt 资产 + LevelGenerationSchema 现行输出（与生产管线同一组装路径）；
    /// - 两侧共享同一模板/资源/种子上下文与请求参数（温度/双约束），唯一变量 = 精简本身；
    /// - 场景顺序 旧/新 交错执行（抵消 API 侧时序偏差）。
    /// 离线回归门禁见 PromptOptimizationTests（无需网络）；本工具负责需求验收口径（真实 prompt_tokens）。
    /// </summary>
    public static class PromptBenchmarkRunner
    {
        private const string Title = "Prompt 优化基准";
        private const int ScenarioCount = 3;

        /// <summary> 固定场景描述（覆盖规模/风格差异；模板与种子跨新旧两侧共用） </summary>
        private static readonly string[] ScenarioPrompts =
        {
            "小型森林营地：1 个巡逻弓箭手，1 个宝箱，任务为抵达篝火",
            "中型沙漠要塞：4 个弓箭手守卫，2 个宝箱，主线为击败首领，支线为收集 3 份补给",
            "雪山村庄：2 个村民 NPC，1 个宝箱藏在房屋后，任务为护送商人过桥"
        };

        private static readonly int[] ScenarioSeeds = { 1001, 2002, 3003 }; // 固定种子：可复现

        [MenuItem("Tools/AI Level Generator Tests/运行 Prompt 优化基准（3 次×新旧对比）")]
        public static async void RunPromptBenchmark()
        {
            var client = ServiceLocator.Get<IDeepSeekClient>();
            if (client == null)
            {
                Debug.LogError($"[{Title}] DeepSeek 客户端未注册（ServiceLocator），请检查 GeneratorServiceInitializer");
                return;
            }

            var templateManager = ServiceLocator.Get<ITemplateManager>();
            var promptTemplate = templateManager?.GetDefaultPromptTemplate(); // 当前 Prompt 资产（生产同源）
            var levelTemplate = templateManager?.GetLevelTemplates()?.FirstOrDefault(t => t != null); // 基准固定模板
            var resourceNames = ServiceLocator.Get<IResourceMapper>()?.GetAllLogicalNames();
            if (promptTemplate == null || levelTemplate == null || resourceNames == null || resourceNames.Count == 0)
            {
                Debug.LogError($"[{Title}] 模板/资源未就绪（Prompt 资产、关卡模板、资源映射缺失），无法基准");
                return;
            }

            Debug.Log($"[{Title}] 开始：{ScenarioCount} 场景 × 2 侧（旧基线/当前）= {ScenarioCount * 2} 次真实 API 调用" +
                $" | 模板={levelTemplate.DisplayName ?? levelTemplate.TemplateId} | 资源 {resourceNames.Count} 条 | 旧/新交错执行");

            // 旧侧 Prompt 模板实例（冻结文本，经同一 PromptBuilder 插值 → 与当前侧同口径）
            var oldPromptTemplate = ScriptableObject.CreateInstance<PromptTemplate>();
            oldPromptTemplate.SystemPromptTemplate = PromptBaselineV1.SystemPrompt;
            oldPromptTemplate.UserPromptTemplate = PromptBaselineV1.UserPromptTemplate;

            var oldTokens = new List<int>();
            var newTokens = new List<int>();
            var parseFails = 0;
            var apiFails = 0;

            try
            {
                for (var i = 0; i < ScenarioCount; i++)
                {
                    var request = new GenerationRequest
                    {
                        Prompt = ScenarioPrompts[i],
                        TemplateId = levelTemplate.TemplateId,
                        RandomSeed = ScenarioSeeds[i]
                    };
                    var context = PromptBuilder.CreateContext(request, levelTemplate, resourceNames);

                    // 旧基线侧（System/User/Schema 全部取冻结常量）
                    var oldPrompt = new PromptBuilder().Build(oldPromptTemplate, context);
                    var oldResult = await RunOneAsync(client, $"场景{i + 1}-旧基线", oldPrompt,
                        PromptBaselineV1.SchemaParametersJson);
                    if (oldResult < 0) apiFails++;
                    else oldTokens.Add(oldResult);
                    if (oldResult == -2) parseFails++;

                    // 当前侧（System/User 取 Prompt 资产，Schema 取现行代码输出 —— 生产同路径）
                    var newPrompt = new PromptBuilder().Build(promptTemplate, context);
                    var newResult = await RunOneAsync(client, $"场景{i + 1}-当前", newPrompt, null);
                    if (newResult < 0) apiFails++;
                    else newTokens.Add(newResult);
                    if (newResult == -2) parseFails++;
                }
            }
            finally
            {
                if (oldPromptTemplate != null) UnityEngine.Object.DestroyImmediate(oldPromptTemplate);
            }

            // 汇总与判定（各 3 次均值；需求验收口径 = usage.prompt_tokens）
            var oldAvg = oldTokens.Count > 0 ? oldTokens.Average() : 0.0;
            var newAvg = newTokens.Count > 0 ? newTokens.Average() : 0.0;
            var reduction = oldAvg > 0 ? 1.0 - newAvg / oldAvg : 0.0;

            Debug.Log("========================================");
            Debug.Log($"[{Title}] 完成：prompt_tokens 旧均值 {oldAvg:F0}（{string.Join("/", oldTokens)}） → " +
                $"新均值 {newAvg:F0}（{string.Join("/", newTokens)}） | 下降 {reduction:P0} | 解析失败 {parseFails} / API 失败 {apiFails}");
            Debug.Log(reduction >= 0.20
                ? $"[{Title}] 验收结果：达标（prompt_tokens 下降 ≥20%）✓"
                : $"[{Title}] 验收结果：未达标（prompt_tokens 下降 <20%），请继续精简 Prompt 后重跑");
            if (apiFails > 0)
                Debug.LogError($"[{Title}] 存在 API 失败 {apiFails} 次（token 统计已剔除失败样本，均值基于 {oldTokens.Count + newTokens.Count}/6 次成功调用）");
        }

        /// <summary>
        /// 单次真实请求：组装与生产一致的消息/工具/双约束 → 发送 → 记录 usage.prompt_tokens。
        /// schemaParametersJson 传 null 时用当前 LevelGenerationSchema 构建（资源 enum 动态注入）；
        /// 返回：prompt_tokens（成功）；-1 API/解析异常；-2 成功但结果不可解析（计 usage 也计解析失败）。
        /// </summary>
        private static async Task<int> RunOneAsync(IDeepSeekClient client, string label, PromptBuildResult prompt,
            string schemaParametersJson)
        {
            var resourceNames = ServiceLocator.Get<IResourceMapper>()?.GetAllLogicalNames();
            var parameters = schemaParametersJson ?? LevelGenerationSchema.BuildParametersJson(resourceNames);

            var chatRequest = new DeepSeekChatRequest
            {
                Messages = new List<DeepSeekMessage>
                {
                    new() { Role = "system", Content = prompt.SystemPrompt },
                    new() { Role = "user", Content = prompt.UserPrompt }
                },
                Temperature = 0.7f,
                Tools = new List<DeepSeekTool>
                {
                    new()
                    {
                        Function = new DeepSeekToolFunction
                        {
                            Name = LevelGenerationSchema.FunctionName,
                            Description = LevelGenerationSchema.FunctionDescription,
                            ParametersJson = parameters
                        }
                    }
                },
                ToolChoiceJson = LevelGenerationSchema.CreateToolChoiceJson(),
                ResponseFormatJson = LevelGenerationSchema.CreateJsonObjectResponseFormat()
            };

            try
            {
                var response = await client.ChatAsync(chatRequest);
                var usage = response?.Usage;
                if (usage == null)
                {
                    Debug.LogError($"[{Title}] {label}：响应无 usage 统计（无法计 token），按失败处理");
                    return -1;
                }

                var raw = ExtractStructuredJson(response);
                var parseOk = raw != null && LevelGenerationParser.Parse(raw).IsValid;
                var parseFails = parseOk ? "" : "（响应不可解析）";
                Debug.Log($"[{Title}] {label}：prompt {usage.PromptTokens} | completion {usage.CompletionTokens} | total {usage.TotalTokens}{parseFails}");
                return parseOk ? usage.PromptTokens : -2;
            }
            catch (DeepSeekException e)
            {
                Debug.LogError($"[{Title}] {label}：调用失败（已剔除统计）：{e.FriendlyMessage}");
                return -1;
            }
        }

        /// <summary> 提取结构化内容：tool_calls.arguments 优先，content 兜底（与 LLMGenerator 同规则） </summary>
        private static string ExtractStructuredJson(DeepSeekChatResponse response)
        {
            if (response.Choices == null || response.Choices.Count == 0) return null;
            var message = response.Choices[0].Message;
            if (message == null) return null;
            if (message.ToolCalls != null && message.ToolCalls.Count > 0
                && !string.IsNullOrEmpty(message.ToolCalls[0].Arguments))
                return message.ToolCalls[0].Arguments;
            return string.IsNullOrEmpty(message.Content) ? null : message.Content;
        }
    }
}
