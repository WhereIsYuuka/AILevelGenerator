using System;
using System.Collections.Generic;
using AILevelGenerator.Runtime.Interfaces.Templates;
using AILevelGenerator.Runtime.Templates;
using NUnit.Framework;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 模板管理器单元测试（第五周-Day4，替代 TemplateProviderTests）：
    /// 查询/注册/注销/Upsert 保序/变更事件（纯逻辑）+ Reload 加载源语义 + 真实资产加载（EditMode 可访问 AssetDatabase）。
    /// 资产断言只检查 TemplateId 存在性，不断言 Guideline/DisplayName 等文案内容（策划改文案不应破坏测试）。
    /// ScriptableObject 统一用 CreateInstance 创建（Unity 规范），TearDown 销毁。
    /// </summary>
    public class TemplateManagerTests
    {
        private readonly List<ScriptableObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var so in _created)
                if (so != null) UnityEngine.Object.DestroyImmediate(so); // 文件含 using System（Func），Object 需全限定
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

        private TemplateManager CreateMemoryManager()
        {
            return new TemplateManager(
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

        /// <summary> 真实资产管理器：TemplateAssetSource 扫描 Assets/Settings/（Reload 后立即可查询） </summary>
        private TemplateManager LoadFromAssets()
        {
            var manager = new TemplateManager(new TemplateAssetSource());
            Assert.IsTrue(manager.Reload(), "真实资产源 Reload 应成功");
            return manager;
        }

        // —— 查询（旧 Provider 语义回归） ——

        [Test]
        public void 注入内存模板_GetLevelTemplates返回全部且顺序一致()
        {
            var manager = CreateMemoryManager();
            var templates = manager.GetLevelTemplates();
            Assert.AreEqual(2, templates.Count);
            Assert.AreEqual("linear", templates[0].TemplateId);
            Assert.AreEqual("open_world", templates[1].TemplateId);
        }

        [Test]
        public void 注入内存模板_GetTemplateById命中与未命中()
        {
            var manager = CreateMemoryManager();
            Assert.IsNotNull(manager.GetTemplateById("linear"));
            Assert.IsNull(manager.GetTemplateById("不存在的模板"));
            Assert.IsNull(manager.GetTemplateById(null));
            Assert.IsNull(manager.GetTemplateById(""));
        }

        [Test]
        public void 注入内存模板_任务模板查询()
        {
            var manager = CreateMemoryManager();
            Assert.AreEqual(1, manager.GetTaskTemplates().Count);
            Assert.IsNotNull(manager.GetTaskTemplateById("kill"));
            Assert.IsNull(manager.GetTaskTemplateById("xxx"));
        }

        [Test]
        public void 注入内存模板_默认Prompt取第一个()
        {
            var manager = CreateMemoryManager();
            Assert.IsNotNull(manager.GetDefaultPromptTemplate());
            Assert.AreEqual("default", manager.GetDefaultPromptTemplate().TemplateId);
            Assert.IsNotNull(manager.GetPromptTemplateById("backup"));
            Assert.IsNull(manager.GetPromptTemplateById("xxx"));
        }

        [Test]
        public void 构造_空集合_不抛异常且查询安全()
        {
            var manager = new TemplateManager(null, null, null);
            Assert.IsEmpty(manager.GetLevelTemplates());
            Assert.IsEmpty(manager.GetTaskTemplates());
            Assert.IsNull(manager.GetDefaultPromptTemplate());
        }

        // —— 动态注册 / 注销（第五周-Day4） ——

        [Test]
        public void Register_新模板追加尾部_并触发一次变更事件()
        {
            var manager = CreateMemoryManager();
            var fired = 0;
            manager.TemplatesChanged += () => fired++;

            manager.RegisterLevelTemplate(NewLevel("defense", "塔防防守"));

            Assert.AreEqual(3, manager.GetLevelTemplates().Count);
            Assert.AreEqual("defense", manager.GetLevelTemplates()[2].TemplateId, "新增应追加尾部");
            Assert.AreEqual(1, fired, "单次注册只触发一次变更事件");
        }

        [Test]
        public void Register_同TemplateId_就地替换保序()
        {
            var manager = CreateMemoryManager();
            manager.RegisterLevelTemplate(NewLevel("new_tpl", "新模板"));
            var replaced = NewLevel("open_world", "开放世界V2"); // 同 ID 替换（如资产重载后同 ID 热更）

            manager.RegisterLevelTemplate(replaced);

            Assert.AreEqual(3, manager.GetLevelTemplates().Count, "同 ID 替换不新增条目");
            Assert.AreEqual("linear", manager.GetLevelTemplates()[0].TemplateId);
            Assert.AreEqual("open_world", manager.GetLevelTemplates()[1].TemplateId, "同 ID 就地替换应保持原位置");
            Assert.AreEqual("开放世界V2", manager.GetLevelTemplates()[1].DisplayName);
            Assert.AreSame(replaced, manager.GetTemplateById("open_world"), "应持有新实例");
        }

        [Test]
        public void Register_空ID模板_追加尾部且可查询()
        {
            var manager = new TemplateManager(null, null, null);
            var blank = NewLevel("", "无ID模板");

            manager.RegisterLevelTemplate(blank);

            Assert.AreEqual(1, manager.GetLevelTemplates().Count);
            Assert.AreSame(blank, manager.GetLevelTemplates()[0], "空 ID 无法定位，容错追加尾部");
        }

        [Test]
        public void Register_任务与Prompt模板_同类Upsert隔离()
        {
            var manager = new TemplateManager(null, null, null);

            manager.RegisterTaskTemplate(NewTask("kill", "击杀任务"));
            manager.RegisterPromptTemplate(NewPrompt("default", "默认提示词"));

            Assert.AreEqual(0, manager.GetLevelTemplates().Count, "三类模板互不串扰");
            Assert.IsNotNull(manager.GetTaskTemplateById("kill"));
            Assert.AreEqual("default", manager.GetDefaultPromptTemplate().TemplateId);
        }

        [Test]
        public void Unregister_命中移除返回true_未命中返回false且不触发事件()
        {
            var manager = CreateMemoryManager();
            var fired = 0;
            manager.TemplatesChanged += () => fired++;

            Assert.IsTrue(manager.UnregisterLevelTemplate("linear"), "命中应移除并返回 true");
            Assert.IsNull(manager.GetTemplateById("linear"));
            Assert.AreEqual(1, manager.GetLevelTemplates().Count);
            Assert.AreEqual(1, fired, "成功注销应触发变更事件");

            Assert.IsFalse(manager.UnregisterLevelTemplate("linear"), "未命中应返回 false");
            Assert.IsFalse(manager.UnregisterLevelTemplate(null));
            Assert.AreEqual(1, fired, "未命中/空 ID 不触发事件");
        }

        [Test]
        public void Unregister_空ID与不存在ID_静默失败()
        {
            var manager = CreateMemoryManager();
            Assert.IsFalse(manager.UnregisterLevelTemplate("不存在"));
            // 跨类别注销不命中：用关卡模板 API 注销任务模板 ID → 返回 false 且不误删任务模板
            Assert.IsFalse(manager.UnregisterLevelTemplate("kill"), "任务模板与关卡模板按类别隔离，互不误删");
            Assert.IsNotNull(manager.GetTaskTemplateById("kill"), "注销关卡模板不应影响任务模板");
        }

        // —— Reload 动态加载（第五周-Day4） ——

        /// <summary> 测试假加载源：每次 Load 返回快照（new 引用，模拟资产目录每次扫描都是新实例） </summary>
        private class FakeSource : ITemplateSource
        {
            private readonly Func<TemplateCollection> _factory;
            public FakeSource(Func<TemplateCollection> factory) => _factory = factory;
            public TemplateCollection Load() => _factory();
        }

        [Test]
        public void Reload_整体替换三类模板_并触发一次事件()
        {
            var source = new FakeSource(() => new TemplateCollection
            {
                LevelTemplates = new List<LevelTemplate> { NewLevel("linear", "线性闯关") },
                TaskTemplates = new List<TaskTemplate> { NewTask("kill", "击杀任务") },
                PromptTemplates = new List<PromptTemplate> { NewPrompt("default", "默认提示词") }
            });
            var manager = new TemplateManager(source);
            manager.RegisterLevelTemplate(NewLevel("manual", "手动注册")); // 先注册增量条目（模拟重载前状态）
            var fired = 0;
            manager.TemplatesChanged += () => fired++;

            Assert.IsTrue(manager.Reload());
            Assert.AreEqual(1, fired, "Reload 成功后触发一次事件");
            Assert.AreEqual(1, manager.GetLevelTemplates().Count);
            Assert.AreEqual("linear", manager.GetLevelTemplates()[0].TemplateId, "手动增量被源快照覆盖（重载语义契约：源为准）");
            Assert.IsNull(manager.GetTemplateById("manual"), "被源覆盖的条目不再可查");
            Assert.AreEqual(1, manager.GetTaskTemplates().Count);
            Assert.AreEqual("default", manager.GetDefaultPromptTemplate().TemplateId);
        }

        [Test]
        public void Reload_未注入加载源_返回false且不触发事件()
        {
            var manager = CreateMemoryManager();
            var fired = 0;
            manager.TemplatesChanged += () => fired++;

            Assert.IsFalse(manager.Reload(), "无源时应返回 false");
            Assert.AreEqual(0, fired);
            Assert.AreEqual(2, manager.GetLevelTemplates().Count, "Reload 失败不影响既有注册");
        }

        // —— 真实资产加载（原 LoadFromAssets 回归，改经 TemplateAssetSource + Reload 链路） ——

        [Test]
        public void LoadFromAssets_加载全部内置关卡模板_数量不少于四且含四类id()
        {
            var manager = LoadFromAssets();
            var templates = manager.GetLevelTemplates();

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
            var manager = LoadFromAssets();
            Assert.IsNotNull(manager.GetDefaultPromptTemplate(), "Assets/Settings/PromptTemplates 下应有 Prompt 模板资产");
        }

        [Test]
        public void LoadFromAssets_任务模板已加载()
        {
            var manager = LoadFromAssets();
            Assert.GreaterOrEqual(manager.GetTaskTemplates().Count, 2, "内置击杀/收集任务模板资产应全部加载");
        }

        [Test]
        public void LoadFromAssets_重复Reload_集合可整体替换且事件按次触发()
        {
            var manager = LoadFromAssets();
            var fired = 0;
            manager.TemplatesChanged += () => fired++;

            Assert.IsTrue(manager.Reload(), "资产源重复 Reload 应成功（策划改资产后刷新语义）");
            Assert.AreEqual(1, fired);
            Assert.GreaterOrEqual(manager.GetLevelTemplates().Count, 4, "重载后集合仍完整");
        }
    }
}
