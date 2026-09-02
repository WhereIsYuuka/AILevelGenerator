using System;
using System.Collections.Generic;

namespace AILevelGenerator.Runtime.Utilities
{
    /// <summary>
    /// 确定性随机数工具（第五周-Day1，种子确定性机制核心）。
    /// 基于 PCG32（Permuted Congruential Generator）的固定实现：全部用 uint/ulong 纯算术，
    /// 不依赖任何运行时随机实现（UnityEngine.Random 全局静态不可控、System.Random 算法随
    /// .NET 版本/平台可漂移），保证「相同种子 → 完全相同序列」跨平台稳定。
    ///
    /// 种子契约（全项目强制）：
    /// - 所有模板/生成逻辑中的随机（位置、旋转、选型、巡逻点…）一律走本工具；
    /// - 严禁混用 UnityEngine.Random 与 System.Random（否则种子完全失效，见需求避坑指南）；
    /// - 每个实例是独立随机流，模板侧用 RandomSeedUtility.Derive 派生子种子创建新流，
    ///   保证后续新增随机逻辑不影响既有种子已产出的序列（向后确定性）。
    /// </summary>
    public sealed class DeterministicRandom
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong Increment = 1442695040888963407UL; // 奇数增量（PCG 要求）

        private ulong _state;

        public DeterministicRandom(int seed)
        {
            // 种子先经 SplitMix64 打散：PCG 直接以小种子起步时相邻种子序列相关性高，
            // 打散后保证「相邻种子 → 结果明显不同」（Day1 验收标准 2）。
            _state = SplitMix64((ulong)(uint)seed);
        }

        // —— 基础抽取 ——

        private uint NextUInt32()
        {
            var old = _state;
            _state = old * Multiplier + Increment;
            var xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            var rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        /// <summary> 取 [0, maxExclusive) 的整数（maxExclusive 必须 &gt; 0，拒绝偏差） </summary>
        public int Range(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive 必须大于 0");
            // 取模会引入微小偏差，但对关卡随机内容可忽略；此路径简单且边界确定。
            return (int)(NextUInt32() % (uint)maxExclusive);
        }

        /// <summary> 取 [minInclusive, maxExclusive) 的整数 </summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive 必须大于 minInclusive");
            return minInclusive + Range(maxExclusive - minInclusive);
        }

        /// <summary> 取 [0, 1) 的浮点数（高 24 位抽取，均匀稳定） </summary>
        public float NextUnitFloat()
        {
            return (NextUInt32() >> 8) * (1f / 16777216f);
        }

        /// <summary> 取 [min, max] 区间的浮点数 </summary>
        public float Range(float min, float max)
        {
            if (max < min)
                throw new ArgumentOutOfRangeException(nameof(max), "max 不能小于 min");
            return min + (max - min) * NextUnitFloat();
        }

        /// <summary> 概率命中：[0,1) 均匀值小于 p 则 true（p=0 恒 false，p=1 恒 true） </summary>
        public bool Chance(float probability)
        {
            if (probability < 0f || probability > 1f)
                throw new ArgumentOutOfRangeException(nameof(probability), "概率必须在 [0,1] 区间");
            return NextUnitFloat() < probability;
        }

        /// <summary> 随机 ±1（选型/朝向翻转用） </summary>
        public int Sign()
        {
            return (NextUInt32() & 1u) == 0u ? 1 : -1;
        }

        // —— 选择 / 排列（Day2 敌人选型、巡逻点排列等） ——

        /// <summary> 等概率从列表中选一项（列表为空抛异常） </summary>
        public T Choice<T>(IReadOnlyList<T> items)
        {
            if (items == null || items.Count == 0)
                throw new ArgumentException("选择列表不能为空", nameof(items));
            return items[Range(items.Count)];
        }

        /// <summary> 按权重选下标：weights 与 items 等长，返回选中下标（实现模板权重配置入口） </summary>
        public int WeightedIndex(IReadOnlyList<float> weights)
        {
            if (weights == null || weights.Count == 0)
                throw new ArgumentException("权重列表不能为空", nameof(weights));
            var total = 0f;
            foreach (var w in weights)
            {
                if (w < 0f) throw new ArgumentException("权重不能为负", nameof(weights));
                total += w;
            }
            if (total <= 0f)
                throw new ArgumentException("权重总和必须大于 0", nameof(weights));
            var roll = NextUnitFloat() * total;
            var acc = 0f;
            for (var i = 0; i < weights.Count; i++)
            {
                acc += weights[i];
                if (roll < acc) return i;
            }
            return weights.Count - 1; // 浮点累计误差兜底
        }

        /// <summary> 按权重从列表中选一项（items 与 weights 等长） </summary>
        public T WeightedChoice<T>(IReadOnlyList<T> items, IReadOnlyList<float> weights)
        {
            if (items == null || weights == null || items.Count != weights.Count)
                throw new ArgumentException("items 与 weights 必须等长且非空", nameof(items));
            return items[WeightedIndex(weights)];
        }

        /// <summary> Fisher-Yates 原地洗牌（用确定性流，同种子置换完全一致），返回同一 list 便于链式 </summary>
        public IList<T> Shuffle<T>(IList<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = Range(i + 1); // [0, i]
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        // —— 场景几何（位置/旋转） ——

        /// <summary> 随机 Y 轴欧拉角 [0, 360)，物体朝向随机化入口 </summary>
        public float RotationY()
        {
            return Range(0f, 360f);
        }

        /// <summary> 把种子打散为良好的初始状态（SplitMix64 单步） </summary>
        private static ulong SplitMix64(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }
}
