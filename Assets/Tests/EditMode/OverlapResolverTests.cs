using System.Collections.Generic;
using AILevelGenerator.Runtime.Utilities;
using NUnit.Framework;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 重叠解析器单元测试（Day2）：
    /// 覆盖相交判定、重叠体积计算、单对/多物体分离、迭代收敛与验收指标（10 怪物重叠率 &lt;10%）。
    /// </summary>
    public class OverlapResolverTests
    {
        /// <summary> 标准怪物：1×2×1（宽×高×深），贴近演示预制体尺寸 </summary>
        private static LayoutObject Monster(float x, float z) => new(new Vector3(x, 1f, z), new Vector3(1f, 2f, 1f));

        [Test]
        public void 无重叠_重叠率为零()
        {
            var objects = new List<LayoutObject> { Monster(0f, 0f), Monster(3f, 0f), Monster(0f, 3f) };
            Assert.AreEqual(0f, OverlapResolver.GetOverlapRatio(objects));
        }

        [Test]
        public void 完全重叠_重叠率为05()
        {
            // 两个同尺寸物体完全重叠：重叠体积 = 1 个物体体积，总体积 = 2 个物体体积 → 50%
            var objects = new List<LayoutObject> { Monster(0f, 0f), Monster(0f, 0f) };
            Assert.AreEqual(0.5f, OverlapResolver.GetOverlapRatio(objects), 0.0001f);
        }

        [Test]
        public void 边缘相切_不算重叠()
        {
            // 间距恰好等于半宽之和：penX = 0 → 不相交
            var a = Monster(0f, 0f);
            var b = Monster(1f, 0f);
            Assert.IsFalse(OverlapResolver.IsHorizontallyOverlapping(a, b), "边缘相切不算重叠");
        }

        [Test]
        public void 部分重叠_体积计算正确()
        {
            // a(0,0,0) 与 b(0.25,0,0)：x 重叠 0.75，y 全重叠 2，z 全重叠 1 → 体积 1.5
            var a = Monster(0f, 0f);
            var b = Monster(0.25f, 0f);
            Assert.AreEqual(1.5f, OverlapResolver.GetOverlapVolume(a, b), 0.0001f);
        }

        [Test]
        public void 单对重叠_分离后不再重叠()
        {
            var objects = new List<LayoutObject> { Monster(0f, 0f), Monster(0.5f, 0f) };
            Assert.IsTrue(OverlapResolver.IsHorizontallyOverlapping(objects[0], objects[1]));

            OverlapResolver.ResolveRound(objects);

            Assert.IsFalse(OverlapResolver.IsHorizontallyOverlapping(objects[0], objects[1]), "分离后应不再重叠");
            Assert.AreEqual(0f, OverlapResolver.GetOverlapRatio(objects), 0.0001f);
        }

        [Test]
        public void 分离方向_沿最小穿透轴()
        {
            // penX = 0.5，penZ = 0.4 → 应沿 Z 轴分离（位移最小，保持布局意图）
            var objects = new List<LayoutObject>
            {
                Monster(0f, 0f),
                new(new Vector3(0.5f, 1f, 0.6f), new Vector3(1f, 2f, 1f))
            };
            var x0 = objects[0].Position.x;
            var z0 = objects[0].Position.z;

            OverlapResolver.TrySeparate(objects, 0, 1);

            Assert.AreEqual(x0, objects[0].Position.x, 0.0001f, "最小穿透轴为 Z，X 不应移动");
            Assert.AreNotEqual(z0, objects[0].Position.z, "应沿 Z 轴分离");
            Assert.IsFalse(OverlapResolver.IsHorizontallyOverlapping(objects[0], objects[1]));
        }

        [Test]
        public void 十个怪物两行重叠排布_迭代后重叠率小于10百分比()
        {
            // 2 行 × 5 列，行列间距 0.5m（物体 1×2×1）→ 行内/行间严重重叠，初始重叠率 ≈ 65%
            var objects = new List<LayoutObject>();
            for (var row = 0; row < 2; row++)
            for (var col = 0; col < 5; col++)
                objects.Add(Monster(col * 0.5f, row * 0.5f));

            var initialRatio = OverlapResolver.GetOverlapRatio(objects);
            Assert.Greater(initialRatio, 0.5f, "测试布局应确实重叠严重");

            var finalRatio = OverlapResolver.ResolveAll(objects);

            Assert.Less(finalRatio, 0.1f, $"验收指标：重叠体积占比应 < 10%（实际 {finalRatio:P1}）");
        }

        [Test]
        public void 全部挤在原点_迭代收敛不死循环且重叠率改善()
        {
            // 全重叠极端布局：验证 ResolveAll 有轮数上限（不死循环）且能显著改善
            var objects = new List<LayoutObject>();
            for (var i = 0; i < 10; i++) objects.Add(Monster(0f, 0f));

            var initialRatio = OverlapResolver.GetOverlapRatio(objects);
            Assert.Greater(initialRatio, 1f, "10 个物体完全重叠：成对累加重叠体积应为总体积的 4.5 倍");

            var finalRatio = OverlapResolver.ResolveAll(objects, maxRounds: 10);

            Assert.Less(finalRatio, initialRatio, "迭代后重叠率应显著改善");
            Assert.IsTrue(finalRatio <= initialRatio, "结果应为有限值（未死循环）");
        }

        [Test]
        public void 多轮迭代_逐步收敛到无重叠()
        {
            // 3 个物体链式重叠：单轮分离不够，验证 ResolveAll 迭代收敛
            var objects = new List<LayoutObject> { Monster(0f, 0f), Monster(0.5f, 0f), Monster(1f, 0f) };
            var finalRatio = OverlapResolver.ResolveAll(objects, maxRounds: 5);
            Assert.AreEqual(0f, finalRatio, 0.001f);
        }
    }
}
