using System;

namespace AILevelGenerator.Runtime.Prompting
{
    /// <summary>
    /// Prompt Token 启发式估算器（第五周-Day5）：为 Prompt 精简提供无网络回归门禁。
    /// 规则：CJK 区字符（U+2E80 起，含中文标点/全角）≈ 1 字 1 Token；其余 ASCII 类 ≈ 4 字符 1 Token（向上取整）。
    /// 依据：DeepSeek/openai 系 BPE 对中文≈1字/tok、紧凑 JSON 与英文≈3~4 字符/tok 的工程近似，**非精确分词**。
    /// 用途边界：只用于"精简前后对比"的确定性断言（两端同口径，系统性偏差抵消）；
    /// 需求验收口径（Token 消耗↓20%）以真实 API usage.prompt_tokens 3 次均值对比为准（见 PromptBenchmarkRunner）。
    /// </summary>
    public static class PromptTokenEstimator
    {
        /// <summary> 估算文本 Token 数（空文本返回 0） </summary>
        public static int Estimate(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            var cjk = 0;
            var ascii = 0;
            foreach (var c in text)
            {
                if (c >= 0x2E80) cjk++; // 中文/日韩/全角/扩展区近似归类（含中文标点）
                else ascii++;
            }
            return cjk + (ascii + 3) / 4;
        }
    }
}
