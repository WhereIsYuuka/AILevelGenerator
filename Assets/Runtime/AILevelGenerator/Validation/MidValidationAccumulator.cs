using System.Collections.Generic;
using AILevelGenerator.Runtime.Data;

namespace AILevelGenerator.Runtime.Validation
{
    /// <summary>
    /// 生成中校验（Mid）累积器（第四周-Day3）：构建器每帧批次实例化后调用
    /// ValidateBatch(本帧批次)，校验"已构建前缀"的字段与数值范围。
    /// 设计要点：
    /// 1. **增长前缀而非批次切片**：Props 指向持续增长的同源 List，错误路径的
    ///    props[i] 下标与全量数据逐字一致（切片会破坏全局索引定位，把策划引向错误字段）。
    /// 2. **零逐帧副本**：增长数据只建一次，其余字段（Tasks/Terrain/PlayerStartPosition）
    ///    引用源数据，无逐帧分配；每帧对前缀重验是 O(n²) 量级，百级实体可忽略。
    /// 3. **可插拔**：校验规则全部走 ValidatorRegistry 的 Mid 阶段注册（复用
    ///    DataBoundsValidator / ResourceValidator），核心只做累积调度。
    /// </summary>
    public class MidValidationAccumulator
    {
        private readonly ValidatorRegistry _registry;
        private readonly List<PropPlacement> _growingProps = new();
        private readonly LevelData _growingData;

        /// <summary> 增长前缀数据（Props 为同源增长列表，其余字段引用源数据） </summary>
        public LevelData Data => _growingData;

        /// <param name="registry">校验注册表（可为 null：Mid 校验整体禁用，恒返回合法结果）</param>
        /// <param name="source">构建源数据：Tasks/Terrain/PlayerStartPosition 引用复用（无逐帧副本）</param>
        public MidValidationAccumulator(ValidatorRegistry registry, LevelData source)
        {
            _registry = registry;
            _growingData = new LevelData
            {
                LevelName = source?.LevelName,
                Description = source?.Description,
                PlayerStartPosition = source?.PlayerStartPosition ?? UnityEngine.Vector3.zero,
                Tasks = source?.Tasks,
                Terrain = source?.Terrain,
                Props = _growingProps // 指向增长列表：累积语义 + 全局索引正确
            };
        }

        /// <summary>
        /// 累积并校验本帧批次：AddRange 到增长前缀后跑 Mid 阶段全部已注册校验器。
        /// 失败 → 调用方（构建器）立即终止构建并走全量回滚。
        /// </summary>
        public ValidationResult ValidateBatch(IReadOnlyList<PropPlacement> batch)
        {
            if (_registry == null) return new ValidationResult(); // Mid 未启用：恒通过
            if (batch != null) _growingProps.AddRange(batch);
            return _registry.Run(ValidationStage.Mid, _growingData);
        }
    }
}
