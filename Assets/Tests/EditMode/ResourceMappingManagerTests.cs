using System.Collections.Generic;
using AILevelGenerator.Runtime.Mappings;
using NUnit.Framework;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 资源映射管理器 EditMode 单元测试。
    /// 纯内存构造配置（不依赖磁盘资产），覆盖精确匹配、别名、模糊打分、大小写、缓存重建。
    /// </summary>
    public class ResourceMappingManagerTests
    {
        private PrefabMappingConfig _config;
        private readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PrefabMappingConfig>();
            _config.Entries.Add(new PrefabMappingEntry
            {
                LogicalName = "敌人-弓箭手",
                Prefab = CreateFake("Enemy_Archer"),
                Aliases = new List<string> { "敌人", "弓箭手", "Enemy" }
            });
            _config.Entries.Add(new PrefabMappingEntry
            {
                LogicalName = "宝箱",
                Prefab = CreateFake("Chest"),
                Aliases = new List<string> { "箱子", "Chest" }
            });
            _config.Entries.Add(new PrefabMappingEntry
            {
                LogicalName = "NPC",
                Prefab = CreateFake("NPC_Villager"),
                Aliases = new List<string> { "村民", "NPC" }
            });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            if (_config != null) Object.DestroyImmediate(_config);
        }

        /// <summary> 创建假预制体引用（HideAndDontSave 防止污染场景与测试结果） </summary>
        private GameObject CreateFake(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            _spawned.Add(go);
            return go;
        }

        private ResourceMappingManager CreateManager() => new(_config);

        [Test]
        public void TryGetPrefab_逻辑名精确匹配_返回对应预制体()
        {
            var manager = CreateManager();
            Assert.IsTrue(manager.TryGetPrefab("敌人-弓箭手", out var prefab));
            Assert.AreEqual("Enemy_Archer", prefab.name);
        }

        [Test]
        public void TryGetPrefab_别名精确匹配_返回对应预制体()
        {
            var manager = CreateManager();
            Assert.IsTrue(manager.TryGetPrefab("弓箭手", out var prefab));
            Assert.AreEqual("Enemy_Archer", prefab.name);
        }

        [Test]
        public void TryGetPrefab_模糊关键字包含_返回对应预制体()
        {
            var manager = CreateManager();
            // "NPC" 同时是宝箱条目的别名？不 —— "NPC" 是 NPC 条目的逻辑名，宝箱别名是 Chest/箱子
            Assert.IsTrue(manager.TryGetPrefab("箱子", out var chest));
            Assert.AreEqual("Chest", chest.name);
            Assert.IsTrue(manager.TryGetPrefab("村民", out var npc));
            Assert.AreEqual("NPC_Villager", npc.name);
        }

        [Test]
        public void TryGetPrefab_未命中_返回False且null()
        {
            var manager = CreateManager();
            Assert.IsFalse(manager.TryGetPrefab("不存在的物体", out var prefab));
            Assert.IsNull(prefab);
        }

        [Test]
        public void TryGetPrefab_空白输入_返回False()
        {
            var manager = CreateManager();
            Assert.IsFalse(manager.TryGetPrefab("", out _));
            Assert.IsFalse(manager.TryGetPrefab("   ", out _));
            Assert.IsFalse(manager.TryGetPrefab(null, out _));
        }

        [Test]
        public void GetPrefab_英文别名_大小写不敏感()
        {
            var manager = CreateManager();
            Assert.IsTrue(manager.TryGetPrefab("enemy", out var prefab));
            Assert.AreEqual("Enemy_Archer", prefab.name);
            Assert.IsTrue(manager.TryGetPrefab("CHEST", out prefab));
            Assert.AreEqual("Chest", prefab.name);
        }

        [Test]
        public void GetPrefabByFuzzy_多候选_取最高分()
        {
            // "敌人-弓箭手" 命中：逻辑名包含"敌人"（100）+ 别名"敌人"包含（50）+ 别名"弓箭手"包含（50）
            // "宝箱"/"NPC" 均不命中
            var manager = CreateManager();
            var result = manager.GetPrefabByFuzzy("敌人");
            Assert.IsNotNull(result);
            Assert.AreEqual("Enemy_Archer", result.name);
        }

        [Test]
        public void RebuildCache_条目删除后_重建索引才停止命中陈旧数据()
        {
            var manager = CreateManager();
            Assert.IsTrue(manager.TryGetPrefab("宝箱", out _));

            _config.Entries.RemoveAt(1); // 删除"宝箱"条目

            // 缓存未重建：精确索引仍持有旧条目引用 → 陈旧数据仍可命中（这正是需要 RebuildCache 的原因）
            Assert.IsTrue(manager.TryGetPrefab("宝箱", out _), "未重建缓存时精确索引仍含陈旧条目");

            manager.RebuildCache();
            // 重建后：精确 miss，模糊兜底也 miss（"宝箱"与其余条目名称/别名无包含关系）
            Assert.IsFalse(manager.TryGetPrefab("宝箱", out _), "重建索引后应无法命中已删除条目");
        }

        [Test]
        public void 空Prefab条目_被跳过()
        {
            _config.Entries.Add(new PrefabMappingEntry
            {
                LogicalName = "空引用",
                Prefab = null,
                Aliases = new List<string> { "空" }
            });
            var manager = CreateManager();
            Assert.IsFalse(manager.TryGetPrefab("空引用", out _), "未绑定预制体的条目不应参与精确匹配");
            Assert.IsNull(manager.GetPrefabByFuzzy("空引用"), "未绑定预制体的条目不应参与模糊匹配");
        }

        [Test]
        public void 重复逻辑名_后配置覆盖前配置()
        {
            _config.Entries.Add(new PrefabMappingEntry
            {
                LogicalName = "敌人-弓箭手", // 与第一条重复
                Prefab = CreateFake("Enemy_Archer_V2"),
                Aliases = new List<string>()
            });
            var manager = CreateManager();
            Assert.IsTrue(manager.TryGetPrefab("敌人-弓箭手", out var prefab));
            Assert.AreEqual("Enemy_Archer_V2", prefab.name, "精确索引应按配置顺序后写覆盖");
        }
    }
}
