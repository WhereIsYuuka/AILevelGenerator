using AILevelGenerator.Runtime.Mappings;
using UnityEditor;
using UnityEngine;

namespace AILevelGenerator.Editor.Inspectors
{
    /// <summary>
    /// 资源映射配置自定义 Inspector：
    /// 1. 数据体检：重复逻辑名 / 空引用条目警示（编辑期数据质量把关）
    /// 2. 匹配测试工具：输入任意名称 → 实时显示模糊匹配结果（验收"输入名称可正确返回对应预制体"）
    /// </summary>
    [CustomEditor(typeof(PrefabMappingConfig))]
    public class PrefabMappingConfigEditor : UnityEditor.Editor
    {
        private string _testKeyword = string.Empty;
        private GameObject _matchResult;
        private string _matchDetail = string.Empty;

        private PrefabMappingConfig Config => (PrefabMappingConfig)target;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawValidationSummary();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("Entries"), true);

            serializedObject.ApplyModifiedProperties();

            DrawFuzzyTestSection();
        }

        /// <summary> 数据体检：重复逻辑名、空引用条目提示 </summary>
        private void DrawValidationSummary()
        {
            var duplicates = Config.GetDuplicateNames();
            if (duplicates.Count > 0)
                EditorGUILayout.HelpBox($"逻辑名重复（模糊匹配结果将不确定）：{string.Join("、", duplicates)}", MessageType.Warning);

            var emptyCount = 0;
            foreach (var entry in Config.Entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.LogicalName) || entry.Prefab == null)
                    emptyCount++;
            }
            if (emptyCount > 0)
                EditorGUILayout.HelpBox($"存在 {emptyCount} 条无效条目（逻辑名为空或未绑定预制体），将被管理器跳过", MessageType.Info);

            EditorGUILayout.Space();
        }

        /// <summary> 匹配测试工具：输入名称 → 显示精确/模糊匹配结果 </summary>
        private void DrawFuzzyTestSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("匹配测试（验证逻辑名 → 预制体）", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _testKeyword = EditorGUILayout.TextField("输入名称", _testKeyword);
            if (GUILayout.Button("测试匹配", GUILayout.Width(80)))
                RunMatchTest();
            EditorGUILayout.EndHorizontal();

            if (_matchResult != null)
            {
                EditorGUILayout.ObjectField("匹配结果", _matchResult, typeof(GameObject), false);
                if (!string.IsNullOrEmpty(_matchDetail))
                    EditorGUILayout.HelpBox(_matchDetail, MessageType.Info);
            }
            else if (!string.IsNullOrEmpty(_matchDetail))
            {
                EditorGUILayout.HelpBox(_matchDetail, MessageType.Error);
            }
        }

        private void RunMatchTest()
        {
            var manager = new ResourceMappingManager(Config);
            if (manager.TryGetPrefab(_testKeyword, out _matchResult))
                _matchDetail = $"命中：{_matchResult.name}（匹配模式：{(Config.Entries.Exists(e => e != null && e.LogicalName == _testKeyword) ? "精确" : "模糊")}）";
            else
            {
                _matchResult = null;
                _matchDetail = $"未命中任何预制体：\"{_testKeyword}\"";
            }
        }
    }
}
