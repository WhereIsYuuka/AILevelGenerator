using System.Collections.Generic;
using UnityEngine;

namespace AILevelGenerator.Runtime.Utilities
{
    /// <summary> 布局物体抽象（纯数据）：位置 + 世界尺寸，供重叠检测/分离计算 </summary>
    public struct LayoutObject
    {
        public Vector3 Position;
        public Vector3 Size;

        public LayoutObject(Vector3 position, Vector3 size)
        {
            Position = position;
            Size = size;
        }
    }

    /// <summary>
    /// 重叠解析器（纯计算，可单测）：AABB 相交体积量化重叠 → 水平面最小穿透轴分离。
    /// 设计语义：
    /// - 只做水平分离（x/z 轴）：高度由地面贴合统一决定，竖直重叠是正常现象；
    /// - 重叠体积 = 成对 AABB 相交体积之和，重叠率 = 重叠体积 ÷ 物体总体积（可量化验收指标）；
    /// - 分离沿穿透较浅的轴（位移最小，尽量保持 LLM 布局意图），各推一半 + 余量防贴脸误判。
    /// </summary>
    public static class OverlapResolver
    {
        /// <summary> 分离余量（米）：推开后留出空隙，防止贴脸被再次判定为重叠 </summary>
        public const float DefaultSeparationMargin = 0.05f;

        /// <summary> 两物体 AABB 是否水平重叠（x/z 轴相交；y 轴由地面贴合统一决定，不参与判定） </summary>
        public static bool IsHorizontallyOverlapping(LayoutObject a, LayoutObject b)
        {
            var ha = a.Size * 0.5f;
            var hb = b.Size * 0.5f;
            return Mathf.Abs(a.Position.x - b.Position.x) < ha.x + hb.x
                && Mathf.Abs(a.Position.z - b.Position.z) < ha.z + hb.z;
        }

        /// <summary> 两物体相交体积（三轴重叠长度乘积；不相交返回 0） </summary>
        public static float GetOverlapVolume(LayoutObject a, LayoutObject b)
        {
            float OverlapAxis(float d, float halfA, float halfB) =>
                Mathf.Max(0f, halfA + halfB - Mathf.Abs(d));

            return OverlapAxis(a.Position.x - b.Position.x, a.Size.x * 0.5f, b.Size.x * 0.5f)
                 * OverlapAxis(a.Position.y - b.Position.y, a.Size.y * 0.5f, b.Size.y * 0.5f)
                 * OverlapAxis(a.Position.z - b.Position.z, a.Size.z * 0.5f, b.Size.z * 0.5f);
        }

        /// <summary>
        /// 总重叠率：全部重叠对体积之和 ÷ 物体总体积（成对累加；0 = 完全不重叠，1 = 完全重叠）。
        /// 验收指标：10 个怪物构建后该值应 &lt; 0.1（10%）。
        /// </summary>
        public static float GetOverlapRatio(IEnumerable<LayoutObject> objects)
        {
            var list = new List<LayoutObject>(objects); // 需两次遍历 + 索引访问，先物化
            var totalVolume = 0f;
            foreach (var o in list)
                totalVolume += o.Size.x * o.Size.y * o.Size.z;
            if (totalVolume <= 0f) return 0f;

            var overlapVolume = 0f;
            for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
                overlapVolume += GetOverlapVolume(list[i], list[j]);
            return overlapVolume / totalVolume;
        }

        /// <summary>
        /// 单对分离：沿水平面最小穿透轴把 a/b 各推开一半（+余量）。
        /// 返回是否发生了分离；不重叠返回 false。就地修改 objects 中对应项。
        /// </summary>
        public static bool TrySeparate(IList<LayoutObject> objects, int i, int j, float margin = DefaultSeparationMargin)
        {
            var a = objects[i];
            var b = objects[j];
            var ha = a.Size * 0.5f;
            var hb = b.Size * 0.5f;

            var dx = b.Position.x - a.Position.x;
            var dz = b.Position.z - a.Position.z;
            var penX = ha.x + hb.x - Mathf.Abs(dx);
            var penZ = ha.z + hb.z - Mathf.Abs(dz);
            if (penX <= 0f || penZ <= 0f) return false; // 不重叠

            if (penX < penZ)
            {
                var dir = dx >= 0f ? 1f : -1f;
                var push = (penX + margin) * 0.5f;
                a.Position.x -= dir * push;
                b.Position.x += dir * push;
            }
            else
            {
                var dir = dz >= 0f ? 1f : -1f;
                var push = (penZ + margin) * 0.5f;
                a.Position.z -= dir * push;
                b.Position.z += dir * push;
            }

            objects[i] = a;
            objects[j] = b;
            return true;
        }

        /// <summary>
        /// 单轮分离：遍历全部重叠对执行 TrySeparate，返回本轮实际修正的对数（0 = 无重叠）。
        /// 多物体互相重叠时单轮可能不收敛，需 ResolveAll 迭代。
        /// </summary>
        public static int ResolveRound(IList<LayoutObject> objects, float margin = DefaultSeparationMargin)
        {
            var fixedPairs = 0;
            for (var i = 0; i < objects.Count; i++)
            for (var j = i + 1; j < objects.Count; j++)
                if (TrySeparate(objects, i, j, margin)) fixedPairs++;
            return fixedPairs;
        }

        /// <summary>
        /// 迭代解析：循环 ResolveRound 直到无重叠或达轮数上限（防止全重叠极端布局无限循环），
        /// 返回最终残留重叠率。
        /// </summary>
        public static float ResolveAll(IList<LayoutObject> objects, int maxRounds = 10, float margin = DefaultSeparationMargin)
        {
            for (var round = 0; round < maxRounds; round++)
            {
                if (ResolveRound(objects, margin) == 0) break;
            }
            return GetOverlapRatio(objects);
        }
    }
}
