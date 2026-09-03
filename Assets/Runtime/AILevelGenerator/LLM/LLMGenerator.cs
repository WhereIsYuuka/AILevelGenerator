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
using UnityEngine;

namespace AILevelGenerator.Runtime.LLM
{
    /// <summary>
    /// 真实 LLM 生成器（第四周-Day6 接入，第五周-Day1/3/4/5 连续演进）：请求 → Prompt 组装 → Function Calling 双重约束 →
    /// API 调用 → 容错解析 → 模板默认值 → 规模/主线校验 → GenerationResult。
    /// - 依赖注入（IDeepSeekClient / ITemplateManager / IResourceMapper / 两级缓存 / keyProvider），可单测不碰网络
    /// - API key 经 keyProvider 注入检查（Runtime 程序集不引用 UnityEditor，EditorPrefs 由窗口侧闭包提供）
    /// - 第五周-Day5：缓存升级两级（内存 + 磁盘）；键含模板依赖哈希（资产变更自动失效）+ Schema 契约版本；
    ///   命中直接重走解析/模板确定性收尾管线返回，不调 API
    /// - 异常全部转 GenerationResult.Errors（中文 FriendlyMessage），不向调度器抛
    /// </summary>
    public class LLMGenerator : IGenerator
    {
        private readonly IDeepSeekClient _client;
        private readonly Func<string> _apiKeyProvider;
        private readonly ITemplateManager _templateManager;
        private readonly IResourceMapper _resourceMapper;
        private readonly IGenerationCache _cache;
        private readonly ITemplateDependencyHashProvider _dependencyHashProvider; // 可空：null = 缓存键不含依赖哈希（无资产模板路径）
        private readonly PromptBuilder _promptBuilder = new();
        private readonly bool _useJsonMode; // 双重约束开关（true=json_object + function calling；false=仅 function calling）

        public LLMGenerator(
            IDeepSeekClient client,
            Func<string> apiKeyProvider,
            ITemplateManager templateManager,
            IResourceMapper resourceMapper,
            IGenerationCache cache = null,
            bool useJsonMode = true,
            ITemplateDependencyHashProvider dependencyHashProvider = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _apiKeyProvider = apiKeyProvider;
            _templateManager = templateManager;
            _resourceMapper = resourceMapper;
            _cache = cache ?? new GenerationCache();
            _useJsonMode = useJsonMode;
            _dependencyHashProvider = dependencyHashProvider;
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

                // 2. 模板统一收尾（第五周-Day1：ApplyDefaults + 模板确定性随机内容 PostGenerate）
                template?.FinalizeData(parse.Level, request.RandomSeed);
                // 3. 任务模板统一收尾（第五周-Day3：任务链路打通 —— 收集物散布等任务级随机内容）
                // 按任务列表顺序逐任务匹配 TaskType 相同的任务模板资产（Provider 首个命中，无命中=不兜底）；
                // 任务模板与关卡模板共用同一 requestSeed，各模板用 (seed, TemplateId) 派生独立随机流。
                ApplyTaskTemplates(parse.Level, request.RandomSeed);
                if (string.IsNullOrEmpty(parse.Level.Description))
                    parse.Level.Description = request.Prompt;

                // 4. 规模与主线校验（只警告不裁剪：模板注释约定裁剪职责在校验器，此处生成期提示）
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
            if (_templateManager == null || string.IsNullOrEmpty(request?.TemplateId)) return null;
            return _templateManager.GetTemplateById(request.TemplateId);
        }

        /// <summary>
        /// 任务模板统一收尾（第五周-Day3）：按任务列表顺序逐任务调用匹配任务模板的 FinalizeData。
        /// 匹配规则：取 TaskType 与任务类型相同的第一个任务模板（Provider 注入顺序）；
        /// 无任务模板/无匹配 = 空转（任务原样保留，既有无任务模板的调用方零影响）。
        /// 任务模板收尾在关卡模板收尾之后执行：收集物兜底等追加的 Props 会参与后续规模警告与数据级校验。
        /// </summary>
        private void ApplyTaskTemplates(LevelData level, int requestSeed)
        {
            var templates = _templateManager?.GetTaskTemplates();
            if (templates == null || level?.Tasks == null || level.Tasks.Count == 0) return;

            foreach (var task in level.Tasks)
            {
                if (task == null) continue; // 容错：空任务跳过（解析层已防，此处双保险）
                var taskTemplate = MatchTaskTemplate(templates, task.Type);
                taskTemplate?.FinalizeData(task, level, requestSeed);
            }
        }

        /// <summary> 按任务类型取第一个匹配的任务模板（未命中返回 null） </summary>
        private static TaskTemplate MatchTaskTemplate(IReadOnlyList<TaskTemplate> templates, TaskType type)
        {
            if (templates == null) return null;
            foreach (var t in templates)
                if (t != null && t.TaskType == type)
                    return t;
            return null;
        }

        /// <summary> Prompt 组装：默认 Prompt 模板 + 插值上下文（模板指南/资源清单/开关/种子） </summary>
        private PromptBuildResult BuildPrompt(GenerationRequest request, LevelTemplate template)
        {
            var promptTemplate = _templateManager?.GetDefaultPromptTemplate();
            var resourceNames = _resourceMapper?.GetAllLogicalNames();
            var context = PromptBuilder.CreateContext(request, template, resourceNames);
            return _promptBuilder.Build(promptTemplate, context);
        }

        /// <summary> 调用 API 并提取结构化内容（tool_calls.arguments 优先，content 兜底；两级缓存命中短路） </summary>
        private async Task<string> GenerateRawJsonAsync(GenerationRequest request, LevelTemplate template, PromptBuildResult prompt)
        {
            var seed = request?.RandomSeed ?? 0;
            var templateId = request?.TemplateId ?? string.Empty;
            var promptText = request?.Prompt ?? string.Empty;
            // 模板依赖哈希（资产变更 → 键变 → 自动失效）+ Schema 契约版本（代码级 Schema 变更防旧缓存复用）
            var dependencyHash = _dependencyHashProvider?.GetDependencyHash(template) ?? 0UL;
            var schemaVersion = LevelGenerationSchema.SchemaVersion;

            if (_cache.TryGet(templateId, seed, promptText, dependencyHash, schemaVersion, out var cached))
            {
                UnityEngine.Debug.Log($"[AI Generator] 生成缓存命中（未调用 LLM API）：模板 {templateId} | 种子 {seed}（模板资产未变更时重复生成秒回）");
                return cached; // 缓存命中：重走解析 + 模板确定性收尾，与新鲜请求同路径
            }

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

            _cache.Put(templateId, seed, promptText, dependencyHash, schemaVersion, raw);
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

        /// <summary>
        /// 规模与主线自检 → Warning 转译（第五周-Day4）：模板经 CollectScopeViolations 自检规模约束，
        /// 核心框架只做转译不做类型判断 —— 任意模板类型覆写该方法即获得提示能力，新增模板零改动核心层。
        /// 与 TemplateScopeValidator（Error 级拦截）同码双级：生成期提示不裁剪（裁剪职责在数据级校验）。
        /// </summary>
        private static void ValidateScope(LevelParseResult parse, LevelTemplate template, List<ValidationWarning> warnings)
        {
            if (parse.Level == null || template == null) return;

            var violations = new List<ScopeViolation>();
            template.CollectScopeViolations(parse.Level, violations);
            foreach (var violation in violations)
                warnings.Add(new ValidationWarning
                {
                    Code = violation.Code,
                    Message = violation.Message,
                    DataPath = violation.DataPath
                });
        }
    }
}
