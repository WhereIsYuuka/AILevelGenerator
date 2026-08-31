using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;

namespace AILevelGenerator.Runtime.Validation
{
    /// <summary>
    /// 数据级前置校验（资源存在性）：映射表非空 + 每个道具逻辑名非空且能命中预制体。
    /// 未命中（含模糊匹配兜底后）即 100% 拦截，错误定位到具体 props[i].prefabLogicalName。
    /// 用 TryGetPrefab（干净查询）而非 GetPrefab——后者未命中会打 Debug.LogWarning 噪音。
    /// </summary>
    public class ResourceValidator : BaseValidator<LevelData>
    {
        public override ValidationResult Validate(LevelData data, ValidationContext context)
        {
            var result = new ValidationResult();
            if (data == null)
            {
                AddError(result, "DATA_NULL", "校验数据为空（LevelData 为 null）");
                return result;
            }
            if (context?.ResourceMapper == null)
            {
                AddError(result, "RESOURCE_MAPPER_MISSING", "资源映射服务未注入，无法校验资源存在性", "resourceMapper");
                return result;
            }

            if (data.Props == null || data.Props.Count == 0) return result; // 无道具列表视为合法（生成器不产出道具；空列表不校验映射表）

            // 映射表本身为空：有道具但全部无法映射，属配置级错误
            if (context.ResourceMapper.GetAllLogicalNames() == null || context.ResourceMapper.GetAllLogicalNames().Count == 0)
            {
                AddError(result, "RESOURCE_MAPPING_EMPTY", "资源映射表为空，请先配置 PrefabMappingConfig", "resourceMapper");
                return result;
            }

            for (var i = 0; i < data.Props.Count; i++)
            {
                var prop = data.Props[i];
                var path = $"props[{i}].prefabLogicalName";
                if (string.IsNullOrWhiteSpace(prop.PrefabLogicalName))
                {
                    AddError(result, "RESOURCE_NAME_EMPTY", "道具逻辑名为空，无法映射预制体", path);
                    continue;
                }
                if (!context.ResourceMapper.TryGetPrefab(prop.PrefabLogicalName, out _))
                {
                    AddError(result, "RESOURCE_NOT_FOUND", $"资源不存在：{prop.PrefabLogicalName}，请检查资源映射配置", path);
                }
            }

            return result;
        }
    }
}
