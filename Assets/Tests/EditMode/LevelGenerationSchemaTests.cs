using System.Collections.Generic;
using AILevelGenerator.Runtime.LLM;
using AILevelGenerator.Runtime.Parsing;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// Function Calling 工具定义单元测试：Schema 是合法 JSON、覆盖全字段、
    /// 资源逻辑名动态注入 enum、工具/强制调用/JSON 模式常量正确。
    /// </summary>
    public class LevelGenerationSchemaTests
    {
        private static readonly string[] SampleResources = { "敌人-弓箭手", "宝箱", "NPC" };

        // —— 工具定义 ——

        [Test]
        public void CreateTools_返回单个generate_level工具()
        {
            var tools = LevelGenerationSchema.CreateTools(SampleResources);

            Assert.AreEqual(1, tools.Count);
            Assert.AreEqual("function", tools[0].Type);
            Assert.AreEqual("generate_level", tools[0].Function.Name);
            Assert.IsNotEmpty(tools[0].Function.Description);
            Assert.IsNotEmpty(tools[0].Function.ParametersJson);
        }

        [Test]
        public void 常量_强制调用与JSON模式()
        {
            var toolChoice = LevelGenerationSchema.CreateToolChoiceJson();
            var responseFormat = LevelGenerationSchema.CreateJsonObjectResponseFormat();

            StringAssert.Contains("generate_level", toolChoice);
            StringAssert.Contains("\"type\":\"function\"", toolChoice);
            Assert.AreEqual("{\"type\":\"json_object\"}", responseFormat);
        }

        // —— Schema 合法性 ——

        [Test]
        public void ParametersJson_是合法JSON_含全字段()
        {
            var json = LevelGenerationSchema.BuildParametersJson(SampleResources);

            var root = JsonParser.Parse(json); // 抛异常即失败
            Assert.IsTrue(root.IsObject, "Schema 必须是合法 JSON 对象");
            Assert.IsNotNull(root.Get("properties"), "必须有 properties");
            Assert.IsNotNull(root.Get("required"));
            foreach (var key in new[] { "level_name", "description", "player_start_position", "terrain", "props", "tasks" })
                Assert.IsNotNull(root.Get("properties").Get(key), $"缺少字段 {key}");
        }

        [Test]
        public void ParametersJson_任务枚举正确()
        {
            var root = JsonParser.Parse(LevelGenerationSchema.BuildParametersJson(SampleResources));
            var taskType = root.Get("properties").Get("tasks").Get("items").Get("properties").Get("type");

            var enumNode = taskType.Get("enum");
            Assert.IsNotNull(enumNode, "任务类型必须限定枚举");
            Assert.AreEqual(6, enumNode.ArrayValue.Count);
            Assert.AreEqual("Custom", enumNode.GetAt(5).AsString(null));
        }

        // —— 资源 enum 动态注入 ——

        [Test]
        public void ParametersJson_资源逻辑名注入enum()
        {
            var root = JsonParser.Parse(LevelGenerationSchema.BuildParametersJson(SampleResources));
            var logicalName = root.Get("properties").Get("props").Get("items").Get("properties").Get("prefab_logical_name");

            var enumNode = logicalName.Get("enum");
            Assert.IsNotNull(enumNode, "资源逻辑名必须限定枚举");
            Assert.AreEqual(3, enumNode.ArrayValue.Count);
            Assert.AreEqual("敌人-弓箭手", enumNode.GetAt(0).AsString(null));
            Assert.AreEqual("宝箱", enumNode.GetAt(1).AsString(null));
            Assert.AreEqual("NPC", enumNode.GetAt(2).AsString(null));
        }

        [Test]
        public void ParametersJson_资源名为空_不写enum()
        {
            var root = JsonParser.Parse(LevelGenerationSchema.BuildParametersJson(new List<string>()));
            var logicalName = root.Get("properties").Get("props").Get("items").Get("properties").Get("prefab_logical_name");

            Assert.IsNull(logicalName.Get("enum"), "资源名为空时不应写入空 enum（非法 Schema）");
        }

        [Test]
        public void ParametersJson_资源名含特殊字符_正确转义()
        {
            var root = JsonParser.Parse(LevelGenerationSchema.BuildParametersJson(new[] { "宝\"箱", "A\\B" }));
            var enumNode = root.Get("properties").Get("props").Get("items").Get("properties").Get("prefab_logical_name").Get("enum");

            Assert.AreEqual("宝\"箱", enumNode.GetAt(0).AsString(null), "引号必须被转义并还原");
            Assert.AreEqual("A\\B", enumNode.GetAt(1).AsString(null), "反斜杠必须被转义并还原");
        }
    }
}
