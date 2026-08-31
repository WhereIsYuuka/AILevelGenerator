using System;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Templates;
using UnityEngine;

namespace AILevelGenerator.Runtime.Validation
{
    /// <summary>
    /// 模板范围校验（模板专属校验器，构造注入模板实例）：
    /// Props/Tasks 数量与模板 Min/Max 约束一致、主线任务强制、地形尺寸与模板一致。
    /// 错误码复用 LLMGenerator.ValidateScope（同码双级：LLM 生成期产 Warning 提示，此处产 Error 拦截）——
    /// ApplyDefaults 只补默认值不裁剪规模（模板注释契约），数量越界由本校验器负责拦截。
    /// 由 GeneratorServiceInitializer 遍历模板注册（每个模板注册自己的校验器，核心层只做调度）。
    /// </summary>
    public class TemplateScopeValidator : BaseValidator<LevelData>
    {
        private readonly ConfigurableLevelTemplate _template;

        public TemplateScopeValidator(ConfigurableLevelTemplate template)
        {
            _template = template ?? throw new ArgumentNullException(nameof(template));
        }

        public override ValidationResult Validate(LevelData data, ValidationContext context)
        {
            var result = new ValidationResult();
            if (data == null)
            {
                AddError(result, "DATA_NULL", "校验数据为空（LevelData 为 null）");
                return result;
            }

            // 规模约束（0 = 不限制，与模板/LLM 语义一致）；消息格式对齐 LLMGenerator.ValidateScope
            var propCount = data.Props?.Count ?? 0;
            if (_template.MaxPropCount > 0 && propCount > _template.MaxPropCount)
                AddError(result, "PROPS_TOO_MANY", $"道具数量 {propCount} 超过模板上限 {_template.MaxPropCount}", "props");
            if (_template.MinPropCount > 0 && propCount < _template.MinPropCount)
                AddError(result, "PROPS_TOO_FEW", $"道具数量 {propCount} 低于模板下限 {_template.MinPropCount}", "props");

            var taskCount = data.Tasks?.Count ?? 0;
            if (_template.MaxTaskCount > 0 && taskCount > _template.MaxTaskCount)
                AddError(result, "TASKS_TOO_MANY", $"任务数量 {taskCount} 超过模板上限 {_template.MaxTaskCount}", "tasks");
            if (_template.MinTaskCount > 0 && taskCount < _template.MinTaskCount)
                AddError(result, "TASKS_TOO_FEW", $"任务数量 {taskCount} 低于模板下限 {_template.MinTaskCount}", "tasks");

            // 主线任务强制（与 LLM 侧同文案）
            if (_template.ForceMainTask && !HasMainTask(data.Tasks))
                AddError(result, "NO_MAIN_TASK", "模板要求存在主线任务，但生成结果没有 IsMainTask=true 的任务", "tasks");

            // 地形与模板一致（OverrideTerrain=true 时正常链路 ApplyDefaults 已覆盖，此处为防御性兜底）
            if (_template.OverrideTerrain && data.Terrain != null)
            {
                if (data.Terrain.Width != _template.TerrainWidth)
                    AddError(result, "TERRAIN_MISMATCH", $"地形宽度与模板不一致：{data.Terrain.Width} ≠ {_template.TerrainWidth}", "terrain.width");
                if (data.Terrain.Length != _template.TerrainLength)
                    AddError(result, "TERRAIN_MISMATCH", $"地形长度与模板不一致：{data.Terrain.Length} ≠ {_template.TerrainLength}", "terrain.length");
                if (!Mathf.Approximately(data.Terrain.HeightScale, _template.TerrainHeightScale))
                    AddError(result, "TERRAIN_MISMATCH", $"地形高度缩放与模板不一致：{data.Terrain.HeightScale} ≠ {_template.TerrainHeightScale}", "terrain.heightScale");
            }

            return result;
        }

        private static bool HasMainTask(System.Collections.Generic.List<TaskData> tasks)
        {
            if (tasks == null) return false;
            foreach (var t in tasks)
                if (t != null && t.IsMainTask) return true;
            return false;
        }
    }
}
