using UnityEngine;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Utilities;

namespace AILevelGenerator.Runtime.Interfaces.Templates
{
    /// <summary>
    /// 任务模板基类 ScriptableObject 放在 Runtime 保证运行时可读。
    /// 与 LevelTemplate 同构：资产无状态，统一生命周期经 FinalizeData 收尾
    /// （默认值 → 确定性随机钩子 PostGenerate，供收集物散布等任务级随机内容使用）。
    /// </summary>
    public abstract class TaskTemplate : ScriptableObject
    {
        public string TemplateId;
        public string DisplayName;
        public TaskType TaskType;
        [TextArea] public string Description;

        /// <summary> 统一生命周期入口（与 LevelTemplate.FinalizeData 语义一致） </summary>
        public void FinalizeData(TaskData taskData, int requestSeed)
        {
            if (taskData == null) return;
            ApplyDefaults(taskData);
            var rng = new DeterministicRandom(
                RandomSeedUtility.Derive(requestSeed, RandomSeedUtility.StableHash(TemplateId)));
            PostGenerate(taskData, rng);
        }

        /// <summary> 确定性随机钩子：只允许用传入 rng，基类默认无操作 </summary>
        protected virtual void PostGenerate(TaskData taskData, DeterministicRandom rng) { }

        public abstract void ApplyDefaults(TaskData taskData);

        /// <summary> 模板指南文本（随 Prompt 注入 {templateGuideline}，与 LevelTemplate 对齐） </summary>
        public virtual string GetGuideline() => string.Empty;

        /// <summary> 自校验，保存时调用（与 LevelTemplate 对齐） </summary>
        public virtual bool ValidateSelf(out string error)
        {
            error = string.IsNullOrEmpty(TemplateId) ? "TemplateId 缺失" : null;
            return error == null;
        }
    }
}
