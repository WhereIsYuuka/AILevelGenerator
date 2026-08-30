using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Interfaces.Templates;
using AILevelGenerator.Runtime.LLM;
using AILevelGenerator.Runtime.Templates;
using NUnit.Framework;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// LLM 生成器全链路单元测试：fake IDeepSeekClient + 内存模板（不碰真实网络）。
    /// 覆盖：完整链路产出 / 无 key 短路 / API 异常转错误 / 缓存命中不重复调用 /
    /// 规模与主线校验 warning / 模板默认值应用 / 空响应失败。
    /// </summary>
    public class LLMGeneratorTests
    {
        private const string SampleJson = @"{
            ""level_name"": ""森林营地"",
            ""terrain"": {""width"": 120, ""length"": 80},
            ""props"": [ {""prefab_logical_name"": ""宝箱"", ""position"": {""x"": 3} } ],
            ""tasks"": [ {""task_name"": ""击败狼群"", ""type"": ""kill"", ""is_main_task"": true} ]
        }";

        /// <summary> 可注入的 fake 客户端：记录调用次数，按回调返回响应或抛异常 </summary>
        private class FakeDeepSeekClient : IDeepSeekClient
        {
            public int CallCount;
            public DeepSeekChatRequest LastRequest;
            public Func<DeepSeekChatRequest, DeepSeekChatResponse> Responder;

            public Task<DeepSeekChatResponse> ChatAsync(DeepSeekChatRequest request)
            {
                CallCount++;
                LastRequest = request;
                return Task.FromResult(Responder(request));
            }
        }

        /// <summary> 内存资源映射（Prompt 资源清单数据源） </summary>
        private class FakeResourceMapper : IResourceMapper
        {
            private readonly List<string> _names;
            public FakeResourceMapper(params string[] names) => _names = new List<string>(names);

            public IReadOnlyList<string> GetAllLogicalNames() => _names;
            public GameObject GetPrefab(string logicalName) => null;
            public bool TryGetPrefab(string logicalName, out GameObject prefab) { prefab = null; return false; }
            public GameObject GetPrefabByFuzzy(string keyword) => null;
        }

        private readonly List<ScriptableObject> _created = new();
        private FakeDeepSeekClient _client;
        private LLMGenerator _generator;

        [SetUp]
        public void SetUp()
        {
            _client = new FakeDeepSeekClient
            {
                Responder = _ => ToolCallResponse(SampleJson)
            };
            _generator = CreateGenerator();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var so in _created)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _created.Clear();
        }

        /// <summary> 构造生成器：内存模板 + 默认 key + 无缓存隔离 </summary>
        private LLMGenerator CreateGenerator(
            string templateId = null,
            Func<string> keyProvider = null,
            GenerationCache cache = null,
            bool useJsonMode = true)
        {
            var levelTemplate = NewTemplate<ConfigurableLevelTemplate>();
            levelTemplate.TemplateId = templateId ?? "tpl1";
            levelTemplate.DisplayName = "测试模板";
            levelTemplate.Guideline = "以森林为背景";

            var promptTemplate = NewTemplate<PromptTemplate>();
            promptTemplate.SystemPromptTemplate = "你是资深关卡设计师，输出 JSON。{templateGuideline}";
            promptTemplate.UserPromptTemplate = "需求：{userPrompt} 可用物体：{resourceList} {seed}";

            var provider = new TemplateProvider(
                new[] { levelTemplate },
                Array.Empty<TaskTemplate>(),
                new[] { promptTemplate });

            return new LLMGenerator(
                _client,
                keyProvider ?? (() => "test-key"),
                provider,
                new FakeResourceMapper("敌人-弓箭手", "宝箱", "NPC"),
                cache,
                useJsonMode);
        }

        private T NewTemplate<T>() where T : ScriptableObject
        {
            var t = ScriptableObject.CreateInstance<T>();
            _created.Add(t);
            return t;
        }

        private static DeepSeekChatResponse ToolCallResponse(string arguments) => new()
        {
            Choices = new List<DeepSeekChoice>
            {
                new()
                {
                    Message = new DeepSeekResponseMessage
                    {
                        Role = "assistant",
                        ToolCalls = new List<DeepSeekToolCall>
                        {
                            new() { FunctionName = "generate_level", Arguments = arguments }
                        }
                    },
                    FinishReason = "tool_calls"
                }
            }
        };

        private static GenerationRequest SimpleRequest(string prompt = "设计一个森林营地") => new()
        {
            Prompt = prompt,
            TemplateId = "tpl1",
            RandomSeed = 7
        };

        // —— 完整链路 ——

        [Test]
        public async Task 完整链路_解析成功_产出关卡数据()
        {
            var result = await _generator.GenerateAsync(SimpleRequest());

            Assert.IsTrue(result.Success, "合法响应应生成成功");
            Assert.IsNotNull(result.LevelData);
            Assert.AreEqual("森林营地", result.LevelData.LevelName);
            Assert.AreEqual(100, result.LevelData.Terrain.Width, "OverrideTerrain 模板地形默认值覆盖 LLM 输出的 120");
            Assert.AreEqual(1, result.LevelData.Props.Count);
            Assert.AreEqual("宝箱", result.LevelData.Props[0].PrefabLogicalName);
            Assert.AreEqual(1, result.Tasks.Count);
            Assert.AreEqual(TaskType.Kill, result.Tasks[0].Type);
            Assert.IsNotEmpty(result.RawLLMResponse);
            Assert.IsTrue(result.GenerationTime >= 0f);
        }

        [Test]
        public async Task 完整链路_请求包含工具与强制调用与资源enum()
        {
            await _generator.GenerateAsync(SimpleRequest());

            Assert.IsNotNull(_client.LastRequest);
            Assert.IsNotNull(_client.LastRequest.Tools, "必须携带 function calling 工具");
            Assert.AreEqual("generate_level", _client.LastRequest.Tools[0].Function.Name);
            StringAssert.Contains("generate_level", _client.LastRequest.ToolChoiceJson, "必须强制调用工具");
            StringAssert.Contains("json_object", _client.LastRequest.ResponseFormatJson, "默认开启 JSON 模式双约束");
            StringAssert.Contains("宝箱", _client.LastRequest.Tools[0].Function.ParametersJson, "资源逻辑名必须注入 Schema enum");
        }

        [Test]
        public async Task 完整链路_描述为空_回填用户提示词()
        {
            var result = await _generator.GenerateAsync(SimpleRequest("设计一个雪山村庄"));

            Assert.AreEqual("设计一个雪山村庄", result.LevelData.Description, "描述缺省时应回填用户提示词");
        }

        // —— 无 key 短路 ——

        [Test]
        public async Task 未配置Key_返回错误_不发起请求()
        {
            var generator = CreateGenerator(keyProvider: () => "");
            var result = await generator.GenerateAsync(SimpleRequest());

            Assert.IsFalse(result.Success);
            Assert.AreEqual("NO_API_KEY", result.Errors[0].Code);
            StringAssert.Contains("API Key", result.Errors[0].Message);
            Assert.AreEqual(0, _client.CallCount, "无 key 时不应发起 API 请求");
        }

        // —— API 异常 ——

        [Test]
        public async Task API异常_转为失败结果_含中文提示()
        {
            _client.Responder = _ => throw new NetworkException("网络请求失败：无法连接到 DeepSeek 服务");

            var result = await _generator.GenerateAsync(SimpleRequest());

            Assert.IsFalse(result.Success);
            Assert.AreEqual("LLM_ERROR", result.Errors[0].Code);
            StringAssert.Contains("网络请求失败", result.Errors[0].Message);
        }

        [Test]
        public async Task 模型未返回内容_失败并提示()
        {
            _client.Responder = _ => new DeepSeekChatResponse(); // 无 choices

            var result = await _generator.GenerateAsync(SimpleRequest());

            Assert.IsFalse(result.Success);
            StringAssert.Contains("未返回可解析", result.Errors[0].Message);
        }

        // —— 缓存 ——

        [Test]
        public async Task 缓存命中_同参二次_不重复调用API()
        {
            var first = await _generator.GenerateAsync(SimpleRequest());
            var second = await _generator.GenerateAsync(SimpleRequest());

            Assert.IsTrue(first.Success && second.Success);
            Assert.AreEqual(1, _client.CallCount, "同参二次应命中缓存");
            Assert.AreEqual(first.LevelData.LevelName, second.LevelData.LevelName);
        }

        [Test]
        public async Task 缓存未命中_参数变化_重新调用API()
        {
            await _generator.GenerateAsync(SimpleRequest());
            await _generator.GenerateAsync(SimpleRequest("另一个关卡"));

            Assert.AreEqual(2, _client.CallCount, "提示词不同不应命中缓存");
        }

        [Test]
        public async Task 缓存不缓存失败结果()
        {
            _client.Responder = _ => throw new NetworkException("网络波动");
            var first = await _generator.GenerateAsync(SimpleRequest());
            _client.Responder = _ => ToolCallResponse(SampleJson);
            var second = await _generator.GenerateAsync(SimpleRequest());

            Assert.IsFalse(first.Success);
            Assert.IsTrue(second.Success, "失败后重试应重新调用（不缓存失败）");
            Assert.AreEqual(2, _client.CallCount);
        }

        // —— 模板校验 ——

        [Test]
        public async Task 规模超限_生成成功但带警告()
        {
            var generator = CreateGeneratorWithScope(propMin: 5, propMax: 10, forceMainTask: false);
            var result = await generator.GenerateAsync(SimpleRequest());

            Assert.IsTrue(result.Success, "数量越界只警告不阻断");
            Assert.IsTrue(result.Warnings.Exists(w => w.Code == "PROPS_TOO_FEW"), "道具数量低于下限应有警告");
        }

        [Test]
        public async Task 缺主线任务_带警告()
        {
            var generator = CreateGeneratorWithScope(forceMainTask: true);
            _client.Responder = _ => ToolCallResponse(
                "{\"level_name\":\"营地\",\"tasks\":[{\"task_name\":\"支线\",\"is_main_task\":false}]}");

            var result = await generator.GenerateAsync(SimpleRequest());

            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.Warnings.Exists(w => w.Code == "NO_MAIN_TASK"), "模板要求主线时应警告");
        }

        private LLMGenerator CreateGeneratorWithScope(int propMin = 0, int propMax = 0, bool forceMainTask = true)
        {
            var levelTemplate = NewTemplate<ConfigurableLevelTemplate>();
            levelTemplate.TemplateId = "tpl1";
            levelTemplate.MinPropCount = propMin;
            levelTemplate.MaxPropCount = propMax;
            levelTemplate.ForceMainTask = forceMainTask;

            var provider = new TemplateProvider(
                new[] { levelTemplate },
                Array.Empty<TaskTemplate>(),
                new[] { NewTemplate<PromptTemplate>() });

            return new LLMGenerator(_client, () => "test-key", provider, new FakeResourceMapper("宝箱"));
        }

        // —— JSON 模式开关 ——

        [Test]
        public async Task JSON模式关闭_请求不带response_format()
        {
            var generator = CreateGenerator(useJsonMode: false);
            await generator.GenerateAsync(SimpleRequest());

            Assert.IsNull(_client.LastRequest.ResponseFormatJson, "useJsonMode=false 时不应带 response_format");
            Assert.IsNotNull(_client.LastRequest.ToolChoiceJson, "function calling 单约束仍在");
        }
    }
}
