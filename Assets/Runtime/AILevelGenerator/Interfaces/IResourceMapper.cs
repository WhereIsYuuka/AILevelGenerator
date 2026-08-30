using System.Collections.Generic;
using UnityEngine;

namespace AILevelGenerator.Runtime.Interfaces
{
    /// <summary>
    /// 资源映射服务  解耦预制体名称与物理资源
    /// </summary>
    public interface IResourceMapper
    {
        GameObject GetPrefab(string logicalName);
        bool TryGetPrefab(string logicalName, out GameObject prefab);
        GameObject GetPrefabByFuzzy(string keyword); // 模糊匹配（面试亮点）

        /// <summary>
        /// 全部有效逻辑名（按配置顺序），供 Prompt 资源清单告知 LLM 可输出的物体名，
        /// 保证生成结果的 PrefabLogicalName 能命中映射表。无效条目（空逻辑名/未绑定预制体）跳过。
        /// </summary>
        IReadOnlyList<string> GetAllLogicalNames();
    }
}