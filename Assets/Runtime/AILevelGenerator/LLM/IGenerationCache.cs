namespace AILevelGenerator.Runtime.LLM
{
    /// <summary>
    /// 生成请求缓存契约（第五周-Day5）：两级缓存（内存 GenerationCache + 磁盘 DiskGenerationCache）的公共查询面，
    /// LLMGenerator 只依赖此接口，命中顺序等组合逻辑由 TwoLevelGenerationCache 封装。
    /// 键 = 模板 Id + 随机种子 + 用户提示词 + 模板依赖哈希 + Schema 契约版本：
    /// - 模板依赖哈希由注入的 ITemplateDependencyHashProvider 计算（Editor 资产 = AssetDatabase.GetAssetDependencyHash，
    ///   模板资产/脚本变更 → 数值变化 → 缓存自动失效；代码模板无资产返回 0）；
    /// - Schema 版本为代码级契约常量（LevelGenerationSchema.SchemaVersion），Schema 结构/语义变更需手动 +1 防旧缓存复用。
    /// 缓存内容为 LLM 原始 JSON 输出：命中后由生成器重走解析 + 模板确定性收尾管线（与新鲜请求同一条代码路径）。
    /// 实现约定：IO/异常一律降级为 miss（不向生成链路抛错）；非线程安全（调度层串行调用）。
    /// </summary>
    public interface IGenerationCache
    {
        /// <summary> 命中返回缓存原始 JSON（out rawJson），未命中返回 false </summary>
        bool TryGet(string templateId, int seed, string prompt, ulong templateDependencyHash, int schemaVersion, out string rawJson);

        /// <summary> 写入缓存（容量满时按实现各自淘汰策略移除最旧条目；同键刷新内容） </summary>
        void Put(string templateId, int seed, string prompt, ulong templateDependencyHash, int schemaVersion, string rawJson);

        /// <summary> 清空缓存 </summary>
        void Clear();
    }
}
