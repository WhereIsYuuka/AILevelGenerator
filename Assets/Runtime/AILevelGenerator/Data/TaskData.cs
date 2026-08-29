using System;
using System.Collections.Generic;

namespace AILevelGenerator.Runtime.Data
{
    [Serializable]
    public class TaskData
    {
        public string TaskID;
        public string TaskName;
        public string Description;
        public TaskType Type;
        public TaskObjective Objective;
        public RewardData Reward;
        public bool IsMainTask = true;
        public string TriggerCondition; //进入区域、击败敌人
        public float TimeLimit = -1f;
    }

    // 击杀、收集、抵达、护送、防守、自定义
    public enum TaskType { Kill, Collect, Arrive, Escort, Defend, Custom}
    // 计数、到达位置、收集物品、生存时长
    public enum TaskObjective { Count, ReachPosition, CollectItems, TimeSurvive }

    [Serializable]
    public class RewardData
    {
        public int Experience;
        public int Gold;
        public List<string> ItemRewards = new();
    }
}