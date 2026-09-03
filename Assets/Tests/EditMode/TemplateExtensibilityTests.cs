using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Interfaces.Templates;
using AILevelGenerator.Runtime.LLM;
using AILevelGenerator.Runtime.Templates;
using AILevelGenerator.Runtime.Utilities;
using AILevelGenerator.Runtime.Validation;
using NUnit.Framework;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 模板扩展性验收单元测试（第五周-Day4 验收项「新增模板无需修改核心框架」）：
    /// 以"核心框架之外"自定义的 LevelTemplate/TaskTemplate 子类（非 Configurable 家族）验证
    /// 三类扩展面 —— ①管理器动态注册后即可被查询/调度；②模板自检规则经既有
    /// LLMGenerator（Warning）/ValidatorRegistry + TemplateScopeValidator（Error）双级消费，
    /// 核心层没有任何 is-类型判断；③任务模板按 TaskType 命中收尾链路。
    /// 本文件全部内存模板，不依赖资产目录；ScriptableObject 统一 CreateInstance，TearDown 销毁。
    /// </summary>
    public class TemplateExtensibilityTests
    {
        private const string 宝箱 = "宝箱";
        private const string 证物Prop = "自定义-证物";

        private readonly List<ScriptableObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var so in _created)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _created.Clear();
        }

        // —— 扩展模板（模拟"新增模板"：只写模板类 + 注册进管理器，核心零改动） ——

        /// <summary> 自定义关卡模板：演示最小扩展面 = ApplyDefaults + CollectScopeViolations 覆写 </summary>
        private class CustomLevelTemplate : LevelTemplate
        {
            public string CustomGuideline = "自定义规则：至少 2 个宝箱（扩展模板演示）";
            public int MinChests = 2;

            public override void ApplyDefaults(LevelData data) { } // 自定义模板无内置默认值约定（最小实现）

            public override string GetGuideline() => CustomGuideline;

            /// <summary> 自检规则：宝箱数量低于模板下限 → 违规（核心框架只做转译，不感知此规则） </summary>
            public override void CollectScopeViolations(LevelData data, IList<ScopeViolation> violations)
            {
                if (data == null || violations == null) return;
                var chests = CountProps(data, 宝箱);
                if (chests < MinChests)
                    violations.Add(new ScopeViolation
                    {
                        Code = "CUSTOM_CHESTS_TOO_FEW",
                        Message = $"宝箱数量 {chests} 低于自定义模板下限 {MinChests}",
                        DataPath = "props"
                    });
            }

            private static int CountProps(LevelData data, string logicalName)
            {
                if (data.Props == null) return 0;
                var count = 0;
                foreach (var p in data.Props)
                    if (p != null && p.PrefabLogicalName == logicalName) count++;
                return count;
            }
        }

        /// <summary> 自定义任务模板：演示最小扩展面 = ApplyDefaults + PostGenerate 覆写 </summary>
        private class CustomTaskTemplate : TaskTemplate
        {
            /// <summary> 收尾证物逻辑名：命中链路时向关卡追加一个该物体（观测点，勿与模板字段混淆） </summary>
            public string EvidencePropName = 证物Prop;

            public override void ApplyDefaults(TaskData task)
            {
                if (task == null) return;
                task.Reward ??= new RewardData();
                task.Reward.Gold = Math.Max(task.Reward.Gold, 100); // 示例默认值：奖励至少 100 金币
            }

            protected override void PostGenerate(TaskData task, LevelData level, DeterministicRandom rng)
            {
                // 可观测收尾：每个命中任务追加一个证物（level 为 null 的"仅默认值路径"跳过）
                if (level?.Props == null) return;
                level.Props.Add(new PropPlacement { PrefabLogicalName = EvidencePropName, Position = Vector3.zero });
            }
        }

        private T NewTemplate<T>() where T : ScriptableObject
        {
            var t = ScriptableObject.CreateInstance<T>();
            _created.Add(t);
            return t;
        }

        // —— 构造辅助 ——

        private CustomLevelTemplate NewCustomLevel(string id = "custom_level", int minChests = 2)
        {
            var t = NewTemplate<CustomLevelTemplate>();
            t.TemplateId = id;
            t.DisplayName = "自定义关卡模板";
            t.MinChests = minChests;
            return t;
        }

        private CustomTaskTemplate NewCustomTask(string id = "custom_collect")
        {
            var t = NewTemplate<CustomTaskTemplate>();
            t.TemplateId = id;
            t.DisplayName = "自定义任务模板";
            t.TaskType = TaskType.Collect;
            return t;
        }

        private static LevelData NewLevelWithProps(params string[] logicalNames)
        {
            var props = new List<PropPlacement>();
            foreach (var name in logicalNames)
                props.Add(new PropPlacement { PrefabLogicalName = name, Position = Vector3.zero });
            return new LevelData { Props = props, Tasks = new List<TaskData>() };
        }

        /// <summary> 罐装 JSON：1 个宝箱 + 1 个收集任务（数量/字段形态与 CollectTemplateTests 一致） </summary>
        private const string JsonOneChestCollect = @"{
            ""level_name"": ""收集山谷"",
            ""terrain"": {""width"": 100, ""length"": 100},
            ""props"": [ {""prefab_logical_name"": ""宝箱"", ""position"": {""x"": -4, ""z"": -4}} ],
            ""tasks"": [ {""task_name"": ""收集金币"", ""description"": ""拾取散落的金币"",
                ""type"": ""collect"", ""objective"": ""collect_items"", ""is_main_task"": true} ]
        }";

        private class FakeDeepSeekClient : IDeepSeekClient
        {
            public int CallCount;
            public Func<DeepSeekChatRequest, DeepSeekChatResponse> Responder;

            public Task<DeepSeekChatResponse> ChatAsync(DeepSeekChatRequest request)
            {
                CallCount++;
                return Task.FromResult(Responder(request));
            }
        }

        private class FakeResourceMapper : IResourceMapper
        {
            public IReadOnlyList<string> GetAllLogicalNames() => new List<string> { 宝箱, "敌人-近战" };
            public GameObject GetPrefab(string logicalName) => null;
            public bool TryGetPrefab(string logicalName, out GameObject prefab) { prefab = null; return false; }
            public GameObject GetPrefabByFuzzy(string keyword) => null;
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

        /// <summary> 全链路生成器：内存 manager（自定义模板池）+ fake 客户端（罐装响应，不碰网络） </summary>
        private LLMGenerator CreateGenerator(TemplateManager manager)
        {
            var client = new FakeDeepSeekClient { Responder = _ => ToolCallResponse(JsonOneChestCollect) };
            return new LLMGenerator(client, () => "test-key", manager, new FakeResourceMapper());
        }

        private static GenerationRequest Request(string templateId) => new()
        {
            Prompt = "设计一个收集关卡",
            TemplateId = templateId,
            RandomSeed = 7
        };

        // —— 验收 1：注册表调度面 ——

        [Test]
        public void 自定义模板_注册进注册表_违规被范围校验器转错误拦截()
        {
            var template = NewCustomLevel();
            var registry = new ValidatorRegistry();
            registry.RegisterForTemplate(template.TemplateId, new TemplateScopeValidator(template));

            var result = registry.Run(ValidationStage.Pre, NewLevelWithProps(宝箱), template.TemplateId);

            Assert.IsFalse(result.IsValid, "自定义规则违规应被核心注册表拦截");
            Assert.AreEqual("CUSTOM_CHESTS_TOO_FEW", result.Errors[0].Code);
            StringAssert.Contains("低于自定义模板下限", result.Errors[0].Message);
            Assert.AreEqual("props", result.Errors[0].DataPath);
        }

        [Test]
        public void 自定义模板_数据合规_校验通过_不误报()
        {
            var template = NewCustomLevel();
            var registry = new ValidatorRegistry();
            registry.RegisterForTemplate(template.TemplateId, new TemplateScopeValidator(template));

            var result = registry.Run(ValidationStage.Pre, NewLevelWithProps(宝箱, 宝箱), template.TemplateId);

            Assert.IsTrue(result.IsValid, "自定义规则满足（2 宝箱 ≥ 下限 2）时应通过");
        }

        [Test]
        public void 自定义模板_数据级Pre校验_按TemplateId隔离_不串扰其他模板()
        {
            var custom = NewCustomLevel("custom_level");
            var registry = new ValidatorRegistry();
            registry.RegisterForTemplate(custom.TemplateId, new TemplateScopeValidator(custom));

            var result = registry.Run(ValidationStage.Pre, NewLevelWithProps(宝箱), "linear"); // 其他模板 ID

            Assert.IsTrue(result.IsValid, "模板专属校验器只拦截自己注册的 TemplateId");
        }

        // —— 验收 2：LLM 生成链路 Warning 面（核心无 is-类型判断） ——

        [Test]
        public async Task 自定义模板_生成链路_违规转Warning_核心零改动()
        {
            var template = NewCustomLevel();
            var manager = new TemplateManager(
                new[] { (LevelTemplate)template },
                new List<TaskTemplate>(),
                new List<PromptTemplate>());
            var generator = CreateGenerator(manager);

            var result = await generator.GenerateAsync(Request(template.TemplateId));

            Assert.IsTrue(result.Success, "自定义规则违规只警告不阻断生成");
            Assert.IsTrue(result.Warnings.Exists(w => w.Code == "CUSTOM_CHESTS_TOO_FEW"),
                "LLMGenerator 应经 CollectScopeViolations 转译出自定义模板的警告（无需类型判断）");
            Assert.IsTrue(result.Warnings.Exists(w => w.Code == "CUSTOM_CHESTS_TOO_FEW" && w.DataPath == "props"),
                "警告应携带模板给出的字段定位");
        }

        // —— 验收 3：自定义任务模板按 TaskType 命中收尾链路 ——

        [Test]
        public async Task 自定义任务模板_注册管理器_生成链路按TaskType命中收尾()
        {
            var levelTemplate = NewCustomLevel();
            var taskTemplate = NewCustomTask();
            var manager = new TemplateManager(
                new[] { (LevelTemplate)levelTemplate },
                new List<TaskTemplate> { taskTemplate },
                new List<PromptTemplate>());
            var generator = CreateGenerator(manager);

            var result = await generator.GenerateAsync(Request(levelTemplate.TemplateId));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(TaskType.Collect, result.Tasks[0].Type);
            var evidenceCount = 0;
            foreach (var p in result.LevelData.Props)
                if (p != null && p.PrefabLogicalName == 证物Prop) evidenceCount++;
            Assert.AreEqual(1, evidenceCount, "自定义任务模板 FinalizeData 应被既有链路命中（按 TaskType 匹配，无需核心改动）");
            Assert.GreaterOrEqual(result.Tasks[0].Reward?.Gold ?? 0, 100, "自定义任务默认值（奖励兜底）应被应用");
        }

        // —— 验收 4：运行期注销即时生效（无需重载域/重启） ——

        [Test]
        public async Task 注销自定义模板_生成链路立即停止命中()
        {
            var levelTemplate = NewCustomLevel();
            var manager = new TemplateManager(
                new[] { (LevelTemplate)levelTemplate },
                new List<TaskTemplate>(),
                new List<PromptTemplate>());
            var generator = CreateGenerator(manager);

            var before = await generator.GenerateAsync(Request(levelTemplate.TemplateId));
            Assert.IsTrue(before.Warnings.Exists(w => w.Code == "CUSTOM_CHESTS_TOO_FEW"), "注册期应产生自定义警告");

            Assert.IsTrue(manager.UnregisterLevelTemplate(levelTemplate.TemplateId), "注销应返回 true");

            var after = await generator.GenerateAsync(Request(levelTemplate.TemplateId));
            Assert.IsFalse(after.Warnings.Exists(w => w.Code == "CUSTOM_CHESTS_TOO_FEW"),
                "注销后模板不再参与生成链路（管理器动态语义，核心零改动）");
        }

        // —— 验收 5：管理器混合池（自定义 + 资产加载）可共存查询 ——

        [Test]
        public void 管理器_自定义与真实资产模板_混合注册可共存()
        {
            var custom = NewCustomLevel("custom_level");
            var manager = new TemplateManager(new TemplateAssetSource());
            Assert.IsTrue(manager.Reload(), "真实资产源加载应成功");
            manager.RegisterLevelTemplate(custom); // 资产池之外追加自定义模板

            Assert.IsNotNull(manager.GetTemplateById("linear"), "资产模板应保留");
            Assert.AreSame(custom, manager.GetTemplateById("custom_level"), "自定义模板应可查");
        }
    }
}
