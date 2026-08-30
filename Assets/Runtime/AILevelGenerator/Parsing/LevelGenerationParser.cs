using System;
using System.Collections.Generic;
using AILevelGenerator.Runtime.Data;
using UnityEngine;
// Runtime.Data.TerrainData 与 UnityEngine.TerrainData 同名 → 本地别名消歧
using TerrainData = AILevelGenerator.Runtime.Data.TerrainData;

namespace AILevelGenerator.Runtime.Parsing
{
    /// <summary>
    /// 关卡生成解析结果。IsValid=true 且 Level 非空表示 JSON 结构可解析；
    /// Warnings 记录语义修正（非法值兜底/负坐标归零/未知枚举等），不阻断生成。
    /// </summary>
    public class LevelParseResult
    {
        public LevelData Level;
        public bool IsValid;
        public List<ValidationError> Errors = new();
        public List<ValidationWarning> Warnings = new();
    }

    /// <summary>
    /// LLM 输出的 snake_case JSON → 关卡数据 DTO（PascalCase）语义映射层（Day4 容错解析引擎）。
    /// 容错规则：缺失字段默认值；字符串↔数字↔布尔自动转换；非法值兜底 + warning；
    /// 负缩放归零（负坐标/负旋转合法保留）；未知枚举→默认 + warning；空物体逻辑名跳过。
    /// 结构损坏（非对象/非 JSON）→ IsValid=false + 中文错误信息，由调用方决定是否失败。
    /// </summary>
    public static class LevelGenerationParser
    {
        public const string DefaultLevelName = "未命名关卡";
        public const string DefaultTaskName = "任务";

        // —— 入口 ——

        /// <summary> 解析 LLM 原始输出（含代码块剥离/前后杂文截取等容错，由 JsonParser 完成） </summary>
        public static LevelParseResult Parse(string json)
        {
            JsonValue root;
            try
            {
                root = JsonParser.Parse(json);
            }
            catch (JsonParseException e)
            {
                var result = new LevelParseResult { IsValid = false };
                result.Errors.Add(new ValidationError
                {
                    Code = "PARSE_FAILED",
                    Message = $"LLM 输出不是合法 JSON：{e.Message}",
                    DataPath = "llm_response"
                });
                return result;
            }
            return ParseRoot(root);
        }

        /// <summary> 已有 JSON 树 → LevelData（缓存命中重走解析管线时复用，保证结果新鲜） </summary>
        public static LevelParseResult ParseRoot(JsonValue root)
        {
            if (root == null || !root.IsObject)
            {
                var failed = new LevelParseResult { IsValid = false };
                failed.Errors.Add(new ValidationError
                {
                    Code = "NOT_OBJECT",
                    Message = "LLM 输出顶层应为 JSON 对象",
                    DataPath = "llm_response"
                });
                return failed;
            }

            var result = new LevelParseResult { IsValid = true, Level = new LevelData() };
            var level = result.Level;

            level.LevelName = GetStringOrFallback(root, "level_name", DefaultLevelName);
            level.Description = root.GetString("description", string.Empty);
            level.PlayerStartPosition = ParseVector3(root.Get("player_start_position"), Vector3.zero,
                "player_start_position", clampZero: false, result.Warnings); // 负坐标合法（对称布局）
            level.Terrain = ParseTerrain(root.Get("terrain"), result);
            ParseProps(root.Get("props"), level, result);
            ParseTasks(root.Get("tasks"), level, result);
            return result;
        }

        // —— 地形 ——

        private static TerrainData ParseTerrain(JsonValue node, LevelParseResult result)
        {
            const string path = "terrain";
            var terrain = new TerrainData(); // 缺省 100/100/10
            if (node == null || !node.IsObject) return terrain;

            terrain.Width = GetInt(node, "width", 100, path, result.Warnings);
            terrain.Length = GetInt(node, "length", 100, path, result.Warnings);
            terrain.HeightScale = GetFloat(node, "height_scale", 10f, path, result.Warnings);

            // 非正数地形参数无意义 → 默认值 + warning
            if (terrain.Width <= 0)
            {
                AddWarning(result.Warnings, path + ".width", $"地形宽度 {terrain.Width} 非法，使用默认值 100");
                terrain.Width = 100;
            }
            if (terrain.Length <= 0)
            {
                AddWarning(result.Warnings, path + ".length", $"地形长度 {terrain.Length} 非法，使用默认值 100");
                terrain.Length = 100;
            }
            if (terrain.HeightScale <= 0f)
            {
                AddWarning(result.Warnings, path + ".height_scale", $"地形起伏 {terrain.HeightScale} 非法，使用默认值 10");
                terrain.HeightScale = 10f;
            }
            return terrain;
        }

        // —— 物体 ——

        private static void ParseProps(JsonValue node, LevelData level, LevelParseResult result)
        {
            if (node == null || !node.IsArray) return;

            for (var i = 0; i < node.ArrayValue.Count; i++)
            {
                var item = node.ArrayValue[i];
                var path = $"props[{i}]";
                if (!item.IsObject) continue;

                var logicalName = item.GetString("prefab_logical_name", string.Empty);
                if (string.IsNullOrWhiteSpace(logicalName))
                {
                    AddWarning(result.Warnings, path, "prefab_logical_name 为空，该物体已跳过");
                    continue;
                }

                level.Props.Add(new PropPlacement
                {
                    PrefabLogicalName = logicalName,
                    Position = ParseVector3(item.Get("position"), Vector3.zero, path + ".position", clampZero: false, result.Warnings),
                    Rotation = ParseVector3(item.Get("rotation"), Vector3.zero, path + ".rotation", clampZero: false, result.Warnings), // 负旋转角度合法
                    Scale = ParseVector3(item.Get("scale"), Vector3.one, path + ".scale", clampZero: true, result.Warnings) // 负缩放导致翻转/不可见，归零
                });
            }
        }

        // —— 任务 ——

        private static void ParseTasks(JsonValue node, LevelData level, LevelParseResult result)
        {
            if (node == null || !node.IsArray) return;

            for (var i = 0; i < node.ArrayValue.Count; i++)
            {
                var item = node.ArrayValue[i];
                var path = $"tasks[{i}]";
                if (!item.IsObject) continue;

                var timeLimit = GetFloat(item, "time_limit", -1f, path, result.Warnings);
                level.Tasks.Add(new TaskData
                {
                    TaskID = item.GetString("task_id", string.Empty),
                    TaskName = GetStringOrFallback(item, "task_name", DefaultTaskName),
                    Description = item.GetString("description", string.Empty),
                    Type = ParseEnum(item, "type", TaskType.Custom, path, result.Warnings),
                    Objective = ParseEnum(item, "objective", TaskObjective.Count, path, result.Warnings),
                    Reward = ParseReward(item.Get("reward"), path, result.Warnings),
                    IsMainTask = item.GetBool("is_main_task", true),
                    TriggerCondition = item.GetString("trigger_condition", string.Empty),
                    TimeLimit = timeLimit <= 0f ? -1f : timeLimit // 缺失/≤0 → 无时限
                });
            }
        }

        private static RewardData ParseReward(JsonValue node, string path, List<ValidationWarning> warnings)
        {
            var reward = new RewardData(); // 缺省 {0, 0, []}
            if (node == null || !node.IsObject) return reward;

            reward.Experience = GetInt(node, "experience", 0, path + ".reward", warnings);
            reward.Gold = GetInt(node, "gold", 0, path + ".reward", warnings);
            var items = node.Get("item_rewards");
            if (items != null && items.IsArray)
            {
                foreach (var it in items.ArrayValue)
                {
                    var s = it.AsString(null);
                    if (!string.IsNullOrEmpty(s)) reward.ItemRewards.Add(s);
                }
            }
            return reward;
        }

        // —— 基础辅助 ——

        /// <summary> 坐标/缩放解析：非对象 → fallback；组件负值且 clampZero → 归零 + warning </summary>
        private static Vector3 ParseVector3(JsonValue node, Vector3 fallback, string path, bool clampZero, List<ValidationWarning> warnings)
        {
            if (node == null || !node.IsObject) return fallback;
            var v = new Vector3(
                GetFloat(node, "x", fallback.x, path, warnings),
                GetFloat(node, "y", fallback.y, path, warnings),
                GetFloat(node, "z", fallback.z, path, warnings));

            if (clampZero)
            {
                if (v.x < 0f) { v.x = 0f; AddWarning(warnings, path + ".x", "负坐标已归零"); }
                if (v.y < 0f) { v.y = 0f; AddWarning(warnings, path + ".y", "负坐标已归零"); }
                if (v.z < 0f) { v.z = 0f; AddWarning(warnings, path + ".z", "负坐标已归零"); }
            }
            return v;
        }

        /// <summary> 字符串字段：空白 → 默认值（如 level_name 缺省"未命名关卡"） </summary>
        private static string GetStringOrFallback(JsonValue node, string key, string fallback)
        {
            var v = node.GetString(key, null);
            return string.IsNullOrWhiteSpace(v) ? fallback : v;
        }

        /// <summary>
        /// 容错 int 读取：缺失/Null → fallback；字符串数字可解析；完全无法解析 → fallback + warning。
        /// 注意：fallback 同时充当解析失败哨兵，值恰好等于 fallback 时按类型区分合法性。
        /// </summary>
        private static int GetInt(JsonValue node, string key, int fallback, string path, List<ValidationWarning> warnings)
        {
            var v = node.Get(key);
            if (v == null || v.ValueKind == JsonValue.Kind.Null) return fallback;

            var value = v.AsInt(fallback);
            if (value != fallback) return value;
            if (v.ValueKind == JsonValue.Kind.Number) return fallback; // 数字恰好等于 fallback，合法
            if (v.ValueKind == JsonValue.Kind.String && v.StringValue.Trim() == fallback.ToString()) return fallback;

            AddWarning(warnings, path + "." + key, $"「{v.AsString("?")}」无法解析为整数，使用默认值 {fallback}");
            return fallback;
        }

        /// <summary> 容错 float 读取（语义同 GetInt） </summary>
        private static float GetFloat(JsonValue node, string key, float fallback, string path, List<ValidationWarning> warnings)
        {
            var v = node.Get(key);
            if (v == null || v.ValueKind == JsonValue.Kind.Null) return fallback;

            var value = v.AsFloat(fallback);
            if (value != fallback) return value;
            if (v.ValueKind == JsonValue.Kind.Number) return fallback;
            if (v.ValueKind == JsonValue.Kind.String && v.StringValue.Trim() == fallback.ToString()) return fallback;

            AddWarning(warnings, path + "." + key, $"「{v.AsString("?")}」无法解析为数值，使用默认值 {fallback}");
            return fallback;
        }

        /// <summary> 枚举读取：字符串/数字名 → 枚举（忽略大小写）；未知值 → fallback + warning </summary>
        private static T ParseEnum<T>(JsonValue node, string key, T fallback, string path, List<ValidationWarning> warnings) where T : struct
        {
            var s = node.GetString(key, null);
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            if (Enum.TryParse(s, true, out T result)) return result;

            AddWarning(warnings, path + "." + key, $"「{s}」不是合法的 {typeof(T).Name} 值，使用默认 {fallback}");
            return fallback;
        }

        private static void AddWarning(List<ValidationWarning> warnings, string dataPath, string message) =>
            warnings.Add(new ValidationWarning { Code = "PARSE_FALLBACK", Message = message, DataPath = dataPath });
    }
}
