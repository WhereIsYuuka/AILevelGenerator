using UnityEngine;
using AILevelGenerator.Runtime.Data;

namespace AILevelGenerator.Runtime.Interfaces.Templates
{
    /// <summary>
    /// 关卡模板基类 ScriptableObject 放在 Runtime 保证运行时可读
    /// </summary>
    public abstract class LevelTemplate : ScriptableObject
    {
        public string TemplateId;
        public string DisplayName;
        [TextArea(2, 5)] public string Description;

        /// <summary> 应用默认值到 LevelData </summary>
        public abstract void ApplyDefaults(LevelData data);

        /// <summary>
        /// 模板指南文本：描述本模板的布局/内容规则，随 Prompt 注入 {templateGuideline} 告知 LLM。
        /// 数据驱动实现返回配置字段；代码模板可覆写为动态生成文本。PromptBuilder 只依赖此方法取指南。
        /// </summary>
        public virtual string GetGuideline() => string.Empty;

        /// <summary> 自校验，保存时调用 </summary>
        public virtual bool ValidateSelf(out string error)
        {
            error = string.IsNullOrEmpty(TemplateId) ? "TemplateId 缺失" : null;
            return error == null;
        }
    }
}