using UnityEngine;
using AILevelGenerator.Runtime.Data;

namespace AILevelGenerator.Runtime.Interfaces.Templates
{
    public abstract class TaskTemplate : ScriptableObject
    {
        public string TemplateId;
        public string DisplayName;
        public TaskType TaskType;
        [TextArea] public string Description;

        public abstract void ApplyDefaults(TaskData taskData);
    }
}