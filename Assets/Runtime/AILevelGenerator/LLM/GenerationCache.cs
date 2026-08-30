using System.Collections.Generic;

namespace AILevelGenerator.Runtime.LLM
{
    /// <summary>
    /// 生成请求缓存（FIFO，上限 64）：键 = 模板 Id + 随机种子 + 用户提示词（FNV-1a 哈希，跨进程稳定）。
    /// 缓存 LLM 原始 JSON 输出，命中后重走解析管线（保证与新鲜请求同一条代码路径，结果一致）。
    /// 相同参数重复生成（如策划调整 UI 反复点击）直接秒回，节省 API 调用。
    /// </summary>
    public class GenerationCache
    {
        public const int DefaultCapacity = 64;

        private readonly int _capacity;
        private readonly Dictionary<ulong, string> _map = new();
        private readonly Queue<ulong> _order = new(); // FIFO 淘汰顺序

        public int Capacity => _capacity;
        public int Count => _map.Count;

        public GenerationCache(int capacity = DefaultCapacity)
        {
            _capacity = capacity < 1 ? 1 : capacity;
        }

        /// <summary> 组合键哈希（FNV-1a 64 位：不随进程/GC 波动的稳定哈希，可跨域重载一致） </summary>
        public static ulong BuildKey(string templateId, int seed, string prompt)
        {
            return Fnv1a64((templateId ?? string.Empty) + "|" + seed + "|" + (prompt ?? string.Empty));
        }

        /// <summary> 命中返回缓存原始 JSON，未命中返回 false </summary>
        public bool TryGet(string templateId, int seed, string prompt, out string rawJson)
        {
            return _map.TryGetValue(BuildKey(templateId, seed, prompt), out rawJson);
        }

        /// <summary> 写入缓存：满员时淘汰最旧条目；同键更新不改变顺序 </summary>
        public void Put(string templateId, int seed, string prompt, string rawJson)
        {
            var key = BuildKey(templateId, seed, prompt);
            if (_map.ContainsKey(key))
            {
                _map[key] = rawJson; // 同键刷新内容
                return;
            }
            if (_map.Count >= _capacity)
            {
                var oldest = _order.Dequeue();
                _map.Remove(oldest);
            }
            _map[key] = rawJson;
            _order.Enqueue(key);
        }

        public void Clear()
        {
            _map.Clear();
            _order.Clear();
        }

        /// <summary> FNV-1a 64 位哈希（确定性，不依赖 .NET 的随机化字符串哈希） </summary>
        private static ulong Fnv1a64(string text)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            foreach (var c in text)
            {
                hash ^= c;
                hash *= prime;
            }
            return hash;
        }
    }
}
