using System.Collections.Generic;
using System.Text;
using AILevelGenerator.Runtime.Parsing;

namespace AILevelGenerator.Runtime.LLM
{
    /// <summary>
    /// 关卡生成 Function Calling 工具定义（Day3 双重约束之一）。
    /// - generate_level 函数的 JSON Schema 覆盖 LevelData/TaskData 全字段，枚举限定 LLM 只能输出合法值
    /// - prefab_logical_name 的 enum 由资源映射表的逻辑名动态注入（约束物体名必能命中映射表）
    /// - tool_choice 强制调用 + response_format json_object 双约束（兼容性由调用方实测降级）
    /// 手写 JSON 拼接（结构固定），字符串值用 JsonParser.EscapeString 转义。
    /// </summary>
    public static class LevelGenerationSchema
    {
        public const string FunctionName = "generate_level";
        public const string FunctionDescription = "根据自然语言描述生成完整关卡设计，输出关卡结构（snake_case JSON）";

        /// <summary> 任务类型枚举（与 TaskData.TaskType 顺序一致，LLM 按名输出） </summary>
        private static readonly string[] TaskTypeValues = { "Kill", "Collect", "Arrive", "Escort", "Defend", "Custom" };

        /// <summary> 任务目标枚举（与 TaskData.TaskObjective 一致） </summary>
        private static readonly string[] TaskObjectiveValues = { "Count", "ReachPosition", "CollectItems", "TimeSurvive" };

        /// <summary> 创建 Function Calling 工具列表（单工具 generate_level） </summary>
        public static List<DeepSeekTool> CreateTools(IReadOnlyList<string> resourceNames)
        {
            return new List<DeepSeekTool>
            {
                new()
                {
                    Function = new DeepSeekToolFunction
                    {
                        Name = FunctionName,
                        Description = FunctionDescription,
                        ParametersJson = BuildParametersJson(resourceNames)
                    }
                }
            };
        }

        /// <summary> tool_choice 强制调用 generate_level（JSON 文本，直接嵌入请求体） </summary>
        public static string CreateToolChoiceJson() =>
            $"{{\"type\":\"function\",\"function\":{{\"name\":\"{FunctionName}\"}}}}";

        /// <summary> response_format 强制 JSON 对象输出（JSON 文本） </summary>
        public static string CreateJsonObjectResponseFormat() => "{\"type\":\"json_object\"}";

        /// <summary> 构建 generate_level 的 parameters JSON Schema（资源名动态注入 enum） </summary>
        public static string BuildParametersJson(IReadOnlyList<string> resourceNames)
        {
            var sb = new StringBuilder(2048);
            sb.Append("{\"type\":\"object\",\"properties\":{");

            // 关卡基础字段
            sb.Append("\"level_name\":{\"type\":\"string\",\"description\":\"关卡名称\"},");
            sb.Append("\"description\":{\"type\":\"string\",\"description\":\"关卡简介\"},");
            sb.Append("\"player_start_position\":").Append(Vector3Schema("玩家出生点坐标")).Append(',');

            // 地形
            sb.Append("\"terrain\":{\"type\":\"object\",\"description\":\"地形参数\",\"properties\":{")
              .Append("\"width\":{\"type\":\"integer\",\"description\":\"宽度\"},")
              .Append("\"length\":{\"type\":\"integer\",\"description\":\"长度\"},")
              .Append("\"height_scale\":{\"type\":\"number\",\"description\":\"地形起伏程度\"}")
              .Append("}},");

            // 物体列表（prefab_logical_name 枚举动态注入）
            sb.Append("\"props\":{\"type\":\"array\",\"description\":\"场景物体列表，必须引用给定的逻辑名\",\"items\":{\"type\":\"object\",\"properties\":{")
              .Append("\"prefab_logical_name\":{\"type\":\"string\"");
            if (resourceNames != null && resourceNames.Count > 0)
            {
                sb.Append(",\"enum\":[");
                for (var i = 0; i < resourceNames.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(JsonParser.EscapeString(resourceNames[i])).Append('"');
                }
                sb.Append(']');
            }
            sb.Append(",\"description\":\"物体逻辑名，只能从给定 enum 中选择\"},")
              .Append("\"position\":").Append(Vector3Schema("物体位置")).Append(',')
              .Append("\"rotation\":").Append(Vector3Schema("物体旋转欧拉角")).Append(',')
              .Append("\"scale\":").Append(Vector3Schema("物体缩放，省略为 1")).Append(',')
              .Append("\"patrol_points\":{\"type\":\"array\",\"description\":\"巡逻点（可选）：仅巡逻型敌人携带，按列表顺序循环移动的路径点；不输出时由模板按配置确定性兜底\",\"items\":")
              .Append(Vector3Schema("巡逻点坐标，列表顺序即移动顺序"))
              .Append('}') // 关 patrol_points 数组
              .Append("}}},"); // 关 items.properties、items、props 三层

            // 任务列表
            sb.Append("\"tasks\":{\"type\":\"array\",\"description\":\"任务列表（0~3 个，第一个为主任务）\",\"items\":{\"type\":\"object\",\"properties\":{")
              .Append("\"task_id\":{\"type\":\"string\",\"description\":\"任务唯一标识\"},")
              .Append("\"task_name\":{\"type\":\"string\",\"description\":\"任务名称\"},")
              .Append("\"description\":{\"type\":\"string\",\"description\":\"任务说明\"},")
              .Append("\"type\":{\"type\":\"string\",\"enum\":[")
              .Append(StringArrayToJson(TaskTypeValues))
              .Append("],\"description\":\"任务类型\"},")
              .Append("\"objective\":{\"type\":\"string\",\"enum\":[")
              .Append(StringArrayToJson(TaskObjectiveValues))
              .Append("],\"description\":\"任务目标\"},")
              .Append("\"is_main_task\":{\"type\":\"boolean\",\"description\":\"是否主线任务\"},")
              .Append("\"trigger_condition\":{\"type\":\"string\",\"description\":\"触发条件\"},")
              .Append("\"time_limit\":{\"type\":\"number\",\"description\":\"时限（秒），0 或省略表示无时限\"},")
              .Append("\"reward\":{\"type\":\"object\",\"properties\":{")
              .Append("\"experience\":{\"type\":\"integer\"},")
              .Append("\"gold\":{\"type\":\"integer\"},")
              .Append("\"item_rewards\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"物品奖励列表\"}")
              .Append("}}") // 关 reward.properties、reward
              .Append("}}") // 关 items.properties、items
              .Append("}"); // 关 tasks 对象

            // 必填字段与未知字段约束
            sb.Append("},\"required\":[\"level_name\",\"props\",\"tasks\"],\"additionalProperties\":false}");
            return sb.ToString();
        }

        /// <summary> {x,y,z} 坐标对象 Schema 模板（自带完整闭合） </summary>
        private static string Vector3Schema(string description)
        {
            return $"{{\"type\":\"object\",\"description\":\"{description}\",\"properties\":{{" +
                   "\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"z\":{\"type\":\"number\"}}," +
                   "\"additionalProperties\":false}";
        }

        /// <summary> 字符串数组 → JSON 数组文本（值转义） </summary>
        private static string StringArrayToJson(IReadOnlyList<string> values)
        {
            var sb = new StringBuilder(64);
            for (var i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(JsonParser.EscapeString(values[i])).Append('"');
            }
            return sb.ToString();
        }
    }
}
