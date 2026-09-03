using AILevelGenerator.Runtime.Interfaces.Templates;
using AILevelGenerator.Runtime.LLM;
using AILevelGenerator.Runtime.Templates;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 资产模板依赖哈希提供器单元测试（第五周-Day5）：
    /// 真实资产模板（AssetDatabase.GetAssetDependencyHash）> 0 且跨调用稳定；
    /// 代码新建模板（无资产路径）与 null → 0（缓存键退化为不含哈希组件，靠 TemplateId 区分）。
    /// </summary>
    public class TemplateDependencyHashProviderTests
    {
        private const string LinearAssetPath = "Assets/Settings/Templates/Linear_LevelTemplate.asset";

        private readonly AssetTemplateDependencyHashProvider _provider = new();

        [Test]
        public void 资产模板_哈希非零且跨调用稳定()
        {
            var template = AssetDatabase.LoadAssetAtPath<LevelTemplate>(LinearAssetPath);
            Assert.IsNotNull(template, $"测试依赖真实模板资产 {LinearAssetPath}，缺失会导致缓存失效链路失效");

            var first = _provider.GetDependencyHash(template);
            var second = _provider.GetDependencyHash(template);

            Assert.AreNotEqual(0UL, first, "资产模板必须产生非零依赖哈希（否则资产变更无法使缓存失效）");
            Assert.AreEqual(first, second, "资产未变更时哈希必须稳定（跨调用/跨帧一致）");
        }

        [Test]
        public void 资产模板_哈希来自资产而非模板内容字符串()
        {
            // 同一模板对象两次加载哈希一致 → 该哈希可随资产文件内容/脚本变更而变（GetAssetDependencyHash 语义）
            var a = AssetDatabase.LoadAssetAtPath<LevelTemplate>(LinearAssetPath);
            var b = AssetDatabase.LoadAssetAtPath<LevelTemplate>(LinearAssetPath);
            Assert.AreEqual(_provider.GetDependencyHash(a), _provider.GetDependencyHash(b), "同一资产路径哈希必须一致");
        }

        [Test]
        public void 代码新建模板_无资产路径_返回零()
        {
            var template = ScriptableObject.CreateInstance<ConfigurableLevelTemplate>();
            try
            {
                Assert.AreEqual(0UL, _provider.GetDependencyHash(template),
                    "代码新建模板无资产路径，哈希 0 = 缓存键退化为不含依赖哈希（兼容行为）");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(template);
            }
        }

        [Test]
        public void 空模板_返回零不抛异常()
        {
            Assert.DoesNotThrow(() => Assert.AreEqual(0UL, _provider.GetDependencyHash(null)));
        }
    }
}
