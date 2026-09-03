using System;
using System.Globalization;
using System.IO;
using AILevelGenerator.Runtime.LLM;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 磁盘缓存单元测试（第五周-Day5）：持久化跨实例命中、键变化不命中、
    /// 容量 FIFO 淘汰、空文件/目录缺失自愈、清空、垃圾文件名忽略。
    /// 全部使用系统临时目录（Guid 唯一后缀），TearDown 递归删除。
    /// </summary>
    public class DiskGenerationCacheTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ALGGenCache_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
            }
            catch (IOException)
            {
                // 清理失败不影响测试结论（残留于系统临时目录）
            }
        }

        private static string HexFileName(ulong key) => key.ToString("X16", CultureInfo.InvariantCulture) + ".json";

        private static ulong KeyOf(string tpl = "tpl", int seed = 1, string prompt = "设计一个营地",
            ulong depHash = 0UL, int schemaVersion = 0)
            => GenerationCache.BuildKey(tpl, seed, prompt, depHash, schemaVersion);

        [Test]
        public void 持久化_新实例同目录_命中磁盘条目()
        {
            var dir = _tempDir;
            var first = new DiskGenerationCache(dir);
            first.Put("tpl", 42, "设计一个营地", 0UL, 0, "{\"level_name\":\"营地\"}");

            // 新实例（模拟编辑器重启/域重载后重新装配）：目录不变应直接命中磁盘
            var second = new DiskGenerationCache(dir);
            Assert.IsTrue(second.TryGet("tpl", 42, "设计一个营地", 0UL, 0, out var json));
            Assert.AreEqual("{\"level_name\":\"营地\"}", json);
        }

        [Test]
        public void 键变化_依赖哈希或Schema版本不同_磁盘不命中()
        {
            var cache = new DiskGenerationCache(_tempDir);
            cache.Put("tpl", 1, "p", 100UL, 1, "{}");

            Assert.IsFalse(cache.TryGet("tpl", 1, "p", 200UL, 1, out _), "模板依赖哈希不同不应命中");
            Assert.IsFalse(cache.TryGet("tpl", 1, "p", 100UL, 2, out _), "Schema 版本不同不应命中");
            Assert.IsTrue(cache.TryGet("tpl", 1, "p", 100UL, 1, out _), "键完全一致应命中");
        }

        [Test]
        public void 容量_超上限_淘汰写入最旧条目()
        {
            var cache = new DiskGenerationCache(_tempDir, capacity: 2);
            cache.Put("a", 0, "p1", 0UL, 0, "{}");
            cache.Put("b", 0, "p2", 0UL, 0, "{}");
            cache.Put("c", 0, "p3", 0UL, 0, "{}"); // 满员触发淘汰 a

            Assert.AreEqual(2, cache.Count);
            Assert.IsFalse(cache.TryGet("a", 0, "p1", 0UL, 0, out _), "最旧条目应被淘汰");
            Assert.IsTrue(cache.TryGet("c", 0, "p3", 0UL, 0, out _), "新条目应保留");
            Assert.AreEqual(2, Directory.GetFiles(_tempDir, "*.json").Length, "淘汰应同步删除磁盘文件");
        }

        [Test]
        public void 空文件_自愈删除并降级未命中()
        {
            Directory.CreateDirectory(_tempDir);
            var key = KeyOf();
            File.WriteAllText(Path.Combine(_tempDir, HexFileName(key)), string.Empty); // 模拟半写中断残留

            var cache = new DiskGenerationCache(_tempDir);
            Assert.IsFalse(cache.TryGet("tpl", 1, "设计一个营地", 0UL, 0, out _), "空文件应按未命中处理");
            Assert.IsFalse(File.Exists(Path.Combine(_tempDir, HexFileName(key))), "空文件应被自愈删除");
        }

        [Test]
        public void 目录被外部删除_下次写入自动重建()
        {
            var cache = new DiskGenerationCache(_tempDir);
            cache.Put("tpl", 1, "p", 0UL, 0, "第一版");
            Directory.Delete(_tempDir, true); // 外部误删目录（含已入库文件）

            cache.Put("tpl", 2, "p2", 0UL, 0, "第二版"); // 自愈：重建目录并写入
            Assert.IsTrue(Directory.Exists(_tempDir));
            Assert.IsTrue(cache.TryGet("tpl", 2, "p2", 0UL, 0, out var json));
            Assert.AreEqual("第二版", json);
        }

        [Test]
        public void 索引残留但文件已删_命中时自愈移除()
        {
            var cache = new DiskGenerationCache(_tempDir);
            cache.Put("tpl", 1, "p", 0UL, 0, "{}");
            var key = KeyOf("tpl", 1, "p", 0UL, 0);
            File.Delete(Path.Combine(_tempDir, HexFileName(key))); // 索引在、文件被外部删除

            Assert.IsFalse(cache.TryGet("tpl", 1, "p", 0UL, 0, out _), "文件缺失应按未命中处理");
            Assert.AreEqual(0, cache.Count, "失效条目应从索引移除（自愈）");
        }

        [Test]
        public void 清空_索引与全部文件清空()
        {
            var cache = new DiskGenerationCache(_tempDir);
            cache.Put("a", 0, "p1", 0UL, 0, "{}");
            cache.Put("b", 0, "p2", 0UL, 0, "{}");

            cache.Clear();

            Assert.AreEqual(0, cache.Count);
            Assert.AreEqual(0, Directory.GetFiles(_tempDir).Length, "json 与残留 tmp 都应清空");
        }

        [Test]
        public void 垃圾文件_非十六进制名忽略不索引()
        {
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(Path.Combine(_tempDir, "not-a-key.json"), "垃圾内容"); // 手写/第三方残留
            var key = KeyOf();
            File.WriteAllText(Path.Combine(_tempDir, HexFileName(key)), "{\"level_name\":\"营地\"}");

            var cache = new DiskGenerationCache(_tempDir);
            Assert.IsTrue(cache.TryGet("tpl", 1, "设计一个营地", 0UL, 0, out _), "合法条目不受垃圾文件影响（先触发索引懒加载）");
            Assert.AreEqual(1, cache.Count, "非十六进制文件名的垃圾文件不应进索引");
        }

        [Test]
        public void 目录为空串_降级未命中不抛异常()
        {
            var cache = new DiskGenerationCache(() => string.Empty);
            Assert.DoesNotThrow(() =>
            {
                Assert.IsFalse(cache.TryGet("tpl", 1, "p", 0UL, 0, out _));
                cache.Put("tpl", 1, "p", 0UL, 0, "{}");
                cache.Clear();
            });
        }
    }
}
