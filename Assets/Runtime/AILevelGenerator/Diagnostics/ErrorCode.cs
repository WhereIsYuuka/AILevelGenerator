namespace AILevelGenerator.Runtime.Diagnostics
{
    /// <summary>
    /// 全项目统一错误码常量（第四周-Day5：错误码单一事实来源）。
    /// 使用约定：
    /// - 所有校验器/生成器/解析器/调度器一律引用本类常量，禁止散落字符串字面量；
    /// - 新增错误码 = 本类加常量 + ErrorCatalog 注册定义（含分类/严重级/含义/解决建议）；
    /// - 常量 ↔ 目录注册一一对应，由 ErrorCatalogTests 反射校验（漏注册即测试失败）。
    /// </summary>
    public static class ErrorCodes
    {
        // —— 请求级（RequestValidator）——
        public const string REQUEST_NULL = "REQUEST_NULL";
        public const string REQUEST_PROMPT_EMPTY = "REQUEST_PROMPT_EMPTY";
        public const string REQUEST_PROMPT_TOO_LONG = "REQUEST_PROMPT_TOO_LONG";
        public const string REQUEST_TEMPLATE_NOT_FOUND = "REQUEST_TEMPLATE_NOT_FOUND";
        public const string REQUEST_NO_CONTENT = "REQUEST_NO_CONTENT";

        // —— 数据级（DataBoundsValidator / 调度器内联 DATA_NULL 同码）——
        public const string DATA_NULL = "DATA_NULL";
        public const string DATA_POSITION_OUT_OF_RANGE = "DATA_POSITION_OUT_OF_RANGE";
        public const string DATA_NAN_OR_INFINITE = "DATA_NAN_OR_INFINITE";
        public const string DATA_SCALE_INVALID = "DATA_SCALE_INVALID";
        public const string DATA_TERRAIN_INVALID = "DATA_TERRAIN_INVALID";
        public const string DATA_TASK_ID_EMPTY = "DATA_TASK_ID_EMPTY";
        public const string DATA_TASK_ID_DUPLICATE = "DATA_TASK_ID_DUPLICATE";

        // —— 资源（ResourceValidator）——
        public const string RESOURCE_MAPPER_MISSING = "RESOURCE_MAPPER_MISSING";
        public const string RESOURCE_MAPPING_EMPTY = "RESOURCE_MAPPING_EMPTY";
        public const string RESOURCE_NAME_EMPTY = "RESOURCE_NAME_EMPTY";
        public const string RESOURCE_NOT_FOUND = "RESOURCE_NOT_FOUND";

        // —— 模板范围（TemplateScopeValidator 与 LLMGenerator.ValidateScope 同码双级）——
        public const string PROPS_TOO_MANY = "PROPS_TOO_MANY";
        public const string PROPS_TOO_FEW = "PROPS_TOO_FEW";
        public const string TASKS_TOO_MANY = "TASKS_TOO_MANY";
        public const string TASKS_TOO_FEW = "TASKS_TOO_FEW";
        public const string NO_MAIN_TASK = "NO_MAIN_TASK";
        public const string TERRAIN_MISMATCH = "TERRAIN_MISMATCH";

        // —— 后置（PostBuildValidator）——
        public const string POST_ENTITIES_MISSING = "POST_ENTITIES_MISSING";
        public const string POST_COUNT_MISMATCH = "POST_COUNT_MISMATCH";
        public const string POST_ENTITY_NULL = "POST_ENTITY_NULL";
        public const string POST_COMPONENT_MISSING = "POST_COMPONENT_MISSING";
        public const string POST_FLOAT_UNSUPPORTED = "POST_FLOAT_UNSUPPORTED";
        public const string POST_GROUND_MISSING = "POST_GROUND_MISSING";

        // —— 解析（LevelGenerationParser）——
        public const string PARSE_FAILED = "PARSE_FAILED";
        public const string NOT_OBJECT = "NOT_OBJECT";
        public const string PARSE_FALLBACK = "PARSE_FALLBACK";

        // —— LLM 链路（LLMGenerator）——
        public const string NO_API_KEY = "NO_API_KEY";
        public const string LLM_ERROR = "LLM_ERROR";

        // —— 基础设施（ValidatorRegistry / 调度器）——
        public const string VALIDATOR_ERROR = "VALIDATOR_ERROR";

        // —— 演示（MockGenerator，仅演示/测试通道使用）——
        public const string DEMO_FAIL = "DEMO_FAIL";
    }
}
