using System;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Validation;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 校验注册表单元测试（第四周-Day2，可插拔调度中枢）：
    /// 阶段匹配、类型过滤、模板匹配、聚合不短路、异常隔离、日志转发、清空。
    /// 注册表只做调度不写死规则——本组测试验证调度正确性，具体规则由各校验器测试覆盖。
    /// </summary>
    public class ValidatorRegistryTests
    {
        /// <summary> 假校验器：记录调用次数，按配置产出结果（不依赖 BaseValidator，验证注册表对任意 IValidator 的调度） </summary>
        private class FakeValidator<T> : IValidator<T> where T : class
        {
            public int CallCount;
            public ValidationResult Result = new();
            public Func<T, ValidationContext, ValidationResult> Handler;

            public ValidationResult Validate(T data, ValidationContext context)
            {
                CallCount++;
                return Handler != null ? Handler(data, context) : Result;
            }
        }

        /// <summary> BaseValidator 子类探针：验证 SetLogger 转发（注册表仅对基类子类转发日志宿主） </summary>
        private class ProbeValidator : BaseValidator<GenerationRequest>
        {
            public override ValidationResult Validate(GenerationRequest data, ValidationContext context) => new();
            public ILogger CapturedLogger => Logger;
        }

        [Test]
        public void 阶段匹配_仅运行指定阶段的校验器()
        {
            var registry = new ValidatorRegistry();
            var pre = new FakeValidator<GenerationRequest>();
            var mid = new FakeValidator<GenerationRequest>();
            registry.Register(ValidationStage.Pre, pre);
            registry.Register(ValidationStage.Mid, mid);

            var result = registry.Run(ValidationStage.Pre, new GenerationRequest());

            Assert.AreEqual(1, pre.CallCount, "Pre 阶段校验器应被调用");
            Assert.AreEqual(0, mid.CallCount, "Mid 阶段校验器不应在 Pre 阶段运行");
            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void 类型不符_静默跳过不调用()
        {
            var registry = new ValidatorRegistry();
            var requestValidator = new FakeValidator<GenerationRequest>();

            registry.Register(ValidationStage.Pre, requestValidator);
            var result = registry.Run(ValidationStage.Pre, new LevelData());

            Assert.AreEqual(0, requestValidator.CallCount, "数据类型不符的校验器应静默跳过（Pre 阶段请求级/数据级共用同名 Stage 的关键）");
            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void 聚合不短路_收集全部校验器的错误()
        {
            var registry = new ValidatorRegistry();
            registry.Register(ValidationStage.Pre, new FakeValidator<GenerationRequest>
            {
                Handler = (_, _) => { var r = new ValidationResult(); r.Errors.Add(new ValidationError { Code = "ERR_A" }); return r; }
            });
            registry.Register(ValidationStage.Pre, new FakeValidator<GenerationRequest>
            {
                Handler = (_, _) => { var r = new ValidationResult(); r.Errors.Add(new ValidationError { Code = "ERR_B" }); return r; }
            });

            var result = registry.Run(ValidationStage.Pre, new GenerationRequest());

            Assert.AreEqual(2, result.Errors.Count, "应一次性聚合全部校验器的错误");
            Assert.AreEqual("ERR_A", result.Errors[0].Code);
            Assert.AreEqual("ERR_B", result.Errors[1].Code);
        }

        [Test]
        public void 模板匹配_仅运行对应模板与通用校验器()
        {
            var registry = new ValidatorRegistry();
            var generic = new FakeValidator<GenerationRequest>();
            var forA = new FakeValidator<GenerationRequest>();
            var forB = new FakeValidator<GenerationRequest>();
            registry.Register(ValidationStage.Pre, generic);
            registry.RegisterForTemplate("TemplateA", forA);
            registry.RegisterForTemplate("TemplateB", forB);

            registry.Run(ValidationStage.Pre, new GenerationRequest(), "TemplateA");

            Assert.AreEqual(1, generic.CallCount, "通用校验器应作用于全部请求");
            Assert.AreEqual(1, forA.CallCount, "匹配模板的专属校验器应运行");
            Assert.AreEqual(0, forB.CallCount, "不匹配模板的专属校验器不应运行");
        }

        [Test]
        public void 注册模板校验器_默认阶段为前置()
        {
            var registry = new ValidatorRegistry();
            var forA = new FakeValidator<GenerationRequest>();
            registry.RegisterForTemplate("TemplateA", forA);

            // 仅在 Pre 阶段运行（非 Pre 阶段不匹配）
            registry.Run(ValidationStage.Mid, new GenerationRequest(), "TemplateA");

            Assert.AreEqual(0, forA.CallCount, "模板专属校验器默认注册在 Pre 阶段");
        }

        [Test]
        public void 校验器抛异常_转错误条目且不打断聚合链()
        {
            var registry = new ValidatorRegistry();
            var throwing = new FakeValidator<GenerationRequest>
            {
                Handler = (_, _) => throw new InvalidOperationException("校验器内部爆炸")
            };
            var normal = new FakeValidator<GenerationRequest>
            {
                Handler = (_, _) => { var r = new ValidationResult(); r.Errors.Add(new ValidationError { Code = "ERR_B" }); return r; }
            };
            registry.Register(ValidationStage.Pre, throwing);
            registry.Register(ValidationStage.Pre, normal);

            var result = registry.Run(ValidationStage.Pre, new GenerationRequest());

            Assert.AreEqual(2, result.Errors.Count, "异常不应中断后续校验器");
            Assert.AreEqual("VALIDATOR_ERROR", result.Errors[0].Code, "异常应转为统一错误码");
            Assert.AreEqual("ERR_B", result.Errors[1].Code);
            Assert.AreEqual(1, normal.CallCount, "异常后后续校验器仍应被调用");
        }

        [Test]
        public void 设置日志器_转发给已注册的基类子类()
        {
            var registry = new ValidatorRegistry();
            var probe = new ProbeValidator();
            registry.Register(ValidationStage.Pre, probe);
            var logger = new TestLogger();

            registry.SetLogger(logger);

            Assert.AreSame(logger, probe.CapturedLogger, "日志宿主应转发给 BaseValidator 子类");
        }

        [Test]
        public void 清空注册_不再运行任何校验器()
        {
            var registry = new ValidatorRegistry();
            var validator = new FakeValidator<GenerationRequest>();
            registry.Register(ValidationStage.Pre, validator);
            registry.Clear();

            var result = registry.Run(ValidationStage.Pre, new GenerationRequest());

            Assert.AreEqual(0, validator.CallCount);
            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void 空数据_返回空结果不调用任何校验器()
        {
            var registry = new ValidatorRegistry();
            var validator = new FakeValidator<GenerationRequest>();
            registry.Register(ValidationStage.Pre, validator);

            var result = registry.Run(ValidationStage.Pre, (object)null);

            Assert.AreEqual(0, validator.CallCount, "null 判定由调度层负责，注册表不重复执行");
            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void 注册空校验器_抛参数异常()
        {
            var registry = new ValidatorRegistry();
            Assert.Throws<ArgumentNullException>(() => registry.Register(ValidationStage.Pre, (IValidator<GenerationRequest>)null));
            Assert.Throws<ArgumentNullException>(() => registry.RegisterForTemplate<GenerationRequest>("A", null));
        }

        [Test]
        public void 服务未注入_校验器正常降级运行()
        {
            var registry = new ValidatorRegistry(); // 未 SetServices
            var validator = new FakeValidator<GenerationRequest>();
            registry.Register(ValidationStage.Pre, validator);

            var result = registry.Run(ValidationStage.Pre, new GenerationRequest());

            Assert.AreEqual(1, validator.CallCount, "服务未注入不应影响调度（具体校验器自行降级）");
            Assert.IsTrue(result.IsValid);
        }
    }
}
