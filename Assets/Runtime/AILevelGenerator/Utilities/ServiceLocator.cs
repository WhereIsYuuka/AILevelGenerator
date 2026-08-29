using System;
using System.Collections.Generic;

namespace AILevelGenerator.Runtime.Utilities
{
    /// <summary>
    /// 简单服务定位器（编辑器工具内部使用）。
    /// 窗口不直接 new 业务类，通过此处获取调度器等核心服务，便于替换与测试。
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> Services = new();
        private static readonly object Lock = new();

        /// <summary> 注册服务（重复注册覆盖旧实例） </summary>
        public static void Register<T>(T instance) where T : class
        {
            lock (Lock) Services[typeof(T)] = instance;
        }

        /// <summary> 获取服务；未注册返回 null（调用方自行兜底） </summary>
        public static T Get<T>() where T : class
        {
            lock (Lock)
                return Services.TryGetValue(typeof(T), out var instance) ? (T)instance : null;
        }

        /// <summary> 是否已注册 </summary>
        public static bool IsRegistered<T>() where T : class
        {
            lock (Lock) return Services.ContainsKey(typeof(T));
        }

        /// <summary> 注销指定服务 </summary>
        public static void Unregister<T>() where T : class
        {
            lock (Lock) Services.Remove(typeof(T));
        }

        /// <summary> 清空全部注册（测试隔离用，谨慎调用） </summary>
        public static void Clear()
        {
            lock (Lock) Services.Clear();
        }
    }
}
