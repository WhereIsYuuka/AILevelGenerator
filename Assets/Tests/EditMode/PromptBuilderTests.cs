using System.Collections.Generic;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Prompting;
using AILevelGenerator.Runtime.Templates;
using NUnit.Framework;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 提示词构建器单元测试：占位符插值、未知占位符容错、上下文构造、System/User 分离。
    /// PromptTemplate 用 CreateInstance 创建（Unity 规范），TearDown 销毁。
    /// </summary>
    public class PromptBuilderTests
    {
        private readonly PromptBuilder _builder = new();
        private readonly List<ScriptableObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var so in _created)
                if (so != null) Object.DestroyImmediate(so);
            _created.Clear();
        }

        /// <summary> 创建 Prompt 模板实例（CreateInstance + 配置回调） </summary>
        private PromptTemplate NewPromptTemplate()
        {
            var t = ScriptableObject.CreateInstance<PromptTemplate>();
            _created.Add(t);
            return t;
        }

        [Test]
        public void ReplacePlaceholders_全部已知占位符_替换为上下文值()
        {
            var context = new PromptContext
            {
                UserPrompt = "森林营地",
                TemplateName = "线性闯关",
                TemplateGuideline = "单向推进",
                ResourceList = "敌人-弓箭手、宝箱",
                Seed = "42",
                TerrainEnabled = "生成地形",
                PropsEnabled = "生成道具",
                TasksEnabled = "不生成任务"
            };

            var result = _builder.ReplacePlaceholders(
                "描述：{userPrompt}｜模板：{templateName}｜指南：{templateGuideline}｜资源：{resourceList}｜种子：{seed}｜{terrainEnabled}｜{propsEnabled}｜{tasksEnabled}",
                context);

            Assert.AreEqual(
                "描述：森林营地｜模板：线性闯关｜指南：单向推进｜资源：敌人-弓箭手、宝箱｜种子：42｜生成地形｜生成道具｜不生成任务",
                result);
        }

        [Test]
        public void Build_未知占位符_保留原样且记录到结果()
        {
            var template = NewPromptTemplate();
            template.TemplateId = "t";
            template.SystemPromptTemplate = "未知占位符 {unknownKey} 与已知 {seed}";
            var context = new PromptContext { Seed = "7" };

            var result = _builder.Build(template, context);

            Assert.IsTrue(result.SystemPrompt.Contains("{unknownKey}"), "未知占位符应保留原文，不中断构建");
            Assert.IsTrue(result.SystemPrompt.Contains("7"), "已知占位符应正常替换");
            Assert.AreEqual(new[] { "unknownKey" }, result.UnresolvedPlaceholders, "未知占位符应记录供调用方告警");
        }

        [Test]
        public void ReplacePlaceholders_上下文值含花括号_不递归插值()
        {
            // 值中的 {xxx} 不应被二次替换（防注入与循环替换）
            var context = new PromptContext { UserPrompt = "包含 {templateName} 的文本" };

            var result = _builder.ReplacePlaceholders("前 {userPrompt} 后", context);

            Assert.AreEqual("前 包含 {templateName} 的文本 后", result);
        }

        [Test]
        public void CreateContext_从请求与模板构建_字段映射正确()
        {
            var request = new GenerationRequest
            {
                Prompt = "森林营地，3个弓箭手",
                TemplateId = "linear",
                RandomSeed = 42,
                GenerateTerrain = true,
                GenerateProps = false,
                GenerateTasks = true
            };
            var level = ScriptableObject.CreateInstance<ConfigurableLevelTemplate>();
            _created.Add(level);
            level.TemplateId = "linear";
            level.DisplayName = "线性闯关";
            level.Guideline = "单向推进";
            var names = new List<string> { "敌人-弓箭手", "宝箱" };

            var context = PromptBuilder.CreateContext(request, level, names);

            Assert.AreEqual("森林营地，3个弓箭手", context.UserPrompt);
            Assert.AreEqual("线性闯关", context.TemplateName);
            Assert.AreEqual("单向推进", context.TemplateGuideline);
            Assert.AreEqual("敌人-弓箭手、宝箱", context.ResourceList, "资源清单应按顿号分隔");
            Assert.AreEqual("42", context.Seed);
            Assert.AreEqual("生成地形", context.TerrainEnabled, "开关 true 应映射为完整中文指令");
            Assert.AreEqual("不生成道具", context.PropsEnabled, "开关 false 应映射为完整中文指令");
            Assert.AreEqual("生成任务", context.TasksEnabled);
        }

        [Test]
        public void Build_返回System与User分离_且无未解析占位符()
        {
            var template = NewPromptTemplate();
            template.TemplateId = "default";
            template.SystemPromptTemplate = "你是关卡设计师。可用物体：{resourceList}";
            template.UserPromptTemplate = "描述：{userPrompt}";
            var context = PromptBuilder.CreateContext(
                new GenerationRequest { Prompt = "测试描述", RandomSeed = 1 },
                null, null);

            var result = _builder.Build(template, context);

            Assert.IsTrue(result.SystemPrompt.Contains("你是关卡设计师"), "System Prompt 应按模板内容构建");
            Assert.IsTrue(result.UserPrompt.Contains("测试描述"));
            Assert.AreNotEqual(result.SystemPrompt, result.UserPrompt);
            Assert.IsEmpty(result.UnresolvedPlaceholders, "全部占位符均已替换");
        }

        [Test]
        public void Build_空模板或空上下文_不抛异常()
        {
            Assert.DoesNotThrow(() => _builder.Build(null, null));
            Assert.DoesNotThrow(() => _builder.Build(null, new PromptContext()));
            Assert.DoesNotThrow(() => _builder.Build(NewPromptTemplate(), null));
        }
    }
}
