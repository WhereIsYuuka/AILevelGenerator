using System;
using System.Collections.Generic;
// PromptTemplate 实现于 Templates/ 目录（ns AILevelGenerator.Runtime.Templates），此处跨命名空间引用
using AILevelGenerator.Runtime.Templates;

namespace AILevelGenerator.Runtime.Interfaces.Templates
{
    /// <summary>
    /// 模板管理器（第五周-Day4，替代 ITemplateProvider）：模板体系的唯一入口（ServiceLocator 注册）。
    /// - 查询面与旧 Provider 语义一致：窗口下拉、LLM 生成器、校验器消费方零行为变化；
    /// - 动态注册：按 TemplateId Upsert 到对应类别（保序，同 ID 就地替换），支持按 ID 注销；
    ///   代码新建模板实例（内存/资产）运行期注册即生效，无需重载域；
    /// - 动态加载：Reload() 从注入的 ITemplateSource 全量替换并广播 TemplatesChanged
    ///   （策划新增/修改资产 → 点击刷新 → 下拉与生成链路即时生效）；
    /// - 变更事件：注册/注销/重载成功后触发一次，供窗口等 UI 订阅方同步；订阅方须自行防泄漏注销。
    /// </summary>
    public interface ITemplateManager
    {
        // —— 查询（兼容旧 ITemplateProvider 语义，返回集合为注册顺序快照，请勿就地修改） ——

        /// <summary> 全部关卡模板（按注册顺序，窗口下拉数据源） </summary>
        IReadOnlyList<LevelTemplate> GetLevelTemplates();

        /// <summary> 按 TemplateId 查关卡模板，未命中返回 null </summary>
        LevelTemplate GetTemplateById(string id);

        /// <summary> 全部任务模板（按注册顺序） </summary>
        IReadOnlyList<TaskTemplate> GetTaskTemplates();

        /// <summary> 按 TemplateId 查任务模板，未命中返回 null </summary>
        TaskTemplate GetTaskTemplateById(string id);

        /// <summary> 全部 Prompt 模板（按注册顺序） </summary>
        IReadOnlyList<PromptTemplate> GetPromptTemplates();

        /// <summary> 默认 Prompt 模板（取第一个，未配置返回 null） </summary>
        PromptTemplate GetDefaultPromptTemplate();

        /// <summary> 按 TemplateId 查 Prompt 模板，未命中返回 null </summary>
        PromptTemplate GetPromptTemplateById(string id);

        // —— 动态注册 / 注销 ——
        // Upsert 语义：TemplateId 非空且已存在 → 就地替换（保持原位置与顺序）；不存在 → 追加尾部；
        // TemplateId 为空 → 无法定位，一律追加尾部（资产一般有 ID，此为代码模板容错路径）。
        // 注册/注销成功（含空模板入参忽略）不抛异常；成功后触发一次 TemplatesChanged。

        /// <summary> 注册/替换关卡模板（同 TemplateId 就地替换保序） </summary>
        void RegisterLevelTemplate(LevelTemplate template);

        /// <summary> 按 TemplateId 注销关卡模板；命中并移除返回 true，未命中返回 false </summary>
        bool UnregisterLevelTemplate(string templateId);

        /// <summary> 注册/替换任务模板（同 TemplateId 就地替换保序） </summary>
        void RegisterTaskTemplate(TaskTemplate template);

        /// <summary> 按 TemplateId 注销任务模板；命中并移除返回 true，未命中返回 false </summary>
        bool UnregisterTaskTemplate(string templateId);

        /// <summary> 注册/替换 Prompt 模板（同 TemplateId 就地替换保序） </summary>
        void RegisterPromptTemplate(PromptTemplate template);

        /// <summary> 按 TemplateId 注销 Prompt 模板；命中并移除返回 true，未命中返回 false </summary>
        bool UnregisterPromptTemplate(string templateId);

        /// <summary>
        /// 从加载源整体重载三类模板并触发一次 TemplatesChanged（手动注册的增量条目会被源快照覆盖，
        /// 需要保底的增量请改为 Reload 之后重新注册）。未注入加载源时返回 false 且不触发事件。
        /// </summary>
        bool Reload();

        /// <summary> 模板集合变更事件（注册/注销/重载成功后触发一次） </summary>
        event Action TemplatesChanged;
    }
}
