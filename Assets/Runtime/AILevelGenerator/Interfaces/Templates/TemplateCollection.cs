using System.Collections.Generic;
// PromptTemplate 实现于 Templates/ 目录（ns AILevelGenerator.Runtime.Templates），此处跨命名空间引用
using AILevelGenerator.Runtime.Templates;

namespace AILevelGenerator.Runtime.Interfaces.Templates
{
    /// <summary>
    /// 模板集合快照（第五周-Day4）：一次加载/替换的不可变聚合载体，按类别聚合关卡/任务/Prompt 模板。
    /// 由 ITemplateSource.Load 产出、TemplateManager.Reload 整体替换；
    /// 列表引用一经构造不再改写（替换 = 换新实例），保证外部遍历期间不被并发/再入修改破坏。
    /// </summary>
    public sealed class TemplateCollection
    {
        public IReadOnlyList<LevelTemplate> LevelTemplates;
        public IReadOnlyList<TaskTemplate> TaskTemplates;
        public IReadOnlyList<PromptTemplate> PromptTemplates;
    }
}
