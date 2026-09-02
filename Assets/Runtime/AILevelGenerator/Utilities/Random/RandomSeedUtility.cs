namespace AILevelGenerator.Runtime.Utilities
{
    /// <summary>
    /// 种子派生工具（第五周-Day1）：模板/任务/随机流的种子统一由此派生。
    ///
    /// 设计动机：
    /// - 全局只允许一个「根种子」（GenerationRequest.RandomSeed），各随机流用
    ///   (根种子 + 稳定盐) 派生独立子种子，互不串扰；
    /// - Derive 用 SplitMix32 终末化：盐差 1 也产生明显不同的子种子（Day1 验收标准 2）；
    /// - StableHash 用 FNV-1a 自实现（string.GetHashCode 跨运行时不稳定，禁止使用）；
    /// - 确定性契约：同输入 → 同输出，跨平台、跨 .NET 运行时不变。
    /// </summary>
    public static class RandomSeedUtility
    {
        /// <summary> 稳定字符串哈希（FNV-1a 32 位，返回 int 便于与其他种子混合） </summary>
        public static int StableHash(string text)
        {
            const uint offset = 2166136261u;
            const uint prime = 16777619u;
            var hash = offset;
            if (text != null)
                foreach (var c in text)
                {
                    hash ^= (uint)(c & 0xFFFF);
                    hash *= prime;
                    hash ^= (uint)(c >> 16);
                    hash *= prime;
                }
            return (int)hash;
        }

        /// <summary> 由根种子与盐派生确定性子种子（SplitMix32 终末化打散） </summary>
        public static int Derive(int baseSeed, int salt)
        {
            // 与 SplitMix64 同一家族：加常数 → 混淆 → 终末化，保证相邻输入输出差异大
            var z = (uint)baseSeed + (uint)salt + 0x9E3779B9u;
            z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
            z = (z ^ (z >> 13)) * 0xC2B2AE35u;
            z ^= z >> 16;
            return (int)z;
        }
    }
}
