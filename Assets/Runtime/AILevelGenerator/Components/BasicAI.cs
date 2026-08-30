using System.Collections.Generic;
using UnityEngine;

namespace AILevelGenerator.Runtime.Components
{
    /// <summary>
    /// 演示组件（Week3-Day4）：基础巡逻 AI。运行时绕出生点做水平圆周巡逻（不依赖 NavMesh，
    /// 纯数学路径，Editor 环境下即可演示"可正常运行"）。配置参数键：
    /// patrolRadius（巡逻半径，默认 5）、moveSpeed（移动速度，默认 2）、yawDirection（旋转方向，1=顺时针/-1=逆时针，默认 1）。
    /// Day5 环境适配后可与 NavMeshAgent 组合升级为寻路 AI。
    /// </summary>
    public class BasicAI : MonoBehaviour, IBindableComponent
    {
        [SerializeField, Tooltip("巡逻半径（参数键 patrolRadius）")]
        private float _patrolRadius = 5f;

        [SerializeField, Tooltip("移动速度（参数键 moveSpeed）")]
        private float _moveSpeed = 2f;

        [SerializeField, Tooltip("旋转方向：1=顺时针 / -1=逆时针（参数键 yawDirection）")]
        private int _yawDirection = 1;

        private Vector3 _homePosition;  // 出生点（巡逻圆心）
        private float _angle;           // 当前相位角

        private void Awake()
        {
            _homePosition = transform.position;
        }

        private void Update()
        {
            if (_moveSpeed <= 0f) return;
            _angle += _moveSpeed * Time.deltaTime / Mathf.Max(0.1f, _patrolRadius) * _yawDirection;
            var offset = new Vector3(Mathf.Cos(_angle), 0f, Mathf.Sin(_angle)) * _patrolRadius;
            transform.position = _homePosition + offset;
        }

        public void OnComponentBound(IReadOnlyDictionary<string, string> parameters)
        {
            if (parameters == null) return;

            if (TryParseFloat(parameters, "patrolRadius", out var radius))
                _patrolRadius = Mathf.Max(0.1f, radius);
            if (TryParseFloat(parameters, "moveSpeed", out var speed))
                _moveSpeed = Mathf.Max(0f, speed);
            if (parameters.TryGetValue("yawDirection", out var dir) && int.TryParse(dir, out var yaw) && yaw != 0)
                _yawDirection = yaw > 0 ? 1 : -1;
        }

        private static bool TryParseFloat(IReadOnlyDictionary<string, string> parameters, string key, out float value)
        {
            if (parameters.TryGetValue(key, out var raw) && float.TryParse(raw, out value))
                return true;
            if (parameters != null && parameters.ContainsKey(key))
                Debug.LogWarning($"[BasicAI] 参数 {key} 非法（\"{parameters[key]}\"），保持默认值");
            value = 0f;
            return false;
        }
    }
}
