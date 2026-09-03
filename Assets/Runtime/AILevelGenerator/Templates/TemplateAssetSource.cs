#if UNITY_EDITOR
using System.Collections.Generic;
using AILevelGenerator.Runtime.Interfaces.Templates;
using UnityEditor;
using UnityEngine;

namespace AILevelGenerator.Runtime.Templates
{
    /// <summary>
    /// Editor 模板资产加载源（第五周-Day4，原 TemplateProvider.LoadFromAssets 迁入）：
    /// 从 Assets/Settings/ 目录扫描全部模板资产，实现 ITemplateSource 供 TemplateManager.Reload 使用。
    /// - Templates/ 目录按基类过滤加载 LevelTemplate/TaskTemplate（可命中全部派生类资产）；
    /// - PromptTemplates/ 目录加载 PromptTemplate；
    /// - 目录缺失/单个资产加载失败时容错跳过，不抛异常；
    /// - 按资产路径（Ordinal）排序保证加载顺序确定性 —— 任务模板"同 TaskType 首个命中"、
    ///   默认 Prompt"取第一个"都不受 AssetDatabase.FindAssets 内部顺序影响；
    /// - 编译域：#if UNITY_EDITOR（AssetDatabase 为 Editor API，Runtime 程序集在非编辑器目标裁剪；
    ///   先例见旧 TemplateProvider，EditMode 测试可直接验证资产加载）。
    /// </summary>
    public class TemplateAssetSource : ITemplateSource
    {
        private const string TemplateFolder = "Assets/Settings/Templates";
        private const string PromptTemplateFolder = "Assets/Settings/PromptTemplates";

        /// <summary> 扫描 Assets/Settings/ 全部模板资产（单次事务性快照，见接口注释容错约定） </summary>
        public TemplateCollection Load()
        {
            return new TemplateCollection
            {
                LevelTemplates = LoadAssets<LevelTemplate>(TemplateFolder),
                TaskTemplates = LoadAssets<TaskTemplate>(TemplateFolder),
                PromptTemplates = LoadAssets<PromptTemplate>(PromptTemplateFolder)
            };
        }

        /// <summary> 按目录扫描某类型全部资产（t: 基类名过滤可命中派生类；目录不存在返回空列表） </summary>
        private static List<T> LoadAssets<T>(string folder) where T : Object
        {
            var result = new List<T>();
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder });
            if (guids == null || guids.Length == 0) return result;

            var paths = new List<string>(guids.Length);
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }
            paths.Sort(System.StringComparer.Ordinal); // 路径序稳定排序：加载顺序确定性契约

            foreach (var path in paths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset == null)
                {
                    // 资产类型与过滤不符（如派生类脚本丢失）：跳过并提示，不阻断其余资产
                    Debug.LogWarning($"[AI Generator] 模板资产加载失败（类型不匹配或脚本丢失）：{path}");
                    continue;
                }
                result.Add(asset);
            }
            return result;
        }
    }
}
#endif
