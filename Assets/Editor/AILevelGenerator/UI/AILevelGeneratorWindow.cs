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
        private Button _clearLogBtn;
        private ScrollView _logScroll;
        private Label _logContent;
        private Label _statusLabel;
        private TextField _apiKeyField;
        private Button _saveKeyBtn;
        private Button _testConnectionBtn;
        private Label _apiStatusLabel;

        /// <summary> 模板下拉选项缓存：choices（DisplayName）与模板对象一一对应，index 即模板下标 </summary>
        private readonly List<LevelTemplate> _templateOptions = new();

        // 调度链路（经 ServiceLocator 获取，窗口不 new 任何具体业务类）
        private IGeneratorScheduler _scheduler;
        private bool _stateSubscribed; // CreateGUI 会被多次调用，防止重复订阅 StateChanged
        
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
            
            Log("工具初始化完成");
        }

        private void BindControls()
        {
            _templateDropdown = rootVisualElement.Q<DropdownField>("template-dropdown");
            _seedField = rootVisualElement.Q<IntegerField>("seed-field");
            _inputField = rootVisualElement.Q<TextField>("input-field");
            _generateBtn = rootVisualElement.Q<Button>("generate-button");
            _clearLogBtn = rootVisualElement.Q<Button>("clear-log-button");
            _logScroll = rootVisualElement.Q<ScrollView>("log-scroll");
            _logContent = rootVisualElement.Q<Label>("log-content");
            _statusLabel = rootVisualElement.Q<Label>("status-label");
            _apiKeyField = rootVisualElement.Q<TextField>("api-key-field");
            _saveKeyBtn = rootVisualElement.Q<Button>("save-key-button");
            _testConnectionBtn = rootVisualElement.Q<Button>("test-connection-button");
            _apiStatusLabel = rootVisualElement.Q<Label>("api-status-label");

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
            _clearLogBtn.clicked += OnClearLogClicked;
            if (_saveKeyBtn != null) _saveKeyBtn.clicked += OnSaveApiKeyClicked;
            if (_testConnectionBtn != null) _testConnectionBtn.clicked += OnTestConnectionClicked;
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

            // fire-and-forget：调度器内部捕获全部异常并转为 Failed 状态，返回的 Task 永不清零，可安全丢弃
            _ = _scheduler.StartGenerationAsync(new GenerationRequest
            {
                Prompt = input,
                TemplateId = templateId,
                RandomSeed = seed
            });
        }

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

            if (!_stateSubscribed)
            {
                _scheduler.StateChanged += OnSchedulerStateChanged;
                _stateSubscribed = true;
            }

            OnSchedulerStateChanged(_scheduler.CurrentState); // 初始渲染
        }

        /// <summary>
        /// 调度器状态变更处理：更新状态标签与生成按钮可用性（单一入口，与状态机保持一致）
        /// </summary>
        private void OnSchedulerStateChanged(GenerationTaskState state)
        {
            if (this == null || _statusLabel == null) return; // 窗口销毁后回调保护

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

            // 只在编辑器空闲时刷新 UI（性能优化）
            EditorApplication.delayCall += RenderLogsSafe;
        }

        private void RenderLogsSafe()
        {
            // 安全校验：窗口若已销毁，直接跳过
            if (this == null || _logContent == null) return;
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
            // 清理延迟回调与状态订阅，防止内存泄露
            EditorApplication.delayCall -= RenderLogsSafe;
            if (_scheduler != null && _stateSubscribed)
            {
                _scheduler.StateChanged -= OnSchedulerStateChanged;
                _stateSubscribed = false;
            }
        }
    }
}