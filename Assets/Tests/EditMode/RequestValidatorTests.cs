using System;
using System.Collections.Generic;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Interfaces.Templates;
using AILevelGenerator.Runtime.Templates;
using AILevelGenerator.Runtime.Validation;
using NUnit.Framework;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 请求级前置校验单元测试（输入合法性，100% 拦截非法输入）：
    /// 空/超长描述、模板存在性、生成开关全关；错误信息定位到具体字段（path）。
    /// </summary>
    public class RequestValidatorTests
    {
        /// <summary> 假模板提供者：按注册表返回模板（其余方法空实现） </summary>
        private class FakeTemplateProvider : ITemplateProvider
        {
            private readonly Dictionary<string, LevelTemplate> _templates = new();

            public FakeTemplateProvider WithTemplate(string id)
            {
                var t = ScriptableObject.CreateInstance<ConfigurableLevelTemplate>();
                t.TemplateId = id;
                _templates[id] = t;
                return this;
            }

            public IReadOnlyList<LevelTemplate> GetLevelTemplates() => new List<LevelTemplate>(_templates.Values);
            public LevelTemplate GetTemplateById(string id) => _templates.TryGetValue(id, out var t) ? t : null;
            public IReadOnlyList<TaskTemplate> GetTaskTemplates() => Array.Empty<TaskTemplate>();
            public TaskTemplate GetTaskTemplateById(string id) => null;
            public PromptTemplate GetDefaultPromptTemplate() => null;
            public PromptTemplate GetPromptTemplateById(string id) => null;
        }

        private static ValidationResult Validate(GenerationRequest request, ITemplateProvider provider = null)
        {
            var validator = new RequestValidator();
            return validator.Validate(request, new ValidationContext { TemplateProvider = provider });
        }

        private static GenerationRequest CreateRequest() => new GenerationRequest
        {
            Prompt = "森林营地，3个巡逻弓箭手，1个宝箱",
            TemplateId = "战斗关卡"
        };

        [Test]
        public void 空白描述_报缺少描述错误且路径定位到prompt()
        {
            var request = CreateRequest();
            request.Prompt = "   ";

            var result = Validate(request);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("REQUEST_PROMPT_EMPTY", result.Errors[0].Code);
            StringAssert.Contains("缺少描述", result.Errors[0].Message);
            Assert.AreEqual("prompt", result.Errors[0].DataPath);
        }

        [Test]
        public void 描述超长_报超长错误()
        {
            var request = CreateRequest();
            request.Prompt = new string('测', 2001); // 上限 2000

            var result = Validate(request);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("REQUEST_PROMPT_TOO_LONG", result.Errors[0].Code);
            StringAssert.Contains("2000", result.Errors[0].Message);
            Assert.AreEqual("prompt", result.Errors[0].DataPath);
        }

        [Test]
        public void 指定模板但不存在_报模板不存在错误()
        {
            var request = CreateRequest();
            request.TemplateId = "不存在的模板";

            var result = Validate(request, new FakeTemplateProvider().WithTemplate("战斗关卡"));

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("REQUEST_TEMPLATE_NOT_FOUND", result.Errors[0].Code);
            Assert.AreEqual("templateId", result.Errors[0].DataPath);
        }

        [Test]
        public void 指定模板且存在_模板检查通过()
        {
            var result = Validate(CreateRequest(), new FakeTemplateProvider().WithTemplate("战斗关卡"));

            Assert.IsTrue(result.IsValid, "模板存在时不应报错");
        }

        [Test]
        public void 未指定模板_跳过模板存在性检查()
        {
            var request = CreateRequest();
            request.TemplateId = "";

            // Provider 即使为空也不应触发模板查询（TemplateId 空即跳过）
            var result = Validate(request, new FakeTemplateProvider());

            Assert.IsTrue(result.IsValid, "未指定模板不应校验模板存在性");
        }

        [Test]
        public void 模板提供者未注入_跳过模板存在性检查()
        {
            var request = CreateRequest();
            request.TemplateId = "任何模板";

            var result = Validate(request); // provider = null

            Assert.IsTrue(result.IsValid, "服务未注入时应降级跳过，不误报");
        }

        [Test]
        public void 三个生成开关全关_报无内容错误()
        {
            var request = CreateRequest();
            request.GenerateTerrain = false;
            request.GenerateProps = false;
            request.GenerateTasks = false;

            var result = Validate(request);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("REQUEST_NO_CONTENT", result.Errors[0].Code);
        }

        [Test]
        public void 合法请求_校验通过()
        {
            var result = Validate(CreateRequest());

            Assert.IsTrue(result.IsValid, "合法请求（非空描述 + 开关开启）应通过");
        }

        [Test]
        public void 空请求_报空请求错误()
        {
            var result = Validate(null);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("REQUEST_NULL", result.Errors[0].Code);
        }
    }
}
