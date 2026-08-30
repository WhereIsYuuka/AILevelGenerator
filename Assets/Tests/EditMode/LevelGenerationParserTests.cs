using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Parsing;
using NUnit.Framework;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 容错解析引擎单元测试：LLM 输出 snake_case JSON → LevelData 的语义映射。
    /// 覆盖：全字段映射 / 缺失字段默认值 / 类型转换 / 非法值兜底 + warning / 负坐标归零 / 结构损坏失败。
    /// </summary>
    public class LevelGenerationParserTests
    {
        // —— 全字段映射 ——

        [Test]
        public void 解析_完整JSON_全字段映射()
        {
            var json = @"{
                ""level_name"": ""森林营地"",
                ""description"": ""营地入口的森林地带"",
                ""player_start_position"": {""x"": 10, ""y"": 0.5, ""z"": -4},
                ""terrain"": {""width"": 120, ""length"": 80, ""height_scale"": 5},
                ""props"": [
                    {""prefab_logical_name"": ""敌人-弓箭手"", ""position"": {""x"": 5, ""y"": 0, ""z"": 5}, ""rotation"": {""y"": 90}, ""scale"": {""x"": 1, ""y"": 1, ""z"": 1}},
                    {""prefab_logical_name"": ""宝箱"", ""position"": {""x"": -3, ""z"": 2}}
                ],
                ""tasks"": [
                    {""task_id"": ""t1"", ""task_name"": ""击杀狼群"", ""description"": ""消灭 3 只狼"", ""type"": ""kill"", ""objective"": ""count"", ""time_limit"": 300,
                     ""reward"": {""experience"": 100, ""gold"": 50, ""item_rewards"": [""狼皮"", ""牙齿""]},
                     ""is_main_task"": true, ""trigger_condition"": ""进入营地"",
                     ""placements"": []}
                ]
            }";

            var result = LevelGenerationParser.Parse(json);

            Assert.IsTrue(result.IsValid, "结构可解析时 IsValid 应为 true");
            Assert.IsEmpty(result.Errors);
            var level = result.Level;
            Assert.IsNotNull(level);
            Assert.AreEqual("森林营地", level.LevelName);
            Assert.AreEqual("营地入口的森林地带", level.Description);
            Assert.AreEqual(new Vector3(10f, 0.5f, -4f), level.PlayerStartPosition);
            Assert.AreEqual(120, level.Terrain.Width);
            Assert.AreEqual(80, level.Terrain.Length);
            Assert.AreEqual(5f, level.Terrain.HeightScale);
            Assert.AreEqual(2, level.Props.Count);
            Assert.AreEqual("敌人-弓箭手", level.Props[0].PrefabLogicalName);
            Assert.AreEqual(new Vector3(5f, 0f, 5f), level.Props[0].Position);
            Assert.AreEqual(new Vector3(0f, 90f, 0f), level.Props[0].Rotation);
            Assert.AreEqual(Vector3.one, level.Props[0].Scale);
            Assert.AreEqual(new Vector3(-3f, 0f, 2f), level.Props[1].Position, "缺失组件应为 0");

            Assert.AreEqual(1, level.Tasks.Count);
            var task = level.Tasks[0];
            Assert.AreEqual("t1", task.TaskID);
            Assert.AreEqual("击杀狼群", task.TaskName);
            Assert.AreEqual(TaskType.Kill, task.Type);
            Assert.AreEqual(TaskObjective.Count, task.Objective);
            Assert.AreEqual(300f, task.TimeLimit);
            Assert.IsTrue(task.IsMainTask);
            Assert.AreEqual("进入营地", task.TriggerCondition);
            Assert.AreEqual(100, task.Reward.Experience);
            Assert.AreEqual(50, task.Reward.Gold);
            CollectionAssert.AreEqual(new[] { "狼皮", "牙齿" }, task.Reward.ItemRewards);
        }

        // —— 缺失字段默认值 ——

        [Test]
        public void 解析_空对象_全部使用默认值()
        {
            var result = LevelGenerationParser.Parse("{}");

            Assert.IsTrue(result.IsValid);
            var level = result.Level;
            Assert.AreEqual("未命名关卡", level.LevelName);
            Assert.AreEqual("", level.Description);
            Assert.AreEqual(Vector3.zero, level.PlayerStartPosition);
            Assert.AreEqual(100, level.Terrain.Width);
            Assert.AreEqual(100, level.Terrain.Length);
            Assert.AreEqual(10f, level.Terrain.HeightScale);
            Assert.IsEmpty(level.Props);
            Assert.IsEmpty(level.Tasks);
        }

        [Test]
        public void 解析_空levelName与taskName_使用默认名称()
        {
            var result = LevelGenerationParser.Parse(
                "{\"level_name\":\"\",\"tasks\":[{\"task_name\":\"  \"}]}");

            Assert.AreEqual("未命名关卡", result.Level.LevelName);
            Assert.AreEqual("任务", result.Level.Tasks[0].TaskName);
        }

        [Test]
        public void 解析_无时限任务_timeLimit缺省负一()
        {
            var result = LevelGenerationParser.Parse("{\"tasks\":[{}]}");

            Assert.AreEqual(-1f, result.Level.Tasks[0].TimeLimit, "未设置时限应为无时限（-1）");
            Assert.AreEqual(TaskType.Custom, result.Level.Tasks[0].Type);
            Assert.AreEqual(TaskObjective.Count, result.Level.Tasks[0].Objective);
        }

        // —— 类型转换 ——

        [Test]
        public void 解析_字符串数字与布尔_自动转换()
        {
            var result = LevelGenerationParser.Parse(
                "{\"terrain\":{\"width\":\"120\",\"height_scale\":\"5\"},\"tasks\":[{\"type\":\"KILL\",\"time_limit\":\"30.5\",\"is_main_task\":\"false\",\"reward\":{\"gold\":\"42\"}}]}");

            var terrain = result.Level.Terrain;
            Assert.AreEqual(120, terrain.Width, "字符串数字应转换为 int");
            Assert.AreEqual(5f, terrain.HeightScale, "字符串数字应转换为 float");
            var task = result.Level.Tasks[0];
            Assert.AreEqual(TaskType.Kill, task.Type, "枚举字符串应忽略大小写转换");
            Assert.AreEqual(30.5f, task.TimeLimit, "时间限制字符串数字应转换");
            Assert.IsFalse(task.IsMainTask, "字符串布尔应转换");
            Assert.AreEqual(42, task.Reward.Gold);
        }

        // —— 非法值兜底 + warning ——

        [Test]
        public void 解析_负坐标保留_负缩放归零并警告()
        {
            var result = LevelGenerationParser.Parse(
                "{\"player_start_position\":{\"x\":-5,\"y\":-1},\"props\":[{\"prefab_logical_name\":\"宝箱\",\"position\":{\"x\":-3},\"scale\":{\"x\":-2}}]}");

            Assert.AreEqual(new Vector3(-5f, -1f, 0f), result.Level.PlayerStartPosition, "负坐标合法，应保留");
            Assert.AreEqual(new Vector3(-3f, 0f, 0f), result.Level.Props[0].Position, "负坐标合法，应保留");
            Assert.AreEqual(new Vector3(0f, 1f, 1f), result.Level.Props[0].Scale, "负 x 缩放应归零，缺失 y/z 保持默认 1");
            Assert.AreEqual(1, result.Warnings.Count, "仅负缩放产生警告");
            StringAssert.Contains("负", result.Warnings[0].Message);
        }

        [Test]
        public void 解析_负旋转角度_保留不归零()
        {
            var result = LevelGenerationParser.Parse(
                "{\"props\":[{\"prefab_logical_name\":\"宝箱\",\"rotation\":{\"y\":-45}}]}");

            Assert.AreEqual(-45f, result.Level.Props[0].Rotation.y, "负旋转角度合法，不应归零");
            Assert.IsEmpty(result.Warnings);
        }

        [Test]
        public void 解析_未知枚举_使用默认并警告()
        {
            var result = LevelGenerationParser.Parse("{\"tasks\":[{\"type\":\"飞行\",\"objective\":\"神秘目标\"}]}");

            var task = result.Level.Tasks[0];
            Assert.AreEqual(TaskType.Custom, task.Type, "未知任务类型应兜底为 Custom");
            Assert.AreEqual(TaskObjective.Count, task.Objective, "未知目标应兜底为 Count");
            Assert.AreEqual(2, result.Warnings.Count);
            StringAssert.Contains("飞行", result.Warnings[0].Message);
        }

        [Test]
        public void 解析_空物体逻辑名_跳过并警告()
        {
            var result = LevelGenerationParser.Parse(
                "{\"props\":[{\"prefab_logical_name\":\"宝箱\"},{\"prefab_logical_name\":\"\"},{\"prefab_logical_name\":null}]}");

            Assert.AreEqual(1, result.Level.Props.Count, "空逻辑名物体应跳过");
            Assert.AreEqual("宝箱", result.Level.Props[0].PrefabLogicalName);
            Assert.AreEqual(2, result.Warnings.Count);
        }

        [Test]
        public void 解析_地形参数非法_使用默认并警告()
        {
            var result = LevelGenerationParser.Parse("{\"terrain\":{\"width\":0,\"length\":-50,\"height_scale\":\"abc\"}}");

            Assert.AreEqual(100, result.Level.Terrain.Width, "宽度 0 非法应回默认");
            Assert.AreEqual(100, result.Level.Terrain.Length, "负长度应回默认");
            Assert.AreEqual(10f, result.Level.Terrain.HeightScale, "无法解析的字符串应回默认");
            Assert.AreEqual(3, result.Warnings.Count);
        }

        [Test]
        public void 解析_无法解析的数字_使用默认并警告()
        {
            var result = LevelGenerationParser.Parse(
                "{\"player_start_position\":{\"x\":\"abc\"},\"tasks\":[{\"time_limit\":\"很长\",\"reward\":{\"gold\":\"很多\"}}]}");

            Assert.AreEqual(0f, result.Level.PlayerStartPosition.x, "非法坐标字符串应使用默认");
            Assert.AreEqual(-1f, result.Level.Tasks[0].TimeLimit, "非法时限应视为无时限");
            Assert.AreEqual(0, result.Level.Tasks[0].Reward.Gold, "非法奖励应使用默认");
            Assert.AreEqual(3, result.Warnings.Count);
        }

        [Test]
        public void 解析_存在warning_IsValid仍为true()
        {
            var result = LevelGenerationParser.Parse("{\"terrain\":{\"width\":0},\"props\":[{\"prefab_logical_name\":\"\"}]}");

            Assert.IsTrue(result.IsValid, "语义修正（warning）不应视为解析失败");
            Assert.IsEmpty(result.Errors);
            Assert.AreEqual(2, result.Warnings.Count);
        }

        // —— 结构损坏失败 ——

        [Test]
        public void 解析_非JSON文本_IsValid为false_含中文错误()
        {
            var result = LevelGenerationParser.Parse("好的，我来设计一个关卡。");

            Assert.IsFalse(result.IsValid);
            Assert.IsNull(result.Level);
            Assert.AreEqual(1, result.Errors.Count);
            StringAssert.Contains("不是合法 JSON", result.Errors[0].Message);
        }

        [Test]
        public void 解析_顶层数组_IsValid为false()
        {
            var result = LevelGenerationParser.Parse("[1,2,3]");

            Assert.IsFalse(result.IsValid);
            Assert.IsNull(result.Level);
            StringAssert.Contains("对象", result.Errors[0].Message);
        }
    }
}
