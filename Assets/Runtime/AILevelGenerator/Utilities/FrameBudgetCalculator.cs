using System;
using System.Collections.Generic;

namespace AILevelGenerator.Runtime.Utilities
{
    /// <summary>
    /// 帧率自适应计算器（纯逻辑，可单测）：
    /// 输入每帧实测间隔（EditorApplication.timeSinceStartup 差值）→ 滑动平均 → 输出每帧实例化预算。
    /// 语义：编辑器越卡（平均帧间隔越大）→ 每帧实例化数越少；编辑器流畅 → 加快吞吐。
    /// 预算 = clamp( BasePerFrame × TargetFrameTimeMs ÷ 平均帧间隔ms , Min , Max )
    /// </summary>
    public class FrameBudgetCalculator
    {
        private readonly Queue<float> _recentDeltas = new();
        private readonly int _windowSize;
        private readonly float _targetFrameTimeMs;
        private readonly int _basePerFrame;
        private readonly int _minPerFrame;
        private readonly int _maxPerFrame;

        /// <summary> 滑动平均帧间隔（秒）；无样本时为 0 </summary>
        public float AverageDeltaTime { get; private set; }

        public FrameBudgetCalculator(int windowSize = 10, float targetFrameTimeMs = 8f,
            int basePerFrame = 3, int minPerFrame = 1, int maxPerFrame = 30)
        {
            _windowSize = Math.Max(1, windowSize);
            _targetFrameTimeMs = Math.Max(0.5f, targetFrameTimeMs);
            _basePerFrame = Math.Max(1, basePerFrame);
            _minPerFrame = Math.Max(1, minPerFrame);
            _maxPerFrame = Math.Max(_minPerFrame, maxPerFrame); // 下限高于上限时以上限为准
        }

        /// <summary> 记录一帧实测间隔（秒）。非法值（≤0）忽略，防止零除与瞬断干扰统计 </summary>
        public void RecordDeltaTime(float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;
            _recentDeltas.Enqueue(deltaSeconds);
            while (_recentDeltas.Count > _windowSize) _recentDeltas.Dequeue();

            var sum = 0f;
            foreach (var delta in _recentDeltas) sum += delta;
            AverageDeltaTime = sum / _recentDeltas.Count;
        }

        /// <summary>
        /// 当前每帧实例化预算。无样本时返回基准值（编辑器刚启动，按目标帧率假设）。
        /// </summary>
        public int GetBudgetPerFrame()
        {
            if (_recentDeltas.Count == 0) return _basePerFrame;
            var avgMs = AverageDeltaTime * 1000f;
            var budget = (int)Math.Round(_basePerFrame * _targetFrameTimeMs / avgMs, MidpointRounding.AwayFromZero);
            return Math.Clamp(budget, _minPerFrame, _maxPerFrame);
        }

        /// <summary> 清空样本（新一轮构建重新统计帧率） </summary>
        public void Reset()
        {
            _recentDeltas.Clear();
            AverageDeltaTime = 0f;
        }
    }
}
