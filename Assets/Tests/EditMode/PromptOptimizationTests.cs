using AILevelGenerator.Runtime.LLM;
using AILevelGenerator.Runtime.Prompting;
using AILevelGenerator.Runtime.Templates;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// Prompt 精简回归门禁（第五周-Day5，无网络）：以 PromptBaselineV1（改造前逐字冻结）为锚点，
    /// 用 PromptTokenEstimator（启发式）断言「System + User 模板 + Schema 参数」估算 Token 较基线下降 ≥20%
    /// （估算只用于离线确定性门禁；需求验收口径以真实 API usage.prompt_tokens 3 次均值为准，见 PromptBenchmarkRunner）。
    /// 附结构守恒断言：精简只允许删描述文本，Schema 的结构/枚举/必填/未知字段约束必须原样保留（防止误删语义约束）。
    /// 估算口径：System/User 取模板资产原始文本（占位符两侧新旧同口径）；Schema 一侧新旧均以 BaselineResources
    /// 注入 enum（函数名/工具描述等动态请求公共部分不随精简变化，两侧同时剔除）。
    /// </summary>
    public class PromptOptimizationTests
    {
        private const string PromptAssetPath = "Assets/Settings/PromptTemplates/Default_PromptTemplate.asset";

        private const string SystemTag = "System";
        private const string UserTag = "User";
        private const string SchemaTag = "Schema";

        [Test]
        public void 精简后估算Token_较基线下降不低于百分之二十()
        {
            var systemNew = AssetDatabase.LoadAssetAtPath<PromptTemplate>(PromptAssetPath).SystemPromptTemplate;
            var userNew = AssetDatabase.LoadAssetAtPath<PromptTemplate>(PromptAssetPath).UserPromptTemplate;
            var schemaNew = LevelGenerationSchema.BuildParametersJson(PromptBaselineV1.BaselineResources);

            var baselineParts = new[] { PromptBaselineV1.SystemPrompt, PromptBaselineV1.UserPromptTemplate, PromptBaselineV1.SchemaParametersJson };
            var newParts = new[] { systemNew, userNew, schemaNew };

            var baselineTotal = PromptTokenEstimator.Estimate(baselineParts[0]) + PromptTokenEstimator.Estimate(baselineParts[1]) + PromptTokenEstimator.Estimate(baselineParts[2]);
            var newTotal = PromptTokenEstimator.Estimate(newParts[0]) + PromptTokenEstimator.Estimate(newParts[1]) + PromptTokenEstimator.Estimate(newParts[2]);
            var ratio = (double)newTotal / baselineTotal;

            Debug.Log($"[AI Generator] Prompt 精简离线回归：估算 Token 基线 {baselineTotal} → 当前 {newTotal}（下降 {1 - ratio:P0}，目标 ≥20%）| " +
                $"System {PromptTokenEstimator.Estimate(baselineParts[0])}→{PromptTokenEstimator.Estimate(newParts[0])} | " +
                $"User {PromptTokenEstimator.Estimate(baselineParts[1])}→{PromptTokenEstimator.Estimate(newParts[1])} | " +
                $"Schema {PromptTokenEstimator.Estimate(baselineParts[2])}→{PromptTokenEstimator.Estimate(newParts[2])}");

            Assert.LessOrEqual(ratio, 0.80,
                $"估算 Token 下降未达 20% 门禁（{ratio:P1}）。请继续精简 Prompt 或重定契约基线（PromptBaselineV2）");
            Assert.Greater(newTotal, 0, "估算值不应为 0（资产加载失败会在此暴露）");
        }

        [Test]
        public void 精简只删描述_各部分估算不得回涨()
        {
            var systemNew = AssetDatabase.LoadAssetAtPath<PromptTemplate>(PromptAssetPath).SystemPromptTemplate;
            var userNew = AssetDatabase.LoadAssetAtPath<PromptTemplate>(PromptAssetPath).UserPromptTemplate;
            var schemaNew = LevelGenerationSchema.BuildParametersJson(PromptBaselineV1.BaselineResources);

            Assert.LessOrEqual(PromptTokenEstimator.Estimate(systemNew), PromptTokenEstimator.Estimate(PromptBaselineV1.SystemPrompt), "System 不得回涨");
            Assert.LessOrEqual(PromptTokenEstimator.Estimate(userNew), PromptTokenEstimator.Estimate(PromptBaselineV1.UserPromptTemplate), "User 模板不得回涨");
            Assert.LessOrEqual(PromptTokenEstimator.Estimate(schemaNew), PromptTokenEstimator.Estimate(PromptBaselineV1.SchemaParametersJson), "Schema 不得回涨");
        }

        [Test]
        public void Schema精简_结构枚举必填约束原样保留()
        {
            var schema = LevelGenerationSchema.BuildParametersJson(PromptBaselineV1.BaselineResources);

            // 必填与未知字段约束（裁剪禁止触碰的部分）
            StringAssert.Contains("\"required\":[\"level_name\",\"props\",\"tasks\"]", schema, "必填约束必须保留");
            StringAssert.EndsWith("\"additionalProperties\":false}", schema, "顶层未知字段约束必须保留");
            StringAssert.Contains("\"is_main_task\":{\"type\":\"boolean\"}", schema, "任务字段约束必须保留");

            // 资源逻辑名 enum（动态注入源 = BaselineResources，逐一验证防串改）
            foreach (var name in PromptBaselineV1.BaselineResources)
                StringAssert.Contains("\"" + name + "\"", schema, $"资源逻辑名 {name} 必须留在 enum 中");

            // 任务类型/目标枚举
            StringAssert.Contains("\"Kill\",\"Collect\",\"Arrive\",\"Escort\",\"Defend\",\"Custom\"", schema, "任务类型枚举必须保留");
            StringAssert.Contains("\"Count\",\"ReachPosition\",\"CollectItems\",\"TimeSurvive\"", schema, "任务目标枚举必须保留");

            // 语义消歧说明（裁剪时保留清单，防止未来误删导致 LLM 理解退化）
            StringAssert.Contains("欧拉角(度)", schema);
            StringAssert.Contains("秒，0=无时限", schema);
            StringAssert.Contains("只能取 enum 之一", schema);
            StringAssert.Contains("巡逻路径点，敌人按顺序循环移动", schema);
        }
    }
}
