using System;
using System.Collections.Generic;
using AILevelGenerator.Runtime.Utilities;
using UnityEngine;
using UnityEngine.AI;

namespace AILevelGenerator.Editor.Builders
{
    /// <summary>
    /// 环境自动适配（Week3-Day5）：全局 NavMesh 烘焙器（Editor 侧执行体）。
    /// 链路：全场景收集几何源（PhysicsColliders，按碰撞体烘焙——可走性语义）→ 计算世界范围
    /// → BuildNavMeshData（同步）→ AddNavMeshData 注册运行时数据（NavMesh.SamplePosition 即可查询，
    /// NavMeshAgent 可识别）。
    /// 设计要点：
    /// 1. 不向场景添加任何组件（NavMeshSurface 方案需要场景预置组件，全局烘焙用 CollectSources 零污染）。
    /// 2. 重复烘焙先移除旧数据再注册（叠加残留会让 SamplePosition 命中过期区域）。
    /// 3. 状态经 NavMeshBakeTracker（Runtime 纯逻辑）记录，调用方（构建器）负责「烘焙中」提示与日志。
    /// 4. 失败不抛异常（try/catch 全捕获转 Failed 结果），生成流程不中断——烘焙失败只影响寻路，不影响场景实例化。
    /// </summary>
    public class NavMeshBaker
    {
        private NavMeshDataInstance _dataInstance; // 已注册的运行时 NavMesh 数据（重复烘焙时先移除）

        /// <summary> 烘焙范围外扩余量（米）：收集到的几何 AABB 基础上外扩，防止边缘体素被裁剪 </summary>
        private const float BoundsPadding = 2f;

        /// <summary>
        /// 同步执行全局烘焙并注册数据。结果（成功/失败 + 消息）写入 tracker；返回是否成功。
        /// 同步阻塞：调用方必须在调用前给出「烘焙中」用户提示（DisplayProgressBar/日志）。
        /// excludeRoot：剔除该根下的几何源（本次生成实体不作为障碍物，见 NavMeshSourceFilter）。
        /// </summary>
        public bool BakeGlobal(NavMeshBakeTracker tracker, Transform excludeRoot = null)
        {
            tracker?.BeginBaking();
            try
            {
                // 1) 全场景收集：root=null 收集整个场景；PhysicsColliders = 按碰撞体几何烘焙（生产可走性语义）
                var sources = new List<NavMeshBuildSource>();
                var markups = new List<NavMeshBuildMarkup>();
                NavMeshBuilder.CollectSources(null, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);

                // 1.5) 剔除本次生成实体的几何源：角色/NPC 不作为 NavMesh 障碍物——
                // 否则实体自身占据区域被抠洞，其 NavMeshAgent 脚下无数据（isOnNavMesh=false）无法寻路
                if (excludeRoot != null)
                    sources.RemoveAll(s => NavMeshSourceFilter.IsUnderRoot(s, excludeRoot));

                if (sources.Count == 0)
                {
                    tracker?.Fail("未收集到任何可烘焙几何（场景中没有带 Collider 的物体）");
                    return false;
                }

                // 2) 世界范围：sources 的 AABB 并集（矩阵角点变换，兼容 local/world 尺寸语义）+ 外扩余量
                var bounds = ComputeWorldBounds(sources);
                if (bounds.size.x <= 0f || bounds.size.z <= 0f)
                {
                    tracker?.Fail("烘焙范围无效（几何 AABB 为空）");
                    return false;
                }

                // 3) 同步烘焙：默认代理设置（索引 0 = Humanoid）；数据原点放在世界范围中心，网格输出为世界坐标
                var settings = NavMesh.GetSettingsByIndex(0);
                var data = NavMeshBuilder.BuildNavMeshData(settings, sources,
                    new Bounds(Vector3.zero, bounds.size), bounds.center, Quaternion.identity);
                if (data == null)
                {
                    tracker?.Fail("BuildNavMeshData 返回空（烘焙参数异常）");
                    return false;
                }

                // 4) 注册运行时数据：先移除旧实例（重复烘焙时残留数据会叠加，SamplePosition 命中过期区域）
                if (_dataInstance.valid) NavMesh.RemoveNavMeshData(_dataInstance);
                _dataInstance = NavMesh.AddNavMeshData(data);

                tracker?.Complete(sources.Count);
                return true;
            }
            catch (Exception ex)
            {
                tracker?.Fail(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 计算全部几何源的世界 AABB（烘焙范围）。
        /// 两种尺寸来源：
        /// 1. 基础形状（Box/Capsule/Sphere…）：source.size 为形状尺寸（局部），经 source.transform 的 8 角点变换取并集；
        /// 2. Mesh 形状（如 MeshCollider）：CollectSources 输出的 source.size 恒为 (0,0,0)（实测），
        ///    尺寸须从 source.component（Collider）的世界包围盒取——Collider.bounds 由 transform+形状数学计算，
        ///    不依赖物理引擎注册（EditMode 动态 Collider 也有效，见 Week3-Day2 已知坑）。
        /// </summary>
        private static Bounds ComputeWorldBounds(List<NavMeshBuildSource> sources)
        {
            var hasAny = false;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (var source in sources)
            {
                hasAny = true;
                if (source.shape == NavMeshBuildSourceShape.Mesh && source.component is Collider meshCollider)
                {
                    // MeshCollider：直接用世界 AABB（bounds 已含 transform）
                    min = Vector3.Min(min, meshCollider.bounds.min);
                    max = Vector3.Max(max, meshCollider.bounds.max);
                    continue;
                }

                var half = source.size * 0.5f;
                var m = source.transform;
                for (var x = -1; x <= 1; x += 2)
                {
                    for (var y = -1; y <= 1; y += 2)
                    {
                        for (var z = -1; z <= 1; z += 2)
                        {
                            var corner = m.MultiplyPoint(new Vector3(half.x * x, half.y * y, half.z * z));
                            min = Vector3.Min(min, corner);
                            max = Vector3.Max(max, corner);
                        }
                    }
                }
            }

            if (!hasAny) return new Bounds(Vector3.zero, Vector3.zero);
            var center = (min + max) * 0.5f;
            var size = max - min + Vector3.one * (BoundsPadding * 2f); // 外扩，防边缘裁剪
            return new Bounds(center, size);
        }
    }
}
