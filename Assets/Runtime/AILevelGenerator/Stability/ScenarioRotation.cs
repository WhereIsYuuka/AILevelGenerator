using System.Collections.Generic;
using AILevelGenerator.Runtime.Scheduling;

namespace AILevelGenerator.Runtime.Stability
{
    /// <summary>
    /// 稳定性测试场景断言类型（第四周-Day6/7）：每轮结束后按类型校验场景状态——
    /// 失败轮场景必须回到生成前（全量回滚 / 零变更 / 增量清理），成功轮必须出现生成根。
    /// </summary>
    public enum StabilityAssertion
    {
        /// <summary> 零副作用：拦截轮专用——未流转/未建快照/未触发报告/场景不变 </summary>
        NoSideEffect,

        /// <summary> 场景零变更：失败发生在构建之前（生成异常/业务失败/数据校验拦截/生成中取消） </summary>
        ZeroChange,

        /// <summary> 全量回滚恢复：构建污染后自动回滚，场景指纹回到生成前 </summary>
        RollbackRestored,

        /// <summary> 增量清理恢复：构建中取消后本次根被删除，场景指纹回到生成前 </summary>
        IncrementalClean,

        /// <summary> 生成根出现：成功轮场景必须出现 [AI Generated] 根（实体数断言由编排器按场景补充） </summary>
        GenRootCreated
    }

    /// <summary> 稳定性测试单轮规格（第四周-Day6/7）：场景 + 期望终态 + 期望回滚行为 + 断言类型。</summary>
    public class StabilityRoundSpec
    {
        /// <summary> 注入场景 </summary>
        public StabilityScenario Scenario;

        /// <summary> 期望终态（拦截轮为 Ready——未进入状态流转） </summary>
        public GenerationTaskState ExpectedState;

        /// <summary> 是否期望触发自动回滚 </summary>
        public bool ExpectRollbackTriggered;

        /// <summary> 是否期望回滚成功（仅 ExpectRollbackTriggered 时有意义） </summary>
        public bool ExpectRollbackSucceeded;

        /// <summary> 场景断言类型 </summary>
        public StabilityAssertion Assertion;
    }

    /// <summary>
    /// 稳定性测试 20 轮固定轮换表（第四周-Day6/7）：
    /// 确定性序列（可复现、可单测契约），覆盖全部 14 种场景——
    /// 成功 9 轮（7 正常 + 2 边界成功）、失败 8 轮（其中回滚触发 5 轮：4 成功 + 1 注入失败）、取消 2 轮、拦截 1 轮。
    /// 契约（ScenarioRotationTests 锁定）：恰好 20 项、每种场景至少一次、回滚触发恰好 5 次（成功 4/失败 1）、
    /// 相邻轮场景不同、拦截轮恰好 1 次且期望 Ready。
    /// 编排器（Editor 程序集）逐轮执行并自校验——轮换表是"要测什么"的唯一事实来源。
    /// </summary>
    public static class ScenarioRotation
    {
        /// <summary> 轮数（验收口径：连续 20 次生成） </summary>
        public const int RoundCount = 20;

        /// <summary> 固定序列（静态初始化，确定性） </summary>
        public static readonly IReadOnlyList<StabilityRoundSpec> Rounds = Build();

        /// <summary> 成功轮数（回滚成功率之外，成功率的主要来源） </summary>
        public const int NormalSuccessCount = 7;

        private static List<StabilityRoundSpec> Build()
        {
            var rounds = new List<StabilityRoundSpec>(RoundCount);

            // 失败/取消/拦截轮（期望 Failed 的生成任务：构建前失败零变更，构建后失败回滚恢复）
            rounds.Add(Round(StabilityScenario.NormalSuccess, GenerationTaskState.Success, Assertion: StabilityAssertion.GenRootCreated));
            rounds.Add(Round(StabilityScenario.GeneratorThrows, GenerationTaskState.Failed, Assertion: StabilityAssertion.ZeroChange));
            rounds.Add(Round(StabilityScenario.NormalSuccess, GenerationTaskState.Success, Assertion: StabilityAssertion.GenRootCreated));
            rounds.Add(Round(StabilityScenario.GeneratorBusinessFail, GenerationTaskState.Failed, Assertion: StabilityAssertion.ZeroChange));
            rounds.Add(Round(StabilityScenario.RequestBlocked, GenerationTaskState.Ready, Assertion: StabilityAssertion.NoSideEffect));
            rounds.Add(Round(StabilityScenario.NormalSuccess, GenerationTaskState.Success, Assertion: StabilityAssertion.GenRootCreated));
            rounds.Add(Round(StabilityScenario.ResourceMissing, GenerationTaskState.Failed, Assertion: StabilityAssertion.ZeroChange));
            rounds.Add(Round(StabilityScenario.ZeroEntities, GenerationTaskState.Success, Assertion: StabilityAssertion.GenRootCreated));
            rounds.Add(Round(StabilityScenario.MidValidationFail, GenerationTaskState.Failed,
                rollbackTriggered: true, rollbackSucceeded: true, Assertion: StabilityAssertion.RollbackRestored));
            rounds.Add(Round(StabilityScenario.NormalSuccess, GenerationTaskState.Success, Assertion: StabilityAssertion.GenRootCreated));
            rounds.Add(Round(StabilityScenario.BuildFail, GenerationTaskState.Failed,
                rollbackTriggered: true, rollbackSucceeded: true, Assertion: StabilityAssertion.RollbackRestored));
            rounds.Add(Round(StabilityScenario.NormalSuccess, GenerationTaskState.Success, Assertion: StabilityAssertion.GenRootCreated));
            rounds.Add(Round(StabilityScenario.BuilderThrows, GenerationTaskState.Failed,
                rollbackTriggered: true, rollbackSucceeded: true, Assertion: StabilityAssertion.RollbackRestored));
            rounds.Add(Round(StabilityScenario.NanCoordinate, GenerationTaskState.Failed, Assertion: StabilityAssertion.ZeroChange));
            rounds.Add(Round(StabilityScenario.PostValidationFail, GenerationTaskState.Failed,
                rollbackTriggered: true, rollbackSucceeded: true, Assertion: StabilityAssertion.RollbackRestored));
            rounds.Add(Round(StabilityScenario.NormalSuccess, GenerationTaskState.Success, Assertion: StabilityAssertion.GenRootCreated));
            rounds.Add(Round(StabilityScenario.CancelDuringBuild, GenerationTaskState.Failed, Assertion: StabilityAssertion.IncrementalClean));
            rounds.Add(Round(StabilityScenario.CancelDuringGenerate, GenerationTaskState.Failed, Assertion: StabilityAssertion.ZeroChange));
            rounds.Add(Round(StabilityScenario.RollbackFail, GenerationTaskState.Failed,
                rollbackTriggered: true, rollbackSucceeded: false, Assertion: StabilityAssertion.ZeroChange));
            rounds.Add(Round(StabilityScenario.NormalSuccess, GenerationTaskState.Success, Assertion: StabilityAssertion.GenRootCreated));

            return rounds;
        }

        private static StabilityRoundSpec Round(StabilityScenario scenario, GenerationTaskState expectedState,
            bool rollbackTriggered = false, bool rollbackSucceeded = false, StabilityAssertion Assertion = StabilityAssertion.ZeroChange)
            => new()
            {
                Scenario = scenario,
                ExpectedState = expectedState,
                ExpectRollbackTriggered = rollbackTriggered,
                ExpectRollbackSucceeded = rollbackSucceeded,
                Assertion = Assertion
            };
    }
}
