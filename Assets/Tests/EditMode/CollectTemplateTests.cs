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
// Runtime.Data.TerrainData 与 UnityEngine.TerrainData 同名 → 本地别名消歧
using TerrainData = AILevelGenerator.Runtime.Data.TerrainData;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 收集任务模板单元测试（第五周-Day3）：
    /// 1. 模板级 —— ConfigurableTaskTemplate 收集扩展：收集物（金币/道具）确定性散布兜底 ——
    ///    相同种子+相同输入 → 完全一致；不同种子 → 明显不同；LLM 已产出的收集物不覆盖只补齐；
    ///    数量/环形范围/间距/边界夹取均符合配置参数（地形贴合 y=0 水平散点由场景构建层 FitToGround 完成）。
    /// 2. 链路级 —— LLMGenerator 任务模板接入（按 TaskType 匹配，无匹配=不兜底）。
    /// </summary>
    public class CollectTemplateTests
    {
        private const string 金币 = "金币";
        private const string 药水 = "道具-生命药水";
        private const string 宝箱 = "宝箱";

        private readonly List<ScriptableObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var so in _created)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _created.Clear();
        }

        // —— 构造辅助 ——

        /// <summary>
        /// 基础收集任务模板：金币(权重2) + 生命药水(权重1) 混合、数量下限 12、
        /// 环形范围 6~30、间距 2.5、边距 3。奖励兜底：30 金币 + 触发条件「拾取物品」。
        /// </summary>
        private ConfigurableTaskTemplate NewCollectTemplate()
        {
            var t = ScriptableObject.CreateInstance<ConfigurableTaskTemplate>();
            _created.Add(t);
            t.TemplateId = "collect_tpl";
            t.DisplayName = "收集任务";
            t.TaskType = TaskType.Collect;
            t.DefaultReward = new RewardData { Gold = 30 };
            t.DefaultTriggerCondition = "拾取物品";
            t.CollectibleOptions = new List<CollectibleTypeOption>
            {
                new() { LogicalName = 金币, Weight = 2f },
                new() { LogicalName = 药水, Weight = 1f }
            };
            t.MinCollectibleCount = 12;
            t.CollectSpawnRingMin = 6f;
            t.CollectSpawnRingMax = 30f;
            t.CollectMinSpacing = 2.5f;
            t.CollectBoundsMargin = 3f;
            return t;
        }

        /// <summary> 纯金币单类型模板（类型断言场景用） </summary>
        private ConfigurableTaskTemplate NewSingleTypeTemplate()
        {
            var t = NewCollectTemplate();
            t.CollectibleOptions = new List<CollectibleTypeOption> { new() { LogicalName = 金币, Weight = 1f } };
            return t;
        }

        /// <summary> 带收集任务与可选初始物体的关卡（含任务槽，用于模板级 FinalizeData） </summary>
        private static LevelData NewLevelWithTask(TaskData task, params PropPlacement[] props)
        {
            var level = NewLevel(props);
            if (task != null) level.Tasks.Add(task);
            return level;
        }

        /// <summary> 100×100 地形 + 出生点在原点 + 可选初始实体 </summary>
        private static LevelData NewLevel(params PropPlacement[] props)
        {
            var level = new LevelData
            {
                PlayerStartPosition = Vector3.zero,
                Terrain = new TerrainData { Width = 100, Length = 100, HeightScale = 8f },
                Props = new List<PropPlacement>(props),
                Tasks = new List<TaskData>()
            };
            return level;
        }

        private static TaskData CollectTask() => new() { TaskID = "t-collect", Type = TaskType.Collect };

        private static PropPlacement Collectible(string name, float x, float z) =>
            new() { PrefabLogicalName = name, Position = new Vector3(x, 0f, z) };

        private static PropPlacement 宝箱位置(float x, float z) => Collectible(宝箱, x, z);

        private static int CountCollectibles(LevelData level, string name)
        {
            var count = 0;
            foreach (var prop in level.Props)
                if (prop != null && prop.PrefabLogicalName == name) count++;
            return count;
        }

        /// <summary> 逐字节 JSON 对比（含 Props 全部字段） </summary>
        private static string ToJson(LevelData level) => JsonUtility.ToJson(level);

        // —— 确定性 ——

        [Test]
        public void 收集散布_同种子两遍_关卡数据完全一致()
        {
            var template = NewSingleTypeTemplate();
            var levelA = NewLevelWithTask(CollectTask());
            var levelB = NewLevelWithTask(CollectTask());

            template.FinalizeData(levelA.Tasks[0], levelA, 42);
            template.FinalizeData(levelB.Tasks[0], levelB, 42);

            Assert.AreEqual(ToJson(levelA), ToJson(levelB), "同种子+同输入必须产出逐字节一致的关卡数据");
        }

        [Test]
        public void 收集散布_异种子_数量一致但位置不同()
        {
            var template = NewSingleTypeTemplate();
            var levelA = NewLevelWithTask(CollectTask());
            var levelB = NewLevelWithTask(CollectTask());

            template.FinalizeData(levelA.Tasks[0], levelA, 1);
            template.FinalizeData(levelB.Tasks[0], levelB, 2);

            Assert.AreEqual(12, CountCollectibles(levelA, 金币), "异种子不应影响数量（数量由配置决定）");
            Assert.AreEqual(12, CountCollectibles(levelB, 金币));
            var same = 0;
            for (var i = 0; i < levelA.Props.Count; i++)
                if (levelA.Props[i].Position == levelB.Props[i].Position) same++;
            Assert.Less(same, levelA.Props.Count, "异种子下绝大多数收集物位置应不同");
        }

        // —— 兜底语义：只补齐不覆盖 ——

        [Test]
        public void 收集散布_LLM自带收集物_只补齐不覆盖()
        {
            var template = NewSingleTypeTemplate();
            // LLM 已产出 3 枚金币（远在环带之外的位置也应原样保留）+ 1 个宝箱
            var llmCoins = new[]
            {
                Collectible(金币, 60f, 60f),
                Collectible(金币, -58f, 40f),
                Collectible(金币, 50f, -45f),
                Collectible(宝箱, -4f, -4f)
            };
            var level = NewLevelWithTask(CollectTask(), llmCoins);

            template.FinalizeData(level.Tasks[0], level, 42);

            Assert.AreEqual(12, CountCollectibles(level, 金币), "命中清单的 LLM 收集物 + 兜底补齐应达到数量下限");
            Assert.AreEqual(1, CountCollectibles(level, 宝箱), "无关实体不得被增删");
            Assert.AreEqual(13, level.Props.Count, "LLM 4 个实体 + 补齐 9 个金币");
            for (var i = 0; i < llmCoins.Length; i++)
                Assert.AreEqual(llmCoins[i].Position, level.Props[i].Position,
                    "LLM 已产出实体必须原样保留（追加一律在尾部），第 {0} 个被改动", i);
        }

        [Test]
        public void 收集散布_LLM已达标_不再补齐不裁剪()
        {
            var template = NewSingleTypeTemplate();
            // LLM 给出 15 枚金币 > 下限 12：数量由 LLM 决定（越界由校验器负责），模板只提示不裁剪
            var many = new List<PropPlacement>();
            for (var i = 0; i < 15; i++) many.Add(Collectible(金币, i * 2f, 0f));
            var level = NewLevelWithTask(CollectTask(), many.ToArray());

            template.FinalizeData(level.Tasks[0], level, 42);

            Assert.AreEqual(15, CountCollectibles(level, 金币), "已达标时不得再补，也不得裁剪 LLM 内容");
        }

        // —— 数量与位置符合配置参数 ——

        [Test]
        public void 收集散布_数量与环形范围_符合配置参数()
        {
            var template = NewSingleTypeTemplate();
            var level = NewLevelWithTask(CollectTask());

            template.FinalizeData(level.Tasks[0], level, 42);

            Assert.AreEqual(12, CountCollectibles(level, 金币), "数量必须等于配置下限（LLM 未产出时）");
            for (var i = 0; i < level.Props.Count; i++)
            {
                var prop = level.Props[i];
                var dist = new Vector2(prop.Position.x, prop.Position.z).magnitude;
                Assert.GreaterOrEqual(dist, 6f - 0.05f, "收集物不得低于环形内径");
                Assert.LessOrEqual(dist, 30f + 0.05f, "收集物不得超出环形外径");
                Assert.AreEqual(0f, prop.Position.y, "模板产出水平散点（y=0），地面贴合由场景构建层完成");
                Assert.AreEqual(0f, prop.Rotation.x, "收集物仅绕 Y 轴随机朝向");
                Assert.AreEqual(0f, prop.Rotation.z);
                Assert.GreaterOrEqual(prop.Rotation.y, 0f);
                Assert.LessOrEqual(prop.Rotation.y, 360f);
                Assert.AreEqual(Vector3.one, prop.Scale, "默认单位缩放");
            }
        }

        [Test]
        public void 收集散布_边界夹取_不穿出地形()
        {
            var template = NewSingleTypeTemplate();
            // 24×24 小地形 + 边距 3 → 有效范围 |x|,|z| ≤ 9；环形外径 40 保证存在越界候选被夹回
            template.CollectSpawnRingMax = 40f;
            var level = NewLevelWithTask(CollectTask());
            level.Terrain.Width = 24;
            level.Terrain.Length = 24;

            template.FinalizeData(level.Tasks[0], level, 42);

            Assert.AreEqual(12, CountCollectibles(level, 金币));
            for (var i = 0; i < level.Props.Count; i++)
            {
                Assert.LessOrEqual(Mathf.Abs(level.Props[i].Position.x), 9f + 0.001f, "不得穿出地形边界（含边距）");
                Assert.LessOrEqual(Mathf.Abs(level.Props[i].Position.z), 9f + 0.001f, "不得穿出地形边界（含边距）");
            }
        }

        [Test]
        public void 收集散布_混合类型_选型命中清单且权重生效()
        {
            var template = NewCollectTemplate(); // 金币 2 : 药水 1
            var totalCoin = 0;
            var totalPotion = 0;
            for (var seed = 42; seed < 47; seed++)
            {
                var level = NewLevelWithTask(CollectTask());
                template.FinalizeData(level.Tasks[0], level, seed);
                foreach (var prop in level.Props)
                {
                    Assert.IsTrue(prop.PrefabLogicalName == 金币 || prop.PrefabLogicalName == 药水,
                        "选型结果必须来自配置的收集物清单");
                    if (prop.PrefabLogicalName == 金币) totalCoin++;
                    else totalPotion++;
                }
            }
            Assert.AreEqual(60, totalCoin + totalPotion, "5 种子 × 12 枚全部落位");
            Assert.Greater(totalCoin, totalPotion, "金币权重(2)>药水(1)，合计抽取必须更多（确定性统计）");
        }

        // —— 间距语义 ——

        [Test]
        public void 收集散布_间距合理时_收集物两两保持最小间距()
        {
            var template = NewSingleTypeTemplate();
            template.CollectMinSpacing = 1f;
            var level = NewLevelWithTask(CollectTask());

            template.FinalizeData(level.Tasks[0], level, 42);

            Assert.AreEqual(12, CountCollectibles(level, 金币));
            for (var i = 0; i < level.Props.Count; i++)
                for (var j = i + 1; j < level.Props.Count; j++)
                {
                    var d = Vector2.Distance(
                        new Vector2(level.Props[i].Position.x, level.Props[i].Position.z),
                        new Vector2(level.Props[j].Position.x, level.Props[j].Position.z));
                    Assert.GreaterOrEqual(d, 1f - 0.01f, "拒绝采样下收集物之间必须保持最小间距");
                }
        }

        [Test]
        public void 收集散布_间距过大致无解_数量优先放宽仍必然达成()
        {
            var template = NewSingleTypeTemplate();
            template.CollectMinSpacing = 40f; // 环形 6~30 内 12 点两两 40m 无解 → 尝试上限后接受末候选
            var level = NewLevelWithTask(CollectTask());

            Assert.DoesNotThrow(() => template.FinalizeData(level.Tasks[0], level, 42));
            Assert.AreEqual(12, CountCollectibles(level, 金币), "间距无解时数量仍必须达成（不因拒绝采样死循环）");
        }

        // —— 开关语义 / 多任务共享下限 ——

        [Test]
        public void 收集物清单为空_功能关闭零影响()
        {
            var template = NewSingleTypeTemplate();
            template.CollectibleOptions.Clear(); // 总开关关闭（MinCollectibleCount 残留配置也须零影响）
            var level = NewLevelWithTask(CollectTask(), 宝箱位置(-3f, -3f));

            template.FinalizeData(level.Tasks[0], level, 42);

            Assert.AreEqual(1, level.Props.Count, "关闭收集清单时不得增删任何物体");
        }

        [Test]
        public void 多个收集任务_共享关卡数量下限_不重复散布()
        {
            var template = NewSingleTypeTemplate();
            var level = NewLevelWithTask(CollectTask());
            level.Tasks.Add(CollectTask());

            template.FinalizeData(level.Tasks[0], level, 42);
            template.FinalizeData(level.Tasks[1], level, 42); // 第二任务见已达标 → 空转

            Assert.AreEqual(12, CountCollectibles(level, 金币),
                "收集物为关卡级实体，数量下限是关卡维度：第二任务不应叠加补位");

            // 与单任务关卡（任务槽相同 → 子流一致）逐位置一致
            var single = NewLevelWithTask(CollectTask());
            template.FinalizeData(single.Tasks[0], single, 42);
            Assert.AreEqual(single.Props.Count, level.Props.Count);
            for (var i = 0; i < single.Props.Count; i++)
                Assert.AreEqual(single.Props[i].Position, level.Props[i].Position);
        }

        // —— 自校验 ——

        [Test]
        public void ValidateSelf_半径倒挂_拒绝()
        {
            var template = NewSingleTypeTemplate();
            template.CollectSpawnRingMin = 30f;
            template.CollectSpawnRingMax = 6f;

            Assert.IsFalse(template.ValidateSelf(out var error));
            StringAssert.Contains("倒挂", error);
        }

        [Test]
        public void ValidateSelf_配置数量但清单为空_拒绝()
        {
            var template = NewCollectTemplate();
            template.CollectibleOptions.Clear();

            Assert.IsFalse(template.ValidateSelf(out var error));
            StringAssert.Contains("CollectibleOptions", error);
        }

        [Test]
        public void ValidateSelf_合法收集配置_通过()
        {
            var template = NewCollectTemplate();
            Assert.IsTrue(template.ValidateSelf(out _));
        }

        [Test]
        public void 仅默认值路径_无关卡上下文_不散布不抛()
        {
            var template = NewSingleTypeTemplate();
            var task = CollectTask();
            var plain = new LevelData(); // level 非空但任务不在列表中（引用缺失路径）
            Assert.DoesNotThrow(() => template.FinalizeData(task, plain, 42));
            Assert.DoesNotThrow(() => template.FinalizeData(task, null, 42), "level 为 null 必须容忍");
        }
    }

    /// <summary> 链路级用例：LLMGenerator 任务模板接入（fake 客户端 + 罐装 JSON，不碰真实网络） </summary>
    public class CollectTemplateChainTests
    {
        private const string 金币 = "金币";
        private const string 宝箱 = "宝箱";

        /// <summary> 罐装 JSON：1 个宝箱 + 收集主任务（LLM 未产出任何收集物） </summary>
        private const string CollectJson = @"{
            ""level_name"": ""收集山谷"",
            ""terrain"": {""width"": 100, ""length"": 100},
            ""props"": [ {""prefab_logical_name"": ""宝箱"", ""position"": {""x"": -4, ""z"": -4}} ],
            ""tasks"": [ {""task_name"": ""收集金币"", ""description"": ""拾取散落的金币"",
                ""type"": ""collect"", ""objective"": ""collect_items"", ""is_main_task"": true} ]
        }";

        /// <summary> 罐装 JSON：LLM 自带 3 枚金币（测试只补齐不覆盖） </summary>
        private const string CollectJsonWithCoins = @"{
            ""level_name"": ""收集山谷"",
            ""terrain"": {""width"": 100, ""length"": 100},
            ""props"": [
                {""prefab_logical_name"": ""金币"", ""position"": {""x"": 60, ""z"": 60}},
                {""prefab_logical_name"": ""金币"", ""position"": {""x"": -58, ""z"": 40}},
                {""prefab_logical_name"": ""金币"", ""position"": {""x"": 50, ""z"": -45}},
                {""prefab_logical_name"": ""宝箱"", ""position"": {""x"": -4, ""z"": -4}}
            ],
            ""tasks"": [ {""task_name"": ""收集金币"", ""description"": ""拾取散落的金币"",
                ""type"": ""collect"", ""objective"": ""collect_items"", ""is_main_task"": true} ]
        }";

        /// <summary> 罐装 JSON：击杀 + 收集双任务（击杀任务也缺默认字段，验证按类型匹配各自收尾） </summary>
        private const string MixedTasksJson = @"{
            ""level_name"": ""混合关卡"",
            ""terrain"": {""width"": 100, ""length"": 100},
            ""props"": [ {""prefab_logical_name"": ""宝箱"", ""position"": {""x"": -4, ""z"": -4}} ],
            ""tasks"": [
                {""task_name"": ""清除敌人"", ""type"": ""kill"", ""objective"": ""count"", ""is_main_task"": true},
                {""task_name"": ""收集金币"", ""description"": ""拾取散落的金币"",
                 ""type"": ""collect"", ""objective"": ""collect_items"", ""is_main_task"": false}
            ]
        }";

        /// <summary> 可注入的 fake 客户端（记录最近请求，按回调返回） </summary>
        private class FakeDeepSeekClient : IDeepSeekClient
        {
            public DeepSeekChatRequest LastRequest;
            public Func<DeepSeekChatRequest, DeepSeekChatResponse> Responder;

            public Task<DeepSeekChatResponse> ChatAsync(DeepSeekChatRequest request)
            {
                LastRequest = request;
                return Task.FromResult(Responder(request));
            }
        }

        /// <summary> 内存资源映射（仅提供逻辑名清单） </summary>
        private class FakeResourceMapper : IResourceMapper
        {
            public IReadOnlyList<string> GetAllLogicalNames() => new[] { "敌人-近战", 金币, 宝箱 };
            public GameObject GetPrefab(string logicalName) => null;
            public bool TryGetPrefab(string logicalName, out GameObject prefab) { prefab = null; return false; }
            public GameObject GetPrefabByFuzzy(string keyword) => null;
        }

        private readonly List<ScriptableObject> _created = new();
        private FakeDeepSeekClient _client;

        [TearDown]
        public void TearDown()
        {
            foreach (var so in _created)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _created.Clear();
        }

        [SetUp]
        public void SetUp()
        {
            _client = new FakeDeepSeekClient { Responder = _ => ToolCallResponse(CollectJson) };
        }

        // —— 构造辅助 ——

        private ConfigurableTaskTemplate NewTemplate(TaskType type, string id, int gold)
        {
            var t = ScriptableObject.CreateInstance<ConfigurableTaskTemplate>();
            _created.Add(t);
            t.TemplateId = id;
            t.DisplayName = type == TaskType.Kill ? "击杀任务" : "收集任务";
            t.TaskType = type;
            t.DefaultReward = new RewardData { Gold = gold };
            t.DefaultTriggerCondition = type == TaskType.Kill ? "击败敌人" : "拾取物品";
            return t;
        }

        /// <summary> 单金币类型收集任务模板（数量下限 12，环形 6~30） </summary>
        private ConfigurableTaskTemplate NewCollectTemplate()
        {
            var t = NewTemplate(TaskType.Collect, "collect_tpl", 30);
            t.CollectibleOptions = new List<CollectibleTypeOption> { new() { LogicalName = 金币, Weight = 1f } };
            t.MinCollectibleCount = 12;
            t.CollectSpawnRingMin = 6f;
            t.CollectSpawnRingMax = 30f;
            t.CollectMinSpacing = 2.5f;
            t.CollectBoundsMargin = 3f;
            return t;
        }

        private LLMGenerator CreateChainGenerator(params TaskTemplate[] taskTemplates)
        {
            var levelTemplate = ScriptableObject.CreateInstance<ConfigurableLevelTemplate>();
            _created.Add(levelTemplate);
            levelTemplate.TemplateId = "tpl1";
            levelTemplate.DisplayName = "测试模板";
            levelTemplate.OverrideTerrain = false; // 地形尺寸以罐装 JSON 为准

            var promptTemplate = ScriptableObject.CreateInstance<PromptTemplate>();
            _created.Add(promptTemplate);
            promptTemplate.SystemPromptTemplate = "你是资深关卡设计师，输出 JSON。";
            promptTemplate.UserPromptTemplate = "需求：{userPrompt}";

            var manager = new TemplateManager(
                new[] { levelTemplate }, taskTemplates, new[] { promptTemplate });

            return new LLMGenerator(
                _client, () => "test-key", manager, new FakeResourceMapper());
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

        private static GenerationRequest SimpleRequest() => new()
        {
            Prompt = "设计一个收集金币的关卡",
            TemplateId = "tpl1",
            RandomSeed = 7
        };

        private static int CountByName(LevelData level, string name)
        {
            var count = 0;
            foreach (var prop in level.Props)
                if (prop != null && prop.PrefabLogicalName == name) count++;
            return count;
        }

        /// <summary> 奖励是否处于"未配置"态：全 0 且无物品（解析层缺省 reward = 空对象而非 null） </summary>
        private static void AssertRewardUnset(RewardData reward)
        {
            Assert.IsNotNull(reward);
            Assert.AreEqual(0, reward.Experience);
            Assert.AreEqual(0, reward.Gold);
            Assert.IsEmpty(reward.ItemRewards);
        }

        // —— 链路用例 ——

        [Test]
        public async Task 链路_Collect任务_金币确定性补齐到配置下限()
        {
            var generator = CreateChainGenerator(NewCollectTemplate());

            var result = await generator.GenerateAsync(SimpleRequest());

            Assert.IsTrue(result.Success, "收集兜底不应阻断生成链路");
            var level = result.LevelData;
            Assert.AreEqual(1, CountByName(level, 宝箱), "LLM 宝箱必须保留");
            Assert.AreEqual(12, CountByName(level, 金币), "LLM 未产出收集物时模板应补齐到 MinCollectibleCount=12");
            Assert.AreEqual(13, level.Props.Count);
            Assert.AreEqual(TaskType.Collect, result.Tasks[0].Type);
            foreach (var prop in level.Props)
                if (prop.PrefabLogicalName == 金币)
                {
                    var dist = new Vector2(prop.Position.x, prop.Position.z).magnitude;
                    Assert.GreaterOrEqual(dist, 6f - 0.05f, "链路产物同样受环形范围约束");
                    Assert.LessOrEqual(dist, 30f + 0.05f);
                }
        }

        [Test]
        public async Task 链路_LLM自带金币_只补齐不覆盖()
        {
            var generator = CreateChainGenerator(NewCollectTemplate());
            _client.Responder = _ => ToolCallResponse(CollectJsonWithCoins);

            var result = await generator.GenerateAsync(SimpleRequest());

            Assert.IsTrue(result.Success);
            Assert.AreEqual(12, CountByName(result.LevelData, 金币), "LLM 3 枚 + 补齐 9 枚 = 下限 12");
            Assert.AreEqual(1, CountByName(result.LevelData, 宝箱));
            // LLM 金币原样保留在头部
            Assert.AreEqual(60f, result.LevelData.Props[0].Position.x);
            Assert.AreEqual(60f, result.LevelData.Props[0].Position.z);
        }

        [Test]
        public async Task 链路_同种子两遍一致_异种子位置不同()
        {
            var resultA = await CreateChainGenerator(NewCollectTemplate()).GenerateAsync(SimpleRequest());
            var resultB = await CreateChainGenerator(NewCollectTemplate()).GenerateAsync(SimpleRequest());
            _client.Responder = _ => ToolCallResponse(CollectJson);
            var levelA = resultA.LevelData;
            var levelB = resultB.LevelData;
            Assert.AreEqual(12, CountByName(levelA, 金币));
            Assert.AreEqual(JsonUtility.ToJson(levelA), JsonUtility.ToJson(levelB),
                "同种子+同罐装输入（独立缓存）必须逐字节一致");

            var requestC = SimpleRequest();
            requestC.RandomSeed = 8;
            var levelC = (await CreateChainGenerator(NewCollectTemplate()).GenerateAsync(requestC)).LevelData;
            Assert.AreEqual(12, CountByName(levelC, 金币));
            var same = 0;
            for (var i = 0; i < levelA.Props.Count; i++)
                if (levelA.Props[i].Position == levelC.Props[i].Position) same++;
            Assert.Less(same, levelA.Props.Count, "异种子收集物位置应不同");
        }

        [Test]
        public async Task 链路_击杀模板兜底其击杀任务_收集任务无匹配不散布()
        {
            // Provider 只有击杀任务模板：Collect 类型任务无匹配 → 任务原样保留、不补收集物
            var generator = CreateChainGenerator(NewTemplate(TaskType.Kill, "kill_tpl", 10));
            _client.Responder = _ => ToolCallResponse(MixedTasksJson);

            var result = await generator.GenerateAsync(SimpleRequest());

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.LevelData.Props.Count, "击杀模板不得散布收集物");
            Assert.AreEqual(0, CountByName(result.LevelData, 金币));
            Assert.AreEqual(2, result.Tasks.Count);
            // 击杀任务匹配到击杀模板：默认字段兜底生效
            Assert.AreEqual(10, result.Tasks[0].Reward.Gold, "击杀任务默认奖励应由击杀模板补齐");
            Assert.AreEqual("击败敌人", result.Tasks[0].TriggerCondition);
            // 收集任务无匹配模板：奖励保持解析层空对象（0/0/无物品），不做任何兜底
            AssertRewardUnset(result.Tasks[1].Reward);
        }

        [Test]
        public async Task 链路_双模板混合_各自按类型匹配收尾()
        {
            var killTpl = NewTemplate(TaskType.Kill, "kill_tpl", 10);
            var collectTpl = NewCollectTemplate();
            var generator = CreateChainGenerator(killTpl, collectTpl);
            _client.Responder = _ => ToolCallResponse(MixedTasksJson);

            var result = await generator.GenerateAsync(SimpleRequest());

            Assert.IsTrue(result.Success);
            Assert.AreEqual(12, CountByName(result.LevelData, 金币), "收集模板只对 Collect 任务补散布");
            Assert.AreEqual(1, CountByName(result.LevelData, 宝箱));
            Assert.AreEqual(10, result.Tasks[0].Reward.Gold, "击杀任务默认奖励");
            Assert.AreEqual(30, result.Tasks[1].Reward.Gold, "收集任务默认奖励");
            Assert.AreEqual("拾取物品", result.Tasks[1].TriggerCondition);
        }

        [Test]
        public async Task 链路_无任务模板_任务原样保留零影响()
        {
            var generator = CreateChainGenerator(); // 空任务模板集合
            _client.Responder = _ => ToolCallResponse(CollectJson);

            var result = await generator.GenerateAsync(SimpleRequest());

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.LevelData.Props.Count, "无任务模板时不得散布收集物");
            AssertRewardUnset(result.Tasks[0].Reward); // 无匹配模板：奖励原样保留（空对象），不做兜底
        }
    }
}
