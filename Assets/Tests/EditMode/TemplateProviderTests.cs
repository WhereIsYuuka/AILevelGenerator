using System.Collections.Generic;
using AILevelGenerator.Runtime.Interfaces.Templates;
using AILevelGenerator.Runtime.Templates;
using NUnit.Framework;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 模板提供者单元测试：内存注入查询逻辑（纯逻辑）+ 真实资产加载（EditMode 可访问 AssetDatabase）。
    /// 资产断言只检查 TemplateId 存在性，不断言 Guideline/DisplayName 等文案内容（策划改文案不应破坏测试）。
    /// ScriptableObject 统一用 CreateInstance 创建（Unity 规范），TearDown 销毁。
    /// </summary>
    public class TemplateProviderTests
    {
        private readonly List<ScriptableObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var so in _created)
                if (so != null) Object.DestroyImmediate(so);
            _created.Clear();
        }

        private ConfigurableLevelTemplate NewLevel(string id, string displayName)
        {
            var t = ScriptableObject.CreateInstance<ConfigurableLevelTemplate>();
            _created.Add(t);
            t.TemplateId = id;
            t.DisplayName = displayName;
            return t;
        }

        private ConfigurableTaskTemplate NewTask(string id, string displayName)
        {
            var t = ScriptableObject.CreateInstance<ConfigurableTaskTemplate>();
            _created.Add(t);
            t.TemplateId = id;
            t.DisplayName = displayName;
            return t;
        }

        private PromptTemplate NewPrompt(string id, string displayName)
        {
            var t = ScriptableObject.CreateInstance<PromptTemplate>();
            _created.Add(t);
            t.TemplateId = id;
            t.DisplayName = displayName;
            return t;
        }

        private TemplateProvider CreateMemoryProvider()
        {
            return new TemplateProvider(
                new List<LevelTemplate>
                {
                    NewLevel("linear", "线性闯关"),
                    NewLevel("open_world", "开放世界")
                },
                new List<TaskTemplate>
                {
                    NewTask("kill", "击杀任务")
                },
                new List<PromptTemplate>
                {
                    NewPrompt("default", "默认提示词"),
                    NewPrompt("backup", "备用提示词")
                });
        }

        [Test]
        public void 注入内存模板_GetLevelTemplates返回全部且顺序一致()
        {
            var provider = CreateMemoryProvider();
            var templates = provider.GetLevelTemplates();
            Assert.AreEqual(2, templates.Count);
            Assert.AreEqual("linear", templates[0].TemplateId);
            Assert.AreEqual("open_world", templates[1].TemplateId);
        }

        [Test]
        public void 注入内存模板_GetTemplateById命中与未命中()
        {
            var provider = CreateMemoryProvider();
            Assert.IsNotNull(provider.GetTemplateById("linear"));
            Assert.IsNull(provider.GetTemplateById("不存在的模板"));
            Assert.IsNull(provider.GetTemplateById(null));
            Assert.IsNull(provider.GetTemplateById(""));
        }

        [Test]
        public void 注入内存模板_任务模板查询()
        {
            var provider = CreateMemoryProvider();
            Assert.AreEqual(1, provider.GetTaskTemplates().Count);
            Assert.IsNotNull(provider.GetTaskTemplateById("kill"));
            Assert.IsNull(provider.GetTaskTemplateById("xxx"));
        }

        [Test]
        public void 注入内存模板_默认Prompt取第一个()
        {
            var provider = CreateMemoryProvider();
            Assert.IsNotNull(provider.GetDefaultPromptTemplate());
            Assert.AreEqual("default", provider.GetDefaultPromptTemplate().TemplateId);
            Assert.IsNotNull(provider.GetPromptTemplateById("backup"));
            Assert.IsNull(provider.GetPromptTemplateById("xxx"));
        }

        [Test]
        public void 构造_空集合_不抛异常且查询安全()
        {
            var provider = new TemplateProvider(null, null, null);
            Assert.IsEmpty(provider.GetLevelTemplates());
            Assert.IsEmpty(provider.GetTaskTemplates());
            Assert.IsNull(provider.GetDefaultPromptTemplate());
        }

        [Test]
        public void LoadFromAssets_加载全部内置关卡模板_数量不少于四且含四类id()
        {
            var provider = TemplateProvider.LoadFromAssets();
            var templates = provider.GetLevelTemplates();

            Assert.GreaterOrEqual(templates.Count, 4, "内置四类关卡模板资产应全部加载");
            var ids = new List<string>();
            foreach (var t in templates)
                if (t != null) ids.Add(t.TemplateId);
            Assert.Contains("linear", ids);
            Assert.Contains("open_world", ids);
            Assert.Contains("defense", ids);
            Assert.Contains("puzzle", ids);
        }

        [Test]
        public void LoadFromAssets_默认Prompt模板非空()
        {
            var provider = TemplateProvider.LoadFromAssets();
            Assert.IsNotNull(provider.GetDefaultPromptTemplate(), "Assets/Settings/PromptTemplates 下应有 Prompt 模板资产");
        }

        [Test]
        public void LoadFromAssets_任务模板已加载()
        {
            var provider = TemplateProvider.LoadFromAssets();
            Assert.GreaterOrEqual(provider.GetTaskTemplates().Count, 2, "内置击杀/收集任务模板资产应全部加载");
        }
    }
}
