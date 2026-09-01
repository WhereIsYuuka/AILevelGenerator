using System;
using System.Collections.Generic;
using AILevelGenerator.Runtime.Components;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using UnityEngine;

namespace AILevelGenerator.Runtime.Validation
{
    /// <summary>
    /// 后置校验器（第四周-Day3）：构建完成后由调度器执行，检查三项：
    /// 1. **实体空引用**：实体清单缺失/数量与构建结果不一致/列表含 null（构建器异常清理的痕迹）；
    /// 2. **组件完整性**：按实体名（= PrefabLogicalName）查绑定配置，已配置的组件类型必须真实挂载；
    /// 3. **逻辑可达性**：实体必须被地面物理支撑（悬空实体不可达），可配置开关/测试注入判定。
    /// 设计要点：
    /// - 校验失败 = 场景已污染 → 调度器自动全量回滚（快照在成功路径保留，见 Day2 决策）。
    /// - 降级语义：无绑定配置 → 组件完整性跳过；类型名不可解析 → 跳过（配置问题，绑定期已告警）；
    ///   未探测到地面 → 仅警告并跳过可达性（编辑场景无地面碰撞体属正常，不误报）。
    /// - 可达性用构造注入 Func 委托替代器（groundedOverride）：单测注入确定性判定，零物理环境波动；
    ///   生产路径用 IsGrounded（物理 RaycastAll，自包围盒向下探测，跳过自身及子物体命中）。
    /// </summary>
    public class PostBuildValidator : BaseValidator<PostBuildData>
    {
        /// <summary> 地面探测最大距离（与 SceneLayoutProcessor.FitToGround 的 MaxGroundRayDistance 对齐） </summary>
        public const float MaxGroundRayDistance = 200f;

        private readonly ComponentBindingConfig _bindingConfig; // 可为 null：组件完整性整体降级跳过
        private readonly bool _checkReachability;               // 可达性总开关（默认开）
        private readonly Func<GameObject, bool> _groundedOverride; // 测试注入：非 null 时完全接管可达性判定（跳过预探测）

        public PostBuildValidator(ComponentBindingConfig bindingConfig = null, bool checkReachability = true,
            Func<GameObject, bool> groundedOverride = null)
        {
            _bindingConfig = bindingConfig;
            _checkReachability = checkReachability;
            _groundedOverride = groundedOverride;
        }

        public override ValidationResult Validate(PostBuildData data, ValidationContext context)
        {
            var result = new ValidationResult();
            if (data == null || data.Entities == null)
            {
                AddError(result, "POST_ENTITIES_MISSING", "后置校验数据缺失（实体清单为空），无法校验构建完整性");
                return result;
            }

            // 数量一致性：构建报告数 ≠ 实际实体数 → 构建器实例化异常（漏/多余），逐实体检查已无意义
            if (data.ExpectedCount > 0 && data.Entities.Count != data.ExpectedCount)
            {
                AddError(result, "POST_COUNT_MISMATCH",
                    $"实体数量不一致：构建报告 {data.ExpectedCount} 个，实际 {data.Entities.Count} 个",
                    "entities");
                return result;
            }

            // 可达性预探测：注入 override 时测试全控（跳过预探测）；生产路径先确认场景存在地面，
            // 无地面（空场景/纯逻辑关卡）→ 仅警告并跳过全部可达性检查，避免正常关卡被误报悬空。
            var reachabilityEnabled = _checkReachability;
            if (reachabilityEnabled && _groundedOverride == null && !GroundProbe(data.Entities))
            {
                AddWarning(result, "POST_GROUND_MISSING",
                    "未探测到地面碰撞体，已跳过逻辑可达性检查（编辑场景无地面属正常降级）");
                reachabilityEnabled = false;
            }

            for (var i = 0; i < data.Entities.Count; i++)
            {
                var entity = data.Entities[i];
                var path = $"entities[{i}]";

                // 1) 实体空引用
                if (entity == null)
                {
                    AddError(result, "POST_ENTITY_NULL", "实体为空引用（构建器实例化中断或异常清理的痕迹）", path);
                    continue;
                }

                // 2) 组件完整性：实体名即 PrefabLogicalName，按绑定配置逐条核对组件挂载
                if (_bindingConfig != null)
                {
                    var entries = _bindingConfig.GetBindings(entity.name);
                    if (entries != null)
                    {
                        foreach (var entry in entries)
                        {
                            if (entry == null || string.IsNullOrWhiteSpace(entry.ComponentTypeName)) continue;
                            var type = ComponentTypeResolver.Resolve(entry.ComponentTypeName);
                            if (type == null) continue; // 类型不可解析 = 配置问题（绑定期已告警），非实体缺陷
                            if (entity.GetComponent(type) == null)
                            {
                                AddError(result, "POST_COMPONENT_MISSING",
                                    $"实体缺少绑定组件 [{entry.ComponentTypeName}]，组件完整性校验失败",
                                    $"{path}.components[{entry.ComponentTypeName}]");
                            }
                        }
                    }
                }

                // 3) 逻辑可达性：悬空实体视为不可达（物理支撑判定或测试注入判定）
                if (reachabilityEnabled)
                {
                    var grounded = _groundedOverride != null ? _groundedOverride(entity) : IsGrounded(entity);
                    if (!grounded)
                    {
                        AddError(result, "POST_FLOAT_UNSUPPORTED",
                            "实体悬空无地面支撑，逻辑上不可达（放置/地面适配失败）", path);
                    }
                }
            }

            return result;
        }

        /// <summary> 地面预探测：以首个非空实体为锚向下探测，命中任意支撑即认为场景存在地面 </summary>
        private static bool GroundProbe(List<GameObject> entities)
        {
            foreach (var entity in entities)
            {
                if (entity == null) continue;
                return IsGrounded(entity);
            }
            return false; // 空列表：无实体可探测，视作无地面（与"零实体关卡合法"语义共存）
        }

        /// <summary>
        /// 物理支撑判定（生产路径）：SyncTransforms 后以实体包围盒顶点上方为原点向下 RaycastAll，
        /// 命中**非自身/子物体**的任意碰撞体即认为有地面支撑。
        /// 自身命中必须跳过（实体自带碰撞体在脚下，不算支撑）；同级实体命中算支撑（实体被实体叠放属合法布局）。
        /// </summary>
        public static bool IsGrounded(GameObject entity)
        {
            if (entity == null) return false;
            Physics.SyncTransforms(); // 编辑期动态实例化的碰撞体需同步变换才进入物理场景

            // 包围盒顶点：Collider 优先，Renderer 兜底，纯标记物体用位置点
            var collider = entity.GetComponentInChildren<Collider>();
            var renderer = entity.GetComponentInChildren<Renderer>();
            float topY;
            if (collider != null) topY = collider.bounds.max.y;
            else if (renderer != null) topY = renderer.bounds.max.y;
            else topY = entity.transform.position.y;

            var origin = new Vector3(entity.transform.position.x, topY + 1f, entity.transform.position.z);
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, MaxGroundRayDistance))
            {
                if (hit.collider == null) continue;
                if (IsSelfOrChild(hit.collider.transform, entity.transform)) continue; // 自身/子物体命中不算支撑
                return true;
            }
            return false;
        }

        /// <summary> candidate 是否 root 自身或其子物体（逐级上溯判定） </summary>
        private static bool IsSelfOrChild(Transform candidate, Transform root)
        {
            while (candidate != null)
            {
                if (candidate == root) return true;
                candidate = candidate.parent;
            }
            return false;
        }
    }
}
