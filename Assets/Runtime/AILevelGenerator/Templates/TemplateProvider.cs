using System.Collections.Generic;
using System.Linq;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Interfaces.Templates;

namespace AILevelGenerator.Runtime.Templates
{
    /// <summary>
    /// 模板提供者：持有关卡/任务/Prompt 三类模板集合，按 TemplateId 查询。
    /// 核心为纯逻辑构造注入（测试传内存模板即可，不依赖磁盘）；
    /// Editor 下经 LoadFromAssets 从 Assets/Settings/ 加载全部模板资产。
    /// </summary>
    public class TemplateProvider : ITemplateProvider
    {
        private readonly List<LevelTemplate> _levelTemplates;
        private readonly List<TaskTemplate> _taskTemplates;
        private readonly List<PromptTemplate> _promptTemplates;

        public TemplateProvider(
            IEnumerable<LevelTemplate> levelTemplates,
            IEnumerable<TaskTemplate> taskTemplates,
            IEnumerable<PromptTemplate> promptTemplates)
        {
            // 空集合容忍：各模板组可暂缺，但引用不得为 null
            _levelTemplates = new List<LevelTemplate>(levelTemplates ?? new List<LevelTemplate>());
            _taskTemplates = new List<TaskTemplate>(taskTemplates ?? new List<TaskTemplate>());
            _promptTemplates = new List<PromptTemplate>(promptTemplates ?? new List<PromptTemplate>());
        }

        /// <summary> 全部关卡模板（按注入顺序，即配置顺序，窗口下拉数据源） </summary>
        public IReadOnlyList<LevelTemplate> GetLevelTemplates() => _levelTemplates;

        /// <summary> 按 TemplateId 查关卡模板，未命中返回 null </summary>
        public LevelTemplate GetTemplateById(string id)
            => string.IsNullOrEmpty(id) ? null : _levelTemplates.FirstOrDefault(t => t != null && t.TemplateId == id);

        /// <summary> 全部任务模板（按注入顺序） </summary>
        public IReadOnlyList<TaskTemplate> GetTaskTemplates() => _taskTemplates;

        /// <summary> 按 TemplateId 查任务模板，未命中返回 null </summary>
        public TaskTemplate GetTaskTemplateById(string id)
            => string.IsNullOrEmpty(id) ? null : _taskTemplates.FirstOrDefault(t => t != null && t.TemplateId == id);

        /// <summary> 默认 Prompt 模板：取第一个，未配置返回 null </summary>
        public PromptTemplate GetDefaultPromptTemplate() => _promptTemplates.Count > 0 ? _promptTemplates[0] : null;

        /// <summary> 按 TemplateId 查 Prompt 模板，未命中返回 null </summary>
        public PromptTemplate GetPromptTemplateById(string id)
            => string.IsNullOrEmpty(id) ? null : _promptTemplates.FirstOrDefault(t => t != null && t.TemplateId == id);

#if UNITY_EDITOR
        /// <summary>
        /// 从 Assets/Settings/ 目录加载全部模板资产（编辑器启动注册时调用）。
        /// 关卡/任务模板在 Templates/，Prompt 模板在 PromptTemplates/；目录缺失时返回空集合（不抛异常）。
        /// </summary>
        public static TemplateProvider LoadFromAssets()
        {
            return new TemplateProvider(
                LoadAssets<LevelTemplate>("Assets/Settings/Templates"),
                LoadAssets<TaskTemplate>("Assets/Settings/Templates"),
                LoadAssets<PromptTemplate>("Assets/Settings/PromptTemplates"));
        }

        /// <summary> 按目录加载某类型全部资产（t: 基类名过滤，可命中全部派生类资产；目录不存在返回空列表） </summary>
        private static List<T> LoadAssets<T>(string folder) where T : UnityEngine.Object
        {
            var result = new List<T>();
            var guids = UnityEditor.AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder });
            foreach (var guid in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) result.Add(asset);
            }
            return result;
        }
#endif
    }
}
