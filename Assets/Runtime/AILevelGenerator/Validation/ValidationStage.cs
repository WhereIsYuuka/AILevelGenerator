namespace AILevelGenerator.Runtime.Validation
{
    /// <summary>
    /// 校验阶段（三层校验体系的调度维度）：
    /// Pre = 请求/数据前置校验（第四周-Day2，生成前与构建前拦截）；
    /// Mid/Post = 生成中/后置校验（第四周-Day3：构建中 Mid 累积器与构建后 PostBuildValidator 已按阶段接入）。
    /// 同一阶段下请求级与数据级校验器通过"数据类型过滤"共存（见 ValidatorRegistry.Run）。
    /// </summary>
    public enum ValidationStage
    {
        Pre,
        Mid,
        Post
    }
}
