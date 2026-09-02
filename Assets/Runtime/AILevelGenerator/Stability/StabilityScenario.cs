namespace AILevelGenerator.Runtime.Stability
{
    /// <summary>
    /// 稳定性测试场景（第四周-Day6/7）：枚举一次生成任务的「注入路径」——
    /// 每种场景代表一条核心链路（成功/各异常点/取消/边界），编排器按场景装配隔离链路并注入异常源。
    /// 纯逻辑枚举 + 中文显示名（可单测）；「每轮期望行为」由 ScenarioRotation 轮换表声明（同样可单测）。
    /// </summary>
    public enum StabilityScenario
    {
        /// <summary> 正常成功：完整链路成功，场景出现生成根 </summary>
        NormalSuccess,

        /// <summary> 生成器抛异常：LLM 阶段崩溃 → LLM_ERROR → Failed（场景零变更，丢弃快照） </summary>
        GeneratorThrows,

        /// <summary> 生成器业务失败：返回 Success=false + DEMO_FAIL → Failed（场景零变更） </summary>
        GeneratorBusinessFail,

        /// <summary> 请求被前置校验拦截：空描述 → 拦截（零副作用：不流转/不建快照/不触发报告） </summary>
        RequestBlocked,

        /// <summary> 资源不存在：数据级前置校验 RESOURCE_NOT_FOUND → Failed（未构建，零变更） </summary>
        ResourceMissing,

        /// <summary> Mid 生成中校验失败：映射表为空 → 首帧 RESOURCE_MAPPING_EMPTY → 构建终止 → 全量回滚 </summary>
        MidValidationFail,

        /// <summary> 构建失败：构建器返回 Failed → 全量回滚 </summary>
        BuildFail,

        /// <summary> 构建器抛异常：构建协程异常 → 全量回滚 </summary>
        BuilderThrows,

        /// <summary> Post 后置校验失败：绑定配置声明组件但实体未挂载 → POST_COMPONENT_MISSING → 全量回滚 </summary>
        PostValidationFail,

        /// <summary> 生成中取消：LLM 结果返回前取消 → 结果丢弃，场景零变更 </summary>
        CancelDuringGenerate,

        /// <summary> 构建中取消：分帧实例化期间取消 → 增量删除本次根，场景恢复 </summary>
        CancelDuringBuild,

        /// <summary> 回滚失败：快照存在但 RollbackToSnapshot 返回 false → 保持 Failed + 如实报告 </summary>
        RollbackFail,

        /// <summary> 0 实体边界：空关卡构建成功（无进度事件 NaN 风险） </summary>
        ZeroEntities,

        /// <summary> NaN 坐标边界：非法坐标被 DataBounds 校验拦截 → Failed（构建器有限性检查的上一层防线） </summary>
        NanCoordinate
    }

    /// <summary> 稳定性场景扩展：中文显示名（日志/汇总文本） </summary>
    public static class StabilityScenarioExtensions
    {
        public static string ToDisplayName(this StabilityScenario scenario) => scenario switch
        {
            StabilityScenario.NormalSuccess => "正常成功",
            StabilityScenario.GeneratorThrows => "生成器异常",
            StabilityScenario.GeneratorBusinessFail => "生成器业务失败",
            StabilityScenario.RequestBlocked => "请求拦截",
            StabilityScenario.ResourceMissing => "资源不存在",
            StabilityScenario.MidValidationFail => "Mid 校验失败",
            StabilityScenario.BuildFail => "构建失败",
            StabilityScenario.BuilderThrows => "构建器异常",
            StabilityScenario.PostValidationFail => "Post 校验失败",
            StabilityScenario.CancelDuringGenerate => "生成中取消",
            StabilityScenario.CancelDuringBuild => "构建中取消",
            StabilityScenario.RollbackFail => "回滚失败",
            StabilityScenario.ZeroEntities => "0 实体",
            StabilityScenario.NanCoordinate => "NaN 坐标",
            _ => scenario.ToString()
        };
    }
}
