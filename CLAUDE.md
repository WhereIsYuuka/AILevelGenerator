# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

Unity 6 项目（URP 17.5.0）：**AI 关卡生成器** —— 通过 AI（MCP + LLM）在 Unity 编辑器中自动生成关卡的工具项目。当前处于骨架搭建阶段：目录结构已建好，绝大部分尚未实现（唯一实际脚本是 `Assets/RunTime/PlayerMovement.cs`）。

## 关键环境事实（容易踩坑）

- **Input System 为"仅新"模式**（`ProjectSettings.asset` 中 `activeInputHandler: 1`）。旧 API `Input.GetAxisRaw` / `Input.GetKey` 会抛 `InvalidOperationException`，一律使用 `UnityEngine.InputSystem`（如 `Keyboard.current.wKey.isPressed`）。
- 项目使用 URP，场景内已有 Main Camera / Directional Light / Global Volume，渲染配置在 `Assets/Settings/`（PC_RPAsset / Mobile_RPAsset）。
- 无构建、lint、测试脚本；验证方式是在 Unity 编辑器中打开 `Assets/Scenes/ToolScene.unity` 运行。`com.unity.test-framework` 已装但无测试。

## 目录架构

```
Assets/
├── RunTime/          # 运行时代码（骨架：Common/、Entities/、Tasks/ 子目录为空）
│   └── PlayerMovement.cs   # WASD 角色控制器（新 Input System + CharacterController）
├── Editor/
│   ├── AILevelGenerator/   # 编辑器生成工具（骨架：Core/、Templates/、UI/、Utils/ 为空）
│   └── CLI/                # 命令行工具入口（空）
├── GlobalSettings/         # 生成配置（骨架：PrefabMapping/、PromptTemplates/、ValidatorRules/ 为空）
│                            # 意图：AI prompt 模板、预制体映射、结果校验规则
├── Prefabs/  Resources/  Materials/  # 资源（Materials 已有 Blue/Red/Yellow 基础材质）
├── Settings/               # URP 渲染管线资产
└── Scenes/ToolScene.unity  # 主场景：Plane 地面 + Player（胶囊体，moveSpeed=5）
```

## Unity MCP 集成（重要）

项目通过 MCP 让 Claude 直接操作 Unity 编辑器，链路为：

```
Claude Code ⇄ MCP stdio ⇄ uvx 桥接(mcp-for-unity) ⇄ WebSocket:6400 ⇄ Unity 编辑器内插件
```

- **Unity 端**：`com.coplaydev.unity-mcp`（"MCP For Unity" 插件，git URL 安装，代码在 `Library/PackageCache/com.coplaydev.unity-mcp@...`），在 Unity 的 Tools 菜单中启动 WebSocket 服务，默认端口 **6400**（见 `PortManager.cs`）。
- **Claude Code 端**：服务器 `UnityMCP` 配置在 **`~/.claude.json` 的项目级条目**中（注意：项目根 `.mcp.json` 是空的 `{"mcpServers": {}}`，是历史遗留，别被误导）。
- **正确启动命令**：`uvx --from mcpforunityserver mcp-for-unity`。三个已知坑：
  1. 直接 `uvx mcpforunityserver` 会失败——包内没有该可执行名，只有 `mcp-for-unity` 和 `unity-mcp`。
  2. `unity-mcp` 是子命令式 CLI（`status`/`gameobject` 等），不是 stdio 服务器；`mcp-for-unity` 才是。
  3. `mcp-for-unity` 启动时把 ASCII banner 打印到 stderr（无害），stdout 是干净的 JSON-RPC。
- 排查时可用 `printf '{"jsonrpc":"2.0",...initialize...}' | uvx --from mcpforunityserver mcp-for-unity` 验证 stdio 服务器，用 `netstat -ano | grep 6400` 验证 Unity 端是否监听。
- 注意：Claude Code 在**会话启动时**加载 MCP 配置，修改后需重启会话（或 `/mcp reconnect`）才生效。

## 场景/脚本手工编辑约定

- 场景是纯 YAML，可直接编辑（如 [ToolScene.unity](Assets/Scenes/ToolScene.unity)）。手工添加对象时用内置资源引用，无需额外文件：Plane mesh `{fileID: 10209}`、Capsule `{fileID: 10208}`、默认白材质 `{fileID: 10303}`（guid 均为 `0000000000000000e000000000000000` 系列，材质用 `f000...`）。
- 新建 `.cs` 文件必须同时手写 `.meta`（含自定 32 位 hex guid），场景里 `MonoBehaviour.m_Script` 按该 guid 引用。Unity 在脚本导入时生成的 guid 与场景引用不一致会导致组件丢失。
- 命名空间统一用 `AILevelGenerator.RunTime`（运行时）。
