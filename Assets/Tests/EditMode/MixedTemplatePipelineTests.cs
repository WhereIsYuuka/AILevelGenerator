using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
    /// 双模板混合链路测试（第五周-Day6）：战斗关卡模板 + 收集任务模板在**同一次生成**里同时收尾的引擎级验证。
    /// 复用 fake IDeepSeekClient（不碰真实网络）驱动 LLMGenerator 完整链路（LLM 弱产出 → 双兜底确定性补齐），
    /// 覆盖：双兜底同关生效 / 任务模板按 TaskType 命中门控 / 多收集任务共享数量池 / LLM 达标不补位 /
    /// 种子确定性 / 缓存重放一致。真实 API 侧的 10 次联调验收见编辑器工具 MixedTemplateIntegrationRunner。
    /// 边界说明（沿用第五周约定）：敌人/收集物本体由 LLM 产出，模板只兜「欠数」与「缺巡逻点」；
    /// 数量下限为关卡维度（多收集任务共享同一数量池，先补不重复）。
    /// </summary>
    public class MixedTemplatePipelineTests
    {
        private const string WeakMixedJson = @"{
            ""level_name"": ""混合验证关卡"",
            ""description"": ""战斗 + 收集双模板混合链路验证"",
            ""props"": [
                {""prefab_logical_name"": ""敌人-近战"", ""position"": {""x"": 5, ""y"": 0, ""z"": 5}},
                {""prefab_logical_name"": ""金币"", ""position"": {""x"": -5, ""y"": 0, ""z"": 5}}
            ],
            ""tasks"": [
                {""task_name"": ""击败驻地守军"", ""type"": ""kill"", ""is_main_task"": true},
                {""task_name"": ""收集散落金币"", ""type"": ""collect"", ""is_main_task"": false}
            ]
        }";

        /// <summary> 可注入的 fake 客户端：记录调用次数，按回调返回响应或抛异常 </summary>
        private sealed class FakeDeepSeekClient : IDeepSeekClient
        {
            public int CallCount;
            public Func<DeepSeekChatRequest, DeepSeekChatResponse> Responder;

            public Task<DeepSeekChatResponse> ChatAsync(DeepSeekChatRequest request)
            {
                CallCount++;
                return Task.FromResult(Responder(request));
            }
        }

        /// <summary> 内存资源映射（Prompt 资源清单数据源；GetPrefab 不落库，本测试只到数据层） </summary>
        private sealed class FakeResourceMapper : IResourceMapper
        {
            private readonly List<string> _names;

            public FakeResourceMapper(params string[] names) => _names = new List<string>(names);

            public IReadOnlyList<string> GetAllLogicalNames() => _names;
            public GameObject GetPrefab(string logicalName) => null;
            public bool TryGetPrefab(string logicalName, out GameObject prefab) { prefab = null; return false; }
            public GameObject GetPrefabByFuzzy(string keyword) => null;
        }

        private static readonly string[] EnemyNames = { "敌人-近战", "敌人-弓箭手", "敌人-精英" };
        private static readonly string[] CollectibleNames = { "金币", "道具-生命药水" };

        private readonly List<ScriptableObject> _created = new();
        private FakeDeepSeekClient _client;
        private LLMGenerator _generator;

        [SetUp]
        public void SetUp()
        {
            _client = new FakeDeepSeekClient { Responder = _ => ToolCallResponse(WeakMixedJson) };
            _generator = CreateGenerator(seed: 777);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var so in _created)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _created.Clear();
        }

        // —— 链路装配：战斗关卡模板 + 收集任务模板 + Prompt 模板（与生产资产同参数，代码内建保证可复现） ——

        /// <summary> 构造生成器：双模板混合链路的统一装配点（含任务模板 = 与既有 LLMGeneratorTests 的差异点） </summary>
        private LLMGenerator CreateGenerator(string rawJson = null, int seed = 777)
        {
            if (rawJson != null)
                _client.Responder = _ => ToolCallResponse(rawJson);

            var levelTemplate = NewTemplate<ConfigurableLevelTemplate>();
            levelTemplate.TemplateId = "mixed_battle";
            levelTemplate.DisplayName = "混合战斗模板";
            levelTemplate.Guideline = "敌人驻守关键节点，道具沿路径分布";
            levelTemplate.OverrideTerrain = true;
            levelTemplate.TerrainWidth = 100;
            levelTemplate.TerrainLength = 100;
            levelTemplate.TerrainHeightScale = 8;
            levelTemplate.ForceMainTask = true;
            levelTemplate.EnemyOptions = new List<EnemyTypeOption>
            {
                new() { LogicalName = "敌人-近战", Weight = 4 },
                new() { LogicalName = "敌人-弓箭手", Weight = 3 },
                new() { LogicalName = "敌人-精英", Weight = 1 }
            };
            levelTemplate.MinEnemyCount = 6;
            levelTemplate.PatrolPointsPerEnemy = 2;
            levelTemplate.PatrolRadiusMin = 2f;
            levelTemplate.PatrolRadiusMax = 6f;
            levelTemplate.EnemySpawnRingMin = 8f;
            levelTemplate.EnemySpawnRingMax = 25f;
            levelTemplate.EnemyMinSpacing = 6f;
            levelTemplate.BoundsMargin = 3f;

            var collectTemplate = NewTemplate<ConfigurableTaskTemplate>();
            collectTemplate.TemplateId = "mixed_collect";
            collectTemplate.DisplayName = "混合收集任务";
            collectTemplate.Description = "收集指定数量的物品";
            collectTemplate.TaskType = TaskType.Collect;
            collectTemplate.DefaultReward = new RewardData { Gold = 30 };
            collectTemplate.CollectibleOptions = new List<CollectibleTypeOption>
            {
                new() { LogicalName = "金币", Weight = 2 },
                new() { LogicalName = "道具-生命药水", Weight = 1 }
            };
            collectTemplate.MinCollectibleCount = 12;
            collectTemplate.CollectSpawnRingMin = 6f;
            collectTemplate.CollectSpawnRingMax = 30f;
            collectTemplate.CollectMinSpacing = 2.5f;
            collectTemplate.CollectBoundsMargin = 3f;

            var promptTemplate = NewTemplate<PromptTemplate>();
            promptTemplate.SystemPromptTemplate = "你是资深关卡设计师，输出 JSON。{templateGuideline}";
            promptTemplate.UserPromptTemplate = "需求：{userPrompt} 可用物体：{resourceList} {seed}";

            var manager = new TemplateManager(
                new[] { levelTemplate },
                new[] { collectTemplate },
                new[] { promptTemplate });

            return new LLMGenerator(
                _client,
                () => "test-key",
                manager,
                new FakeResourceMapper("敌人-近战", "敌人-弓箭手", "敌人-精英", "金币", "道具-生命药水"),
                cache: null, // 默认内存缓存：同参重放命中验证（Day6 场景）
                useJsonMode: true,
                dependencyHashProvider: null);
        }

        private static GenerationRequest Request(int seed) => new()
        {
            Prompt = "设计一个混合验证关卡：主线战斗约 5 名敌人，支线收集金币",
            TemplateId = "mixed_battle",
            RandomSeed = seed
        };

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

        // —— 双模板混合核心语义 ——

        [Test]
        public async Task 混合链路_战斗与收集双兜底同时生效_LLM内容保留()
        {
            // LLM 弱产出：敌人 1（近战）/ 收集物 1（金币）→ 战斗兜底补至 6、收集兜底补至 12（先补不覆盖）
            var result = await _generator.GenerateAsync(Request(777));

            Assert.IsTrue(result.Success, $"双兜底混合链路应生成成功：{JoinErrors(result)}");
            Assert.AreEqual(6, CountByNames(result.LevelData, EnemyNames), "敌人应确定性补齐到 MinEnemyCount=6");
            Assert.AreEqual(12, CountByNames(result.LevelData, CollectibleNames), "收集物应确定性补齐到 MinCollectibleCount=12");
            Assert.IsTrue(HasPropAt(result.LevelData, "敌人-近战", new Vector3(5f, 0f, 5f)), "LLM 已产出的敌人不得被覆盖");
            Assert.IsTrue(HasPropAt(result.LevelData, "金币", new Vector3(-5f, 0f, 5f)), "LLM 已产出的收集物不得被覆盖");

            foreach (var prop in result.LevelData.Props.Where(p => EnemyNames.Contains(p.PrefabLogicalName)))
                Assert.AreEqual(2, prop.PatrolPoints.Count, "无巡逻点的敌人应由模板补齐 PatrolPointsPerEnemy=2 个巡逻点");

            var collectTask = result.Tasks.FirstOrDefault(t => t.Type == TaskType.Collect);
            Assert.IsNotNull(collectTask, "LLM 应产出 Collect 任务（混合证据前置条件）");
            Assert.AreEqual(30, collectTask.Reward?.Gold ?? 0, "收集任务奖励空时应由任务模板默认值兜底");
        }

        [Test]
        public async Task 混合链路_无收集任务时收集兜底不介入_战斗兜底照常()
        {
            // 任务模板按 TaskType 首个命中匹配：无 Collect 任务 = 收集模板不参与收尾（敌人兜底属关卡模板，不受影响）
            const string killOnlyJson = @"{
                ""level_name"": ""纯战斗关卡"",
                ""props"": [
                    {""prefab_logical_name"": ""敌人-近战"", ""position"": {""x"": 3, ""y"": 0, ""z"": 3}},
                    {""prefab_logical_name"": ""金币"", ""position"": {""x"": -3, ""y"": 0, ""z"": 3}}
                ],
                ""tasks"": [
                    {""task_name"": ""击败守军"", ""type"": ""kill"", ""is_main_task"": true}
                ]
            }";
            var result = await CreateGenerator(killOnlyJson).GenerateAsync(Request(778));

            Assert.IsTrue(result.Success, $"应生成成功：{JoinErrors(result)}");
            Assert.AreEqual(1, CountByNames(result.LevelData, CollectibleNames), "无 Collect 任务时收集兜底不得介入（金币 1 枚保持原样）");
            Assert.AreEqual(6, CountByNames(result.LevelData, EnemyNames), "战斗兜底与任务列表无关，敌人仍补齐到 MinEnemyCount=6");
        }

        [Test]
        public async Task 混合链路_同关卡两个收集任务共享数量池_不重复补位()
        {
            const string twoCollectJson = @"{
                ""level_name"": ""双收集关卡"",
                ""props"": [
                    {""prefab_logical_name"": ""金币"", ""position"": {""x"": 2, ""y"": 0, ""z"": 2}}
                ],
                ""tasks"": [
                    {""task_name"": ""击败营地守卫"", ""type"": ""kill"", ""is_main_task"": true},
                    {""task_name"": ""收集金币"", ""type"": ""collect"", ""is_main_task"": false},
                    {""task_name"": ""收集生命药水"", ""type"": ""collect"", ""is_main_task"": false}
                ]
            }";
            var result = await CreateGenerator(twoCollectJson).GenerateAsync(Request(779));

            Assert.IsTrue(result.Success, $"应生成成功：{JoinErrors(result)}");
            // 关卡维度数量池：第一个收集任务把收集物补齐到 12，第二个看到已达标不重复补位（下限 12 而非 12×任务数）
            Assert.AreEqual(12, CountByNames(result.LevelData, CollectibleNames), "多个收集任务共享同一数量池，只补到下限不重复");
        }

        [Test]
        public async Task 混合链路_LLM达标不补位_仅补齐缺失巡逻点()
        {
            // LLM 富产出：敌人 6（=MinEnemyCount）已含 1 名带 2 巡逻点的精英、金币 12（=MinCollectibleCount）
            // → 双兜底均不补位（原样保留、不裁剪不追加）；仅无巡逻点的敌人由模板补齐巡逻点，LLM 已有巡逻点不覆盖
            // 注意：Unity 6 编译器为 C# 9，不支持 raw string literal（"""），JSON 段一律用 verbatim string（@""）拼接
            var json = new StringBuilder();
            json.AppendLine(@"{ ""level_name"": ""富产出关卡"", ""props"": [
                { ""prefab_logical_name"": ""敌人-近战"", ""position"": { ""x"": 1, ""y"": 0, ""z"": 1 } },
                { ""prefab_logical_name"": ""敌人-近战"", ""position"": { ""x"": 3, ""y"": 0, ""z"": 1 } },
                { ""prefab_logical_name"": ""敌人-弓箭手"", ""position"": { ""x"": 11, ""y"": 0, ""z"": 1 } },
                { ""prefab_logical_name"": ""敌人-弓箭手"", ""position"": { ""x"": 13, ""y"": 0, ""z"": 1 } },
                { ""prefab_logical_name"": ""敌人-精英"", ""position"": { ""x"": 21, ""y"": 0, ""z"": 1 },
                  ""patrol_points"": [ { ""x"": 24, ""y"": 0, ""z"": 1 }, { ""x"": 18, ""y"": 0, ""z"": 1 } ] },
                { ""prefab_logical_name"": ""敌人-精英"", ""position"": { ""x"": 23, ""y"": 0, ""z"": 1 } },
            ");
            // 金币 12 枚沿一条直线散开（z=40 与敌人 z=1 分区，互不干扰兜底判定；末项不带尾逗号保证 JSON 合法）
            for (var i = 0; i < 12; i++)
                json.AppendLine($"{{ \"prefab_logical_name\": \"金币\", \"position\": {{ \"x\": {i * 4 - 22}, \"y\": 0, \"z\": 40 }} }}{(i < 11 ? "," : "")}");
            json.AppendLine(@"],
                ""tasks"": [
                    { ""task_name"": ""清剿营地"", ""type"": ""kill"", ""is_main_task"": true },
                    { ""task_name"": ""拾取金币"", ""type"": ""collect"", ""is_main_task"": false }
                ]
            }");
            var result = await CreateGenerator(json.ToString()).GenerateAsync(Request(780));

            Assert.IsTrue(result.Success, $"应生成成功：{JoinErrors(result)}");
            Assert.AreEqual(6, CountByNames(result.LevelData, EnemyNames), "敌人 6 名已达标：战斗兜底不得追加");
            Assert.AreEqual(12, CountByNames(result.LevelData, CollectibleNames), "金币 12 枚已达标：收集兜底不得追加");
            Assert.AreEqual(6 + 12, result.LevelData.Props.Count, "达标场景 Props 总数应原样保留（不补不裁）");

            // 巡逻点：LLM 已带的精英巡逻点不覆盖；其余无巡逻点敌人补齐 PatrolPointsPerEnemy=2
            var elite = result.LevelData.Props.First(p => p.PrefabLogicalName == "敌人-精英" && p.Position.x == 21f);
            Assert.AreEqual(2, elite.PatrolPoints.Count, "LLM 已输出的巡逻点不得被模板覆盖");
            Assert.AreEqual(24f, elite.PatrolPoints[0].x);
            Assert.AreEqual(18f, elite.PatrolPoints[1].x);
            var filled = result.LevelData.Props.Where(p => EnemyNames.Contains(p.PrefabLogicalName) && p.Position.x != 21f);
            Assert.IsTrue(filled.All(p => p.PatrolPoints.Count == 2), "无巡逻点的敌人应由模板补齐 2 个巡逻点");
        }

        [Test]
        public async Task 混合链路_种子确定_同种子布局一致_不同种子布局不同()
        {
            var first = await _generator.GenerateAsync(Request(777));
            var secondGen = CreateGenerator(seed: 777); // 全新实例（独立缓存），同 LLM 弱产出 + 同种子
            var second = await secondGen.GenerateAsync(Request(777));
            var thirdGen = CreateGenerator(seed: 778);
            var third = await thirdGen.GenerateAsync(Request(778));

            Assert.IsTrue(first.Success && second.Success && third.Success);
            Assert.AreEqual(Fingerprint(first.LevelData), Fingerprint(second.LevelData), "同种子双模板收尾结果应逐字段一致");
            Assert.AreNotEqual(Fingerprint(first.LevelData), Fingerprint(third.LevelData), "不同种子兜底布局应不同（确定性随机流已生效）");
        }

        [Test]
        public async Task 混合链路_同参重放命中缓存_不重复调用API且结果一致()
        {
            var request = Request(781);
            var first = await _generator.GenerateAsync(request);
            var second = await _generator.GenerateAsync(request);

            Assert.IsTrue(first.Success && second.Success);
            Assert.AreEqual(1, _client.CallCount, "同参重放应命中缓存，不重复调用 API");
            Assert.AreEqual(Fingerprint(first.LevelData), Fingerprint(second.LevelData), "缓存重放应产出与新鲜生成完全一致的结果（确定性收尾）");
        }

        // —— 辅助 ——

        private static int CountByNames(LevelData level, IEnumerable<string> names)
        {
            if (level?.Props == null) return 0;
            var set = new HashSet<string>(names);
            return level.Props.Count(p => p != null && set.Contains(p.PrefabLogicalName));
        }

        private static bool HasPropAt(LevelData level, string logicalName, Vector3 position)
            => level?.Props != null && level.Props.Any(p => p != null && p.PrefabLogicalName == logicalName
                && p.Position.x == position.x && p.Position.y == position.y && p.Position.z == position.z);

        /// <summary> LevelData 全字段确定序列化（同种子断言逐字段全等；与联调工具同规则） </summary>
        private static string Fingerprint(LevelData level)
        {
            var sb = new StringBuilder();
            sb.Append(level.LevelName).Append('|').Append(level.Description).Append('|');
            sb.Append(F(level.PlayerStartPosition)).Append('|');
            if (level.Terrain != null)
                sb.Append(level.Terrain.Width).Append('x').Append(level.Terrain.Length).Append('x').Append(level.Terrain.HeightScale);
            sb.Append('|');
            foreach (var p in level.Props)
            {
                if (p == null) continue;
                sb.Append(p.PrefabLogicalName).Append('@').Append(F(p.Position)).Append('!').Append(F(p.Rotation))
                    .Append('!').Append(F(p.Scale)).Append('#');
                if (p.PatrolPoints != null)
                    foreach (var point in p.PatrolPoints)
                        sb.Append(F(point)).Append('&');
                sb.Append('|');
            }
            foreach (var task in level.Tasks)
            {
                if (task == null) continue;
                sb.Append(task.TaskID).Append('|').Append(task.TaskName).Append('|').Append(task.Description).Append('|')
                    .Append(task.Type).Append('|').Append(task.Objective).Append('|').Append(task.IsMainTask).Append('|')
                    .Append(task.TimeLimit).Append('|').Append(task.TriggerCondition).Append('|');
                if (task.Reward != null)
                    sb.Append(task.Reward.Experience).Append('+').Append(task.Reward.Gold).Append('+')
                        .Append(task.Reward.ItemRewards != null ? string.Join(",", task.Reward.ItemRewards) : "");
                sb.Append(';');
            }
            return sb.ToString();
        }

        private static string F(Vector3 v)
            => v.x.ToString("R", CultureInfo.InvariantCulture) + "," + v.y.ToString("R", CultureInfo.InvariantCulture) + "," + v.z.ToString("R", CultureInfo.InvariantCulture);

        private static string JoinErrors(GenerationResult result)
            => result?.Errors != null && result.Errors.Count > 0
                ? string.Join("；", result.Errors.Select(e => $"{e.Code}：{e.Message}"))
                : "无错误条目";
    }
}
