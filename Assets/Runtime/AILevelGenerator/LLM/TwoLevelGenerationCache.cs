namespace AILevelGenerator.Runtime.LLM
{
    /// <summary>
    /// 两级缓存组合器（第五周-Day5）：内存优先 → 磁盘兜底 → 磁盘命中回填内存。
    /// - 内存命中：零 IO 秒回（窗口快速重复生成的主通道）；
    /// - 磁盘命中：跨编辑器重启/域重载的持久通道，命中后回填内存（下次同请求直接内存命中）；
    /// - 双双未命中：返回 false 由生成器调 API，成功后经 Put 同时写两级。
    /// 磁盘层可为 null（退化为纯内存缓存，测试/无盘场景）。
    /// 键语义与两级一致（见 IGenerationCache 注释：模板依赖哈希 + Schema 版本随键参与失效判断）。
    /// </summary>
    public class TwoLevelGenerationCache : IGenerationCache
    {
        private readonly IGenerationCache _memory;
        private readonly IGenerationCache _disk;

        public TwoLevelGenerationCache(IGenerationCache memory, IGenerationCache disk = null)
        {
            _memory = memory ?? new GenerationCache();
            _disk = disk;
        }

        /// <summary> 内存层实例（测试/观测用） </summary>
        public IGenerationCache MemoryCache => _memory;

        /// <summary> 磁盘层实例（未注入磁盘层时为 null） </summary>
        public IGenerationCache DiskCache => _disk;

        public bool TryGet(string templateId, int seed, string prompt, ulong templateDependencyHash, int schemaVersion, out string rawJson)
        {
            if (_memory.TryGet(templateId, seed, prompt, templateDependencyHash, schemaVersion, out rawJson))
                return true; // 内存命中：主通道

            if (_disk == null) return false;
            if (!_disk.TryGet(templateId, seed, prompt, templateDependencyHash, schemaVersion, out rawJson))
                return false;

            _memory.Put(templateId, seed, prompt, templateDependencyHash, schemaVersion, rawJson); // 磁盘命中回填内存
            return true;
        }

        public void Put(string templateId, int seed, string prompt, ulong templateDependencyHash, int schemaVersion, string rawJson)
        {
            _memory.Put(templateId, seed, prompt, templateDependencyHash, schemaVersion, rawJson);
            _disk?.Put(templateId, seed, prompt, templateDependencyHash, schemaVersion, rawJson);
        }

        public void Clear()
        {
            _memory.Clear();
            _disk?.Clear();
        }
    }
}
