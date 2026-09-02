using AILevelGenerator.Runtime.Scheduling;

namespace AILevelGenerator.Runtime.Stability
{
    /// <summary>
    /// 稳定性测试单轮结果（第四周-Day6/7）：一轮生成任务的完整观测——
    /// 实际终态 vs 期望终态、场景断言、报告计数、回滚触发/结果、耗时。
    /// 纯数据 DTO（可单测）；通过 = 终态匹配 + 场景断言通过 + 报告计数匹配 三者同时成立。
    /// </summary>
    public class StabilityRoundResult
    {
        /// <summary> 轮次（1-based） </summary>
        public int Index;

        /// <summary> 本轮注入的场景 </summary>
        public StabilityScenario Scenario;

        /// <summary> 实际终态（拦截轮为 Ready——未进入状态流转） </summary>
        public GenerationTaskState ActualState;

        /// <summary> 期望终态（轮换表声明） </summary>
        public GenerationTaskState ExpectedState;

        /// <summary> 终态是否与期望一致 </summary>
        public bool StateMatched;

        /// <summary> 场景断言是否通过（按断言类型：零变更/回滚恢复/增量清理/生成根出现/零副作用） </summary>
        public bool AssertionPassed;

        /// <summary> 报告事件计数是否匹配（拦截轮 0 次，其余 1 次） </summary>
        public bool ReportCountMatched;

        /// <summary> 是否触发了自动回滚（真实或注入） </summary>
        public bool RollbackTriggered;

        /// <summary> 回滚是否成功（未触发时为 false，不计入回滚成功率分母） </summary>
        public bool RollbackSucceeded;

        /// <summary> 单轮耗时（秒） </summary>
        public double RoundTimeSeconds;

        /// <summary> 失败原因说明（终态不符/断言失败/报告计数异常时填写，供汇总溯源） </summary>
        public string Note;

        /// <summary> 本轮通过：终态 + 场景 + 报告计数三者全部符合预期 </summary>
        public bool Passed => StateMatched && AssertionPassed && ReportCountMatched;

        /// <summary> 单轮汇总文本：`第 N/20 轮：场景名（状态）通过/失败 [原因] 耗时 Xs` </summary>
        public string ToSummaryLine(int totalRounds) =>
            $"第 {Index}/{totalRounds} 轮：{Scenario.ToDisplayName()}（{ActualState.ToDisplayName()}）" +
            (Passed ? " 通过" : $" 失败" + (string.IsNullOrEmpty(Note) ? "" : $" [{Note}]")) +
            $" 耗时 {RoundTimeSeconds:F2}s";
    }
}
