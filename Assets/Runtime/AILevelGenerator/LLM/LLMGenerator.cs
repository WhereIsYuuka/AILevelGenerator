using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Diagnostics;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Interfaces.Templates;
using AILevelGenerator.Runtime.Parsing;
using AILevelGenerator.Runtime.Prompting;
using AILevelGenerator.Runtime.Templates;

namespace AILevelGenerator.Runtime.LLM
{
    /// <summary>
    /// 真实 LLM 生成器（Day5 全链路编排）：请求 → Prompt 组装 → Function Calling 双重约束 →
    /// API 调用 → 容错解析 → 模板默认值 → 规模/主线校验 → GenerationResult。
    /// - 依赖注入（IDeepSeekClient / ITemplateProvider / IResourceMapper / 缓存 / keyProvider），可单测不碰网络
    /// - API key 经 keyProvider 注入检查（Runtime 程序集不引用 UnityEditor，EditorPrefs 由窗口侧闭包提供）
    /// - 缓存命中直接重走解析管线返回，不调 API
    /// - 异常全部转 GenerationResult.Errors（中文 FriendlyMessage），不向调度器抛
    /// </summary>
    public class LLMGenerator : IGenerator
    {
        private readonly IDeepSeekClient _client;
        private readonly Func<string> _apiKeyProvider;
        private readonly ITemplateProvider _templateProvider;
        private readonly IResourceMapper _resourceMapper;
        private readonly GenerationCache _cache;
        private readonly PromptBuilder _promptBuilder = new();
        private readonly bool _useJsonMode; // 双重约束开关（true=json_object + function calling；false=仅 function calling）

        public LLMGenerator(
            IDeepSeekClient client,
            Func<string> apiKeyProvider,
            ITemplateProvider templateProvider,
            IResourceMapper resourceMapper,
            GenerationCache cache = null,
            bool useJsonMode = true)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _apiKeyProvider = apiKeyProvider;
            _templateProvider = templateProvider;
            _resourceMapper = resourceMapper;
            _cache = cache ?? new GenerationCache();
            _useJsonMode = useJsonMode;
        }

        public async Task<GenerationResult> GenerateAsync(GenerationRequest request)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new GenerationResult { GenerationTime = 0f };

            try
            {
                // 1. key 前置检查（明确中文提示，不发无效请求）
                if (string.IsNullOrWhiteSpace(_apiKeyProvider?.Invoke()))
                {
                    result.Success = false;
                    result.Errors.Add(new ValidationError
                    {
                        Code = ErrorCodes.NO_API_KEY,
                        Message = "未配置 DeepSeek API Key（请先在「API 设置」中保存 Key）"
                    });
                    return result;
                }

                var template = ResolveLevelTemplate(request);
                var prompt = BuildPrompt(request, template);
                var raw = await GenerateRawJsonAsync(request, template, prompt); // 含缓存命中短路

                result.RawLLMResponse = raw;
                var parse = LevelGenerationParser.Parse(raw);
                if (!parse.IsValid)
                {
                    result.Success = false;
                    result.Errors.AddRange(parse.Errors);
                    return result;
                }

                // 2. 模板统一收尾（Day5-W1：ApplyDefaults + 模板确定性随机内容 PostGenerate）
                template?.FinalizeData(parse.Level, request.RandomSeed);
                if (string.IsNullOrEmpty(parse.Level.Description))
                    parse.Level.Description = request.Prompt;

                // 3. 规模与主线校验（只警告不裁剪：模板注释约定裁剪职责在校验器，此处生成期提示）
                ValidateScope(parse, template, result.Warnings);

                result.Success = true;
                result.LevelData = parse.Level;
                result.Tasks = parse.Level.Tasks;
                result.Warnings.InsertRange(0, parse.Warnings);
                return result;
            }
            catch (DeepSeekException e)
            {
                result.Success = false;
                result.Errors.Add(new ValidationError { Code = ErrorCodes.LLM_ERROR, Message = e.FriendlyMessage });
                return result;
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Errors.Add(new ValidationError
                {
                    Code = ErrorCodes.LLM_ERROR,
                    Message = $"生成失败：{e.Message}"
                });
                return result;
            }
            finally
            {
                result.GenerationTime = (float)stopwatch.Elapsed.TotalSeconds;
            }
        }

        /// <summary> 按请求 TemplateId 取关卡模板（未命中/未传返回 null，走默认 Prompt） </summary>
        private LevelTemplate ResolveLevelTemplate(GenerationRequest request)
        {
            if (_templateProvider == null || string.IsNullOrEmpty(request?.TemplateId)) return null;
            return _templateProvider.GetTemplateById(request.TemplateId);
        }

        /// <summary> Prompt 组装：默认 Prompt 模板 + 插值上下文（模板指南/资源清单/开关/种子） </summary>
        private PromptBuildResult BuildPrompt(GenerationRequest request, LevelTemplate template)
        {
            var promptTemplate = _templateProvider?.GetDefaultPromptTemplate();
            var resourceNames = _resourceMapper?.GetAllLogicalNames();
            var context = PromptBuilder.CreateContext(request, template, resourceNames);
            return _promptBuilder.Build(promptTemplate, context);
        }

        /// <summary> 调用 API 并提取结构化内容（tool_calls.arguments 优先，content 兜底；缓存命中短路） </summary>
        private async Task<string> GenerateRawJsonAsync(GenerationRequest request, LevelTemplate template, PromptBuildResult prompt)
        {
            var seed = request?.RandomSeed ?? 0;
            var templateId = request?.TemplateId ?? string.Empty;
            var promptText = request?.Prompt ?? string.Empty;

            if (_cache.TryGet(templateId, seed, promptText, out var cached))
                return cached; // 缓存命中：重走解析管线，与新鲜请求同路径

            var resourceNames = _resourceMapper?.GetAllLogicalNames();
            var chatRequest = new DeepSeekChatRequest
            {
                Messages = new List<DeepSeekMessage>
                {
                    new() { Role = "system", Content = prompt.SystemPrompt },
                    new() { Role = "user", Content = prompt.UserPrompt }
                },
                Temperature = 0.7f,
                Tools = LevelGenerationSchema.CreateTools(resourceNames),
                ToolChoiceJson = LevelGenerationSchema.CreateToolChoiceJson(),
                ResponseFormatJson = _useJsonMode ? LevelGenerationSchema.CreateJsonObjectResponseFormat() : null
            };

            var response = await _client.ChatAsync(chatRequest);

            var raw = ExtractStructuredJson(response);
            if (raw == null)
                throw new ParseException("模型未返回可解析的结构化内容（tool_calls 与 content 均为空）");

            _cache.Put(templateId, seed, promptText, raw);
            return raw;
        }

        /// <summary> 提取结构化 JSON：tool_calls[0].Arguments 优先（function calling 载体），content 兜底 </summary>
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

        /// <summary> 规模与主线校验：模板配置的数量范围越界/缺主线 → warning（0 表示不限制） </summary>
        private static void ValidateScope(LevelParseResult parse, LevelTemplate template, List<ValidationWarning> warnings)
        {
            if (parse.Level == null) return;

            // 模板可能为任意 LevelTemplate 子类，仅当数据驱动模板配置了范围时校验（类型转换失败视为无约束）
            if (template is ConfigurableLevelTemplate config)
            {
                if (config.MaxPropCount > 0 && parse.Level.Props.Count > config.MaxPropCount)
                    AddScopeWarning(warnings, ErrorCodes.PROPS_TOO_MANY, $"道具数量 {parse.Level.Props.Count} 超过模板上限 {config.MaxPropCount}");
                if (config.MinPropCount > 0 && parse.Level.Props.Count < config.MinPropCount)
                    AddScopeWarning(warnings, ErrorCodes.PROPS_TOO_FEW, $"道具数量 {parse.Level.Props.Count} 低于模板下限 {config.MinPropCount}");
                if (config.MaxTaskCount > 0 && parse.Level.Tasks.Count > config.MaxTaskCount)
                    AddScopeWarning(warnings, ErrorCodes.TASKS_TOO_MANY, $"任务数量 {parse.Level.Tasks.Count} 超过模板上限 {config.MaxTaskCount}");
                if (config.MinTaskCount > 0 && parse.Level.Tasks.Count < config.MinTaskCount)
                    AddScopeWarning(warnings, ErrorCodes.TASKS_TOO_FEW, $"任务数量 {parse.Level.Tasks.Count} 低于模板下限 {config.MinTaskCount}");
                if (config.ForceMainTask && !HasMainTask(parse.Level.Tasks))
                    AddScopeWarning(warnings, ErrorCodes.NO_MAIN_TASK, "模板要求存在主线任务，但生成结果没有 IsMainTask=true 的任务");
            }
        }

        private static bool HasMainTask(List<TaskData> tasks)
        {
            foreach (var t in tasks)
                if (t != null && t.IsMainTask) return true;
            return false;
        }

        private static void AddScopeWarning(List<ValidationWarning> warnings, string code, string message) =>
            warnings.Add(new ValidationWarning { Code = code, Message = message });
    }
}
