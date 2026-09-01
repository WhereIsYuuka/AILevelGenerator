using System;
using System.Collections.Generic;
using UnityEngine;
// 别名 using：避免与 UnityEngine.ILogger 歧义
using ILogger = AILevelGenerator.Runtime.Interfaces.ILogger;

namespace AILevelGenerator.Runtime.Components
{
    /// <summary>
    /// 组件绑定器（Week3-Day4）：实体实例化后按逻辑名查配置表，自动挂载逻辑组件并装配参数。
    /// 设计要点（需求："轻量反射做类型映射 + 显式 AddComponent"）：
    /// 1. **反射只做类型映射**：类型名 → Type 解析（Type.GetType + 程序集扫描兜底 + 短名唯一匹配），
    ///    解析结果静态缓存——绑定 N 个实体最多解析一次类型，运行时零反射开销。
    /// 2. **组件添加用显式 AddComponent(Type)**（Unity 引擎原生 API，非业务反射）。
    /// 3. **参数装配走接口约定**（IBindableComponent.OnComponentBound）：组件自行解析键值对，
    ///    绑定器不碰组件内部字段——无反射字段注入，类型安全且可单测。
    /// 4. **失败不阻塞**：类型找不到/添加异常/装配异常 → 记日志跳过该条目，继续后续组件与实体。
    /// </summary>
    public class ComponentBinder
    {
        private readonly ComponentBindingConfig _config;
        private readonly ILogger _logger; // 日志宿主（窗口/调度器注入）；null 时退回 UnityEngine.Debug

        /// <param name="config">组件绑定配置表（可为 null：任何逻辑名都返回空结果，不报错）</param>
        /// <param name="logger">日志宿主（可选）：null 时绑定日志走 UnityEngine.Debug</param>
        public ComponentBinder(ComponentBindingConfig config, ILogger logger = null)
        {
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// 按逻辑名绑定组件到目标实体。未配置绑定/配置缺失 = 空结果（不报错）。
        /// 单个条目失败仅记日志，不影响其余条目与实体（"绑定失败输出日志不阻塞"）。
        /// </summary>
        public ComponentBindResult BindTo(string logicalName, GameObject target)
        {
            var result = new ComponentBindResult();
            if (target == null) return result;

            var entries = _config?.GetBindings(logicalName);
            if (entries == null || entries.Count == 0) return result;

            foreach (var entry in entries)
            {
                result.AttemptedCount++;
                if (entry == null || string.IsNullOrWhiteSpace(entry.ComponentTypeName))
                {
                    result.FailedCount++;
                    LogWarning($"组件绑定跳过：\"{logicalName}\" 存在组件类型名为空的条目（配置表检查）");
                    continue;
                }

                var type = ResolveType(entry.ComponentTypeName);
                if (type == null)
                {
                    result.FailedCount++;
                    LogWarning($"组件绑定失败：\"{logicalName}\" 找不到组件类型 [{entry.ComponentTypeName}]，" +
                               "请检查配置表类型名是否为全限定名（如 AILevelGenerator.Runtime.Components.MonsterHealth）");
                    continue;
                }
                if (!typeof(Component).IsAssignableFrom(type))
                {
                    result.FailedCount++;
                    LogWarning($"组件绑定失败：\"{logicalName}\" 类型 [{entry.ComponentTypeName}] 不是 Component 子类");
                    continue;
                }

                // 幂等：目标已存在同类型组件则跳过（重复绑定不重复挂载）
                if (target.GetComponent(type) != null)
                {
                    result.SkippedCount++;
                    continue;
                }

                Component component;
                try
                {
                    component = target.AddComponent(type); // 显式 AddComponent（引擎原生 API）
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    LogWarning($"组件绑定失败：\"{logicalName}\" 添加组件 [{entry.ComponentTypeName}] 异常：{ex.Message}");
                    continue;
                }

                // 参数装配：实现 IBindableComponent 的组件自行消费键值对；未实现则组件已挂载但参数不生效
                if (component is IBindableComponent bindable)
                {
                    try
                    {
                        bindable.OnComponentBound(ToParameterDict(entry.Parameters));
                    }
                    catch (Exception ex)
                    {
                        result.FailedCount++;
                        LogWarning($"组件绑定失败：\"{logicalName}\" 装配参数给 [{entry.ComponentTypeName}] 异常：{ex.Message}");
                        continue;
                    }
                }
                else
                {
                    LogWarning($"组件绑定提示：\"{logicalName}\" 组件 [{entry.ComponentTypeName}] 未实现 IBindableComponent，" +
                               "已挂载但配置参数不生效");
                }

                result.BoundCount++;
            }
            return result;
        }

        /// <summary>
        /// 类型映射（轻量反射，可单测）：全限定名（含程序集）→ 程序集扫描 → 短名唯一匹配。
        /// 委托静态解析器 ComponentTypeResolver（第四周-Day3 提取，与后置校验器复用同一套解析语义，
        /// 静态缓存跨实例共享——同类型名全局只解析一次）。
        /// </summary>
        public Type ResolveType(string typeName) => ComponentTypeResolver.Resolve(typeName);

        /// <summary> 参数列表 → 字典（重复键后值覆盖前值；空键忽略） </summary>
        private static Dictionary<string, string> ToParameterDict(List<ParameterOverride> parameters)
        {
            var dict = new Dictionary<string, string>();
            if (parameters == null) return dict;
            foreach (var p in parameters)
            {
                if (p == null || string.IsNullOrEmpty(p.Key)) continue;
                dict[p.Key] = p.Value ?? string.Empty;
            }
            return dict;
        }

        private void LogWarning(string message)
        {
            if (_logger != null) _logger.LogWarning(message);
            else Debug.LogWarning($"[ComponentBinder] {message}");
        }
    }

    /// <summary> 一次 BindTo 的统计结果：调度层据此写构建日志（绑定 N 个、失败 M 个） </summary>
    public class ComponentBindResult
    {
        public int AttemptedCount; // 配置表中尝试绑定的条目数
        public int BoundCount;     // 成功挂载并装配的组件数
        public int FailedCount;    // 失败数（类型找不到/添加异常/装配异常）
        public int SkippedCount;   // 跳过数（目标已存在同类型组件）

        public bool HasError => FailedCount > 0;
    }
}
