using System;
using System.Collections.Generic;

namespace AILevelGenerator.Runtime.Components
{
    /// <summary>
    /// 组件类型解析器（第四周-Day3 提取）：全限定名 → Type 的轻量反射映射，
    /// 供组件绑定器（挂载）与后置校验器（完整性检查）复用同一套解析语义。
    /// 解析结果静态缓存——同类型名全局只解析一次，运行时零反射开销。
    /// </summary>
    public static class ComponentTypeResolver
    {
        /// <summary> 类型名 → Type 静态缓存：跨实例共享，同一类型名全局只解析一次（轻量反射的关键） </summary>
        private static readonly Dictionary<string, Type> TypeCache = new();

        /// <summary>
        /// 类型映射（轻量反射，可单测）：全限定名（含程序集）→ 程序集扫描 → 短名唯一匹配。
        /// 匹配结果缓存；短名存在多个同名类型时返回 null（明确日志提示用全限定名，避免歧义绑定错组件）。
        /// </summary>
        public static Type Resolve(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            if (TypeCache.TryGetValue(typeName, out var cached)) return cached;

            Type type = null;

            // 1) 直接解析：支持程序集限定名（Type.GetType 原生语法）与当前程序集内全限定名
            type = Type.GetType(typeName);

            // 2) 程序集扫描：按全限定名（如 "AILevelGenerator.Runtime.Components.MonsterHealth" 无程序集后缀）。
            //    个别程序集（动态/卸载中）可能抛异常，try/catch 跳过不中断解析。
            if (type == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        type = assembly.GetType(typeName);
                        if (type != null) break;
                    }
                    catch (Exception)
                    {
                        // 该程序集不可查询，跳过继续
                    }
                }
            }

            // 3) 短名唯一匹配（如 "MonsterHealth"）：多命中视为歧义失败，避免绑定错组件
            if (type == null)
            {
                Type candidate = null;
                var ambiguous = false;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var t in assembly.GetTypes())
                        {
                            if (t.Name != typeName) continue;
                            if (candidate != null) { ambiguous = true; break; }
                            candidate = t;
                        }
                    }
                    catch (Exception)
                    {
                        continue; // 该程序集不可查询，跳过继续
                    }
                    if (ambiguous) break;
                }
                if (candidate != null && !ambiguous) type = candidate;
            }

            TypeCache[typeName] = type; // null 也缓存（避免每次解析都全程序集扫描，配置写错时日志不受影响）
            return type;
        }
    }
}
