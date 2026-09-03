namespace AILevelGenerator.Runtime.Interfaces.Templates
{
    /// <summary>
    /// 模板加载源（第五周-Day4，依赖倒置点）：TemplateManager 只依赖本接口执行整体重载，
    /// 不关心模板来自资产扫描还是程序构造。Editor 资产源（TemplateAssetSource）实现于
    /// #if UNITY_EDITOR 编译内，Runtime 纯逻辑程序集不引用 UnityEditor 类型。
    /// </summary>
    public interface ITemplateSource
    {
        /// <summary>
        /// 加载全部三类模板（一次事务性快照）。实现方须自行容错：目录缺失/单个资产损坏
        /// 应跳过并警告，不得抛异常；返回 null 或含空列表均视为"无模板"。
        /// </summary>
        TemplateCollection Load();
    }
}
