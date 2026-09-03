using System.Collections.Generic;

namespace AILevelGenerator.Runtime.Prompting
{
    /// <summary>
    /// Prompt v1 优化前基线（第五周-Day5，回归锚点）：冻结自 2026-09-03 精简改造前的
    /// Default_PromptTemplate.asset（System/User 全文本）与 LevelGenerationSchema.BuildParametersJson
    /// （7 个逻辑名快照）——与改动前管线输出逐字一致（改造前经 MCP 实跑导出）。
    /// 用途：①PromptOptimizationTests 无网络断言"精简后估算 Token 较基线下降 ≥20%"（需求验收的离线门禁）；
    ///      ②PromptBenchmarkRunner 真实 API 对比的"优化前"一侧（重新构造旧 Prompt 与旧 Schema 发请求）。
    /// 注意：常量语义 = 历史快照，**禁止按当前代码"顺手同步"**（同步即失去锚点意义）；如需新基线请新增 V2。
    /// </summary>
    public static class PromptBaselineV1
    {
        /// <summary> 优化前 SystemPromptTemplate（Default_PromptTemplate.asset v1） </summary>
        public const string SystemPrompt =
            "你是一名资深的游戏关卡设计师，根据用户描述与模板指南设计 Unity 关卡方案。\n\n" +
            "设计规则：\n" +
            "1. 可用物体只能从「可用物体」清单中选择，逻辑名必须与清单完全一致，禁止输出清单外的物体名。\n" +
            "2. 必须遵守模板指南的布局规则。\n" +
            "3. 设计结果用 JSON 输出，结构如下：\n" +
            "{\n" +
            "  \"levelName\": \"关卡名称\",\n" +
            "  \"description\": \"关卡描述\",\n" +
            "  \"playerStartPosition\": {\"x\": 0, \"y\": 0, \"z\": 0},\n" +
            "  \"terrain\": {\"width\": 100, \"length\": 100, \"heightScale\": 10},\n" +
            "  \"props\": [{\"logicalName\": \"物体逻辑名\", \"position\": {\"x\": 0, \"y\": 0, \"z\": 0}}],\n" +
            "  \"tasks\": [{\"taskName\": \"任务名\", \"description\": \"描述\", \"type\": \"Kill\", \"objective\": \"Count\", " +
            "\"isMainTask\": true, \"timeLimit\": -1, \"triggerCondition\": \"触发条件\", \"reward\": {\"experience\": 100, \"gold\": 50, \"itemRewards\": []}}]\n" +
            "}\n" +
            "4. 所有坐标必须在合理范围内，道具不得超出地形边界。\n" +
            "5. 直接输出 JSON，不要输出任何解释文字。";

        /// <summary> 优化前 UserPromptTemplate（Default_PromptTemplate.asset v1，占位符原样保留） </summary>
        public const string UserPromptTemplate =
            "请按以下要求设计关卡：\n" +
            "用户描述：{userPrompt}\n" +
            "模板：{templateName}\n" +
            "模板指南：{templateGuideline}\n" +
            "可用物体：{resourceList}\n" +
            "随机种子：{seed}\n" +
            "生成开关：{terrainEnabled}地形、{propsEnabled}道具、{tasksEnabled}任务";

        /// <summary> 优化前基准资源清单（与 Assets/Settings/Mappings/PrefabMapping_Default.asset 的 7 条逻辑名一致） </summary>
        public static readonly IReadOnlyList<string> BaselineResources = new List<string>
        {
            "敌人-弓箭手", "敌人-近战", "敌人-精英", "宝箱", "NPC", "金币", "道具-生命药水"
        };

        /// <summary>
        /// 优化前 BuildParametersJson 输出快照（以 BaselineResources 注入 enum，与改动前管线逐字一致）。
        /// Schema 描述裁剪前的完整文本：结构/枚举/必填约束在精简中零改动，本快照仅作 Token 对比锚点。
        /// </summary>
        public const string SchemaParametersJson =
            "{\"type\":\"object\",\"properties\":{" +
            "\"level_name\":{\"type\":\"string\",\"description\":\"关卡名称\"}," +
            "\"description\":{\"type\":\"string\",\"description\":\"关卡简介\"}," +
            "\"player_start_position\":{\"type\":\"object\",\"description\":\"玩家出生点坐标\",\"properties\":{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"z\":{\"type\":\"number\"}},\"additionalProperties\":false}," +
            "\"terrain\":{\"type\":\"object\",\"description\":\"地形参数\",\"properties\":{\"width\":{\"type\":\"integer\",\"description\":\"宽度\"},\"length\":{\"type\":\"integer\",\"description\":\"长度\"},\"height_scale\":{\"type\":\"number\",\"description\":\"地形起伏程度\"}}}," +
            "\"props\":{\"type\":\"array\",\"description\":\"场景物体列表，必须引用给定的逻辑名\",\"items\":{\"type\":\"object\",\"properties\":{" +
            "\"prefab_logical_name\":{\"type\":\"string\",\"enum\":[\"敌人-弓箭手\",\"敌人-近战\",\"敌人-精英\",\"宝箱\",\"NPC\",\"金币\",\"道具-生命药水\"],\"description\":\"物体逻辑名，只能从给定 enum 中选择\"}," +
            "\"position\":{\"type\":\"object\",\"description\":\"物体位置\",\"properties\":{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"z\":{\"type\":\"number\"}},\"additionalProperties\":false}," +
            "\"rotation\":{\"type\":\"object\",\"description\":\"物体旋转欧拉角\",\"properties\":{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"z\":{\"type\":\"number\"}},\"additionalProperties\":false}," +
            "\"scale\":{\"type\":\"object\",\"description\":\"物体缩放，省略为 1\",\"properties\":{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"z\":{\"type\":\"number\"}},\"additionalProperties\":false}," +
            "\"patrol_points\":{\"type\":\"array\",\"description\":\"巡逻点（可选）：仅巡逻型敌人携带，按列表顺序循环移动的路径点；不输出时由模板按配置确定性兜底\",\"items\":{\"type\":\"object\",\"description\":\"巡逻点坐标，列表顺序即移动顺序\",\"properties\":{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"z\":{\"type\":\"number\"}},\"additionalProperties\":false}}}}," +
            "\"tasks\":{\"type\":\"array\",\"description\":\"任务列表（0~3 个，第一个为主任务）\",\"items\":{\"type\":\"object\",\"properties\":{" +
            "\"task_id\":{\"type\":\"string\",\"description\":\"任务唯一标识\"}," +
            "\"task_name\":{\"type\":\"string\",\"description\":\"任务名称\"}," +
            "\"description\":{\"type\":\"string\",\"description\":\"任务说明\"}," +
            "\"type\":{\"type\":\"string\",\"enum\":[\"Kill\",\"Collect\",\"Arrive\",\"Escort\",\"Defend\",\"Custom\"],\"description\":\"任务类型\"}," +
            "\"objective\":{\"type\":\"string\",\"enum\":[\"Count\",\"ReachPosition\",\"CollectItems\",\"TimeSurvive\"],\"description\":\"任务目标\"}," +
            "\"is_main_task\":{\"type\":\"boolean\",\"description\":\"是否主线任务\"}," +
            "\"trigger_condition\":{\"type\":\"string\",\"description\":\"触发条件\"}," +
            "\"time_limit\":{\"type\":\"number\",\"description\":\"时限（秒），0 或省略表示无时限\"}," +
            "\"reward\":{\"type\":\"object\",\"properties\":{\"experience\":{\"type\":\"integer\"},\"gold\":{\"type\":\"integer\"},\"item_rewards\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"物品奖励列表\"}}}}" +
            "}},\"required\":[\"level_name\",\"props\",\"tasks\"],\"additionalProperties\":false}";
    }
}
