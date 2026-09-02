using System;
using AILevelGenerator.Editor.Builders;
using AILevelGenerator.Editor.Tools;
using AILevelGenerator.Editor.UI;
using AILevelGenerator.Runtime.Components;
using AILevelGenerator.Runtime.Diagnostics;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Interfaces.Templates;
using AILevelGenerator.Runtime.LLM;
using AILevelGenerator.Runtime.Mappings;
using AILevelGenerator.Runtime.Scheduling;
using AILevelGenerator.Runtime.Templates;
using AILevelGenerator.Runtime.Utilities;
using AILevelGenerator.Runtime.Validation;
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

            // 回滚管理器（Day3）：登记生成根物体，取消/失败时经其分帧增量删除（实现体第四周扩展快照兜底）
            var rollbackManager = new RollbackManager();
            ServiceLocator.Register<IRollbackManager>(rollbackManager);

            // 场景级快照（第四周-Day1）：生成前保存场景副本，全量回滚时 OpenScene 原子还原。
            // 与 IRollbackManager 互为两级回滚体系：增量 = 物体级分帧删除；全量 = 场景文件级原子还原。
            ServiceLocator.Register<ISceneSnapshotManager>(SceneSnapshotManager.Instance);

            // 组件绑定器（Day4）：加载默认绑定配置（缺失时仅警告——未配置绑定时构建不挂组件，链路不受影响）
            var bindingConfig = AssetDatabase.LoadAssetAtPath<ComponentBindingConfig>("Assets/Settings/Bindings/ComponentBinding_Default.asset");
            if (bindingConfig == null)
            {
                UnityEngine.Debug.LogWarning("[AI Generator] 未找到组件绑定配置 Assets/Settings/Bindings/ComponentBinding_Default.asset，实体将不自动挂载逻辑组件");
            }
            var componentBinder = new ComponentBinder(bindingConfig);

            // 环境自动适配（Day5）：收尾同步烘焙全局 NavMesh（全场景收集 → BuildNavMeshData → 注册数据）
            var navMeshBaker = new NavMeshBaker();

            // 校验体系（第四周-Day2/3）：注册表先行——构建器（Mid）与调度器（Pre/Post）共享同一实例，
            // 阶段过滤互不干扰（核心只做调度，校验规则全部在具体校验器内，开闭原则）。
            // Pre 前置校验：输入合法性/资源存在性/数值边界/模板范围（模板专属：遍历模板资产逐个注册，跳过空 TemplateId）；
            // Mid 生成中校验：与 Pre 同校验器（DataBounds/Resource），每帧批次兜底数据在 Pre 后被污染/默认值应用的差异面；
            // Post 后置校验：实体空引用/组件完整性/逻辑可达性（可达性开启 = 真实场景验收项），失败自动全量回滚。
            var validatorRegistry = new ValidatorRegistry();
            validatorRegistry.SetServices(ServiceLocator.Get<IResourceMapper>(), ServiceLocator.Get<ITemplateProvider>());
            validatorRegistry.Register(ValidationStage.Pre, new RequestValidator());
            validatorRegistry.Register(ValidationStage.Pre, new ResourceValidator());
            validatorRegistry.Register(ValidationStage.Pre, new DataBoundsValidator());
            foreach (var t in ServiceLocator.Get<ITemplateProvider>()?.GetLevelTemplates() ?? Array.Empty<LevelTemplate>())
                if (t is ConfigurableLevelTemplate config && !string.IsNullOrEmpty(config.TemplateId))
                    validatorRegistry.RegisterForTemplate(config.TemplateId, new TemplateScopeValidator(config));
            validatorRegistry.Register(ValidationStage.Mid, new DataBoundsValidator());
            validatorRegistry.Register(ValidationStage.Mid, new ResourceValidator());
            validatorRegistry.Register(ValidationStage.Post, new PostBuildValidator(bindingConfig));

            // 场景构建器：生成成功后分帧把 LevelData 实例化到场景（依赖资源映射；映射缺失时构建跳过全部 Props）。
            // 注入回滚管理器：取消/失败时经其分帧删除本次生成根，不阻塞编辑器；
            // 注入组件绑定器：实例化后自动挂载逻辑组件（按逻辑名查绑定配置）；
            // 注入 NavMesh 烘焙器：构建收尾同步烘焙全局 NavMesh（「烘焙中」提示 + 完成日志 + 场景状态同步）；
            // 注入校验注册表：每帧批次后跑 Mid 生成中校验（失败立即终止构建 + 两级回滚兜底）。
            ServiceLocator.Register<ILevelBuilder>(new SceneLevelBuilder(ServiceLocator.Get<IResourceMapper>(), rollbackManager, componentBinder, navMeshBaker, validatorRegistry));

            // 调度器：注入构建器后，生成成功会自动进入分帧构建阶段（构建完成才算整条任务成功）；
            // 注入校验注册表（前置校验拦截 + 后置校验回滚）+ 场景快照（校验失败清理 / 构建失败自动全量回滚）
            var scheduler = new GeneratorScheduler(generator, validatorRegistry);
            scheduler.SetBuilder(ServiceLocator.Get<ILevelBuilder>());
            scheduler.SetSnapshotManager(SceneSnapshotManager.Instance);
            ServiceLocator.Register<IGeneratorScheduler>(scheduler);

            // 第四周-Day5：生成报告自动归档（Markdown 落盘 Assets/Temp/GenerateReports/，已被 gitignore）。
            // 初始器订阅 = 无窗口打开/headless 运行也归档；窗口另订阅同一事件渲染报告块（互不依赖）。
            scheduler.GenerationCompleted += report =>
            {
                var path = GenerationReportWriter.Write(report);
                UnityEngine.Debug.Log($"[AI Generator] 生成报告已归档：{path ?? "（落盘失败，详见警告日志）"}");
            };
        }
    }
}
