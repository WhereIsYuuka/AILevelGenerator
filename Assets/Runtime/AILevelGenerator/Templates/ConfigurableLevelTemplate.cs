using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces.Templates;
using UnityEngine;
// 数据层 TerrainData 与 UnityEngine.TerrainData 同名，别名消除歧义（见 CLAUDE.md 已知坑）
using TerrainData = AILevelGenerator.Runtime.Data.TerrainData;

namespace AILevelGenerator.Runtime.Templates
{
    /// <summary>
    /// 数据驱动关卡模板：策划复制资产改配置即可新建模板，无需写代码。
    /// 模板只描述"规则"（指南文本 + 地形默认值 + 规模约束），不写死关卡内容（内容由 LLM 产出）。
    /// 四类内置模板（线性闯关/开放世界/塔防防守/谜题收集）均为此类资产实例。
    /// </summary>
    [CreateAssetMenu(fileName = "LevelTemplate", menuName = "AI Level Generator/关卡模板（数据驱动）")]
    public class ConfigurableLevelTemplate : LevelTemplate
    {
        [Tooltip("模板指南：本模板的布局规则，随 Prompt 注入 {templateGuideline} 告知 LLM。文案避免使用半角花括号，用「」代替")]
        [TextArea(3, 10)]
        public string Guideline;

        [Header("地形默认值")]
        [Tooltip("为 true 时用下方地形默认值覆盖生成结果；false 时仅在生成结果无地形时兜底")]
        public bool OverrideTerrain = true;
        [Min(1)] public int TerrainWidth = 100;
        [Min(1)] public int TerrainLength = 100;
        [Min(0)] public float TerrainHeightScale = 10f;

        [Header("规模约束（0 = 不限制）")]
        [Min(0)] public int MinPropCount;
        [Min(0)] public int MaxPropCount;
        [Min(0)] public int MinTaskCount;
        [Min(0)] public int MaxTaskCount;
        [Tooltip("是否必须存在主线任务（IsMainTask=true）")]
        public bool ForceMainTask = true;

        /// <summary> 覆写基类：返回模板指南（PromptBuilder 只依赖基类方法，保持多态） </summary>
        public override string GetGuideline() => Guideline ?? string.Empty;

        /// <summary>
        /// 应用默认值到 LevelData：地形为空时创建并填默认值；OverrideTerrain 时覆盖已有地形。
        /// 只补默认值不裁剪规模（数量越界由校验器负责，ApplyDefaults 保持单一职责）。
        /// </summary>
        public override void ApplyDefaults(LevelData data)
        {
            if (data == null) return;
            if (data.Terrain == null)
            {
                data.Terrain = new TerrainData
                {
                    Width = TerrainWidth,
                    Length = TerrainLength,
                    HeightScale = TerrainHeightScale
                };
            }
            else if (OverrideTerrain)
            {
                data.Terrain.Width = TerrainWidth;
                data.Terrain.Length = TerrainLength;
                data.Terrain.HeightScale = TerrainHeightScale;
            }
        }

        /// <summary> 自校验：继承基类 TemplateId 检查 + 数量范围合法性（0 表示不限，Max 非 0 时不得小于 Min） </summary>
        public override bool ValidateSelf(out string error)
        {
            if (!base.ValidateSelf(out error)) return false;
            if (MaxPropCount > 0 && MinPropCount > MaxPropCount)
            {
                error = "道具数量范围倒挂：MinPropCount 大于 MaxPropCount";
                return false;
            }
            if (MaxTaskCount > 0 && MinTaskCount > MaxTaskCount)
            {
                error = "任务数量范围倒挂：MinTaskCount 大于 MaxTaskCount";
                return false;
            }
            return true;
        }
    }
}
