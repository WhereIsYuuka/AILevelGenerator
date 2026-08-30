using UnityEngine;

namespace AILevelGenerator.Runtime.Templates
{
    /// <summary>
    /// Prompt 模板资产：System/User 提示词模板文本，支持 {占位符} 插值（占位符清单见 PromptBuilder 与 ToolGuide）。
    /// Day7 真实 LLM 接入时从此处取提示词结构，与关卡/任务模板、资源映射解耦。
    /// </summary>
    [CreateAssetMenu(fileName = "PromptTemplate", menuName = "AI Level Generator/Prompt 模板")]
    public class PromptTemplate : ScriptableObject
    {
        public string TemplateId;
        public string DisplayName;

        [Header("提示词模板（支持 {占位符}，见 ToolGuide 占位符表；文案避免半角花括号）")]
        [TextArea(5, 15)] public string SystemPromptTemplate; // 角色设定 + 输出规范
        [TextArea(5, 15)] public string UserPromptTemplate;   // 用户输入 + 模板指南 + 资源清单等变量

        /// <summary> 自校验：TemplateId 缺失时返回 false（保存资产时提示） </summary>
        public bool ValidateSelf(out string error)
        {
            error = string.IsNullOrEmpty(TemplateId) ? "TemplateId 缺失" : null;
            return error == null;
        }
    }
}
