using System;
using System.Collections.Generic;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Diagnostics;
using AILevelGenerator.Runtime.Interfaces;

namespace AILevelGenerator.Runtime.Validation
{
    /// <summary>
    /// 校验器注册表（可插拔调度中枢，纯 Runtime 逻辑，可单测）：
    /// - 通用校验器按 (ValidationStage, 数据类型) 匹配运行；
    /// - 模板专属校验器额外按 TemplateId 匹配（每个模板可注册自己的校验器）；
    /// - 核心层（调度器/注册表）只做调度，不写死任何校验规则（开闭原则）；
    /// - 不短路：聚合全部校验器的错误，一次性告知所有问题（服务"错误信息清晰明确"验收）。
    /// 类型不符的校验器被静默跳过（Pre 阶段请求级/数据级共用同名 Stage 的关键）；
    /// 单个校验器抛异常转为 VALIDATOR_ERROR 错误条目，不打断聚合链。
    /// </summary>
    public class ValidatorRegistry
    {
        private readonly List<Registration> _validators = new();
        private IResourceMapper _resourceMapper;
        private ITemplateProvider _templateProvider;
        private ILogger _logger;

        /// <summary>
        /// 注入校验依赖的服务（可选）：资源映射（资源存在性校验）、模板提供者（模板存在性校验）。
        /// 未注入时校验器依据自身数据源降级（跳过对应检查，不报错）。
        /// </summary>
        public void SetServices(IResourceMapper resourceMapper = null, ITemplateProvider templateProvider = null)
        {
            _resourceMapper = resourceMapper;
            _templateProvider = templateProvider;
        }

        /// <summary> 注册通用校验器：在指定阶段、数据类型匹配时运行（模板 ID 为空 = 全部请求生效） </summary>
        public void Register<T>(ValidationStage stage, IValidator<T> validator) where T : class
        {
            if (validator == null) throw new ArgumentNullException(nameof(validator));
            _validators.Add(new Registration { Stage = stage, TemplateId = null, Adapter = new GenericAdapter<T>(validator) });
        }

        /// <summary> 注册模板专属校验器：仅当请求 TemplateId 与注册 ID 一致时运行（每个模板可注册自己的校验器） </summary>
        public void RegisterForTemplate<T>(string templateId, IValidator<T> validator) where T : class
        {
            if (validator == null) throw new ArgumentNullException(nameof(validator));
            _validators.Add(new Registration { Stage = ValidationStage.Pre, TemplateId = templateId, Adapter = new GenericAdapter<T>(validator) });
        }

        /// <summary>
        /// 调度入口：运行指定阶段下全部匹配校验器（阶段 + 模板匹配 + 数据类型匹配），聚合结果。
        /// data 为 null 时静默返回空结果（null 判定由调度层负责，此处不重复报错）。
        /// </summary>
        public ValidationResult Run(ValidationStage stage, object data, string templateId = null)
        {
            if (data == null) return new ValidationResult();
            var context = new ValidationContext
            {
                ResourceMapper = _resourceMapper,
                TemplateProvider = _templateProvider,
                TemplateId = templateId
            };
            var result = new ValidationResult();
            foreach (var reg in _validators)
            {
                if (reg.Stage != stage) continue;
                if (reg.TemplateId != null && reg.TemplateId != templateId) continue; // 模板匹配（null = 通用）
                if (!reg.Adapter.DataType.IsAssignableFrom(data.GetType())) continue; // 类型过滤：类型不符跳过
                try
                {
                    result.Merge(reg.Adapter.Validate(data, context));
                }
                catch (Exception ex)
                {
                    // 单点异常不打断聚合链：转错误条目，定位到校验器目标类型
                    result.Errors.Add(new ValidationError
                    {
                        Code = ErrorCodes.VALIDATOR_ERROR,
                        Message = $"校验器异常：{ex.Message}",
                        DataPath = reg.Adapter.DataType.Name
                    });
                }
            }
            return result;
        }

        /// <summary> 把日志宿主转发给全部已注册的 BaseValidator 子类（校验器内可直接经 Logger 输出） </summary>
        public void SetLogger(ILogger logger)
        {
            _logger = logger;
            foreach (var reg in _validators)
                reg.Adapter.SetLogger(logger);
        }

        /// <summary> 清空注册（测试隔离用） </summary>
        public void Clear() => _validators.Clear();

        private sealed class Registration
        {
            public ValidationStage Stage;
            public string TemplateId; // null = 通用校验器
            public IValidatorAdapter Adapter;
        }

        /// <summary> 非泛型适配：注册表以 (DataType, Validate(object)) 统一持有任意泛型校验器 </summary>
        private interface IValidatorAdapter
        {
            Type DataType { get; }
            ValidationResult Validate(object data, ValidationContext context);
            void SetLogger(ILogger logger);
        }

        private sealed class GenericAdapter<T> : IValidatorAdapter where T : class
        {
            private readonly IValidator<T> _inner;
            public GenericAdapter(IValidator<T> inner) => _inner = inner;
            public Type DataType => typeof(T);
            public ValidationResult Validate(object data, ValidationContext context) => _inner.Validate((T)data, context);
            public void SetLogger(ILogger logger)
            {
                if (_inner is BaseValidator<T> baseValidator) baseValidator.SetLogger(logger);
            }
        }
    }
}
