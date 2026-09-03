using AILevelGenerator.Runtime.LLM;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 生成请求缓存单元测试：命中/未命中、FIFO 上限淘汰、同键更新、清空、键稳定性。
    /// </summary>
    public class GenerationCacheTests
    {
        [Test]
        public void 命中_同参两次_第二次返回缓存()
        {
            var cache = new GenerationCache();
            Assert.IsFalse(cache.TryGet("tpl", 42, "设计一个营地", out _), "首次未命中");

            cache.Put("tpl", 42, "设计一个营地", "{\"level_name\":\"营地\"}");

            Assert.IsTrue(cache.TryGet("tpl", 42, "设计一个营地", out var json));
            Assert.AreEqual("{\"level_name\":\"营地\"}", json);
            Assert.AreEqual(1, cache.Count);
        }

        [Test]
        public void 未命中_参数任一不同_不命中()
        {
            var cache = new GenerationCache();
            cache.Put("tpl", 42, "设计一个营地", "{}");

            Assert.IsFalse(cache.TryGet("other", 42, "设计一个营地", out _), "模板不同不命中");
            Assert.IsFalse(cache.TryGet("tpl", 43, "设计一个营地", out _), "种子不同不命中");
            Assert.IsFalse(cache.TryGet("tpl", 42, "设计两个营地", out _), "提示词不同不命中");
        }

        [Test]
        public void 淘汰_超过上限_FIFO淘汰最旧()
        {
            var cache = new GenerationCache(3);
            cache.Put("a", 0, "p1", "{}");
            cache.Put("b", 0, "p2", "{}");
            cache.Put("c", 0, "p3", "{}");
            cache.Put("d", 0, "p4", "{}"); // 淘汰 a

            Assert.AreEqual(3, cache.Count);
            Assert.IsFalse(cache.TryGet("a", 0, "p1", out _), "最旧条目应被淘汰");
            Assert.IsTrue(cache.TryGet("d", 0, "p4", out _));
        }

        [Test]
        public void 更新_同键覆盖_计数不变()
        {
            var cache = new GenerationCache();
            cache.Put("t", 1, "p", "旧");
            cache.Put("t", 1, "p", "新");

            Assert.AreEqual(1, cache.Count);
            Assert.IsTrue(cache.TryGet("t", 1, "p", out var json));
            Assert.AreEqual("新", json);
        }

        [Test]
        public void 清空_恢复空()
        {
            var cache = new GenerationCache();
            cache.Put("t", 1, "p", "{}");
            cache.Clear();

            Assert.AreEqual(0, cache.Count);
            Assert.IsFalse(cache.TryGet("t", 1, "p", out _));
        }

        [Test]
        public void 键_中文提示词与空值_稳定生成()
        {
            var a = GenerationCache.BuildKey("模板A", -7, "设计一个包含宝箱与敌人的森林营地");
            var b = GenerationCache.BuildKey("模板A", -7, "设计一个包含宝箱与敌人的森林营地");
            var c = GenerationCache.BuildKey("模板A", -7, "设计一个包含宝箱与敌人的森林营地！");

            Assert.AreEqual(a, b, "相同参数哈希必须一致");
            Assert.AreNotEqual(a, c, "提示词不同哈希必须不同");
            Assert.AreNotEqual(0UL, a, "空输入哈希不为 0（避免误判空缓存）");
        }

        // —— 第五周-Day5：模板依赖哈希 / Schema 版本参与键（模板资产变更自动失效） ——

        [Test]
        public void 键_依赖哈希不同_键不同()
        {
            var a = GenerationCache.BuildKey("tpl", 1, "p", 100UL, 1);
            var b = GenerationCache.BuildKey("tpl", 1, "p", 200UL, 1);

            Assert.AreNotEqual(a, b, "模板依赖哈希不同（资产变更）键必须不同");
        }

        [Test]
        public void 键_Schema版本不同_键不同()
        {
            var a = GenerationCache.BuildKey("tpl", 1, "p", 100UL, 1);
            var b = GenerationCache.BuildKey("tpl", 1, "p", 100UL, 2);

            Assert.AreNotEqual(a, b, "Schema 契约版本不同键必须不同");
        }

        [Test]
        public void 键_旧三参入口_等价于哈希版本为零()
        {
            Assert.AreEqual(
                GenerationCache.BuildKey("tpl", 42, "提示词"),
                GenerationCache.BuildKey("tpl", 42, "提示词", 0UL, 0),
                "旧三参入口语义应等价于依赖哈希/版本为零（既有调用方零改动）");
        }

        [Test]
        public void 缓存_依赖哈希变化_旧条目不命中()
        {
            var cache = new GenerationCache();
            cache.Put("tpl", 42, "设计一个营地", 100UL, 1, "{}");

            Assert.IsFalse(cache.TryGet("tpl", 42, "设计一个营地", 200UL, 1, out _), "哈希不同（资产已变更）不应命中旧条目");
            Assert.IsTrue(cache.TryGet("tpl", 42, "设计一个营地", 100UL, 1, out _), "哈希一致应命中");
        }
    }
}
