using System;
using System.Collections.Generic;

namespace AILevelGenerator.RunTime.Data
{
    [Serializable]
    public class GenerationResult
    {
        public bool Success;
        public LevelData LevelData;
        public List<TaskData> Tasks = new();
        public List<ValidationError> Errors = new();
        public List<ValidationWarning> Warnings = new();
        public float GenerationTime;
        public string RawLLMResponse;
    }

    [Serializable]
    public class ValidationError
    {
        public string Code;
        public string Message;
        public string DataPath;
    }

    [Serializable]
    public class ValidationWarning
    {
        public string Code;
        public string Message;
        public string DataPath;
    }
}