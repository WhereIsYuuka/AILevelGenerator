using AILevelGenerator.Runtime.Data;

namespace AILevelGenerator.Runtime.Interfaces
{
    /// <summary>
    /// 校验器接口 支持泛型，可校验任意类型
    /// </summary>
    public interface IValidator<T> where T : class
    {
        ValidationResult Validate(T data, ValidationContext context);
    }

    public class ValidationContext
    {
        public IResourceMapper ResourceMapper{get; set;}
        public string ScenePath {get; set;}
    }
}