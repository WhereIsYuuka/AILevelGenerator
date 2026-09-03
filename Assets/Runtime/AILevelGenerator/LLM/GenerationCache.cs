using System.Collections.Generic;

namespace AILevelGenerator.Runtime.LLM
{
    /// <summary>
    /// 内存生成缓存（两级缓存第一级，FIFO 上限 64）：
    /// 键 = 模板 Id + 随机种子 + 用户提示词 + 模板依赖哈希 + Schema 契约版本（FNV-1a 哈希，跨进程稳定）。
    /// - 模板依赖哈希由调用方注入（ITemplateDependencyHashProvider，Editor 资产 = AssetDatabase.GetAssetDependencyHash）：
    ///   模板资产变更 → 数值变化 → 键变化 → 旧条目自动失效（第五周-Day5）；
    /// - schemaVersion = LevelGenerationSchema.SchemaVersion：Schema 结构/语义代码变更时手动 +1 防旧缓存复用；
    /// - 旧三参 API（无哈希/版本）保留为委托入口（等价 0/0），既有调用方与测试零改动。
    /// 缓存 LLM 原始 JSON 输出，命中后重走解析管线（保证与新鲜请求同一条代码路径，结果一致）。
    /// 相同参数重复生成（如策划调整 UI 反复点击）直接秒回，节省 API 调用。
    /// 非线程安全（调度层串行调用）。
    /// </summary>
    public class GenerationCache : IGenerationCache
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

        /// <summary> 组合键哈希（FNV-1a 64 位：不随进程/GC 波动的稳定哈希，可跨域重载一致）——旧三参入口（等价哈希/版本为 0） </summary>
        public static ulong BuildKey(string templateId, int seed, string prompt)
            => BuildKey(templateId, seed, prompt, 0UL, 0);

        /// <summary> 组合键哈希（含模板依赖哈希 + Schema 版本组件；FNV-1a 64 位跨进程稳定） </summary>
        public static ulong BuildKey(string templateId, int seed, string prompt, ulong templateDependencyHash, int schemaVersion)
        {
            return Fnv1a64((templateId ?? string.Empty) + "|" + seed + "|" + (prompt ?? string.Empty) +
                           "|" + templateDependencyHash + "|" + schemaVersion);
        }

        /// <summary> 命中返回缓存原始 JSON，未命中返回 false（旧三参入口，等价哈希/版本为 0） </summary>
        public bool TryGet(string templateId, int seed, string prompt, out string rawJson)
            => TryGet(templateId, seed, prompt, 0UL, 0, out rawJson);

        /// <summary> 命中返回缓存原始 JSON，未命中返回 false（键含模板依赖哈希与 Schema 版本） </summary>
        public bool TryGet(string templateId, int seed, string prompt, ulong templateDependencyHash, int schemaVersion, out string rawJson)
        {
            return _map.TryGetValue(BuildKey(templateId, seed, prompt, templateDependencyHash, schemaVersion), out rawJson);
        }

        /// <summary> 写入缓存：满员时淘汰最旧条目；同键更新不改变顺序（旧三参入口，等价哈希/版本为 0） </summary>
        public void Put(string templateId, int seed, string prompt, string rawJson)
            => Put(templateId, seed, prompt, 0UL, 0, rawJson);

        /// <summary> 写入缓存：满员时淘汰最旧条目；同键更新不改变顺序 </summary>
        public void Put(string templateId, int seed, string prompt, ulong templateDependencyHash, int schemaVersion, string rawJson)
        {
            var key = BuildKey(templateId, seed, prompt, templateDependencyHash, schemaVersion);
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
