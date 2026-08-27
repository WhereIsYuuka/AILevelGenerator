using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;


namespace AILevelGenerator.Runtime.Validation
{
    /// <summary>
    /// 校验器抽象基类 —— 提供日志与便捷方法
    /// </summary>
    public abstract class BaseValidator<T> : IValidator<T> where T : class
    {
        protected ILogger Logger { get; private set; }

        public void SetLogger(ILogger logger) => Logger = logger;

        public abstract ValidationResult Validate(T data, ValidationContext context);

        protected void AddError(ValidationResult result, string code, string message, string path = "")
        {
            result.Errors.Add(new ValidationError { Code = code, Message = message, DataPath = path });
        }

        protected void AddWarning(ValidationResult result, string code, string message, string path = "")
        {
            result.Warnings.Add(new ValidationWarning { Code = code, Message = message, DataPath = path });
        }
    }
}