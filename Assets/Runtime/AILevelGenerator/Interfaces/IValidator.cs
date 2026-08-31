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
        /// <summary> 当前请求的模板标识（模板专属校验器按此匹配） </summary>
        public string TemplateId {get; set;}
        /// <summary> 模板提供者（模板存在性校验用；未注入时校验器应降级跳过） </summary>
        public ITemplateProvider TemplateProvider {get; set;}
    }
}