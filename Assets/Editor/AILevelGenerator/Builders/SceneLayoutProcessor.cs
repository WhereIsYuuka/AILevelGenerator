using System.Collections.Generic;
using AILevelGenerator.Runtime.Utilities;
using UnityEngine;

namespace AILevelGenerator.Editor.Builders
{
    /// <summary>
    /// 场景布局处理器（Day2）：地面贴合 + 重叠检测/分离（实例化后的场景侧处理）。
    /// 重叠检测双层：
    ///   1. 粗筛 —— Physics.OverlapSphere（需求要求；无 Collider 的物体退化为包围球距离判定）；
    ///   2. 精算 —— AABB 相交体积与分离计算在 OverlapResolver（纯计算，可单测量化重叠率）。
    /// 地面贴合 —— 从物体顶向下 Physics.Raycast 命中地面，把物体底部对齐到命中点。
    /// </summary>
    public static class SceneLayoutProcessor
    {
        /// <summary> 地面射线最大距离（米）：超过视为无地面，保持原位不强行下压 </summary>
        private const float MaxGroundRayDistance = 200f;

        /// <summary> OverlapSphere 粗筛球体半径加量（米） </summary>
        private const float SpherePadding = 0.1f;

        /// <summary>
        /// 地面贴合：向下射线把物体底部贴到地面。
        /// RaycastAll 并跳过本次生成根下的命中（root 参数），防止重叠物体互相挡住射线导致"叠罗汉"。
        /// 返回是否贴合成功（无命中保持原位）。
        /// </summary>
        public static bool FitToGround(GameObject instance, Transform root = null)
        {
            var bounds = GetWorldBounds(instance);
            if (!bounds.HasValue) return false;

            // 从物体顶部上方发射：避免从内部发射与地面 collider 起点重叠
            var origin = new Vector3(bounds.Value.center.x, bounds.Value.max.y + 1f, bounds.Value.center.z);
            var hits = Physics.RaycastAll(origin, Vector3.down, MaxGroundRayDistance);
            foreach (var hit in hits)
            {
                // 跳过本次生成物体自身的命中（重叠布局下射线会先打到相邻怪物）
                if (root != null && hit.transform.IsChildOf(root)) continue;
                var offset = hit.point.y - bounds.Value.min.y; // 物体底部 → 地面
                if (Mathf.Abs(offset) > 0.001f)
                    instance.transform.position += new Vector3(0f, offset, 0f);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 单轮重叠分离：收集全部实例 bounds → 粗筛候选对（OverlapSphere/包围球）→ 逐对精算分离 → 写回位置。
        /// 返回本轮修正的对数（0 = 已无重叠，分帧迭代的收敛判断依据）。
        /// </summary>
        public static int ResolveRound(List<GameObject> instances)
        {
            if (instances == null || instances.Count < 2) return 0;

            var objects = new List<LayoutObject>(instances.Count);
            foreach (var go in instances)
            {
                var b = GetWorldBounds(go);
                objects.Add(b.HasValue
                    ? new LayoutObject(b.Value.center, b.Value.size)
                    : new LayoutObject(go.transform.position, Vector3.one)); // 无 bounds 兜底：按 1m 立方体参与计算
            }

            var fixedPairs = 0;
            for (var i = 0; i < instances.Count; i++)
            {
                for (var j = i + 1; j < instances.Count; j++)
                {
                    // 粗筛：两物体包围球不相交则必不重叠，跳过精算（OverlapSphere 基于 Collider，
                    // 无 Collider 的物体用包围球距离判定兜底，保证任意预制体都能参与布局）
                    if (!IsCandidatePair(instances[i], instances[j], objects[i], objects[j])) continue;
                    if (OverlapResolver.TrySeparate(objects, i, j)) fixedPairs++;
                }
            }

            for (var k = 0; k < instances.Count; k++)
            {
                // 只应用分离后的 x/z：objects[k].Position 存的是包围盒中心，与物体原点（transform.position）
                // 在 y 轴可能不同（取决于模型锚点），直接整向量写回会造成 y 漂移
                var pos = objects[k].Position;
                var current = instances[k].transform.position;
                instances[k].transform.position = new Vector3(pos.x, current.y, pos.z);
            }
            return fixedPairs;
        }

        /// <summary> 残留重叠率（验收指标：10 个怪物构建后应 &lt; 10%） </summary>
        public static float GetOverlapRatio(List<GameObject> instances)
        {
            if (instances == null || instances.Count == 0) return 0f;
            var objects = new List<LayoutObject>(instances.Count);
            foreach (var go in instances)
            {
                var b = GetWorldBounds(go);
                objects.Add(b.HasValue
                    ? new LayoutObject(b.Value.center, b.Value.size)
                    : new LayoutObject(go.transform.position, Vector3.one));
            }
            return OverlapResolver.GetOverlapRatio(objects);
        }

        /// <summary>
        /// 候选对粗筛：OverlapSphere 命中 b 的 Collider 即候选（需求要求的检测手段）。
        /// 兜底：EditMode 下运行时动态创建的 Collider 可能尚未注册进物理场景（OverlapSphere 查询不到，
        /// 见 ToolGuide Week3-Day2"已知坑"），故再叠加纯几何的包围球相交判定 —— 判定偏松只会让
        /// TrySeparate 空跑一次（不重叠返回 false），不会产生错误分离。
        /// </summary>
        private static bool IsCandidatePair(GameObject a, GameObject b, LayoutObject la, LayoutObject lb)
        {
            var colliderA = a.GetComponentInChildren<Collider>();
            var colliderB = b.GetComponentInChildren<Collider>();

            // a 的包围球扫过 b 的 Collider 即视为候选
            if (colliderA != null || colliderB != null)
            {
                var radius = la.Size.magnitude * 0.5f + SpherePadding;
                foreach (var hit in Physics.OverlapSphere(la.Position, radius))
                {
                    if (hit.transform == b.transform || hit.transform.IsChildOf(b.transform)) return true;
                }
            }

            // 纯几何兜底：两物体包围球相交即候选（不依赖物理场景）
            return BoundingSpheresOverlap(la, lb);
        }

        private static bool BoundingSpheresOverlap(LayoutObject a, LayoutObject b)
        {
            var dist = (a.Position - b.Position).magnitude;
            return dist < a.Size.magnitude * 0.5f + b.Size.magnitude * 0.5f;
        }

        /// <summary> 世界空间包围盒：Renderer 优先（可见物必有），其次 Collider；都没有返回 null </summary>
        private static Bounds? GetWorldBounds(GameObject go)
        {
            var renderer = go.GetComponentInChildren<Renderer>();
            if (renderer != null) return renderer.bounds;

            var collider = go.GetComponentInChildren<Collider>();
            if (collider != null) return collider.bounds;

            return null;
        }
    }
}
