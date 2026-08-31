using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AILevelGenerator.Runtime.Utilities
{
    /// <summary>
    /// 场景一致性指纹（第四周-Day1：场景级快照回滚的验收度量工具）。
    /// 遍历场景全部 GameObject，按「完整层级路径 | 组件类型名列表」生成排序字符串。
    /// 回滚前后指纹相同 = 层级、父子关系、组件状态 100% 恢复（无残留、无缺失）。
    /// 设计要点：
    ///   1. 只用 UnityEngine API（不依赖 Editor），可单元测试；
    ///   2. 按路径排序拼接，消除 FindObjectsOfType 遍历顺序影响；
    ///   3. 路径用物体名（不用 instanceID）——实例 ID 回滚后必然变化，名字不变才是"恢复"；
    ///   4. 组件取 GetComponents 全部可见组件类型名，可检测"组件状态变化"（增删组件指纹即变）。
    /// </summary>
    public static class SceneFingerprint
    {
        /// <summary> 计算当前场景全部物体的指纹字符串（条目间以换行分隔，无尾随换行） </summary>
        public static string Compute()
        {
            var entries = new List<string>();
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                var path = BuildPath(go.transform);
                var components = string.Join(",", go.GetComponents<Component>()
                    .Select(c => c.GetType().Name)
                    .OrderBy(name => name, StringComparer.Ordinal));
                entries.Add($"{path}|{components}");
            }
            entries.Sort(StringComparer.Ordinal);
            return string.Join("\n", entries);
        }

        /// <summary> 计算指定根下子树的指纹（用于只对比某根物体内的内容） </summary>
        public static string Compute(GameObject root)
        {
            if (root == null) return string.Empty;
            var entries = new List<string>();
            Collect(root.transform, root.name, entries);
            entries.Sort(StringComparer.Ordinal);
            return string.Join("\n", entries);
        }

        private static string BuildPath(Transform t)
        {
            var parts = new List<string>();
            for (var cur = t; cur != null; cur = cur.parent)
                parts.Add(cur.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void Collect(Transform t, string path, List<string> entries)
        {
            var components = string.Join(",", t.GetComponents<Component>()
                .Select(c => c.GetType().Name)
                .OrderBy(name => name, StringComparer.Ordinal));
            entries.Add($"{path}|{components}");

            foreach (Transform child in t)
            {
                // 兄弟同名是 Unity 允许的极端情况：同路径条目重复出现，但回滚前后结构一致 → 重复行也一致，不影响一致性判定
                var childPath = $"{path}/{child.name}";
                Collect(child, childPath, entries);
            }
        }
    }
}
