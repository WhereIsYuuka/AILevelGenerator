using UnityEditor;

namespace AILevelGenerator.Editor.UI
{
    /// <summary>
    /// DeepSeek API Key 的 EditorPrefs 存取封装（单点收口）。
    /// 安全约束：key 只存编辑器偏好（本机注册表），绝不进代码/资产/git/日志。
    /// 窗口读取与保存均经此类，防止散落的 EditorPrefs 键名。
    /// </summary>
    public static class DeepSeekApiKeySettings
    {
        private const string EditorPrefsKey = "AILevelGenerator.DeepSeekApiKey";

        /// <summary> 读取已保存的 Key（未保存返回空串） </summary>
        public static string GetApiKey() => EditorPrefs.GetString(EditorPrefsKey, string.Empty);

        /// <summary> 保存 Key（空值也允许，表示清除） </summary>
        public static void SaveApiKey(string apiKey) => EditorPrefs.SetString(EditorPrefsKey, apiKey ?? string.Empty);
    }
}
