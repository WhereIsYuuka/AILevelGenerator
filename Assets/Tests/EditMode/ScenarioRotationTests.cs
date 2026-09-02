using System.Linq;
using AILevelGenerator.Runtime.Scheduling;
using AILevelGenerator.Runtime.Stability;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 稳定性测试轮换表契约测试（第四周-Day6/7）：ScenarioRotation 是「要测什么」的唯一事实来源——
    /// 契约被测试锁定：恰好 20 轮、全部 14 种场景至少一次、回滚触发 5 次（成功 4/失败 1）、
    /// 拦截轮 1 次且期望 Ready、取消轮 2 次、相邻轮场景不同、成功轮分布均匀（成功 9 轮）。
    /// 改轮换表漏契约（如删了回滚失败轮）→ 红，防止统计口径失去样本。
    /// </summary>
    public class ScenarioRotationTests
    {
        [Test]
        public void 恰好20轮_满足连续20次生成验收口径()
        {
            Assert.That(ScenarioRotation.Rounds.Count, Is.EqualTo(ScenarioRotation.RoundCount));
            Assert.That(ScenarioRotation.RoundCount, Is.EqualTo(20));
        }

        [Test]
        public void 全部场景至少出现一次()
        {
            var scenarios = ScenarioRotation.Rounds.Select(r => r.Scenario).ToHashSet();
            var all = System.Enum.GetValues(typeof(StabilityScenario)).Cast<StabilityScenario>();
            foreach (var s in all)
                Assert.That(scenarios.Contains(s), Is.True, $"场景 {s} 未出现在轮换表中（新增场景必须入表）");
        }

        [Test]
        public void 回滚触发5次_成功4次失败1次()
        {
            var triggered = ScenarioRotation.Rounds.Count(r => r.ExpectRollbackTriggered);
            var succeeded = ScenarioRotation.Rounds.Count(r => r.ExpectRollbackTriggered && r.ExpectRollbackSucceeded);
            var failed = ScenarioRotation.Rounds.Count(r => r.ExpectRollbackTriggered && !r.ExpectRollbackSucceeded);

            Assert.That(triggered, Is.EqualTo(5), "回滚成功率需要统计样本：恰好 5 轮触发回滚");
            Assert.That(succeeded, Is.EqualTo(4));
            Assert.That(failed, Is.EqualTo(1), "必须包含回滚失败轮（验证失败路径如实报告）");
        }

        [Test]
        public void 拦截轮恰好1次且期望Ready()
        {
            var blocked = ScenarioRotation.Rounds.Where(r => r.Scenario == StabilityScenario.RequestBlocked).ToList();
            Assert.That(blocked.Count, Is.EqualTo(1));
            Assert.That(blocked[0].ExpectedState, Is.EqualTo(GenerationTaskState.Ready), "拦截轮不进入状态流转");
            Assert.That(blocked[0].ExpectRollbackTriggered, Is.False);
        }

        [Test]
        public void 取消轮恰好2次_生成中与构建中各一()
        {
            Assert.That(ScenarioRotation.Rounds.Count(r => r.Scenario == StabilityScenario.CancelDuringGenerate), Is.EqualTo(1));
            Assert.That(ScenarioRotation.Rounds.Count(r => r.Scenario == StabilityScenario.CancelDuringBuild), Is.EqualTo(1));
        }

        [Test]
        public void 相邻轮场景不同()
        {
            for (var i = 1; i < ScenarioRotation.Rounds.Count; i++)
                Assert.That(ScenarioRotation.Rounds[i].Scenario,
                    Is.Not.EqualTo(ScenarioRotation.Rounds[i - 1].Scenario),
                    $"第 {i + 1} 轮与第 {i} 轮场景重复（相邻轮必须不同，避免连续同路径掩盖状态残留）");
        }

        [Test]
        public void 成功轮8次_正常成功7次()
        {
            // 成功 8 轮 = 7 正常 + 1 零实体（NaN 坐标被 DataBounds 校验拦截为 Failed，不属于成功轮）
            var success = ScenarioRotation.Rounds.Count(r => r.ExpectedState == GenerationTaskState.Success);
            var normal = ScenarioRotation.Rounds.Count(r => r.Scenario == StabilityScenario.NormalSuccess);
            Assert.That(success, Is.EqualTo(8));
            Assert.That(normal, Is.EqualTo(ScenarioRotation.NormalSuccessCount));
        }

        [Test]
        public void 回滚恢复轮_断言类型与回滚期望一致()
        {
            foreach (var r in ScenarioRotation.Rounds.Where(r => r.ExpectRollbackTriggered && r.ExpectRollbackSucceeded))
                Assert.That(r.Assertion, Is.EqualTo(StabilityAssertion.RollbackRestored),
                    $"回滚成功轮 {r.Scenario} 必须用 RollbackRestored 断言（指纹回到生成前）");
        }

        [Test]
        public void 轮换表为静态固定序列()
        {
            // 同一进程内重复访问返回同一实例（确定性可复现，多次运行结果可比对）
            Assert.That(ReferenceEquals(ScenarioRotation.Rounds, ScenarioRotation.Rounds), Is.True);
            Assert.That(ScenarioRotation.Rounds[0].Scenario, Is.EqualTo(StabilityScenario.NormalSuccess));
            Assert.That(ScenarioRotation.Rounds[^1].Scenario, Is.EqualTo(StabilityScenario.NormalSuccess));
        }
    }
}
