using System.Collections.Generic;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Diagnostics;
using AILevelGenerator.Runtime.Scheduling;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 生成报告 Markdown 格式化测试（第四周-Day5）：纯逻辑无 IO。
    /// 断言章节齐全、数字 InvariantCulture 输出（区域无关）、问题条目含错误码/定位/建议。
    /// </summary>
    public class GenerationReportFormatterTests
    {
        private static GenerationReport CreateReport()
        {
            var builder = new GenerationReportBuilder();
            var result = new GenerationResult
            {
                Success = false,
                GenerationTime = 1.5f,
                Errors = new List<ValidationError>
                {
                    new() { Code = ErrorCodes.RESOURCE_NOT_FOUND, Message = "资源不存在：宝箱", DataPath = "props[0]" }
                }
            };
            return builder.Build(new GenerationRequest { Prompt = "森林营地", TemplateId = "forest", RandomSeed = 7 },
                result, null, GenerationTaskState.Failed,
                rollbackTriggered: true, rollbackSucceeded: true, totalTimeSeconds: 2.25f);
        }

        [Test]
        public void 空报告_返回空串()
        {
            Assert.That(GenerationReportFormatter.FormatMarkdown(null), Is.EqualTo(string.Empty));
        }

        [Test]
        public void 章节齐全_含标题与摘要()
        {
            var md = GenerationReportFormatter.FormatMarkdown(CreateReport());

            Assert.That(md, Does.StartWith("# 生成报告：失败"));
            Assert.That(md, Does.Contain("## 请求摘要"));
            Assert.That(md, Does.Contain("## 内容统计"));
            Assert.That(md, Does.Contain("## 构建摘要"));
            Assert.That(md, Does.Contain("## 校验问题（错误 1 / 警告 0）"));
            Assert.That(md, Does.Contain("## 回滚"));
        }

        [Test]
        public void 请求摘要_模板种子描述齐全()
        {
            var md = GenerationReportFormatter.FormatMarkdown(CreateReport());
            Assert.That(md, Does.Contain("- 模板：forest（ID：forest）"));
            Assert.That(md, Does.Contain("- 随机种子：7"));
            Assert.That(md, Does.Contain("- 描述：森林营地"));
        }

        [Test]
        public void 问题条目_含错误码定位与建议()
        {
            var md = GenerationReportFormatter.FormatMarkdown(CreateReport());
            Assert.That(md, Does.Contain("[错误] RESOURCE_NOT_FOUND：资源不存在：宝箱（props[0]）"));
            Assert.That(md, Does.Contain("建议：")); // 目录 hint 已补全
        }

        [Test]
        public void 回滚章节_记录自动回滚结果()
        {
            var md = GenerationReportFormatter.FormatMarkdown(CreateReport());
            Assert.That(md, Does.Contain("已自动回滚成功"));
        }

        [Test]
        public void 原始响应_非空时输出代码块()
        {
            var report = CreateReport();
            report.RawLlmResponse = "{\"level_name\":\"测试\"}";
            var md = GenerationReportFormatter.FormatMarkdown(report);

            Assert.That(md, Does.Contain("## 原始 LLM 响应"));
            Assert.That(md, Does.Contain("```text"));
            Assert.That(md, Does.Contain("{\"level_name\":\"测试\"}"));
        }

        [Test]
        public void 数字输出_区域无关()
        {
            var md = GenerationReportFormatter.FormatMarkdown(CreateReport());
            // 2.25s 在 zh-CN/en-US 等区域均为 "2.25"（InvariantCulture），避免区域设置差异导致断言失败
            Assert.That(md, Does.Contain("总耗时：2.25s（LLM 1.5s + 构建 0s）"));
        }
    }
}
