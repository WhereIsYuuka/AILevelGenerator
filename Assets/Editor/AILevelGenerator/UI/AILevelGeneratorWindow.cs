using System;
using System.Collections.Generic;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Interfaces.Templates;
using AILevelGenerator.Runtime.LLM;
using AILevelGenerator.Runtime.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
// 别名 using：避免与 UnityEngine.ILogger 歧义（类声明与显式实现保持全限定名）
using IGeneratorScheduler = AILevelGenerator.Runtime.Interfaces.IGeneratorScheduler;
using GenerationTaskState = AILevelGenerator.Runtime.Scheduling.GenerationTaskState;

namespace AILevelGenerator.Editor.UI
{
    /// <summary>
    /// AI关卡生成工具主窗口
    /// 职责：仅负责 UI 渲染与事件转发，不包含任何业务逻辑
    /// 实现 ILogger：窗口即日志宿主，供校验器/生成器通过 SetLogger 注入
    /// </summary>
    public class AILevelGeneratorWindow : EditorWindow, AILevelGenerator.Runtime.Interfaces.ILogger
    {
        /// <summary> Editor 资源路径常量：UXML 随代码版本管理，跨机器稳定；运行时资源才禁止硬编码路径 </summary>
        private const string UxmlAssetPath = "Assets/Editor/AILevelGenerator/UI/LevelGeneratorWindow.uxml";

        [SerializeField] private VisualTreeAsset _uxmlAsset; // Inspector 拖拽兜底（窗口布局持久化时生效）

        private DropdownField _templateDropdown;
        private IntegerField _seedField;
        private TextField _inputField;
        private Button _generateBtn;
        private Button _cancelBtn;
        private ProgressBar _progressBar;
        private Button _clearLogBtn;
        private ScrollView _logScroll;
        private Label _logContent;
        private Label _statusLabel;
        private TextField _apiKeyField;
        private Button _saveKeyBtn;
        private Button _testConnectionBtn;
        private Label _apiStatusLabel;
        private Toggle _autoRunToggle; // Day6 联调：生成成功后自动进入播放模式（勾选后开启）
        private Button _rollbackBtn;   // 第四周-Day1：回滚到生成前快照（场景级）

        /// <summary> 模板下拉选项缓存：choices（DisplayName）与模板对象一一对应，index 即模板下标 </summary>
        private readonly List<LevelTemplate> _templateOptions = new();

        // 调度链路（经 ServiceLocator 获取，窗口不 new 任何具体业务类）
        private IGeneratorScheduler _scheduler;
        private ILevelBuilder _levelBuilder; // Day3：订阅 ProgressChanged 驱动进度条实时刷新
        private GenerationTaskState _previousState = GenerationTaskState.Ready; // Day6：自动运行判别用（生成中→成功才触发）
        // 注：状态/进度订阅不设防重标志——CreateGUI 会多次调用，但 C# 事件对同一方法组委托
        // 自动去重；且若 ServiceLocator 实例被覆盖注册，旧订阅会失效，每次 CreateGUI 须对当前实例重订阅。
        private readonly List<string> _logEntries = new List<string>(); // 内存缓存，避免字符串频繁拼接
        private const int MaxLogEntries = 500; // 防止内存泄漏

        [MenuItem("Tools/AI Level Generator")]
        public static void ShowWindow()
        {
            var window = GetWindow<AILevelGeneratorWindow>("AI关卡生成工具");
            window.minSize = new Vector2(520, 600);
        }

        private void CreateGUI()
        {
            // 1. 加载 UXML：路径常量优先（Editor 资产随代码走），Inspector 拖拽绑定作为兜底
            var uxml = _uxmlAsset != null ? _uxmlAsset : AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlAssetPath);
            if (uxml == null)
            {
                Debug.LogError($"[AI Generator] UXML 资源未绑定且路径加载失败：{UxmlAssetPath}");
                return;
            }
            rootVisualElement.Add(uxml.Instantiate());

            // 2. 绑定控件
            BindControls();
            
            // 3. 初始化下拉
            InitTemplateOptions();
            
            // 4. 注册事件
            RegisterEvents();

            // 5. 接线调度器：ServiceLocator 获取 → 注入日志宿主 → 订阅状态 → 初始渲染
            InitScheduler();

            // 6. 渲染已有日志（窗口重绘时保留）
            RenderLogs();

            // 7. 回滚按钮状态与快照存在性联动（无快照时禁用）
            RefreshRollbackButton();

            Log("工具初始化完成");
        }

        private void BindControls()
        {
            _templateDropdown = rootVisualElement.Q<DropdownField>("template-dropdown");
            _seedField = rootVisualElement.Q<IntegerField>("seed-field");
            _inputField = rootVisualElement.Q<TextField>("input-field");
            _generateBtn = rootVisualElement.Q<Button>("generate-button");
            _cancelBtn = rootVisualElement.Q<Button>("cancel-button");
            _progressBar = rootVisualElement.Q<ProgressBar>("progress-bar");
            _clearLogBtn = rootVisualElement.Q<Button>("clear-log-button");
            _logScroll = rootVisualElement.Q<ScrollView>("log-scroll");
            _logContent = rootVisualElement.Q<Label>("log-content");
            _statusLabel = rootVisualElement.Q<Label>("status-label");
            _apiKeyField = rootVisualElement.Q<TextField>("api-key-field");
            _saveKeyBtn = rootVisualElement.Q<Button>("save-key-button");
            _testConnectionBtn = rootVisualElement.Q<Button>("test-connection-button");
            _apiStatusLabel = rootVisualElement.Q<Label>("api-status-label");
            _autoRunToggle = rootVisualElement.Q<Toggle>("auto-run-toggle");
            _rollbackBtn = rootVisualElement.Q<Button>("rollback-button");

            // 开启富文本支持，用于显示彩色日志
            _logContent.enableRichText = true;

            // API Key 回填（密码框掩码显示，用户可覆盖后重新保存）
            if (_apiKeyField != null)
                _apiKeyField.value = DeepSeekApiKeySettings.GetApiKey();
        }

        /// <summary>
        /// 初始化模板下拉：从 ITemplateProvider 动态加载关卡模板资产（策划新增资产即出现在下拉，无需改代码）。
        /// 下拉显示 DisplayName，实际选中的 TemplateId 由 OnGenerateClicked 按 index 从缓存映射。
        /// </summary>
        private void InitTemplateOptions()
        {
            _templateOptions.Clear();
            _templateDropdown.choices.Clear();

            var provider = ServiceLocator.Get<ITemplateProvider>();
            if (provider == null)
            {
                LogError("模板提供者未注册（ServiceLocator），请检查 GeneratorServiceInitializer");
                _generateBtn?.SetEnabled(false);
                return;
            }

            foreach (var template in provider.GetLevelTemplates())
            {
                if (template == null) continue;
                _templateOptions.Add(template);
                _templateDropdown.choices.Add(string.IsNullOrEmpty(template.DisplayName) ? template.TemplateId : template.DisplayName);
            }

            if (_templateOptions.Count == 0)
            {
                LogError("未加载到任何关卡模板资产，请检查 Assets/Settings/Templates/ 目录");
                _generateBtn?.SetEnabled(false);
                return;
            }
            _templateDropdown.index = 0; // 设置 choices 后必须显式赋值才有初值
        }

        private void RegisterEvents()
        {
            _generateBtn.clicked += OnGenerateClicked;
            if (_cancelBtn != null) _cancelBtn.clicked += OnCancelClicked;
            _clearLogBtn.clicked += OnClearLogClicked;
            if (_saveKeyBtn != null) _saveKeyBtn.clicked += OnSaveApiKeyClicked;
            if (_testConnectionBtn != null) _testConnectionBtn.clicked += OnTestConnectionClicked;
            if (_rollbackBtn != null) _rollbackBtn.clicked += OnRollbackClicked;
        }

        /// <summary> 取消生成：转发调度器（构建阶段分帧清理本次物体，生成阶段丢弃结果） </summary>
        private void OnCancelClicked()
        {
            LogWarning("用户点击取消生成");
            _scheduler?.CancelGeneration();
        }

        /// <summary> 保存 API Key：写 EditorPrefs + 即时更新已注册客户端（无需重载域） </summary>
        private void OnSaveApiKeyClicked()
        {
            var key = _apiKeyField?.value?.Trim() ?? string.Empty;
            DeepSeekApiKeySettings.SaveApiKey(key);

            var client = ServiceLocator.Get<IDeepSeekClient>() as DeepSeekClient;
            client?.UpdateApiKey(key);

            Log(string.IsNullOrEmpty(key) ? "已清除 API Key" : "API Key 已保存（仅存本机编辑器偏好，不进入项目文件）");
            SetApiStatus(string.IsNullOrEmpty(key) ? "" : "已保存", false);
        }

        /// <summary> 测试连接：最小请求调用 DeepSeek，验证 Key 有效性与网络可达 </summary>
        private async void OnTestConnectionClicked()
        {
            var client = ServiceLocator.Get<IDeepSeekClient>();
            if (client == null)
            {
                LogError("DeepSeek 客户端未注册（ServiceLocator），请检查 GeneratorServiceInitializer");
                return;
            }

            var key = _apiKeyField?.value?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                LogError("请先输入并保存 API Key 再测试连接");
                return;
            }
            // 测试前同步到客户端（覆盖未保存直接点测试的场景）
            (client as DeepSeekClient)?.UpdateApiKey(key);

            _testConnectionBtn?.SetEnabled(false);
            SetApiStatus("连接中...", false);
            Log($"正在测试连接：{DeepSeekClient.DefaultBaseUrl}");

            try
            {
                var response = await client.ChatAsync(new DeepSeekChatRequest
                {
                    Messages = new List<AILevelGenerator.Runtime.LLM.DeepSeekMessage>
                    {
                        new() { Role = "user", Content = "ping" }
                    }
                });
                var ok = response != null && response.Choices != null && response.Choices.Count > 0;
                LogSuccess(ok ? "连接成功：DeepSeek API 可正常访问" : "连接成功（返回空响应）");
                SetApiStatus(ok ? "连接成功" : "返回空响应", true);
            }
            catch (DeepSeekException e)
            {
                LogError($"连接失败：{e.FriendlyMessage}");
                SetApiStatus("连接失败", false);
            }
            catch (Exception e)
            {
                LogError($"连接失败：{e.Message}");
                SetApiStatus("连接失败", false);
            }
            finally
            {
                _testConnectionBtn?.SetEnabled(true);
            }
        }

        /// <summary> API 状态标签：true=绿色（成功），false=红色（失败） </summary>
        private void SetApiStatus(string text, bool success)
        {
            if (_apiStatusLabel == null) return;
            _apiStatusLabel.text = text;
            _apiStatusLabel.style.color = success ? new Color(0.25f, 0.8f, 0.25f) : new Color(1f, 0.32f, 0.32f);
        }

        /// <summary>
        /// 生成按钮点击 - 只做参数收集与调度转发
        /// </summary>
        private void OnGenerateClicked()
        {
            // 下拉 value 是 DisplayName 字符串，不能直接当 TemplateId；
            // 优先按 index 从缓存取模板，index 越界时按 DisplayName 反查兜底（防外部修改 choices 失同步）
            var selected = _templateDropdown.index >= 0 && _templateDropdown.index < _templateOptions.Count
                ? _templateOptions[_templateDropdown.index]
                : _templateOptions.Find(t => t.DisplayName == _templateDropdown.value);
            var templateId = selected?.TemplateId ?? "未指定";
            var input = _inputField.value;
            var seed = _seedField.value;

            if (string.IsNullOrWhiteSpace(input))
            {
                LogError("请输入关卡描述");
                return;
            }

            // Day6 边界：播放模式中禁止生成——退出播放会重置运行时场景数据（编辑期实例被丢弃），
            // 且运行时 NavMesh 与编辑器数据边界不清。生成只发生在编辑期。
            if (EditorApplication.isPlaying)
            {
                LogError("播放模式中禁止生成关卡，请先停止播放（停止按钮 / Esc）");
                return;
            }

            // 生成前置检查：未配置 Key 直接提示（LLMGenerator 也有 NO_API_KEY 兜底，这里提前拦截体验更好）
            if (string.IsNullOrWhiteSpace(DeepSeekApiKeySettings.GetApiKey()))
            {
                LogError("未配置 DeepSeek API Key，请在「API 设置」中保存后重试");
                return;
            }

            Log($"开始生成 - 模板：{selected?.DisplayName ?? "未知"}（{templateId}），种子：{seed}");
            Log($"描述：{input}");

            if (_scheduler == null)
            {
                LogError("调度器未注册，无法生成");
                return;
            }

            // 第四周-Day4：快照创建已移交调度器（前置校验通过后创建，失败仅警告降级为增量回滚）——
            // 窗口不再拥有快照生命周期，回滚按钮状态由调度器状态变更事件链驱动刷新（Ready→Generating 时快照已就绪）。
            // fire-and-forget：调度器内部捕获全部异常并转为 Failed 状态，返回的 Task 永不清零，可安全丢弃
            _ = _scheduler.StartGenerationAsync(new GenerationRequest
            {
                Prompt = input,
                TemplateId = templateId,
                RandomSeed = seed
            });
        }

        /// <summary>
        /// 回滚按钮点击（第四周-Day1）：全量回滚到生成前快照。
        /// 前置校验：快照存在 / 非播放模式（管理器内部） / 非生成中（生成协程未结束前禁止换场景）。
        /// 成功路径：管理器完成场景还原（OpenScene + 回写 + 重烘焙 + 删临时文件）后，
        /// 复位状态机（事件链驱动状态行/进度条/按钮复位）+ 清日志 + 追加回滚提示。
        /// </summary>
        private void OnRollbackClicked()
        {
            var snapshot = ServiceLocator.Get<ISceneSnapshotManager>();
            if (snapshot == null)
            {
                LogError("快照服务未注册（ServiceLocator），回滚不可用");
                return;
            }
            if (!snapshot.HasSnapshot)
            {
                LogWarning("当前没有生成前快照，无法回滚（点击「生成关卡」会自动创建快照）");
                return;
            }
            if (_scheduler == null || _scheduler.IsBusy)
            {
                LogWarning("生成进行中禁止回滚，请等待完成或先取消");
                return;
            }

            LogWarning("正在回滚到生成前快照（场景将整体恢复到快照时刻）...");
            if (snapshot.RollbackToSnapshot())
            {
                _scheduler.ResetToReady(); // 状态机强制复位（事件链自动刷新状态行/进度条/生成按钮）
                ResetUiState("已回滚到生成前快照：场景已恢复至快照时刻，无残留");
            }
            else
            {
                LogError("回滚失败（原场景文件未被改写，可继续工作），详见 Console 日志");
            }
        }

        /// <summary>
        /// 回滚后界面重置（公开，供菜单等外部回滚入口复用）：
        /// 清空日志面板 + 按调度器当前状态重放一次渲染（状态行/进度条/按钮复位）+ 追加提示日志。
        /// </summary>
        public void ResetUiState(string message = null)
        {
            if (this == null) return;
            OnClearLogClicked();
            OnSchedulerStateChanged(_scheduler?.CurrentState ?? GenerationTaskState.Ready);
            if (!string.IsNullOrEmpty(message)) LogSuccess(message);
            RefreshRollbackButton();
        }

        /// <summary> 回滚按钮可用状态与快照存在性联动（无快照时禁用并提示） </summary>
        private void RefreshRollbackButton()
        {
            if (_rollbackBtn == null) return;
            var hasSnapshot = ServiceLocator.Get<ISceneSnapshotManager>()?.HasSnapshot == true;
            _rollbackBtn.SetEnabled(hasSnapshot);
            _rollbackBtn.tooltip = hasSnapshot
                ? "回滚到生成前快照：整体恢复层级/组件/NavMesh 至快照时刻，无残留"
                : "无可用快照：点击「生成关卡」会自动创建生成前快照";
        }

        /// <summary> 公开的按钮状态刷新入口（菜单等外部快照操作后联动窗口，不清日志） </summary>
        public void RefreshSnapshotButton() => RefreshRollbackButton();

        /// <summary>
        /// 从 ServiceLocator 获取调度器并接线：注入日志宿主、订阅状态变更、渲染初始状态
        /// </summary>
        private void InitScheduler()
        {
            _scheduler = ServiceLocator.Get<IGeneratorScheduler>();
            if (_scheduler == null)
            {
                LogError("调度器未注册（ServiceLocator），生成功能不可用");
                _generateBtn.SetEnabled(false);
                return;
            }

            _scheduler.SetLogger(this); // 窗口即日志宿主

            // 订阅状态变更（C# 事件对同一方法组委托自动去重，CreateGUI 多次调用不会重复订阅）
            _scheduler.StateChanged += OnSchedulerStateChanged;

            // Day3：订阅构建进度（构建器经 ServiceLocator 获取，窗口不 new 业务类）。
            // 每次 CreateGUI 都对当前 ServiceLocator 实例重订阅——覆盖注册后旧订阅失效，防陈旧订阅。
            _levelBuilder = ServiceLocator.Get<ILevelBuilder>();
            if (_levelBuilder != null)
                _levelBuilder.ProgressChanged += OnBuildProgress;

            OnSchedulerStateChanged(_scheduler.CurrentState); // 初始渲染
        }

        /// <summary>
        /// 构建进度回调（Day3）：分帧构建期间实时刷新进度条。
        /// 构建器在 Editor 主线程触发（EditorCoroutine 处于 update 循环内），UI Toolkit 允许直接赋值、
        /// 渲染在下一帧生效——不依赖 delayCall（编辑器繁忙/MCP 桥接轮询时 delayCall 会滞后，进度条卡顿）。
        /// </summary>
        private void OnBuildProgress(float progress)
        {
            if (this == null || _progressBar == null) return;
            var p = Mathf.Clamp01(progress);
            _progressBar.value = p * 100f;
            _progressBar.title = $"构建中 {p:P0}";
        }

        /// <summary>
        /// 调度器状态变更处理：更新状态标签与生成按钮可用性（单一入口，与状态机保持一致）
        /// </summary>
        private void OnSchedulerStateChanged(GenerationTaskState state)
        {
            if (this == null || _statusLabel == null) return; // 窗口销毁后回调保护

            // Day6 联调：勾选「生成后自动运行」且本轮真实从生成中 → 成功时，自动进入播放模式
            // 验证"输入→生成→构建→运行"全链路。用 _previousState 判别（仅本次任务触发的成功才自动运行，
            // 防止窗口重开/回放旧状态时误触发）；EnterPlaymode 即用户手动点击 Play，安全。
            if (state == GenerationTaskState.Success && _previousState == GenerationTaskState.Generating
                && _autoRunToggle != null && _autoRunToggle.value)
            {
                LogSuccess("生成完成，自动进入播放模式验证运行效果...");
                EditorApplication.EnterPlaymode();
            }
            _previousState = state;

            var (text, color) = state switch
            {
                GenerationTaskState.Ready => ("待命", new Color(0.62f, 0.62f, 0.62f)),
                GenerationTaskState.Generating => ("生成中...", new Color(0.25f, 0.6f, 1f)),
                GenerationTaskState.Success => ("生成成功", new Color(0.25f, 0.8f, 0.25f)),
                GenerationTaskState.Failed => ("生成失败", new Color(1f, 0.32f, 0.32f)),
                _ => (state.ToString(), Color.white)
            };
            _statusLabel.text = text;
            _statusLabel.style.color = color;

            if (_generateBtn != null)
                _generateBtn.SetEnabled(!_scheduler.IsBusy); // 生成中禁用，防重复提交

            // Day3：进度条与取消按钮仅在生成/构建中显示；任务结束隐藏并归零
            var busy = state == GenerationTaskState.Generating;
            if (_progressBar != null)
            {
                _progressBar.style.display = busy ? DisplayStyle.Flex : DisplayStyle.None;
                if (!busy) _progressBar.value = 0f;
            }
            if (_cancelBtn != null)
            {
                _cancelBtn.style.display = busy ? DisplayStyle.Flex : DisplayStyle.None;
                _cancelBtn.SetEnabled(busy);
            }

            // Day2：状态变更后联动回滚按钮（自动回滚已消费快照 → 按钮随快照存在性复位）
            RefreshRollbackButton();
        }

        private void OnClearLogClicked()
        {
            _logEntries.Clear();
            RenderLogs();
        }

        #region 日志系统（线程安全 + 富文本）

        public void Log(string message)
        {
            AppendLog("INFO", message, null);
        }

        public void LogError(string message)
        {
            AppendLog("ERROR", message, "red");
        }

        public void LogSuccess(string message)
        {
            AppendLog("SUCCESS", message, "green");
        }

        public void LogWarning(string message)
        {
            AppendLog("WARNING", message, "orange");
        }

        private void AppendLog(string level, string message, string colorHex)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            string formatted;
            
            if (!string.IsNullOrEmpty(colorHex))
                formatted = $"[{timestamp}] <color={colorHex}>[{level}]</color> {message}";
            else
                formatted = $"[{timestamp}] [{level}] {message}";

            _logEntries.Add(formatted);

            // 防止内存溢出
            if (_logEntries.Count > MaxLogEntries)
                _logEntries.RemoveAt(0);

            // 同步渲染（同 OnBuildProgress 经验）：MCP 桥接/编辑器繁忙时 delayCall 不保证触发，
            // 曾出现"缓存有条目但日志面板空白"（回滚后提示缺失）。日志为低频操作，直接渲染成本可忽略。
            RenderLogs();
        }

        private void RenderLogs()
        {
            if (_logContent == null) return;
            _logContent.text = string.Join("\n", _logEntries);
            
            // 自动滚到底部（带安全校验）
            if (_logScroll != null && _logContent != null)
            {
                _logScroll.ScrollTo(_logContent);
            }
        }

        #endregion

        #region ILogger 实现（窗口即日志宿主，供校验器/生成器通过 SetLogger 注入）

        void AILevelGenerator.Runtime.Interfaces.ILogger.Log(string message) => Log(message);

        void AILevelGenerator.Runtime.Interfaces.ILogger.LogWarning(string message) => LogWarning(message);

        void AILevelGenerator.Runtime.Interfaces.ILogger.LogError(string message) => LogError(message);

        void AILevelGenerator.Runtime.Interfaces.ILogger.LogSuccess(string message) => LogSuccess(message);

        void AILevelGenerator.Runtime.Interfaces.ILogger.Clear() => OnClearLogClicked();

        event Action<string, AILevelGenerator.Runtime.Interfaces.LogLevel>
            AILevelGenerator.Runtime.Interfaces.ILogger.OnLogReceived
        {
            // 窗口日志直接渲染进 UI，无需向外部转发
            add { }
            remove { }
        }

        #endregion

        private void OnDestroy()
        {
            // 清理状态/进度订阅，防止内存泄露（对未订阅的委托 -= 是安全空操作）
            if (_scheduler != null)
                _scheduler.StateChanged -= OnSchedulerStateChanged;
            if (_levelBuilder != null)
                _levelBuilder.ProgressChanged -= OnBuildProgress;
        }
    }
}