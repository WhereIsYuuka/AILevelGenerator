using System.Collections.Generic;
using System.Linq;

namespace AILevelGenerator.Runtime.Stability
{
    /// <summary>
    /// 稳定性测试汇总结果（第四周-Day6/7）：20 轮轮换测试的统计口径。
    /// 成功率 = 通过轮数 ÷ 总轮数；回滚成功率 = 回滚成功轮数 ÷ 触发回滚轮数（0 除安全 → 0，文本显示 N/A）。
    /// 纯逻辑统计（可单测）：编排器只收集轮结果，本类负责口径与汇总文本——统计口径被测试锁定，不会漂移。
    /// </summary>
    public class StabilityTestResult
    {
        /// <summary> 全部轮次明细（按执行顺序） </summary>
        public List<StabilityRoundResult> Rounds = new();

        /// <summary> 总耗时（秒，全部轮次） </summary>
        public double TotalTimeSeconds;

        /// <summary> 通过轮数（终态 + 场景断言 + 报告计数全部符合预期） </summary>
        public int PassedCount => Rounds.Count(r => r.Passed);

        /// <summary> 触发回滚的轮数（含注入的回滚失败轮） </summary>
        public int RollbackTriggeredCount => Rounds.Count(r => r.RollbackTriggered);

        /// <summary> 回滚成功的轮数 </summary>
        public int RollbackSucceededCount => Rounds.Count(r => r.RollbackTriggered && r.RollbackSucceeded);

        /// <summary> 成功率（0~1）；空结果返回 0（0 除安全） </summary>
        public double SuccessRate => Rounds.Count == 0 ? 0d : (double)PassedCount / Rounds.Count;

        /// <summary> 回滚成功率（0~1）；未触发任何回滚时返回 0（0 除安全，文本层显示 N/A 区分） </summary>
        public double RollbackSuccessRate =>
            RollbackTriggeredCount == 0 ? 0d : (double)RollbackSucceededCount / RollbackTriggeredCount;

        /// <summary> 是否全部通过（至少 1 轮且全过） </summary>
        public bool AllPassed => Rounds.Count > 0 && PassedCount == Rounds.Count;

        /// <summary>
        /// 汇总文本：`稳定性测试：20/20 轮通过（成功率 100.0%），回滚触发 5 次 / 成功 4 次（回滚成功率 80.0%），总耗时 12.3s → PASS`。
        /// 未触发回滚时回滚段显示「N/A（本批未触发回滚）」——区分"0% 失败"与"无统计样本"。
        /// </summary>
        public string ToSummaryText()
        {
            var rollbackText = RollbackTriggeredCount == 0
                ? "回滚触发 0 次（N/A：本批未触发回滚）"
                : $"回滚触发 {RollbackTriggeredCount} 次 / 成功 {RollbackSucceededCount} 次（回滚成功率 {RollbackSuccessRate:P1}）";
            return $"稳定性测试：{PassedCount}/{Rounds.Count} 轮通过（成功率 {SuccessRate:P1}），{rollbackText}，" +
                   $"总耗时 {TotalTimeSeconds:F1}s → {(AllPassed ? "PASS" : "FAIL")}";
        }
    }
}
