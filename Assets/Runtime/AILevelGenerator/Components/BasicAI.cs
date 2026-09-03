using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace AILevelGenerator.Runtime.Components
{
    /// <summary>
    /// 演示组件（第三周-Day4/5）：基础巡逻 AI。运行时绕出生点做水平圆周巡逻。
    /// Day5 环境适配升级：参数 useNavMesh=true 时自动挂载 NavMeshAgent，沿圆周目标点 SetDestination
    /// 寻路（可被 NavMeshAgent 识别、走 NavMesh 而非穿模）；未启用或未烘焙 NavMesh（isOnNavMesh=false）
    /// 时回落纯数学圆周巡逻（Editor 环境即可演示）。配置参数键：
    /// patrolRadius（巡逻半径，默认 5）、moveSpeed（移动速度，默认 2）、yawDirection（旋转方向，1=顺时针/-1=逆时针，默认 1）、
    /// useNavMesh（是否走 NavMesh 寻路，默认 false）。
    /// </summary>
    public class BasicAI : MonoBehaviour, IBindableComponent
    {
        [SerializeField, Tooltip("巡逻半径（参数键 patrolRadius）")]
        private float _patrolRadius = 5f;

        [SerializeField, Tooltip("移动速度（参数键 moveSpeed）")]
        private float _moveSpeed = 2f;

        [SerializeField, Tooltip("旋转方向：1=顺时针 / -1=逆时针（参数键 yawDirection）")]
        private int _yawDirection = 1;

        [SerializeField, Tooltip("是否走 NavMesh 寻路（参数键 useNavMesh）：true 时自动挂载 NavMeshAgent 沿圆周点寻路；false 用纯数学巡逻")]
        private bool _useNavMesh;

        private Vector3 _homePosition;  // 出生点（巡逻圆心）
        private float _angle;           // 当前相位角
        private NavMeshAgent _agent;    // useNavMesh=true 时自动挂载的寻路代理
        private float _agentRebuildAt = -100f; // 上次重建 agent 的时间戳（冷却用，-100 = 首帧立即允许重建）

        private void Awake()
        {
            _homePosition = transform.position;
        }

        private void Update()
        {
            if (_moveSpeed <= 0f) return;

            // Day5 agent 自愈：域重载（进播放模式）会清空运行时注册的 NavMesh 数据，
            // 而 NavMeshAgent 在场景加载（早于守卫 NavMeshPlayModeGuard 的 EnteredPlayMode 烘焙）
            // 时创建会失败——组件在但 native agent 无效（isOnNavMesh=false）。
            // 守卫烘焙完成后（播放模式第一帧起 NavMesh 已就绪），此处移除失效 agent 重建即可自愈；
            // 带 1s 冷却防反复 Destroy/AddComponent 死循环。EditMode（非播放）不重建（isOnNavMesh 恒 false 是引擎行为）。
            if (_useNavMesh && Application.isPlaying && Time.time - _agentRebuildAt > 1f)
            {
                var valid = _agent != null && _agent.isOnNavMesh;
                if (!valid)
                {
                    if (_agent != null) Destroy(_agent);
                    _agent = GetComponent<NavMeshAgent>();
                    if (_agent == null) _agent = gameObject.AddComponent<NavMeshAgent>();
                    _agentRebuildAt = Time.time;
                }
            }

            _angle += _moveSpeed * Time.deltaTime / Mathf.Max(0.1f, _patrolRadius) * _yawDirection;
            var offset = new Vector3(Mathf.Cos(_angle), 0f, Mathf.Sin(_angle)) * _patrolRadius;
            var target = _homePosition + offset;

            // Day5：NavMesh 寻路优先——代理在 NavMesh 上时走 SetDestination；否则回落纯数学巡逻
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.SetDestination(target);
            }
            else
            {
                transform.position = target;
            }
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
            if (parameters.TryGetValue("useNavMesh", out var nav) && bool.TryParse(nav, out var useNav))
                _useNavMesh = useNav;

            // 启用寻路：自动挂载 NavMeshAgent（组件装配的职责是"按参数升级组件自身能力"）。
            // 未烘焙 NavMesh 时 isOnNavMesh=false，Update 自动回落数学巡逻，无需额外处理。
            if (_useNavMesh && _agent == null)
                _agent = gameObject.AddComponent<NavMeshAgent>();
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
