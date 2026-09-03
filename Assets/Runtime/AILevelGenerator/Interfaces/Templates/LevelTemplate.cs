using System.Collections.Generic;
using UnityEngine;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Utilities;

namespace AILevelGenerator.Runtime.Interfaces.Templates
{
    /// <summary>
    /// 关卡模板基类 ScriptableObject 放在 Runtime 保证运行时可读。
    /// 第五周-Day1 起统一生命周期：模板资产本身保持无状态（生成期写入会脏化资产，
    /// 禁止在资产字段上存运行期状态），种子与随机流全部经 FinalizeData 传入。
    /// </summary>
    public abstract class LevelTemplate : ScriptableObject
    {
        public string TemplateId;
        public string DisplayName;
        [TextArea(2, 5)] public string Description;

        /// <summary>
        /// 统一生命周期入口（模板方法模式）：生成器一律调用此方法收尾数据，禁止散调 ApplyDefaults。
        /// 流程：ApplyDefaults 补默认值 → 用 (请求种子, TemplateId) 派生的确定性随机流执行 PostGenerate。
        /// 数据为 null 时静默返回（兼容历史行为：无模板/空数据路径不抛异常）。
        /// </summary>
        public void FinalizeData(LevelData data, int requestSeed)
        {
            if (data == null) return;
            ApplyDefaults(data);
            var rng = new DeterministicRandom(
                RandomSeedUtility.Derive(requestSeed, RandomSeedUtility.StableHash(TemplateId)));
            PostGenerate(data, rng);
        }

        /// <summary>
        /// 模板自有随机内容的确定性钩子（Day2 巡逻点、Day3 收集物散落等在此实现）。
        /// 种子契约：实现内只允许用传入 rng，严禁混用 UnityEngine.Random / System.Random。
        /// 基类默认无操作，模板无需随机内容时不覆写。
        /// </summary>
        protected virtual void PostGenerate(LevelData data, DeterministicRandom rng) { }

        /// <summary> 应用默认值到 LevelData </summary>
        public abstract void ApplyDefaults(LevelData data);

        /// <summary>
        /// 模板规模自检（第五周-Day4，开闭原则落点）：模板把"生成结果不符合自身约束"逐条写入 violations，
        /// 由核心框架统一转译消费 —— LLM 生成器转 Warning（生成期提示）、范围校验器转 Error（拦截）。
        /// 模板只描述规则，不感知消费方的严重级别与调度路径；新增模板类型只需覆写本方法，
        /// 核心层（LLMGenerator/ValidatorRegistry/TemplateScopeValidator）零改动即获得校验能力。
        /// 实现约定：data/violations 为 null 时静默返回；规则顺序即消费方输出顺序（先违规项优先展示）。
        /// </summary>
        public virtual void CollectScopeViolations(LevelData data, IList<ScopeViolation> violations) { }

        /// <summary>
        /// 模板指南文本：描述本模板的布局/内容规则，随 Prompt 注入 {templateGuideline} 告知 LLM。
        /// 数据驱动实现返回配置字段；代码模板可覆写为动态生成文本。PromptBuilder 只依赖此方法取指南。
        /// </summary>
        public virtual string GetGuideline() => string.Empty;

        /// <summary> 自校验，保存时调用 </summary>
        public virtual bool ValidateSelf(out string error)
        {
            error = string.IsNullOrEmpty(TemplateId) ? "TemplateId 缺失" : null;
            return error == null;
        }
    }
}