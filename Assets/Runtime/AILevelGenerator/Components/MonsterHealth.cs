using System.Collections.Generic;
using UnityEngine;

namespace AILevelGenerator.Runtime.Components
{
    /// <summary>
    /// 演示组件（第三周-Day4）：怪物血量。配置参数键 maxHealth（整数字符串）。
    /// 实现 IBindableComponent 走显式装配：非法值保持默认并警告，永不抛异常（绑定链路不中断）。
    /// </summary>
    public class MonsterHealth : MonoBehaviour, IBindableComponent
    {
        [SerializeField, Tooltip("最大血量（参数键 maxHealth）")]
        private int _maxHealth = 100;

        public int MaxHealth => _maxHealth;

        public void OnComponentBound(IReadOnlyDictionary<string, string> parameters)
        {
            if (parameters == null) return;
            if (parameters.TryGetValue("maxHealth", out var raw))
            {
                if (int.TryParse(raw, out var parsed) && parsed > 0)
                {
                    _maxHealth = parsed;
                }
                else
                {
                    Debug.LogWarning($"[MonsterHealth] 参数 maxHealth 非法（\"{raw}\"），保持默认值 {_maxHealth}", this);
                }
            }
        }
    }
}
