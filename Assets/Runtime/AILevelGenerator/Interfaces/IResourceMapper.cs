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
    }
}