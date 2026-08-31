using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Validation;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 校验器抽象基类单元测试（第四周-Day2）：
    /// AddError/AddWarning 便捷方法写入正确、SetLogger 注入持有。
    /// </summary>
    public class BaseValidatorTests
    {
        /// <summary> 探针校验器：暴露基类受保护成员供断言 </summary>
        private class ProbeValidator : BaseValidator<GenerationRequest>
        {
            public ValidationResult RunAddError(GenerationRequest data, string code, string message, string path)
            {
                var result = new ValidationResult();
                AddError(result, code, message, path);
                return result;
            }

            public ValidationResult RunAddWarning(GenerationRequest data, string code, string message, string path)
            {
                var result = new ValidationResult();
                AddWarning(result, code, message, path);
                return result;
            }

            public override ValidationResult Validate(GenerationRequest data, ValidationContext context)
            {
                var result = new ValidationResult();
                if (data == null) AddError(result, "DATA_NULL", "校验数据为空");
                return result;
            }

            public ILogger CapturedLogger => Logger;
        }

        [Test]
        public void 添加错误_写入错误列表且路径正确()
        {
            var validator = new ProbeValidator();
            var result = validator.RunAddError(new GenerationRequest(), "TEST_CODE", "测试错误", "prompt");

            Assert.AreEqual(1, result.Errors.Count);
            Assert.AreEqual("TEST_CODE", result.Errors[0].Code);
            Assert.AreEqual("测试错误", result.Errors[0].Message);
            Assert.AreEqual("prompt", result.Errors[0].DataPath);
            Assert.IsFalse(result.IsValid, "存在错误即视为校验失败");
        }

        [Test]
        public void 添加警告_写入警告列表且不影响通过性()
        {
            var validator = new ProbeValidator();
            var result = validator.RunAddWarning(new GenerationRequest(), "WARN_CODE", "测试警告", "props");

            Assert.AreEqual(1, result.Warnings.Count);
            Assert.AreEqual("WARN_CODE", result.Warnings[0].Code);
            Assert.IsTrue(result.IsValid, "仅警告不应视为校验失败");
        }

        [Test]
        public void 设置日志器_基类持有注入实例()
        {
            var validator = new ProbeValidator();
            var logger = new TestLogger();

            validator.SetLogger(logger);

            Assert.AreSame(logger, validator.CapturedLogger, "注入的日志器应被基类持有供子类使用");
        }

        [Test]
        public void 校验空数据_子类经基类便捷方法正常报错()
        {
            var validator = new ProbeValidator();

            var result = validator.Validate(null, new ValidationContext());

            Assert.AreEqual("DATA_NULL", result.Errors[0].Code);
        }
    }
}
