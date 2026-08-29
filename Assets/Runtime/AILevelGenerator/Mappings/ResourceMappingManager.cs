using System.Collections.Generic;
using AILevelGenerator.Runtime.Interfaces;
using UnityEngine;

namespace AILevelGenerator.Runtime.Mappings
{
    /// <summary>
    /// 资源映射管理器：逻辑名 → 预制体。
    /// 查找顺序：逻辑名精确匹配（字典缓存）→ 别名精确匹配 → 名称/别名包含匹配（打分取最高）。
    /// 纯逻辑类（非 MonoBehaviour），由调用方持有（后续由 ServiceLocator 统一管理）；
    /// 配置资产被策划修改后需调用 RebuildCache() 刷新精确索引。
    /// </summary>
    public class ResourceMappingManager : IResourceMapper
    {
        private readonly PrefabMappingConfig _config;

        /// <summary> 精确索引：规范化逻辑名 → 条目（避免每次精确匹配全表扫描） </summary>
        private readonly Dictionary<string, PrefabMappingEntry> _exactMap = new();

        public ResourceMappingManager(PrefabMappingConfig config)
        {
            _config = config;
            RebuildCache();
        }

        /// <summary> 重建精确索引，配置变更后调用 </summary>
        public void RebuildCache()
        {
            _exactMap.Clear();
            if (_config == null || _config.Entries == null) return;
            foreach (var entry in _config.Entries)
            {
                // 跳过无效条目：无逻辑名或未绑定预制体
                if (entry == null || string.IsNullOrEmpty(entry.LogicalName) || entry.Prefab == null) continue;
                _exactMap[Normalize(entry.LogicalName)] = entry;
            }
        }

        /// <summary> 精确 → 模糊查找；未命中时打警告并返回 null </summary>
        public GameObject GetPrefab(string logicalName)
        {
            if (TryGetPrefab(logicalName, out var prefab)) return prefab;
            Debug.LogWarning($"[ResourceMapping] 未找到逻辑名 \"{logicalName}\" 对应的预制体，请检查资源映射配置");
            return null;
        }

        /// <summary> 精确 → 模糊查找，未命中返回 false </summary>
        public bool TryGetPrefab(string logicalName, out GameObject prefab)
        {
            prefab = null;
            if (string.IsNullOrWhiteSpace(logicalName)) return false;

            // 1. 精确匹配（大小写不敏感，O(1) 字典查询）
            if (_exactMap.TryGetValue(Normalize(logicalName), out var exact))
            {
                prefab = exact.Prefab;
                return true;
            }

            // 2. 模糊匹配兜底
            prefab = GetPrefabByFuzzy(logicalName);
            return prefab != null;
        }

        /// <summary>
        /// 模糊匹配：遍历全部条目打分，返回最高分条目；未命中返回 null。
        /// 打分规则：逻辑名完全相等 1000 / 别名完全相等 500 / 逻辑名互相包含 100 / 每个别名互相包含 50。
        /// 配置条目通常不足百条，全表扫描可接受（无需索引优化）。
        /// </summary>
        public GameObject GetPrefabByFuzzy(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword) || _config == null || _config.Entries == null) return null;
            var key = Normalize(keyword);

            PrefabMappingEntry best = null;
            var bestScore = 0;
            foreach (var entry in _config.Entries)
            {
                if (entry == null || entry.Prefab == null) continue;
                var score = ScoreEntry(entry, key);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = entry;
                }
            }
            return best?.Prefab;
        }

        /// <summary>
        /// 单条打分：逻辑名精确命中权重最高，其次别名精确，最后是双向包含。
        /// 命中维度越多（如名称+多个别名同时包含）分数越高，天然支持"多关键字叠加"。
        /// </summary>
        private static int ScoreEntry(PrefabMappingEntry entry, string keyword)
        {
            var score = 0;
            var logical = Normalize(entry.LogicalName);
            if (logical == keyword) score += 1000;
            else if (logical.Contains(keyword) || keyword.Contains(logical)) score += 100;

            if (entry.Aliases != null)
            {
                foreach (var alias in entry.Aliases)
                {
                    var a = Normalize(alias);
                    if (string.IsNullOrEmpty(a)) continue;
                    if (a == keyword) score += 500;
                    else if (a.Contains(keyword) || keyword.Contains(a)) score += 50;
                }
            }
            return score;
        }

        /// <summary> 规范化：去首尾空白 + 小写（中文无大小写问题，英文别名大小写不敏感） </summary>
        private static string Normalize(string s) => s == null ? "" : s.Trim().ToLowerInvariant();
    }
}
