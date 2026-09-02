using System.Collections.Generic;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Diagnostics;
using AILevelGenerator.Runtime.Scheduling;
using NUnit.Framework;
using UnityEngine;
// 数据层 TerrainData 与 UnityEngine.TerrainData 同名，别名消除歧义（见 CLAUDE.md 已知坑）
using TerrainData = AILevelGenerator.Runtime.Data.TerrainData;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 生成报告构建器测试（第四周-Day5「生成报告输出」核心）：
    /// 成功/业务失败/取消/异常/空入参全路径降级、Prompt 截断、错误在前排序、
    /// 未知错误码分类降级、问题条目补全目录 分类+解决建议。
    /// </summary>
    public class GenerationReportBuilderTests
    {
        private readonly GenerationReportBuilder _builder = new();

        private static GenerationRequest CreateRequest(string prompt = "森林营地，1个宝箱", string templateId = "forest") =>
            new GenerationRequest { Prompt = prompt, TemplateId = templateId, RandomSeed = 42 };

        private static GenerationResult CreateSuccessResult() => new GenerationResult
        {
            Success = true,
            GenerationTime = 1.25f,
            LevelData = new LevelData
            {
                LevelName = "森林营地",
                Props = new List<PropPlacement>
                {
                    new() { PrefabLogicalName = "宝箱", Position = Vector3.zero, Scale = Vector3.one },
                    new() { PrefabLogicalName = "敌人-弓箭手", Position = Vector3.one, Scale = Vector3.one },
                    new() { PrefabLogicalName = "NPC", Position = Vector3.one * 2f, Scale = Vector3.one }
                },
                Terrain = new TerrainData { Width = 100, Length = 100, HeightScale = 10f },
                Tasks = new List<TaskData>
                {
                    new() { TaskID = "T1", IsMainTask = true },
                    new() { TaskID = "T2", IsMainTask = false }
                }
            },
            RawLLMResponse = "{\"level_name\":\"森林营地\"}"
        };

        private static LevelBuildResult CreateBuildResult() =>
            LevelBuildResult.Succeeded(3, 0, 0.75f, overlapRatio: 0.02f, resolvedPairs: 1,
                boundComponents: 2, bindFailed: 0);

        [Test]
        public void 成功报告_全字段填充()
        {
            var report = _builder.Build(CreateRequest(), CreateSuccessResult(), CreateBuildResult(),
                GenerationTaskState.Success, totalTimeSeconds: 2.5f);

            Assert.That(report.FinalState, Is.EqualTo(GenerationTaskState.Success));
            Assert.That(report.StatusText, Is.EqualTo("成功"));
            Assert.That(report.TemplateId, Is.EqualTo("forest"));
            Assert.That(report.RandomSeed, Is.EqualTo(42));
            Assert.That(report.Prompt, Is.EqualTo("森林营地，1个宝箱"));
            Assert.That(report.LevelName, Is.EqualTo("森林营地"));
            Assert.That(report.PropCount, Is.EqualTo(3));
            Assert.That(report.TaskCount, Is.EqualTo(2));
            Assert.That(report.MainTaskCount, Is.EqualTo(1));
            Assert.That(report.HasTerrain, Is.True);
            Assert.That(report.LlmTimeSeconds, Is.EqualTo(1.25f));
            Assert.That(report.BuildTimeSeconds, Is.EqualTo(0.75f));
            Assert.That(report.TotalTimeSeconds, Is.EqualTo(2.5f));
            Assert.That(report.InstantiatedCount, Is.EqualTo(3));
            Assert.That(report.BoundComponents, Is.EqualTo(2));
            Assert.That(report.ResolvedOverlapPairs, Is.EqualTo(1));
            Assert.That(report.OverlapRatio, Is.EqualTo(0.02f));
            Assert.That(report.RawLlmResponse, Does.Contain("level_name"));
        }

        [Test]
        public void 业务失败报告_错误计数与排序()
        {
            var result = new GenerationResult
            {
                Success = false,
                Errors = new List<ValidationError>
                {
                    new() { Code = ErrorCodes.RESOURCE_NOT_FOUND, Message = "资源不存在：宝箱", DataPath = "props[0].prefabLogicalName" }
                },
                Warnings = new List<ValidationWarning>
                {
                    new() { Code = ErrorCodes.PARSE_FALLBACK, Message = "「x」无法解析为整数", DataPath = "terrain.width" }
                }
            };
            var report = _builder.Build(CreateRequest(), result, null, GenerationTaskState.Failed);

            Assert.That(report.StatusText, Is.EqualTo("失败"));
            Assert.That(report.ErrorCount, Is.EqualTo(1));
            Assert.That(report.WarningCount, Is.EqualTo(1));
            Assert.That(report.Issues.Count, Is.EqualTo(2));
            Assert.That(report.Issues[0].Severity, Is.EqualTo(ErrorSeverity.Error), "错误必须排在警告前");
            Assert.That(report.Issues[1].Severity, Is.EqualTo(ErrorSeverity.Warning));
        }

        [Test]
        public void 问题条目_补全分类与解决建议()
        {
            var result = new GenerationResult
            {
                Success = false,
                Errors = new List<ValidationError>
                {
                    new() { Code = ErrorCodes.RESOURCE_NOT_FOUND, Message = "资源不存在：宝箱", DataPath = "props[0]" }
                }
            };
            var report = _builder.Build(CreateRequest(), result, null, GenerationTaskState.Failed);

            var issue = report.Issues[0];
            Assert.That(issue.Code, Is.EqualTo(ErrorCodes.RESOURCE_NOT_FOUND));
            Assert.That(issue.Category, Is.EqualTo(ErrorCategory.Resource));
            Assert.That(issue.Severity, Is.EqualTo(ErrorSeverity.Error));
            Assert.That(issue.Hint, Is.Not.Null.And.Not.Empty, "目录存在即应补全建议");
            Assert.That(issue.DataPath, Is.EqualTo("props[0]"));
        }

        [Test]
        public void 未知错误码_分类降级为Pipeline且不抛异常()
        {
            var result = new GenerationResult
            {
                Success = false,
                Errors = new List<ValidationError> { new() { Code = "SOME_LEGACY_CODE", Message = "旧码" } }
            };
            var report = _builder.Build(null, result, null, GenerationTaskState.Failed);

            Assert.That(report.Issues[0].Category, Is.EqualTo(ErrorCategory.Pipeline));
            Assert.That(report.Issues[0].Hint, Is.EqualTo(string.Empty));
        }

        [Test]
        public void 取消报告_状态文案覆盖生效()
        {
            var report = _builder.Build(CreateRequest(), CreateSuccessResult(), null,
                GenerationTaskState.Failed, statusTextOverride: "已取消");

            Assert.That(report.StatusText, Is.EqualTo("已取消"));
            Assert.That(report.FinalState, Is.EqualTo(GenerationTaskState.Failed));
        }

        [Test]
        public void 异常路径_全null入参安全降级()
        {
            // 生成器抛异常路径：result/buildResult 均为 null，request 可为 null
            var report = _builder.Build(null, null, null, GenerationTaskState.Failed);

            Assert.That(report.FinalState, Is.EqualTo(GenerationTaskState.Failed));
            Assert.That(report.TemplateId, Is.EqualTo(string.Empty));
            Assert.That(report.Prompt, Is.EqualTo(string.Empty));
            Assert.That(report.PropCount, Is.EqualTo(0));
            Assert.That(report.TaskCount, Is.EqualTo(0));
            Assert.That(report.ErrorCount, Is.EqualTo(0));
            Assert.That(report.InstantiatedCount, Is.EqualTo(0));
            Assert.That(report.RollbackNote, Is.EqualTo("未触发"));
        }

        [Test]
        public void Prompt超长_截断至120字()
        {
            var longPrompt = new string('关', 300);
            var report = _builder.Build(CreateRequest(longPrompt), null, null, GenerationTaskState.Failed);

            Assert.That(report.Prompt.Length, Is.EqualTo(121), "120 字 + 省略号");
            Assert.That(report.Prompt.EndsWith("…"));
        }

        [Test]
        public void 回滚信息_未触发_成功_失败三态()
        {
            var baseReport = _builder.Build(CreateRequest(), null, null, GenerationTaskState.Failed);
            Assert.That(baseReport.RollbackTriggered, Is.False);
            Assert.That(baseReport.RollbackNote, Is.EqualTo("未触发"));

            var ok = _builder.Build(CreateRequest(), null, null, GenerationTaskState.Failed,
                rollbackTriggered: true, rollbackSucceeded: true);
            Assert.That(ok.RollbackTriggered, Is.True);
            Assert.That(ok.RollbackSucceeded, Is.True);
            Assert.That(ok.RollbackNote, Does.Contain("已自动回滚成功"));

            var fail = _builder.Build(CreateRequest(), null, null, GenerationTaskState.Failed,
                rollbackTriggered: true, rollbackSucceeded: false);
            Assert.That(fail.RollbackSucceeded, Is.False);
            Assert.That(fail.RollbackNote, Does.Contain("自动回滚失败"));
        }
    }
}
