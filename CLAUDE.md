# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

Unity 6 项目（URP 17.5.0）：**AI 关卡生成器** —— 通过 AI（MCP + LLM）在 Unity 编辑器中自动生成关卡的工具项目。当前处于**基础生成链路已打通（Day5），LLM 实现在接入前**的阶段：

- 已实现：数据 DTO（`GenerationRequest/Result`、`LevelData`、`TaskData`）、核心接口（`IGenerator`、`IResourceMapper`、`ITemplateProvider`、`IValidator<T>`、`ILogger`）、模板基类（`LevelTemplate`/`TaskTemplate`）、校验基类（`BaseValidator<T>`）、编辑器主窗口（UI Toolkit）、**资源映射系统**（`PrefabMappingConfig` + `ResourceMappingManager`，含精确/别名/模糊匹配，见下"资源映射系统"）、**调度控制层（Day5）**：生成任务状态机（准备/生成中/成功/失败）+ 异步调度框架（`GeneratorScheduler` + `IGeneratorScheduler` + `ServiceLocator`）+ `MockGenerator` 占位生成器 + 窗口→调度→日志链路（状态行显示、生成中按钮禁用）。EditMode 单元测试 33 个全部通过（状态机 8 + 调度器 11 + ServiceLocator 4 + 资源映射 10）。
- 未实现（`Assets/Editor/AILevelGenerator/{Builders,Utils}`、`Assets/Editor/CLI/`、`Assets/Runtime/AILevelGenerator/{Entities,Tasks}`、`Assets/GlobalSettings/*`、`Assets/Settings/{GeneratorConfig,Templates}` 均为空占位）：真实 LLM 调用（**替换 [GeneratorServiceInitializer.cs](Assets/Editor/AILevelGenerator/Core/GeneratorServiceInitializer.cs) 中注册的 `MockGenerator` 即可**）、校验器实现、模板配置资产。

### 程序集（asmdef）结构

- **`AILevelGenerator.Runtime`**（`Assets/Runtime/AILevelGenerator/` 下的 asmdef）：全部运行时模块代码（Data/Interfaces/Validation/Mappings/Scheduling/Utilities）。`Assets/Runtime/PlayerMovement.cs` 在 asmdef 目录之外，仍属 `Assembly-CSharp`。所有新建 Runtime 代码都应放入该程序集。
- **编辑器代码**在预定义程序集 `Assembly-CSharp-Editor`（无 asmdef），自动引用上述程序集。
- **测试程序集** `AILevelGenerator.Tests.EditMode`（`Assets/Tests/EditMode/`，Editor-only asmdef，`UNITY_INCLUDE_TESTS` 约束）：**asmdef 里引用"Assembly-CSharp"不可靠**，必须引用真实程序集名（本项目为 `AILevelGenerator.Runtime`）。无 asmdef 的测试文件不会被 Test Framework 发现（编译进 Assembly-CSharp-Editor 也发现不了）。运行方式：Test Runner（EditMode），或 MCP `run_tests`。

## 工具使用文档（用户约定）

`Docs/ToolGuide.md` 收录所有已完成工具/功能的使用说明（作用、如何创建/打开、字段含义、配置方法、验收方式、注意事项）。**每完成一个工具或功能模块，必须在该文档末尾追加对应章节**，并在文档开头的"当前已收录"列表加链接。

## 关键环境事实（容易踩坑）

- **Input System 为"仅新"模式**（`ProjectSettings.asset` 中 `activeInputHandler: 1`）。旧 API `Input.GetAxisRaw`/`Input.GetKey` 会抛 `InvalidOperationException`，一律使用 `UnityEngine.InputSystem`（如 `Keyboard.current.wKey.isPressed`），见 [PlayerMovement.cs](Assets/Runtime/PlayerMovement.cs)。
- **命名空间与目录拼写统一为 `Runtime`**：命名空间 `AILevelGenerator.Runtime`，目录 `Assets/Runtime/`（历史上曾是 `RunTime`，已全部统一，见 git 历史）。
- 无构建、lint、测试脚本（`com.unity.test-framework` 已装但无测试）。验证方式：在 Unity 编辑器中打开 `Assets/Scenes/ToolScene.unity` 运行（WASD 控制 Player），编辑器窗口通过菜单 **Tools > AI Level Generator** 打开。VS Code 调试用 `vstuc` 扩展的 "Attach to Unity" 配置。

## 代码架构（数据流）

生成链路设计为解耦管道，各环节通过接口衔接（见 `Assets/Runtime/AILevelGenerator/`）：

```
窗口点击生成 → GeneratorScheduler.StartGenerationAsync(request)   // 经 ServiceLocator 获取，窗口不 new 业务类
   → GenerationTaskStateMachine 状态流转：准备 → 生成中 → 成功/失败（非法流转拒绝，新一轮自动重置）
   → IGenerator.GenerateAsync()          // 当前为 MockGenerator 占位，Day6 换真实 LLM
   → GenerationResult                    // LevelData + List<TaskData> + ValidationError/Warning 列表
   → 状态 Success/Failed + 日志输出（状态流转与结果经 ILogger 进窗口日志面板）
   → IValidator<T>.Validate(data, ValidationContext)   // 泛型校验器（尚未接入调度链）
   → IResourceMapper.GetPrefab(logicalName)            // 逻辑名 → 预制体解耦（含模糊匹配）
   → 场景实例化（Builder，尚未实现）
```

**Day5 调度层要点**：状态机/调度器/ServiceLocator/MockGenerator 全部在 `AILevelGenerator.Runtime` 程序集（测试程序集无法引用编辑器预定义程序集，放 Runtime 才能单测）；`[InitializeOnLoad]` 的 [GeneratorServiceInitializer.cs](Assets/Editor/AILevelGenerator/Core/GeneratorServiceInitializer.cs) 负责启动注册；调度器返回的 Task 永不清零（内部全捕获转 Failed），窗口 fire-and-forget；禁止 `.Result/.Wait()`（Editor 同步上下文必死锁）。

- **数据层** [Data/](Assets/Runtime/AILevelGenerator/Data/)：纯 DTO，`[Serializable]`。`PropPlacement.PrefabLogicalName` 是资源映射表的 Key，不直接引用资源。`TerrainData` 与 Unity 的 `TerrainData` 类同名（不同命名空间）——注意不要引用混淆。
- **模板系统**：`ITemplateProvider` 供 `LevelTemplate`/`TaskTemplate`（ScriptableObject，放 Runtime 保证运行时可读），各自有 `ApplyDefaults()` 应用到对应 Data。模板资产目录意图为 `Assets/GlobalSettings/PromptTemplates` 与 `Assets/Settings/Templates`（均为空）。
- **UI 层**：类 `AILevelGeneratorWindow`（文件同名）只做渲染与事件转发，不 new 任何业务类（计划用 ServiceLocator 获取调度器，见 [AILevelGeneratorWindow.cs](Assets/Editor/AILevelGenerator/UI/AILevelGeneratorWindow.cs) 中注释掉的代码）。窗口实现 `ILogger` 接口（显式实现，规避 `UnityEngine.ILogger` 歧义），后续 `BaseValidator.SetLogger(窗口)` 即可把校验日志送进 UI。UXML 加载：**Editor 资产用路径常量 + `AssetDatabase.LoadAssetAtPath` 优先，`[SerializeField] VisualTreeAsset` Inspector 拖拽兜底**（Editor 资产随代码版本管理，跨机器稳定；"禁止硬编码路径"只适用于运行时资源）。日志面板用富文本 + `EditorApplication.delayCall` 延迟刷新，`OnDestroy` 必须注销回调。

### 资源映射系统（Day4）

- **配置资产**：`PrefabMappingConfig`（ScriptableObject，`Assets/Runtime/AILevelGenerator/Mappings/`），`Entries` 列表 = 逻辑名（`PropPlacement.PrefabLogicalName` 的 Key）+ 预制体引用 + `Aliases` 模糊关键字。编辑期校验：逻辑名重复会 `Debug.LogWarning`。资产实例：`Assets/Settings/Mappings/PrefabMapping_Default.asset`（含 敌人-弓箭手/宝箱/NPC 三条）。
- **管理器**：`ResourceMappingManager : IResourceMapper`（纯逻辑类，构造注入配置，`RebuildCache()` 重建精确索引）。查找顺序：逻辑名精确（字典缓存，大小写不敏感）→ 别名精确（500 分）→ 名称/别名双向包含（100/50 分/个，取最高分）。注意语义：**模糊查询实时读配置（不经缓存），精确路径的缓存只影响"条目删除/改名后的陈旧命中"** —— 这是 `RebuildCache` 存在的意义。
- **自定义 Inspector**（`Assets/Editor/AILevelGenerator/Inspectors/PrefabMappingConfigEditor.cs`）：重复逻辑名/无效条目警示 + "匹配测试"工具（输入名称 → 显示命中结果），供策划可视化配置与验收。
- **演示预制体**：`Assets/Prefabs/Demo/`（Enemy_Archer 红胶囊、Chest 黄方块、NPC_Villager 蓝胶囊，基础 primitive + 既有材质）。
- 配置资产**统一放 `Assets/Settings/`** 下按类型分子目录（Mappings/Templates/GeneratorConfig）；`Assets/GlobalSettings/` 空目录与 Settings 职责重叠，待清理。

## Unity MCP 集成

项目通过 MCP 让 Claude 直接操作 Unity 编辑器，链路：`Claude Code ⇄ MCP stdio ⇄ uvx 桥接(mcp-for-unity) ⇄ WebSocket:6400 ⇄ Unity 编辑器内插件`。

- **配置位置**：项目根 `.mcp.json`（`unityMCP` 条目，`uvx --from mcpforunityserver mcp-for-unity --transport stdio`）。
- Unity 端插件 `com.coplaydev.unity-mcp`（git URL 安装，代码在 `Library/PackageCache/com.coplaydev.unity-mcp@...`），需在 Unity 的 Tools 菜单启动 WebSocket 服务，默认端口 6400。
- 已知坑：包内可执行名只有 `mcp-for-unity` 和 `unity-mcp`，`uvx mcpforunityserver` 会失败；`unity-mcp` 是子命令式 CLI，不是 stdio 服务器。`mcp-for-unity` 启动时把 ASCII banner 打到 stderr（无害），stdout 是干净 JSON-RPC。
- 排查：`printf '{"jsonrpc":"2.0",...initialize...}' | uvx --from mcpforunityserver mcp-for-unity` 验证 stdio 服务器；`netstat -ano | grep 6400` 验证 Unity 端监听。
- Claude Code 在**会话启动时**加载 MCP 配置，修改 `.mcp.json` 后需重启会话（或 `/mcp reconnect`）才生效。

## 场景/脚本手工编辑约定

- 场景是纯 YAML，可直接编辑（如 [ToolScene.unity](Assets/Scenes/ToolScene.unity)）。手工添加对象时用内置资源引用，无需额外文件：Plane mesh `{fileID: 10209}`、Capsule `{fileID: 10208}`、默认白材质 `{fileID: 10303}`（guid 均为 `0000000000000000e000000000000000` 系列，材质用 `f000...`）。
- 新建 `.cs` 文件必须同时手写 `.meta`（含自定 32 位 hex guid），场景里 `MonoBehaviour.m_Script` 按该 guid 引用。Unity 自动生成的 guid 与场景引用不一致会导致组件丢失。
- 代码注释与 UI 文案均为中文，遵循既有风格。
