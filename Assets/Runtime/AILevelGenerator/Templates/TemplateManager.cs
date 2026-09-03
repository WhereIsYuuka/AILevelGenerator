using System;
using System.Collections.Generic;
using AILevelGenerator.Runtime.Interfaces.Templates;

namespace AILevelGenerator.Runtime.Templates
{
    /// <summary>
    /// 模板管理器（第五周-Day4，替代 TemplateProvider）：纯逻辑注册中心，实现 ITemplateManager。
    /// - 构造注入三集合即旧 Provider 语义（内存模板测试/工具零成本迁移）；
    /// - 可选注入 ITemplateSource 支持 Reload 整体重载（资产变更即时生效，Editor 装配路径）；
    /// - 增量注册按 TemplateId Upsert 保序；每次集合变更广播一次 TemplatesChanged；
    /// - 空模板/null 入参静默容忍（模板体系各目录允许暂缺），不抛异常。
    /// </summary>
    public class TemplateManager : ITemplateManager
    {
        private readonly ITemplateSource _source; // 可空：未注入时 Reload 返回 false

        // 分类有序列表：查询返回其只读视图；替换语义 = 整表换新实例（遍历中安全）
        private List<LevelTemplate> _levelTemplates = new();
        private List<TaskTemplate> _taskTemplates = new();
        private List<PromptTemplate> _promptTemplates = new();

        public TemplateManager() : this((ITemplateSource)null) { }

        /// <summary> 旧 Provider 兼容构造：三类模板直接入表（null 集合按空处理），不触发变更事件 </summary>
        public TemplateManager(
            IEnumerable<LevelTemplate> levelTemplates,
            IEnumerable<TaskTemplate> taskTemplates,
            IEnumerable<PromptTemplate> promptTemplates) : this((ITemplateSource)null)
        {
            ReplaceAll(new TemplateCollection
            {
                LevelTemplates = ToList(levelTemplates),
                TaskTemplates = ToList(taskTemplates),
                PromptTemplates = ToList(promptTemplates)
            });
        }

        /// <summary> 加载源构造：初始为空，调用 Reload() 后从源整体载入（Editor 装配路径） </summary>
        public TemplateManager(ITemplateSource source)
        {
            _source = source;
        }

        private static List<T> ToList<T>(IEnumerable<T> items) where T : class
            => items == null ? new List<T>() : new List<T>(items);

        // —— 查询 ——

        /// <summary> 全部关卡模板（按注册顺序，窗口下拉数据源） </summary>
        public IReadOnlyList<LevelTemplate> GetLevelTemplates() => _levelTemplates;

        /// <summary> 按 TemplateId 查关卡模板，未命中返回 null </summary>
        public LevelTemplate GetTemplateById(string id)
            => string.IsNullOrEmpty(id) ? null : _levelTemplates.Find(t => t != null && t.TemplateId == id);

        /// <summary> 全部任务模板（按注册顺序） </summary>
        public IReadOnlyList<TaskTemplate> GetTaskTemplates() => _taskTemplates;

        /// <summary> 按 TemplateId 查任务模板，未命中返回 null </summary>
        public TaskTemplate GetTaskTemplateById(string id)
            => string.IsNullOrEmpty(id) ? null : _taskTemplates.Find(t => t != null && t.TemplateId == id);

        /// <summary> 全部 Prompt 模板（按注册顺序） </summary>
        public IReadOnlyList<PromptTemplate> GetPromptTemplates() => _promptTemplates;

        /// <summary> 默认 Prompt 模板：取第一个，未配置返回 null </summary>
        public PromptTemplate GetDefaultPromptTemplate() => _promptTemplates.Count > 0 ? _promptTemplates[0] : null;

        /// <summary> 按 TemplateId 查 Prompt 模板，未命中返回 null </summary>
        public PromptTemplate GetPromptTemplateById(string id)
            => string.IsNullOrEmpty(id) ? null : _promptTemplates.Find(t => t != null && t.TemplateId == id);

        // —— 动态注册 / 注销（Upsert 保序，见接口注释语义） ——

        /// <summary> 注册/替换关卡模板（同 TemplateId 就地替换保序） </summary>
        public void RegisterLevelTemplate(LevelTemplate template)
        {
            if (Upsert(_levelTemplates, template, t => t.TemplateId))
                TemplatesChanged?.Invoke();
        }

        /// <summary> 按 TemplateId 注销关卡模板；命中并移除返回 true，未命中返回 false </summary>
        public bool UnregisterLevelTemplate(string templateId)
        {
            var removed = RemoveById(_levelTemplates, templateId, t => t.TemplateId);
            if (removed) TemplatesChanged?.Invoke();
            return removed;
        }

        /// <summary> 注册/替换任务模板（同 TemplateId 就地替换保序） </summary>
        public void RegisterTaskTemplate(TaskTemplate template)
        {
            if (Upsert(_taskTemplates, template, t => t.TemplateId))
                TemplatesChanged?.Invoke();
        }

        /// <summary> 按 TemplateId 注销任务模板；命中并移除返回 true，未命中返回 false </summary>
        public bool UnregisterTaskTemplate(string templateId)
        {
            var removed = RemoveById(_taskTemplates, templateId, t => t.TemplateId);
            if (removed) TemplatesChanged?.Invoke();
            return removed;
        }

        /// <summary> 注册/替换 Prompt 模板（同 TemplateId 就地替换保序） </summary>
        public void RegisterPromptTemplate(PromptTemplate template)
        {
            if (Upsert(_promptTemplates, template, t => t.TemplateId))
                TemplatesChanged?.Invoke();
        }

        /// <summary> 按 TemplateId 注销 Prompt 模板；命中并移除返回 true，未命中返回 false </summary>
        public bool UnregisterPromptTemplate(string templateId)
        {
            var removed = RemoveById(_promptTemplates, templateId, t => t.TemplateId);
            if (removed) TemplatesChanged?.Invoke();
            return removed;
        }

        // —— 动态加载（整体重载） ——

        /// <summary>
        /// 从加载源整体重载三类模板并触发一次 TemplatesChanged；
        /// 源未注入或返回 null 时按"空集合替换"处理但不触发事件（构造期宽容），返回是否有源可用。
        /// </summary>
        public bool Reload()
        {
            if (_source == null) return false;
            var collection = _source.Load();
            ReplaceAll(collection);
            TemplatesChanged?.Invoke();
            return true;
        }

        /// <summary> 模板集合变更事件（注册/注销/重载成功后触发一次） </summary>
        public event Action TemplatesChanged;

        // —— 内部实现 ——

        /// <summary> Upsert：null 忽略；TemplateId 为空追加尾部；同 ID 就地替换；变更返回 true </summary>
        private static bool Upsert<T>(List<T> list, T template, Func<T, string> idOf) where T : class
        {
            if (template == null) return false;
            var id = idOf(template);
            if (string.IsNullOrEmpty(id))
            {
                list.Add(template); // 无 ID 无法定位：追加尾部（容错路径）
                return true;
            }
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] != null && idOf(list[i]) == id)
                {
                    list[i] = template; // 同 ID 就地替换：保持原位置与顺序
                    return true;
                }
            }
            list.Add(template);
            return true;
        }

        /// <summary> 按 TemplateId 移除首个命中条目，返回是否移除 </summary>
        private static bool RemoveById<T>(List<T> list, string templateId, Func<T, string> idOf) where T : class
        {
            if (string.IsNullOrEmpty(templateId)) return false;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] != null && idOf(list[i]) == templateId)
                {
                    list.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary> 整体替换三类列表（null 集合按空处理；整表换新实例保证外部遍历安全） </summary>
        private void ReplaceAll(TemplateCollection collection)
        {
            _levelTemplates = ToList(collection?.LevelTemplates);
            _taskTemplates = ToList(collection?.TaskTemplates);
            _promptTemplates = ToList(collection?.PromptTemplates);
        }
    }
}
