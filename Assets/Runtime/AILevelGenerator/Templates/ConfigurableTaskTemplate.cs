using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces.Templates;
using UnityEngine;

namespace AILevelGenerator.Runtime.Templates
{
    /// <summary>
    /// 数据驱动任务模板：兜底语义 —— 仅当 TaskData 字段为空/默认值时写入模板默认值，不覆盖 LLM 已产出的内容。
    /// 任务类型（TaskType）与模板基础信息来自基类字段；新任务模板 = 复制资产改配置，无需写代码。
    /// </summary>
    [CreateAssetMenu(fileName = "TaskTemplate", menuName = "AI Level Generator/任务模板（数据驱动）")]
    public class ConfigurableTaskTemplate : TaskTemplate
    {
        [Header("默认值（仅兜底，不覆盖生成结果）")]
        [Tooltip("默认时限（秒），-1 = 无时限；仅当生成结果未设置时限（TimeLimit<=0）时生效")]
        [Min(-1)] public float DefaultTimeLimit = -1f;
        [Tooltip("默认奖励（Experience/Gold 全 0 且无物品奖励时视为未配置，不覆盖生成结果）")]
        public RewardData DefaultReward;
        [Tooltip("默认触发条件文本（如 进入区域/击败敌人），仅当生成结果未填写时生效")]
        [TextArea] public string DefaultTriggerCondition;

        /// <summary>
        /// 应用默认值到 TaskData：逐字段兜底 —— 空/默认值才写入，已设置的内容保留。
        /// 判空规则：TimeLimit&lt;=0 视为未设置；Reward 为 null 或全 0 且物品列表为空视为未设置。
        /// </summary>
        public override void ApplyDefaults(TaskData taskData)
        {
            if (taskData == null) return;

            if (string.IsNullOrEmpty(taskData.TaskName))
                taskData.TaskName = DisplayName;
            if (string.IsNullOrEmpty(taskData.Description))
                taskData.Description = Description;
            if (taskData.TimeLimit <= 0f)
                taskData.TimeLimit = DefaultTimeLimit;
            if (taskData.Reward == null || IsRewardEmpty(taskData.Reward))
                taskData.Reward = DefaultReward;
            if (string.IsNullOrEmpty(taskData.TriggerCondition))
                taskData.TriggerCondition = DefaultTriggerCondition;
        }

        /// <summary> 奖励是否为空（Experience/Gold 全 0 且物品列表为空） </summary>
        private static bool IsRewardEmpty(RewardData reward)
        {
            if (reward == null) return true;
            return reward.Experience == 0 && reward.Gold == 0
                   && (reward.ItemRewards == null || reward.ItemRewards.Count == 0);
        }
    }
}
