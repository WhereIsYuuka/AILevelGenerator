using System.Collections.Generic;
using AILevelGenerator.Runtime.Interfaces.Templates;
using AILevelGenerator.Runtime.Templates;

namespace AILevelGenerator.Runtime.Interfaces
{
    /// <summary>
    /// 模板提供者：统一提供关卡/任务/Prompt 三类模板的查询，策划可扩展（复制资产改配置即可）。
    /// 窗口只依赖关卡模板清单；Day7 生成器经此处取 Prompt 模板与任务模板。
    /// </summary>
    public interface ITemplateProvider
    {
        /// <summary> 全部关卡模板（按配置顺序，窗口下拉数据源） </summary>
        IReadOnlyList<LevelTemplate> GetLevelTemplates();

        /// <summary> 按 TemplateId 查关卡模板，未命中返回 null </summary>
        LevelTemplate GetTemplateById(string id);

        /// <summary> 全部任务模板（按配置顺序） </summary>
        IReadOnlyList<TaskTemplate> GetTaskTemplates();

        /// <summary> 按 TemplateId 查任务模板，未命中返回 null </summary>
        TaskTemplate GetTaskTemplateById(string id);

        /// <summary> 默认 Prompt 模板（取第一个，未配置返回 null） </summary>
        PromptTemplate GetDefaultPromptTemplate();

        /// <summary> 按 TemplateId 查 Prompt 模板，未命中返回 null </summary>
        PromptTemplate GetPromptTemplateById(string id);
    }
}