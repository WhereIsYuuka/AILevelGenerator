using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Diagnostics;
using AILevelGenerator.Runtime.Interfaces;
using UnityEngine;

namespace AILevelGenerator.Runtime.Validation
{
    /// <summary>
    /// 数据级前置校验（数值边界）：NaN/Infinity、零/负缩放、坐标超限、地形尺寸越界、任务 ID 空/重复。
    /// 错误路径精确到具体字段（props[i].position / terrain.width / tasks[i].taskID），满足"定位到字段"验收。
    /// 解析器对部分非法值有归一化兜底（负缩放归零等），但整体为零缩放/超大坐标等仍会漏网，由本校验器补位拦截。
    /// </summary>
    public class DataBoundsValidator : BaseValidator<LevelData>
    {
        private const float MaxCoordinate = 10000f;   // 坐标绝对值上限（超出视为数据异常）
        private const int MaxTerrainSize = 10000;     // 地形宽/长上限
        private const float MaxTerrainHeightScale = 1000f;

        public override ValidationResult Validate(LevelData data, ValidationContext context)
        {
            var result = new ValidationResult();
            if (data == null)
            {
                AddError(result, ErrorCodes.DATA_NULL, "校验数据为空（LevelData 为 null）");
                return result;
            }

            // 玩家出生点坐标超限
            if (HasOutOfRangeComponent(data.PlayerStartPosition))
                AddError(result, ErrorCodes.DATA_POSITION_OUT_OF_RANGE, $"出生点坐标超出允许范围（|分量| ≤ {MaxCoordinate}）：{data.PlayerStartPosition}", "playerStartPosition");

            // 道具数值边界
            if (data.Props != null)
            {
                for (var i = 0; i < data.Props.Count; i++)
                {
                    var prop = data.Props[i];
                    var index = i;
                    if (!IsFiniteVector3(prop.Position))
                        AddError(result, ErrorCodes.DATA_NAN_OR_INFINITE, $"道具位置数值非法（NaN 或 Infinity）：{prop.Position}", $"props[{index}].position");
                    if (!IsFiniteVector3(prop.Rotation))
                        AddError(result, ErrorCodes.DATA_NAN_OR_INFINITE, $"道具旋转数值非法（NaN 或 Infinity）：{prop.Rotation}", $"props[{index}].rotation");
                    if (!IsFiniteVector3(prop.Scale))
                        AddError(result, ErrorCodes.DATA_NAN_OR_INFINITE, $"道具缩放数值非法（NaN 或 Infinity）：{prop.Scale}", $"props[{index}].scale");
                    if (prop.Scale.x <= 0f || prop.Scale.y <= 0f || prop.Scale.z <= 0f)
                        AddError(result, ErrorCodes.DATA_SCALE_INVALID, $"道具缩放值必须大于 0：{prop.Scale}（零/负缩放会导致物体不可见）", $"props[{index}].scale");
                    if (HasOutOfRangeComponent(prop.Position))
                        AddError(result, ErrorCodes.DATA_POSITION_OUT_OF_RANGE, $"道具坐标超出允许范围（|分量| ≤ {MaxCoordinate}）：{prop.Position}", $"props[{index}].position");
                }
            }

            // 地形尺寸边界（data.Terrain 为 null 时跳过——无模板时 ApplyDefaults 不执行属正常状态）
            if (data.Terrain != null)
            {
                if (data.Terrain.Width < 1 || data.Terrain.Width > MaxTerrainSize)
                    AddError(result, ErrorCodes.DATA_TERRAIN_INVALID, $"地形宽度超出允许范围（1~{MaxTerrainSize}）：{data.Terrain.Width}", "terrain.width");
                if (data.Terrain.Length < 1 || data.Terrain.Length > MaxTerrainSize)
                    AddError(result, ErrorCodes.DATA_TERRAIN_INVALID, $"地形长度超出允许范围（1~{MaxTerrainSize}）：{data.Terrain.Length}", "terrain.length");
                if (data.Terrain.HeightScale <= 0f || data.Terrain.HeightScale > MaxTerrainHeightScale)
                    AddError(result, ErrorCodes.DATA_TERRAIN_INVALID, $"地形高度缩放超出允许范围（0~{MaxTerrainHeightScale}）：{data.Terrain.HeightScale}", "terrain.heightScale");
            }

            // 任务 ID 完整性：空 / 重复（HashSet 去重，DataPath 定位具体任务）
            if (data.Tasks != null)
            {
                var seen = new System.Collections.Generic.HashSet<string>();
                for (var i = 0; i < data.Tasks.Count; i++)
                {
                    var task = data.Tasks[i];
                    var path = $"tasks[{i}].taskID";
                    if (task == null || string.IsNullOrWhiteSpace(task.TaskID))
                    {
                        AddError(result, ErrorCodes.DATA_TASK_ID_EMPTY, "任务 ID 为空", path);
                        continue;
                    }
                    if (!seen.Add(task.TaskID))
                        AddError(result, ErrorCodes.DATA_TASK_ID_DUPLICATE, $"任务 ID 重复：{task.TaskID}", path);
                }
            }

            return result;
        }

        private static bool IsFiniteVector3(Vector3 v) =>
            float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        private static bool HasOutOfRangeComponent(Vector3 v) =>
            Mathf.Abs(v.x) > MaxCoordinate || Mathf.Abs(v.y) > MaxCoordinate || Mathf.Abs(v.z) > MaxCoordinate;
    }
}
