using System.Collections.Generic;
using UnityEngine;

namespace AILevelGenerator.Runtime.Utilities
{
    /// <summary>
    /// 回滚追踪器（纯数据，可单测）：登记/取出生成根物体。
    /// 删除执行在 Editor 侧（RollbackManager 组合本类 + EditorCoroutine 分帧销毁），
    /// 把"追踪哪些根"与"怎么删"分离，保证追踪逻辑脱离编辑器也能单元测试。
    /// </summary>
    public class RollbackTracker
    {
        private readonly List<GameObject> _roots = new();

        /// <summary> 当前登记数 </summary>
        public int Count => _roots.Count;

        /// <summary> 登记根物体（null 与重复登记忽略） </summary>
        public void Track(GameObject root)
        {
            if (root == null || _roots.Contains(root)) return;
            _roots.Add(root);
        }

        /// <summary> 取出最近一次登记的根（移除并返回）；无登记返回 null </summary>
        public GameObject TakeLast()
        {
            if (_roots.Count == 0) return null;
            var root = _roots[_roots.Count - 1];
            _roots.RemoveAt(_roots.Count - 1);
            return root;
        }

        /// <summary> 清空全部登记 </summary>
        public void Clear() => _roots.Clear();
    }
}
