using System;
using System.Linq;
using System.Reflection;
using AILevelGenerator.Runtime.Diagnostics;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 错误码目录完整性测试（第四周-Day5）：
    /// - ErrorCodes 常量 ↔ ErrorCatalog 注册一一对应（漏注册/多余注册均失败）；
    /// - 每个定义的中文 摘要/建议 非空、分类与严重级枚举合法（「所有错误有明确提示与定位」验收的机器保证）。
    /// 新增错误码时按此契约补全常量 + 目录条目，测试即通过（改一处漏一处 → 红）。
    /// </summary>
    public class ErrorCatalogTests
    {
        [Test]
        public void 全部常量已注册目录条目()
        {
            var codes = GetAllErrorCodes();
            Assert.That(codes.Length, Is.GreaterThan(0), "ErrorCodes 常量列表不应为空");

            foreach (var code in codes)
            {
                Assert.That(ErrorCatalog.TryGet(code, out var def), Is.True,
                    $"错误码 {code} 未在 ErrorCatalog 注册（漏注册）——需补充 ErrorDefinition");
                Assert.That(def.Code, Is.EqualTo(code), "目录条目 Code 与常量不一致");
            }
        }

        [Test]
        public void 目录无多余注册()
        {
            // 目录条目数 == 常量数（多余注册 = 已废弃的码仍挂名，会导致文档/统计失真）
            Assert.That(ErrorCatalog.Count, Is.EqualTo(GetAllErrorCodes().Length),
                $"目录注册数 {ErrorCatalog.Count} 与 ErrorCodes 常量数不一致");
        }

        [Test]
        public void 每个定义摘要与建议非空()
        {
            foreach (var code in GetAllErrorCodes())
            {
                var def = ErrorCatalog.Get(code);
                Assert.That(string.IsNullOrWhiteSpace(def.Summary), Is.False,
                    $"错误码 {code} 缺少中文摘要（Summary）");
                Assert.That(string.IsNullOrWhiteSpace(def.Hint), Is.False,
                    $"错误码 {code} 缺少解决建议（Hint）——验收要求每个错误有明确提示");
            }
        }

        [Test]
        public void 分类与严重级枚举全部合法()
        {
            foreach (var code in GetAllErrorCodes())
            {
                var def = ErrorCatalog.Get(code);
                Assert.That(Enum.IsDefined(typeof(ErrorCategory), def.Category), Is.True,
                    $"错误码 {code} 分类无效：{def.Category}");
                Assert.That(Enum.IsDefined(typeof(ErrorSeverity), def.Severity), Is.True,
                    $"错误码 {code} 严重级无效：{def.Severity}");
            }
        }

        [Test]
        public void 错误码命名规范_前缀与分类一致()
        {
            // 命名约定：请求 REQUEST_* / 数据 DATA_* / 资源 RESOURCE_* / 模板 PROPS|TASKS|NO_MAIN|TERRAIN /
            // 后置 POST_* / 解析 PARSE|NOT_OBJECT / LLM NO_API_KEY|LLM_ERROR / 基础设施 VALIDATOR_ERROR / 演示 DEMO_FAIL
            foreach (var code in GetAllErrorCodes())
            {
                var def = ErrorCatalog.Get(code);
                var prefixOk = def.Category switch
                {
                    ErrorCategory.Request => code.StartsWith("REQUEST_"),
                    ErrorCategory.Data => code.StartsWith("DATA_"),
                    ErrorCategory.Resource => code.StartsWith("RESOURCE_"),
                    ErrorCategory.Template => code.StartsWith("PROPS_") || code.StartsWith("TASKS_")
                        || code.StartsWith("NO_MAIN") || code.StartsWith("TERRAIN_"),
                    ErrorCategory.Post => code.StartsWith("POST_"),
                    ErrorCategory.Parsing => code.StartsWith("PARSE") || code.StartsWith("NOT_OBJECT"),
                    ErrorCategory.Llm => code == "NO_API_KEY" || code == "LLM_ERROR",
                    ErrorCategory.Pipeline => code == "VALIDATOR_ERROR" || code == "DEMO_FAIL",
                    _ => false
                };
                Assert.That(prefixOk, Is.True, $"错误码 {code} 前缀与分类 {def.Category} 不一致（命名规范）");
            }
        }

        [Test]
        public void 未知码安全降级()
        {
            Assert.That(ErrorCatalog.TryGet("UNKNOWN_CODE_XYZ", out var def), Is.False);
            Assert.That(def, Is.Null);
            Assert.That(ErrorCatalog.Get("UNKNOWN_CODE_XYZ"), Is.Null);
            Assert.That(ErrorCatalog.TryGet(null, out _), Is.False);
        }

        /// <summary> 反射收集 ErrorCodes 全部字符串常量（const string，按声明序） </summary>
        private static string[] GetAllErrorCodes() =>
            typeof(ErrorCodes)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue())
                .ToArray();
    }
}
