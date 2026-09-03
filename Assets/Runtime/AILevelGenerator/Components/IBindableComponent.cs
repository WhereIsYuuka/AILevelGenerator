using System.Collections.Generic;

namespace AILevelGenerator.Runtime.Components
{
    /// <summary>
    /// 可绑定组件约定（第三周-Day4）：绑定器装配参数时调用 OnComponentBound，组件自行解析键值对。
    /// 设计意图：**显式装配，无反射字段注入**——反射只用于"类型名 → Type"的一次性类型映射（轻量反射），
    /// 参数如何消费由组件自己决定（TryParse + 默认值兜底），绑定器不关心组件内部字段。
    /// 这样组件逻辑完全类型安全、可单测，且参数来源（配置表/未来 Prompt 生成）与消费方解耦。
    /// </summary>
    public interface IBindableComponent
    {
        /// <summary>
        /// 组件绑定完成回调。参数为配置表键值对（字符串），组件自行解析与校验：
        /// 未知键忽略并警告，非法值保持默认并警告——**参数错误永不抛异常，保证绑定链路不中断**。
        /// </summary>
        void OnComponentBound(IReadOnlyDictionary<string, string> parameters);
    }
}
