using System.Collections.Generic;

namespace AILevelGenerator.Runtime.Diagnostics
{
    /// <summary>
    /// 错误码目录（第四周-Day5）：全项目错误码的单一登记处。
    /// - 静态注册全部内置定义（35 个，与 ErrorCodes 常量一一对应）；
    /// - 未注册码在查询/格式化时安全降级（不抛异常，日志链路永不中断）；
    /// - 完整性（常量 ↔ 注册一一对应）由 ErrorCatalogTests 反射校验。
    /// </summary>
    public static class ErrorCatalog
    {
        private static readonly Dictionary<string, ErrorDefinition> Definitions = new();

        static ErrorCatalog()
        {
            RegisterAll();
        }

        /// <summary> 按错误码查询定义；未注册返回 false（调用方降级为未知码处理） </summary>
        public static bool TryGet(string code, out ErrorDefinition definition)
        {
            definition = null;
            return code != null && Definitions.TryGetValue(code, out definition);
        }

        /// <summary> 按错误码查询定义；未注册返回 null </summary>
        public static ErrorDefinition Get(string code) =>
            code != null && Definitions.TryGetValue(code, out var def) ? def : null;

        /// <summary> 全部已注册定义（只读视图，供测试完整性校验与文档统计） </summary>
        public static IReadOnlyCollection<ErrorDefinition> All => Definitions.Values;

        /// <summary> 已注册错误码数量 </summary>
        public static int Count => Definitions.Count;

        private static void RegisterAll()
        {
            // —— 请求级 ——
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.REQUEST_NULL, Category = ErrorCategory.Request, Severity = ErrorSeverity.Error,
                Summary = "生成请求为空",
                Hint = "调用方缺陷：检查调度器入参，禁止传入 null 请求"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.REQUEST_PROMPT_EMPTY, Category = ErrorCategory.Request, Severity = ErrorSeverity.Error,
                Summary = "生成描述为空或仅空白字符",
                Hint = "在「关卡描述」输入框填写具体描述后重试"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.REQUEST_PROMPT_TOO_LONG, Category = ErrorCategory.Request, Severity = ErrorSeverity.Error,
                Summary = "生成描述超过 2000 字上限",
                Hint = "精简描述至 2000 字以内后重试（可按要点分条描述）"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.REQUEST_TEMPLATE_NOT_FOUND, Category = ErrorCategory.Request, Severity = ErrorSeverity.Error,
                Summary = "指定的关卡模板不存在",
                Hint = "检查 TemplateId 拼写，或确认 Assets/Settings/Templates 下存在对应模板资产"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.REQUEST_NO_CONTENT, Category = ErrorCategory.Request, Severity = ErrorSeverity.Error,
                Summary = "地形/道具/任务三个生成开关全关，无可生成内容",
                Hint = "开启至少一个生成开关（generateTerrain / generateProps / generateTasks）"
            });

            // —— 数据级 ——
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.DATA_NULL, Category = ErrorCategory.Data, Severity = ErrorSeverity.Error,
                Summary = "校验数据为空（LevelData 为 null）",
                Hint = "检查生成器是否产出 LevelData；空数据属生成器异常，查看报告中的原始响应"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.DATA_POSITION_OUT_OF_RANGE, Category = ErrorCategory.Data, Severity = ErrorSeverity.Error,
                Summary = "坐标绝对值超出 ±10000 允许范围",
                Hint = "检查 LLM 输出坐标是否异常放大；在描述中约束坐标语义"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.DATA_NAN_OR_INFINITE, Category = ErrorCategory.Data, Severity = ErrorSeverity.Error,
                Summary = "数值为 NaN 或 Infinity",
                Hint = "检查生成器数值计算与 LLM 输出；NaN 会传染后续布局与烘焙"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.DATA_SCALE_INVALID, Category = ErrorCategory.Data, Severity = ErrorSeverity.Error,
                Summary = "缩放值必须大于 0（零/负缩放物体不可见）",
                Hint = "检查 LLM 输出 scale 字段，约束为正值"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.DATA_TERRAIN_INVALID, Category = ErrorCategory.Data, Severity = ErrorSeverity.Error,
                Summary = "地形尺寸或高度超出允许范围",
                Hint = "检查 terrain 的 width/length/heightScale 输出；模板 OverrideTerrain 可强制覆盖"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.DATA_TASK_ID_EMPTY, Category = ErrorCategory.Data, Severity = ErrorSeverity.Error,
                Summary = "任务 ID 为空",
                Hint = "检查 LLM 输出 tasks 的 taskID 字段；任务必须有唯一 ID"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.DATA_TASK_ID_DUPLICATE, Category = ErrorCategory.Data, Severity = ErrorSeverity.Error,
                Summary = "任务 ID 重复",
                Hint = "检查 LLM 输出 tasks 的 taskID 字段；重复 ID 会导致任务引用错乱"
            });

            // —— 资源 ——
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.RESOURCE_MAPPER_MISSING, Category = ErrorCategory.Resource, Severity = ErrorSeverity.Error,
                Summary = "资源映射服务未注入",
                Hint = "检查 GeneratorServiceInitializer 是否注册 IResourceMapper（依赖 PrefabMapping_Default.asset 存在）"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.RESOURCE_MAPPING_EMPTY, Category = ErrorCategory.Resource, Severity = ErrorSeverity.Error,
                Summary = "资源映射表为空",
                Hint = "在 Assets/Settings/Mappings 配置 PrefabMapping_Default.asset 的逻辑名 → 预制体条目"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.RESOURCE_NAME_EMPTY, Category = ErrorCategory.Resource, Severity = ErrorSeverity.Error,
                Summary = "道具逻辑名为空",
                Hint = "检查 LLM 输出 props 的 prefabLogicalName 字段；逻辑名是资源映射表的 Key"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.RESOURCE_NOT_FOUND, Category = ErrorCategory.Resource, Severity = ErrorSeverity.Error,
                Summary = "逻辑名未命中任何预制体（含模糊匹配兜底）",
                Hint = "在资源映射配置中为该逻辑名添加条目或别名；模糊匹配为包含语义"
            });

            // —— 模板范围 ——
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.PROPS_TOO_MANY, Category = ErrorCategory.Template, Severity = ErrorSeverity.Error,
                Summary = "道具数量超过模板上限",
                Hint = "减少生成描述中的物体数量，或调大模板 MaxPropCount"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.PROPS_TOO_FEW, Category = ErrorCategory.Template, Severity = ErrorSeverity.Error,
                Summary = "道具数量低于模板下限",
                Hint = "增加生成描述中的物体数量，或调小模板 MinPropCount"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.TASKS_TOO_MANY, Category = ErrorCategory.Template, Severity = ErrorSeverity.Error,
                Summary = "任务数量超过模板上限",
                Hint = "减少生成描述中的任务数量，或调大模板 MaxTaskCount"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.TASKS_TOO_FEW, Category = ErrorCategory.Template, Severity = ErrorSeverity.Error,
                Summary = "任务数量低于模板下限",
                Hint = "增加生成描述中的任务数量，或调小模板 MinTaskCount"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.NO_MAIN_TASK, Category = ErrorCategory.Template, Severity = ErrorSeverity.Error,
                Summary = "模板要求存在主线任务，但生成结果没有 IsMainTask=true 的任务",
                Hint = "在生成描述中明确主线目标，或关闭模板 ForceMainTask"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.TERRAIN_MISMATCH, Category = ErrorCategory.Template, Severity = ErrorSeverity.Error,
                Summary = "地形与模板配置不一致",
                Hint = "启用模板 OverrideTerrain 强制地形默认值，或修正 LLM 输出"
            });

            // —— 后置 ——
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.POST_ENTITIES_MISSING, Category = ErrorCategory.Post, Severity = ErrorSeverity.Error,
                Summary = "后置校验数据缺失（实体清单为空）",
                Hint = "构建器异常路径：查看构建日志定位实例化中断原因"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.POST_COUNT_MISMATCH, Category = ErrorCategory.Post, Severity = ErrorSeverity.Error,
                Summary = "实体数量与构建报告不一致（漏/多余实例化）",
                Hint = "构建器实例化异常：查看构建日志与 BuiltObjects 填充逻辑"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.POST_ENTITY_NULL, Category = ErrorCategory.Post, Severity = ErrorSeverity.Error,
                Summary = "实体清单含空引用",
                Hint = "构建器实例化中断或异常清理的痕迹：查看构建日志定位"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.POST_COMPONENT_MISSING, Category = ErrorCategory.Post, Severity = ErrorSeverity.Error,
                Summary = "实体缺少绑定配置声明的组件",
                Hint = "检查组件绑定配置的 ComponentTypeName 是否可解析，或实体是否被错误替换"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.POST_FLOAT_UNSUPPORTED, Category = ErrorCategory.Post, Severity = ErrorSeverity.Error,
                Summary = "实体悬空无地面支撑（逻辑上不可达）",
                Hint = "检查布局/地面贴合阶段日志；实体应被地面物理支撑"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.POST_GROUND_MISSING, Category = ErrorCategory.Post, Severity = ErrorSeverity.Warning,
                Summary = "未探测到地面碰撞体，已跳过逻辑可达性检查",
                Hint = "编辑场景无地面碰撞体属正常降级；如需可达性校验请添加地面"
            });

            // —— 解析 ——
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.PARSE_FAILED, Category = ErrorCategory.Parsing, Severity = ErrorSeverity.Error,
                Summary = "LLM 输出不是合法 JSON",
                Hint = "查看报告中的原始响应并重试；可简化描述降低输出复杂度"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.NOT_OBJECT, Category = ErrorCategory.Parsing, Severity = ErrorSeverity.Error,
                Summary = "LLM 输出顶层不是 JSON 对象",
                Hint = "查看原始响应；模型应输出 generate_level 的 JSON 对象"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.PARSE_FALLBACK, Category = ErrorCategory.Parsing, Severity = ErrorSeverity.Warning,
                Summary = "字段缺失/非法值使用默认值兜底",
                Hint = "查看警告明细中的字段定位；缺失字段按默认值处理不中断生成"
            });

            // —— LLM 链路 ——
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.NO_API_KEY, Category = ErrorCategory.Llm, Severity = ErrorSeverity.Error,
                Summary = "未配置 DeepSeek API Key",
                Hint = "在窗口「API 设置」输入并保存 Key（仅存本机 EditorPrefs，不进项目文件）"
            });
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.LLM_ERROR, Category = ErrorCategory.Llm, Severity = ErrorSeverity.Error,
                Summary = "LLM 服务链路错误（网络/HTTP/API 业务/解析）",
                Hint = "按错误消息分类处理：网络问题检查代理（HTTPS_PROXY 环境变量）、HTTP 检查状态码、业务错误检查 Key 余额与额度"
            });

            // —— 基础设施 ——
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.VALIDATOR_ERROR, Category = ErrorCategory.Pipeline, Severity = ErrorSeverity.Error,
                Summary = "校验器自身异常",
                Hint = "查看校验器实现与目标数据类型；单个校验器异常不打断聚合链"
            });

            // —— 演示 ——
            Register(new ErrorDefinition
            {
                Code = ErrorCodes.DEMO_FAIL, Category = ErrorCategory.Pipeline, Severity = ErrorSeverity.Error,
                Summary = "模拟生成器演示失败路径",
                Hint = "仅演示通道（MockGenerator）：提示词含“失败”触发，非真实缺陷"
            });
        }

        private static void Register(ErrorDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.Code)) return;
            Definitions[def.Code] = def; // 重复注册后者覆盖（防御）
        }
    }
}
