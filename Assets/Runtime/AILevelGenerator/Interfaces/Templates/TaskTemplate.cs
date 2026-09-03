using System.Collections.Generic;
using UnityEngine;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Utilities;

namespace AILevelGenerator.Runtime.Interfaces.Templates
{
    /// <summary>
    /// 任务模板基类 ScriptableObject 放在 Runtime 保证运行时可读。
    /// 与 LevelTemplate 同构：资产无状态，统一生命周期经 FinalizeData 收尾
    /// （默认值 → 确定性随机钩子 PostGenerate，供收集物散布等任务级随机内容使用）。
    /// Day3 起 FinalizeData 接收 LevelData 上下文（可空）：任务级内容（如收集物散布）需要
    /// 关卡数据定位/夹取/追加 Props；仅补默认值时传 null 亦可（Day1 语义退化保留）。
    /// </summary>
    public abstract class TaskTemplate : ScriptableObject
    {
        public string TemplateId;
        public string DisplayName;
        public TaskType TaskType;
        [TextArea] public string Description;

        /// <summary>
        /// 统一生命周期入口（与 LevelTemplate.FinalizeData 语义一致，第五周-Day3 起任务链路调用）。
        /// 流程：ApplyDefaults 补默认值 → 派生确定性随机流执行 PostGenerate。
        /// 种子派生：先 (requestSeed, TemplateId 哈希) 派生子种子；level 非空且任务在列表中时
        /// 再叠任务槽盐（Derive(·, StableHash("TaskSlot."+索引))）——保证同类型多个任务各自独立
        /// 随机流（同种子多个收集任务不会产出完全相同的散点堆叠）；level 为 null（仅默认值路径）
        /// 则退化为 Day1 原派生，序列不受影响。
        /// </summary>
        public void FinalizeData(TaskData taskData, LevelData levelData, int requestSeed)
        {
            if (taskData == null) return;
            ApplyDefaults(taskData);
            var seed = RandomSeedUtility.Derive(requestSeed, RandomSeedUtility.StableHash(TemplateId));
            if (levelData != null)
            {
                var slot = IndexOfTask(levelData.Tasks, taskData);
                if (slot >= 0)
                    seed = RandomSeedUtility.Derive(seed, RandomSeedUtility.StableHash("TaskSlot." + slot));
            }
            var rng = new DeterministicRandom(seed);
            PostGenerate(taskData, levelData, rng);
        }

        /// <summary>
        /// 确定性随机钩子：任务级随机内容在此实现（Day3 收集物散布；传参含关卡数据）。
        /// 种子契约：实现内只允许用传入 rng，严禁混用 UnityEngine.Random / System.Random；
        /// levelData 为 null 时表示仅默认值路径（无关卡上下文），内容散布应静默跳过。
        /// 基类默认无操作。
        /// </summary>
        protected virtual void PostGenerate(TaskData taskData, LevelData levelData, DeterministicRandom rng) { }

        public abstract void ApplyDefaults(TaskData taskData);

        /// <summary> 模板指南文本（随 Prompt 注入 {templateGuideline}，与 LevelTemplate 对齐） </summary>
        public virtual string GetGuideline() => string.Empty;

        /// <summary> 自校验，保存时调用（与 LevelTemplate 对齐） </summary>
        public virtual bool ValidateSelf(out string error)
        {
            error = string.IsNullOrEmpty(TemplateId) ? "TemplateId 缺失" : null;
            return error == null;
        }

        /// <summary> 任务在关卡任务列表中的序号（引用查找；列表为空/未包含返回 -1） </summary>
        private static int IndexOfTask(List<TaskData> tasks, TaskData target)
        {
            if (tasks == null || target == null) return -1;
            for (var i = 0; i < tasks.Count; i++)
                if (ReferenceEquals(tasks[i], target))
                    return i;
            return -1;
        }
    }
}
