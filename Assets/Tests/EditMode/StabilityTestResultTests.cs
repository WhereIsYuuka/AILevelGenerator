using AILevelGenerator.Runtime.Scheduling;
using AILevelGenerator.Runtime.Stability;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 稳定性测试统计口径测试（第四周-Day6/7「统计成功率与回滚成功率」核心）：
    /// 成功率/回滚成功率/0 除安全/汇总文本格式——口径被测试锁定，不会在编排器演化中漂移。
    /// </summary>
    public class StabilityTestResultTests
    {
        private static StabilityRoundResult Round(StabilityScenario scenario, bool passed,
            bool rollbackTriggered = false, bool rollbackSucceeded = false, string note = null) => new()
        {
            Index = 1,
            Scenario = scenario,
            ActualState = passed ? GenerationTaskState.Success : GenerationTaskState.Failed,
            ExpectedState = passed ? GenerationTaskState.Success : GenerationTaskState.Failed,
            StateMatched = passed, // 失败轮标记终态不符 → Passed 整体为 false（与编排器语义一致）
            AssertionPassed = true,
            ReportCountMatched = true,
            RollbackTriggered = rollbackTriggered,
            RollbackSucceeded = rollbackSucceeded,
            RoundTimeSeconds = 0.5,
            Note = note
        };

        [Test]
        public void 空结果_成功率为0且回滚成功率为0不抛异常()
        {
            var result = new StabilityTestResult { TotalTimeSeconds = 0d };
            Assert.That(result.PassedCount, Is.EqualTo(0));
            Assert.That(result.SuccessRate, Is.EqualTo(0d), "0 除安全：空结果成功率返回 0");
            Assert.That(result.RollbackSuccessRate, Is.EqualTo(0d), "0 除安全：未触发回滚返回 0");
            Assert.That(result.AllPassed, Is.False, "空结果不视为全部通过");
        }

        [Test]
        public void 全部通过_成功率100()
        {
            var result = new StabilityTestResult();
            for (var i = 0; i < 20; i++)
                result.Rounds.Add(Round(StabilityScenario.NormalSuccess, passed: true));

            Assert.That(result.PassedCount, Is.EqualTo(20));
            Assert.That(result.SuccessRate, Is.EqualTo(1d));
            Assert.That(result.AllPassed, Is.True);
        }

        [Test]
        public void 混合轮次_成功率按通过数计算()
        {
            var result = new StabilityTestResult();
            result.Rounds.Add(Round(StabilityScenario.NormalSuccess, passed: true));
            result.Rounds.Add(Round(StabilityScenario.GeneratorThrows, passed: true));
            result.Rounds.Add(Round(StabilityScenario.BuildFail, passed: false, note: "终态不符"));

            Assert.That(result.PassedCount, Is.EqualTo(2));
            Assert.That(result.SuccessRate, Is.EqualTo(2d / 3d), "成功率 = 通过轮 ÷ 总轮");
        }

        [Test]
        public void 回滚成功率_只统计触发回滚的轮()
        {
            var result = new StabilityTestResult();
            // 触发 5 次（成功 4 次 + 注入失败 1 次），未触发的不入分母
            result.Rounds.Add(Round(StabilityScenario.MidValidationFail, passed: true, rollbackTriggered: true, rollbackSucceeded: true));
            result.Rounds.Add(Round(StabilityScenario.BuildFail, passed: true, rollbackTriggered: true, rollbackSucceeded: true));
            result.Rounds.Add(Round(StabilityScenario.BuilderThrows, passed: true, rollbackTriggered: true, rollbackSucceeded: true));
            result.Rounds.Add(Round(StabilityScenario.PostValidationFail, passed: true, rollbackTriggered: true, rollbackSucceeded: true));
            result.Rounds.Add(Round(StabilityScenario.RollbackFail, passed: true, rollbackTriggered: true, rollbackSucceeded: false));
            // 未触发回滚的轮（拦截/取消/零变更失败）不入分母
            result.Rounds.Add(Round(StabilityScenario.GeneratorThrows, passed: true));

            Assert.That(result.RollbackTriggeredCount, Is.EqualTo(5));
            Assert.That(result.RollbackSucceededCount, Is.EqualTo(4));
            Assert.That(result.RollbackSuccessRate, Is.EqualTo(0.8d), "回滚成功率 = 回滚成功 ÷ 触发回滚");
        }

        [Test]
        public void 汇总文本_通过时PASS且含成功率与回滚成功率()
        {
            var result = new StabilityTestResult { TotalTimeSeconds = 12.3d };
            for (var i = 0; i < 20; i++)
                result.Rounds.Add(Round(StabilityScenario.NormalSuccess, passed: true));
            // 附加一轮触发回滚以验证回滚段文本
            result.Rounds.Add(Round(StabilityScenario.BuildFail, passed: true, rollbackTriggered: true, rollbackSucceeded: true));

            var text = result.ToSummaryText();
            Assert.That(text, Does.StartWith("稳定性测试：21/21 轮通过"));
            Assert.That(text, Does.Contain("成功率 100.0%"));
            Assert.That(text, Does.Contain("回滚触发 1 次 / 成功 1 次"));
            Assert.That(text, Does.Contain("回滚成功率 100.0%"));
            Assert.That(text, Does.EndWith("→ PASS"));
            Assert.That(text, Does.Contain("总耗时 12.3s"));
        }

        [Test]
        public void 汇总文本_未触发回滚时显示N_A()
        {
            var result = new StabilityTestResult();
            result.Rounds.Add(Round(StabilityScenario.NormalSuccess, passed: true));

            var text = result.ToSummaryText();
            Assert.That(text, Does.Contain("回滚触发 0 次（N/A：本批未触发回滚）"), "0 触发必须显示 N/A，与「0% 失败」区分");
            Assert.That(text, Does.EndWith("→ PASS"));
        }

        [Test]
        public void 汇总文本_有失败轮时FAIL()
        {
            var result = new StabilityTestResult();
            result.Rounds.Add(Round(StabilityScenario.NormalSuccess, passed: true));
            result.Rounds.Add(Round(StabilityScenario.BuildFail, passed: false, rollbackTriggered: true, rollbackSucceeded: false));

            var text = result.ToSummaryText();
            Assert.That(text, Does.Contain("1/2 轮通过"));
            Assert.That(text, Does.Contain("成功率 50.0%"));
            Assert.That(text, Does.EndWith("→ FAIL"));
        }

        [Test]
        public void 单轮汇总行_失败含原因且状态与耗时齐全()
        {
            var line = Round(StabilityScenario.GeneratorThrows, passed: false, note: "终态 Failed ≠ 期望 Success")
                .ToSummaryLine(20);
            Assert.That(line, Does.StartWith("第 1/20 轮：生成器异常（失败） 失败"));
            Assert.That(line, Does.Contain("[终态 Failed ≠ 期望 Success]"));
            Assert.That(line, Does.Contain("耗时 0.50s"));
        }

        [Test]
        public void 单轮汇总行_通过不含原因()
        {
            var line = Round(StabilityScenario.NormalSuccess, passed: true).ToSummaryLine(20);
            Assert.That(line, Does.Contain("第 1/20 轮：正常成功（成功） 通过"));
            Assert.That(line, Does.Not.Contain("["));
        }
    }
}
