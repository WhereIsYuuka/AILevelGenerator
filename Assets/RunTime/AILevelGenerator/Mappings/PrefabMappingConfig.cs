using System;
using System.Collections.Generic;
using UnityEngine;

namespace AILevelGenerator.Runtime.Mappings
{
    /// <summary>
    /// 预制体映射配置：逻辑名（对应 PropPlacement.PrefabLogicalName）→ 预制体。
    /// 策划可在 Inspector 中可视化配置；Aliases 提供模糊匹配关键字。
    /// 资产创建：Assets > Create > AI Level Generator > 资源映射配置
    /// </summary>
    [CreateAssetMenu(fileName = "PrefabMappingConfig", menuName = "AI Level Generator/资源映射配置")]
    public class PrefabMappingConfig : ScriptableObject
    {
        [Tooltip("逻辑名 → 预制体映射条目（逻辑名需唯一）")]
        public List<PrefabMappingEntry> Entries = new();

        /// <summary>
        /// 编辑期自校验：返回所有重复的逻辑名（重复会导致模糊匹配结果不确定）
        /// </summary>
        public List<string> GetDuplicateNames()
        {
            var duplicates = new List<string>();
            var seen = new HashSet<string>();
            foreach (var entry in Entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.LogicalName)) continue;
                if (!seen.Add(entry.LogicalName))
                    duplicates.Add(entry.LogicalName);
            }
            return duplicates;
        }

        /// <summary> 编辑期校验：逻辑名重复时警告 </summary>
        private void OnValidate()
        {
            foreach (var name in GetDuplicateNames())
                Debug.LogWarning($"[PrefabMapping] 逻辑名重复：\"{name}\"，模糊匹配结果将不确定", this);
        }
    }

    /// <summary> 单条映射：逻辑名 + 预制体 + 模糊匹配关键字 </summary>
    [Serializable]
    public class PrefabMappingEntry
    {
        [Tooltip("逻辑名（映射表 Key，生成结果的 PrefabLogicalName 按此匹配），如 \"敌人-弓箭手\"")]
        public string LogicalName;

        [Tooltip("对应的预制体资源")]
        public GameObject Prefab;

        [Tooltip("模糊匹配关键字（如 \"敌人\"、\"弓箭手\"），命中越多匹配度越高，可留空")]
        public List<string> Aliases = new();
    }
}
