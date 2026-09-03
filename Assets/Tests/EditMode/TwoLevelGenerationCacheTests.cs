using AILevelGenerator.Runtime.LLM;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 两级缓存组合器单元测试（第五周-Day5）：内存优先、磁盘命中回填内存、
    /// Put 双写、Clear 双清、磁盘层缺省退化为纯内存。两级均用纯内存实例（磁盘行为另测）。
    /// </summary>
    public class TwoLevelGenerationCacheTests
    {
        private const string Tpl = "tpl1";
        private const string Prompt = "设计一个营地";
        private const int Seed = 7;
        private const ulong DepHash = 123UL;
        private const int SchemaVersion = 1;

        private TwoLevelGenerationCache Create(GenerationCache memory, GenerationCache disk = null)
            => new(memory, disk);

        [Test]
        public void 内存优先_两层同键不同内容_取内存值()
        {
            var memory = new GenerationCache();
            var disk = new GenerationCache();
            memory.Put(Tpl, Seed, Prompt, DepHash, SchemaVersion, "内存值");
            disk.Put(Tpl, Seed, Prompt, DepHash, SchemaVersion, "磁盘值");

            var cache = Create(memory, disk);
            Assert.IsTrue(cache.TryGet(Tpl, Seed, Prompt, DepHash, SchemaVersion, out var json));
            Assert.AreEqual("内存值", json, "内存命中优先，不读磁盘");
        }

        [Test]
        public void 磁盘命中_自动回填内存_下次内存直中()
        {
            var memory = new GenerationCache();
            var disk = new GenerationCache();
            disk.Put(Tpl, Seed, Prompt, DepHash, SchemaVersion, "{\"level_name\":\"营地\"}");

            var cache = Create(memory, disk);
            Assert.IsTrue(cache.TryGet(Tpl, Seed, Prompt, DepHash, SchemaVersion, out var json), "内存未命中应查磁盘");
            Assert.AreEqual("{\"level_name\":\"营地\"}", json);

            Assert.IsTrue(memory.TryGet(Tpl, Seed, Prompt, DepHash, SchemaVersion, out _), "磁盘命中应回填内存（下次零 IO 直中）");
        }

        [Test]
        public void 双双未命中_返回false()
        {
            var cache = Create(new GenerationCache(), new GenerationCache());
            Assert.IsFalse(cache.TryGet(Tpl, Seed, "另一个提示词", DepHash, SchemaVersion, out _));
        }

        [Test]
        public void 写入_同时落两级()
        {
            var memory = new GenerationCache();
            var disk = new GenerationCache();
            var cache = Create(memory, disk);

            cache.Put(Tpl, Seed, Prompt, DepHash, SchemaVersion, "{}");

            Assert.IsTrue(memory.TryGet(Tpl, Seed, Prompt, DepHash, SchemaVersion, out _), "内存层应写入");
            Assert.IsTrue(disk.TryGet(Tpl, Seed, Prompt, DepHash, SchemaVersion, out _), "磁盘层应写入");
        }

        [Test]
        public void 清空_两级同清()
        {
            var memory = new GenerationCache();
            var disk = new GenerationCache();
            var cache = Create(memory, disk);
            cache.Put(Tpl, Seed, Prompt, DepHash, SchemaVersion, "{}");

            cache.Clear();

            Assert.IsFalse(memory.TryGet(Tpl, Seed, Prompt, DepHash, SchemaVersion, out _));
            Assert.IsFalse(disk.TryGet(Tpl, Seed, Prompt, DepHash, SchemaVersion, out _));
        }

        [Test]
        public void 磁盘层缺省_退化为纯内存缓存()
        {
            var cache = Create(new GenerationCache());
            Assert.IsNull(cache.DiskCache, "未注入磁盘层时 DiskCache 应为 null");

            cache.Put(Tpl, Seed, Prompt, DepHash, SchemaVersion, "{}");
            Assert.IsTrue(cache.TryGet(Tpl, Seed, Prompt, DepHash, SchemaVersion, out _), "无磁盘层也应正常内存命中");
            Assert.IsTrue(cache.MemoryCache.TryGet(Tpl, Seed, Prompt, DepHash, SchemaVersion, out _), "写入应落在内存层");
        }
    }
}
