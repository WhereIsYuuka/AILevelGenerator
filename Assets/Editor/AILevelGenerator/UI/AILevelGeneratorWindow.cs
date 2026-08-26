using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AILevelGenerator.Editor.UI
{
    /// <summary>
    /// AI关卡生成工具主窗口
    /// 职责：仅负责 UI 渲染与事件转发，不包含任何业务逻辑
    /// </summary>
    public class LevelGeneratorWindow : EditorWindow
    {
        [SerializeField] private VisualTreeAsset _uxmlAsset; // 在 Inspector 里拖拽赋值，彻底避免硬编码路径

        private DropdownField _templateDropdown;
        private IntegerField _seedField;
        private TextField _inputField;
        private Button _generateBtn;
        private Button _clearLogBtn;
        private ScrollView _logScroll;
        private Label _logContent;
        
        private readonly List<string> _logEntries = new List<string>(); // 内存缓存，避免字符串频繁拼接
        private const int MaxLogEntries = 500; // 防止内存泄漏

        [MenuItem("Tools/AI Level Generator")]
        public static void ShowWindow()
        {
            var window = GetWindow<LevelGeneratorWindow>("AI关卡生成工具");
            window.minSize = new Vector2(520, 600);
        }

        private void CreateGUI()
        {
            // 1. 加载 UXML（使用 SerializeField 拖拽方式）
            if (_uxmlAsset == null)
            {
                Debug.LogError("[AI Generator] UXML 资源未在 Inspector 中绑定，请拖拽赋值。");
                return;
            }
            rootVisualElement.Add(_uxmlAsset.Instantiate());

            // 2. 绑定控件
            BindControls();
            
            // 3. 初始化下拉
            InitTemplateOptions();
            
            // 4. 注册事件
            RegisterEvents();

            // 5. 渲染已有日志（窗口重绘时保留）
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
            
            // 开启富文本支持，用于显示彩色日志
            _logContent.enableRichText = true;
        }

        private void InitTemplateOptions()
        {
            // TODO: Day5 改为从 ITemplateProvider 动态加载
            var templates = new List<string> { "战斗关卡", "收集任务", "解谜关卡", "护送任务" };
            _templateDropdown.choices = templates;
            _templateDropdown.index = 0;
        }

        private void RegisterEvents()
        {
            _generateBtn.clicked += OnGenerateClicked;
            _clearLogBtn.clicked += OnClearLogClicked;
        }

        /// <summary>
        /// 生成按钮点击 - 只做参数收集与调度转发
        /// </summary>
        private void OnGenerateClicked()
        {
            var template = _templateDropdown.value;
            var input = _inputField.value;
            var seed = _seedField.value;

            if (string.IsNullOrWhiteSpace(input))
            {
                LogError("请输入关卡描述");
                return;
            }

            Log($"开始生成 - 模板：{template}，种子：{seed}");
            Log($"描述：{input}");

            // 通过 ServiceLocator 获取调度器，窗口不 new 任何具体类
            // TODO: Day5 接入真实调度器
            // var scheduler = ServiceLocator.Get<IGeneratorScheduler>();
            // scheduler.StartGeneration(new GenerationRequest { Prompt = input, TemplateId = template, RandomSeed = seed });
            
            Log("[提示] 生成链路待接入，当前为界面演示");
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

        private void OnDestroy()
        {
            // 清理延迟回调，防止内存泄露
            EditorApplication.delayCall -= RenderLogsSafe;
        }
    }
}