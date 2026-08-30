using AILevelGenerator.Runtime.Utilities;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 帧率自适应计算器单元测试（Day1）：
    /// 覆盖滑动平均、预算升降、上下限 clamp、非法输入防御、Reset 与构造参数归一。
    /// </summary>
    public class FrameBudgetCalculatorTests
    {
        /// <summary> 标准参数：目标帧耗时 16ms（60fps 整帧），基准每帧 4 个，窗口 3 帧便于观察滑动 </summary>
        private static FrameBudgetCalculator Create() =>
            new(windowSize: 3, targetFrameTimeMs: 16f, basePerFrame: 4, minPerFrame: 1, maxPerFrame: 10);

        [Test]
        public void 无样本_返回基准预算()
        {
            var calc = Create();
            Assert.AreEqual(4, calc.GetBudgetPerFrame(), "未统计到帧率时应按目标帧率假设");
        }

        [Test]
        public void 目标帧率_预算等于基准()
        {
            var calc = Create();
            calc.RecordDeltaTime(0.016f);
            calc.RecordDeltaTime(0.016f);
            calc.RecordDeltaTime(0.016f);

            Assert.AreEqual(0.016f, calc.AverageDeltaTime, 0.0001f);
            Assert.AreEqual(4, calc.GetBudgetPerFrame(), "60fps 下预算应等于基准速率");
        }

        [Test]
        public void 帧间隔增大_预算降低()
        {
            var calc = Create(); // 平均 32ms（编辑器变卡）
            calc.RecordDeltaTime(0.032f);

            Assert.AreEqual(2, calc.GetBudgetPerFrame(), "4×16/32 = 2");
        }

        [Test]
        public void 帧间隔减小_预算升高()
        {
            var calc = Create(); // 平均 8ms（编辑器流畅）
            calc.RecordDeltaTime(0.008f);

            Assert.AreEqual(8, calc.GetBudgetPerFrame(), "4×16/8 = 8");
        }

        [Test]
        public void 预算被上限截断()
        {
            var calc = Create(); // 4×16/1 = 64 → clamp 10
            calc.RecordDeltaTime(0.001f);

            Assert.AreEqual(10, calc.GetBudgetPerFrame());
        }

        [Test]
        public void 预算被下限截断()
        {
            var calc = Create(); // 4×16/1000 = 0.064 → clamp 1
            calc.RecordDeltaTime(1f);

            Assert.AreEqual(1, calc.GetBudgetPerFrame());
        }

        [Test]
        public void 滑动平均_旧样本被新样本替换()
        {
            var calc = Create();
            calc.RecordDeltaTime(0.032f);
            calc.RecordDeltaTime(0.032f);
            calc.RecordDeltaTime(0.032f);
            Assert.AreEqual(2, calc.GetBudgetPerFrame());

            calc.RecordDeltaTime(0.008f); // 窗口滑动：平均 (32+32+8)/3 ≈ 24ms → 4×16/24 ≈ 2.67 → 3
            Assert.AreEqual(3, calc.GetBudgetPerFrame());
            Assert.AreEqual(0.024f, calc.AverageDeltaTime, 0.0001f);
        }

        [Test]
        public void 非法帧间隔_忽略不影响统计()
        {
            var calc = Create();
            calc.RecordDeltaTime(0.016f);
            calc.RecordDeltaTime(-1f);
            calc.RecordDeltaTime(0f);
            calc.RecordDeltaTime(0.016f);

            Assert.AreEqual(0.016f, calc.AverageDeltaTime, 0.0001f, "负值与零间隔应被忽略");
            Assert.AreEqual(4, calc.GetBudgetPerFrame());
        }

        [Test]
        public void Reset_清空样本回归基准()
        {
            var calc = Create();
            calc.RecordDeltaTime(0.032f);
            calc.Reset();

            Assert.AreEqual(0f, calc.AverageDeltaTime);
            Assert.AreEqual(4, calc.GetBudgetPerFrame(), "Reset 后应回到基准预算");
        }

        [Test]
        public void 构造参数_下限高于上限时以上限为准()
        {
            // min=10 高于 max=5：构造时归一为 max=10，预算 48 → clamp 到 10
            var calc = new FrameBudgetCalculator(windowSize: 5, targetFrameTimeMs: 16f, basePerFrame: 3, minPerFrame: 10, maxPerFrame: 5);
            calc.RecordDeltaTime(0.001f);

            Assert.AreEqual(10, calc.GetBudgetPerFrame(), "min 高于 max 时应收敛到 max");
        }
    }
}
