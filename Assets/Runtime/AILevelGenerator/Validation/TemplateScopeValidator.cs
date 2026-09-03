using System;
using System.Collections.Generic;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Diagnostics;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Interfaces.Templates;

namespace AILevelGenerator.Runtime.Validation
{
    /// <summary>
    /// 模板范围校验（模板专属校验器，构造注入模板实例）：
    /// 第五周-Day4 起模板通过 CollectScopeViolations 自检规模约束（Props/Tasks 数量与 Min/Max 一致、
    /// 主线任务强制、地形尺寸一致），本校验器只做转译：违规条目 → Error（拦截）。
    /// 同码双级：LLM 生成期产 Warning 提示，此处产 Error 拦截 —— ApplyDefaults 只补默认值不裁剪规模
    /// （模板注释契约），数量越界由本校验器负责拦截。
    /// 模板类型无关：任意 LevelTemplate 子类覆写 CollectScopeViolations 即获得拦截能力（开闭原则），
    /// 核心框架无需为新增模板类型改动。由刷新链路遍历模板注册（每个模板注册自己的校验器）。
    /// </summary>
    public class TemplateScopeValidator : BaseValidator<LevelData>
    {
        private readonly LevelTemplate _template;

        public TemplateScopeValidator(LevelTemplate template)
        {
            _template = template ?? throw new ArgumentNullException(nameof(template));
        }

        public override ValidationResult Validate(LevelData data, ValidationContext context)
        {
            var result = new ValidationResult();
            if (data == null)
            {
                AddError(result, ErrorCodes.DATA_NULL, "校验数据为空（LevelData 为 null）");
                return result;
            }

            // 模板自检 → Error 转译：违规条目的 错误码/消息/数据路径 均出自模板（规则单点，双级一致）
            var violations = new List<ScopeViolation>();
            _template.CollectScopeViolations(data, violations);
            foreach (var violation in violations)
                AddError(result, violation.Code, violation.Message, violation.DataPath);
            return result;
        }
    }
}
