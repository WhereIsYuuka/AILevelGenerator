namespace AILevelGenerator.Runtime.Diagnostics
{
    /// <summary>
    /// 错误码分类（第四周-Day5「错误信息规范」）：日志分组展示与报告统计维度。
    /// 新增错误码必须先归入某一分类（目录注册强制）。
    /// </summary>
    public enum ErrorCategory
    {
        /// <summary> 请求输入合法性（Prompt/模板 ID/生成开关） </summary>
        Request,

        /// <summary> 生成结果数据完整性（空值/数值边界/ID 冲突） </summary>
        Data,

        /// <summary> 资源映射（逻辑名 → 预制体） </summary>
        Resource,

        /// <summary> 模板范围与一致性（数量约束/主线/地形） </summary>
        Template,

        /// <summary> LLM 输出解析（JSON 合法性/字段兜底） </summary>
        Parsing,

        /// <summary> LLM 服务链路（API Key/网络/HTTP/API 业务） </summary>
        Llm,

        /// <summary> 构建后置校验（实体空引用/组件完整性/逻辑可达性） </summary>
        Post,

        /// <summary> 调度与基础设施（校验器自身异常等） </summary>
        Pipeline
    }
}
