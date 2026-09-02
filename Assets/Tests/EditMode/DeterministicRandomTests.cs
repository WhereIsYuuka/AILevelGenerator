using System;
using System.Collections.Generic;
using AILevelGenerator.Runtime.Utilities;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 确定性随机工具单元测试（第五周-Day1）：
    /// - 黄金值锚定：固化种子 0/42 的首段输出（独立 Python 复算），算法一经变更即失败（跨运行时稳定性保证）
    /// - 同种子流一致 / 异种子明显不同（Day1 验收标准的机制级证明）
    /// - API 边界：Range/Chance/Choice/WeightedChoice/Shuffle/RotationY
    /// </summary>
    public class DeterministicRandomTests
    {
        // —— 黄金值锚定（独立复算：PCG32 + SplitMix64 种子打散，见第五周开发计划文档） ——

        [Test]
        public void 种子0_Range_命中黄金值()
        {
            var rng = new DeterministicRandom(0);
            Assert.AreEqual(980, rng.Range(1000));
            Assert.AreEqual(474, rng.Range(1000));
            Assert.AreEqual(109, rng.Range(1000));
            Assert.AreEqual(856, rng.Range(1000));
            Assert.AreEqual(421, rng.Range(1000));
        }

        [Test]
        public void 种子0_UnitFloat与RotationY_命中黄金值()
        {
            var rng = new DeterministicRandom(0);
            for (var i = 0; i < 5; i++) rng.Range(1000); // 推进到第 5 次抽取后
            Assert.AreEqual(0.18923, rng.NextUnitFloat(), 1e-5);
            Assert.AreEqual(0.590916, rng.NextUnitFloat(), 1e-5);
            Assert.AreEqual(0.480989, rng.NextUnitFloat(), 1e-5);
            Assert.Less(rng.RotationY(), 360f);
            Assert.Less(rng.RotationY(), 360f);
        }

        [Test]
        public void 种子42_命中黄金值()
        {
            var rng = new DeterministicRandom(42);
            Assert.AreEqual(869, rng.Range(1000));
            Assert.AreEqual(483, rng.Range(1000));
            Assert.AreEqual(839, rng.Range(1000));
            Assert.AreEqual(577, rng.Range(1000));
            Assert.AreEqual(757, rng.Range(1000));
            Assert.AreEqual(0.31931, rng.NextUnitFloat(), 1e-5);
            Assert.AreEqual(0.658102, rng.NextUnitFloat(), 1e-5);
            Assert.AreEqual(0.257532, rng.NextUnitFloat(), 1e-5);
        }

        // —— 确定性契约 ——

        [Test]
        public void 同种子_两个实例_序列完全一致()
        {
            var a = new DeterministicRandom(20260830);
            var b = new DeterministicRandom(20260830);
            for (var i = 0; i < 50; i++)
                Assert.AreEqual(a.Range(0, 10000), b.Range(0, 10000), $"第 {i} 次抽取不一致");
            for (var i = 0; i < 20; i++)
                Assert.AreEqual(a.NextUnitFloat(), b.NextUnitFloat(), 1e-6f, $"第 {i} 次浮点抽取不一致");
        }

        [Test]
        public void 不同种子_首段输出_明显不同()
        {
            var a = new DeterministicRandom(1);
            var b = new DeterministicRandom(2);
            var same = 0;
            for (var i = 0; i < 10; i++)
                if (a.Range(0, 1000) == b.Range(0, 1000)) same++;
            Assert.Less(same, 3, "相邻种子输出应明显不同（SplitMix 打散生效）");
        }

        // —— API 边界 ——

        [Test]
        public void Range_整数_落在闭开区间内()
        {
            var rng = new DeterministicRandom(7);
            for (var i = 0; i < 200; i++)
            {
                var v = rng.Range(-5, 8);
                Assert.GreaterOrEqual(v, -5);
                Assert.Less(v, 8);
            }
        }

        [Test]
        public void Range_浮点_单元素区间_恒定返回该值()
        {
            var rng = new DeterministicRandom(7);
            for (var i = 0; i < 20; i++)
                Assert.AreEqual(3.5f, rng.Range(3.5f, 3.5f), 1e-6f);
        }

        [Test]
        public void Range_参数非法_抛异常()
        {
            var rng = new DeterministicRandom(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Range(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Range(5f, 3f));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Range(-1));
        }

        [Test]
        public void Chance_概率0和1_恒为false与true()
        {
            var rng = new DeterministicRandom(3);
            for (var i = 0; i < 50; i++)
            {
                Assert.IsFalse(rng.Chance(0f));
                Assert.IsTrue(rng.Chance(1f));
            }
        }

        [Test]
        public void Choice_遍历列表_每次命中合法下标对应项()
        {
            var rng = new DeterministicRandom(11);
            var items = new List<string> { "近战", "远程", "精英" };
            for (var i = 0; i < 100; i++)
                Assert.Contains(rng.Choice(items), items);
        }

        [Test]
        public void Choice_空列表_抛异常()
        {
            var rng = new DeterministicRandom(1);
            Assert.Throws<ArgumentException>(() => rng.Choice(new List<int>()));
        }

        [Test]
        public void WeightedIndex_权重100_0_永远选中第一项()
        {
            var rng = new DeterministicRandom(5);
            for (var i = 0; i < 50; i++)
                Assert.AreEqual(0, rng.WeightedIndex(new[] { 100f, 0f, 0f }));
        }

        [Test]
        public void WeightedIndex_权重命中率_与配置比例近似()
        {
            var rng = new DeterministicRandom(99);
            const int n = 10000;
            var first = 0;
            for (var i = 0; i < n; i++)
                if (rng.WeightedIndex(new[] { 1f, 3f }) == 0) first++;
            // 期望 ~25%，允许 ±4% 统计波动（确定性流固定种子，不会 flaky）
            Assert.That(first, Is.InRange(n * 0.21, n * 0.29));
        }

        [Test]
        public void Shuffle_同种子_置换一致_且为原集合重排()
        {
            var a = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var b = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var rngA = new DeterministicRandom(2026);
            var rngB = new DeterministicRandom(2026);
            rngA.Shuffle(a);
            rngB.Shuffle(b);
            CollectionAssert.AreEqual(a, b, "同种子洗牌结果必须一致");
            CollectionAssert.AreNotEqual(new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, a, "洗牌不应保持原序");
            a.Sort();
            CollectionAssert.AreEqual(new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, a, "洗牌必须只是重排");
        }
    }
}
