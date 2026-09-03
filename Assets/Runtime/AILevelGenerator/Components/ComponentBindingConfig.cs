using System;
using System.Collections.Generic;
using UnityEngine;

namespace AILevelGenerator.Runtime.Components
{
    /// <summary>
    /// 组件绑定配置表（第三周-Day4）：按逻辑名（对应 PropPlacement.PrefabLogicalName）组织，
    /// 每条逻辑名关联一组"组件绑定条目"——组件类型（全限定名字符串）+ 参数键值对。
    /// 与 PrefabMappingConfig（逻辑名 → 预制体）解耦：资源映射管"实例化什么"，
    /// 组件绑定管"实例化后挂什么逻辑组件"，策划各自独立配置。
    /// 资产创建：Assets > Create > AI Level Generator > 组件绑定配置
    /// </summary>
    [CreateAssetMenu(fileName = "ComponentBindingConfig", menuName = "AI Level Generator/组件绑定配置")]
    public class ComponentBindingConfig : ScriptableObject
    {
        [Tooltip("逻辑名 → 组件绑定列表（逻辑名对应资源映射表的 Key，未配置的实体不绑定任何组件）")]
        public List<LogicalBinding> Bindings = new();

        /// <summary> 查询某逻辑名的组件绑定列表；未配置返回 null（绑定器安全跳过） </summary>
        public List<ComponentBindingEntry> GetBindings(string logicalName)
        {
            if (string.IsNullOrEmpty(logicalName)) return null;
            foreach (var binding in Bindings)
            {
                if (binding != null && binding.LogicalName == logicalName)
                    return binding.Components;
            }
            return null;
        }

        /// <summary> 编辑期自校验：返回所有重复的逻辑名（重复时查询命中先配置的条目，结果不确定） </summary>
        public List<string> GetDuplicateNames()
        {
            var duplicates = new List<string>();
            var seen = new HashSet<string>();
            foreach (var binding in Bindings)
            {
                if (binding == null || string.IsNullOrEmpty(binding.LogicalName)) continue;
                if (!seen.Add(binding.LogicalName))
                    duplicates.Add(binding.LogicalName);
            }
            return duplicates;
        }

        /// <summary> 编辑期校验：逻辑名重复 / 组件类型名为空时警告 </summary>
        private void OnValidate()
        {
            foreach (var name in GetDuplicateNames())
                Debug.LogWarning($"[ComponentBinding] 逻辑名重复：\"{name}\"，绑定结果将不确定", this);

            foreach (var binding in Bindings)
            {
                if (binding == null) continue;
                var seenTypes = new HashSet<string>();
                foreach (var entry in binding.Components)
                {
                    if (entry == null) continue;
                    if (string.IsNullOrWhiteSpace(entry.ComponentTypeName))
                    {
                        Debug.LogWarning($"[ComponentBinding] \"{binding.LogicalName}\" 存在组件类型名为空的条目（将跳过）", this);
                        continue;
                    }
                    if (!seenTypes.Add(entry.ComponentTypeName))
                        Debug.LogWarning($"[ComponentBinding] \"{binding.LogicalName}\" 重复绑定组件类型：{entry.ComponentTypeName}（已存在时跳过重复添加）", this);
                }
            }
        }
    }

    /// <summary> 一条逻辑名的绑定配置：逻辑名 + 组件绑定列表 </summary>
    [Serializable]
    public class LogicalBinding
    {
        [Tooltip("逻辑名（映射表 Key，生成结果的 PrefabLogicalName 按此匹配），如 \"敌人-弓箭手\"")]
        public string LogicalName;

        [Tooltip("该逻辑名实体要绑定的组件列表（按顺序逐个绑定，单个失败不阻塞后续）")]
        public List<ComponentBindingEntry> Components = new();
    }

    /// <summary> 单个组件绑定条目：组件类型全限定名 + 装配参数 </summary>
    [Serializable]
    public class ComponentBindingEntry
    {
        [Tooltip("组件类型全限定名，如 AILevelGenerator.Runtime.Components.MonsterHealth；程序集限定名写法更稳（见 ToolGuide）")]
        public string ComponentTypeName;

        [Tooltip("装配参数（键值对，组件经 IBindableComponent.OnComponentBound 自行解析）")]
        public List<ParameterOverride> Parameters = new();
    }

    /// <summary> 单个参数覆盖：键 + 值（字符串承载，组件内转换；非法值组件保持默认并警告） </summary>
    [Serializable]
    public class ParameterOverride
    {
        public string Key;
        public string Value;
    }
}
