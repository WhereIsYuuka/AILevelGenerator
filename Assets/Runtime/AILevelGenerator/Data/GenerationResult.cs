using System;
using System.Collections.Generic;

namespace AILevelGenerator.Runtime.Data
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

    /// <summary>
    /// 校验结果容器 包含错误列表和警告列表，以及一个快速检查是否通过的属性
    /// </summary>
    [Serializable]
    public class ValidationResult
    {
        public List<ValidationError> Errors = new();
        public List<ValidationWarning> Warnings = new();

        /// <summary>
        /// 只要没有 Error，就视为校验通过（Warning 不影响通过性，仅提示）
        /// </summary>
        public bool IsValid => Errors.Count == 0;

        /// <summary>
        /// 合并另一校验结果：错误/警告就地并入，返回 this（支持聚合链式调用）。
        /// 防御 null 输入与空列表（校验器实现良莠不齐，不信任外部列表非空）。
        /// </summary>
        public ValidationResult Merge(ValidationResult other)
        {
            if (other == null) return this;
            if (other.Errors != null) Errors.AddRange(other.Errors);
            if (other.Warnings != null) Warnings.AddRange(other.Warnings);
            return this;
        }
    }
}