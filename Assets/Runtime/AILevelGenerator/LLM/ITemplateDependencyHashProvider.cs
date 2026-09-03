using AILevelGenerator.Runtime.Interfaces.Templates;

namespace AILevelGenerator.Runtime.LLM
{
    /// <summary>
    /// 模板依赖哈希提供器（第五周-Day5）：为缓存键计算"模板当前状态"哈希，模板变更自动失效。
    /// 实现约定：Runtime 程序集不引用 UnityEditor —— Editor 资产实现（AssetTemplateDependencyHashProvider，
    /// 内部用 AssetDatabase.GetAssetPath + GetAssetDependencyHash）按 TemplateAssetSource 先例整体包 #if UNITY_EDITOR；
    /// 无资产代码模板（CreateInstance）返回 0（无资产可哈希，键退化为不含哈希组件，由 TemplateId 区分）。
    /// </summary>
    public interface ITemplateDependencyHashProvider
    {
        /// <summary> 返回模板的依赖哈希；null / 非资产模板返回 0 </summary>
        ulong GetDependencyHash(LevelTemplate template);
    }
}
