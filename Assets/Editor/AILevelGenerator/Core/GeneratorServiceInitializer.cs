using AILevelGenerator.Editor.UI;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.LLM;
using AILevelGenerator.Runtime.Mappings;
using AILevelGenerator.Runtime.Scheduling;
using AILevelGenerator.Runtime.Templates;
using AILevelGenerator.Runtime.Utilities;
using UnityEditor;

namespace AILevelGenerator.Editor.Core
{
    /// <summary>
    /// 编辑器启动时把核心服务注册进 ServiceLocator（域加载时执行，必然早于窗口 CreateGUI）。
    /// 注册：真实 LLM 生成器（LLMGenerator → DeepSeekClient，Key 经 EditorPrefs 注入）、调度器、
    /// DeepSeek 客户端（供窗口「测试连接」复用同一实例）、模板提供者、资源映射（Prompt 资源清单数据源）。
    /// MockGenerator 类保留（测试/占位用）但不再注册。
    /// </summary>
    [InitializeOnLoad]
    public static class GeneratorServiceInitializer
    {
        static GeneratorServiceInitializer()
        {
            // 模板体系：扫描 Assets/Settings/ 加载全部关卡/任务/Prompt 模板资产
            ServiceLocator.Register<ITemplateProvider>(TemplateProvider.LoadFromAssets());

            // 资源映射：读取默认映射配置（缺失时提示但不等同于注册失败，窗口生成链路不受影响）
            var mappingConfig = AssetDatabase.LoadAssetAtPath<PrefabMappingConfig>("Assets/Settings/Mappings/PrefabMapping_Default.asset");
            if (mappingConfig == null)
            {
                UnityEngine.Debug.LogWarning("[AI Generator] 未找到资源映射配置 Assets/Settings/Mappings/PrefabMapping_Default.asset，Prompt 资源清单将为空");
            }
            else
            {
                ServiceLocator.Register<IResourceMapper>(new ResourceMappingManager(mappingConfig));
            }

            // DeepSeek 客户端（注册供窗口「测试连接」复用；Key 从 EditorPrefs 读取，绝不落盘/进代码）
            var client = new DeepSeekClient(DeepSeekApiKeySettings.GetApiKey());

            // 真实 LLM 生成器：替换 MockGenerator 占位（Key 动态读取，窗口保存新 Key 后无需重载域）
            var generator = new LLMGenerator(
                client,
                () => DeepSeekApiKeySettings.GetApiKey(), // keyProvider：每次生成实时读 EditorPrefs
                ServiceLocator.Get<ITemplateProvider>(),
                ServiceLocator.Get<IResourceMapper>());

            ServiceLocator.Register<IDeepSeekClient>(client);
            ServiceLocator.Register<IGenerator>(generator);
            ServiceLocator.Register<IGeneratorScheduler>(new GeneratorScheduler(generator));
        }
    }
}
