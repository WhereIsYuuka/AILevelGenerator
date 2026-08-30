using UnityEngine;
using UnityEngine.AI;

namespace AILevelGenerator.Runtime.Utilities
{
    /// <summary>
    /// NavMesh 烘焙源过滤器（Week3-Day5）：判断几何源是否应被剔除出烘焙。
    /// 生产语义：角色/NPC/动态实体不应作为 NavMesh 障碍物——若把生成实体的 Collider 也烘焙进去，
    /// 实体自身占据区域会被抠成"洞"，其 NavMeshAgent 的 isOnNavMesh=false、无法落地寻路
    /// （角色层排除是 NavMeshSurface 的标准做法，本项目以"剔除本次生成根"实现同一语义）。
    /// 纯逻辑（可单测）；剔除执行在编辑器侧 NavMeshBaker。
    /// </summary>
    public static class NavMeshSourceFilter
    {
        /// <summary>
        /// 该几何源是否属于 excludeRoot 层级下的物体。
        /// 属于 → 应剔除（不参与烘焙）；root 为 null 或源无组件/非 Collider → 不剔除。
        /// </summary>
        public static bool IsUnderRoot(NavMeshBuildSource source, Transform excludeRoot)
        {
            if (excludeRoot == null || source.component == null) return false;
            if (!(source.component is Collider collider)) return false;
            return collider.transform != null && collider.transform.IsChildOf(excludeRoot);
        }
    }
}
