using System;
using System.Collections.Generic;
using UnityEngine;

namespace AILevelGenerator.Runtime.Data
{
    /// <summary>
    /// 后置校验输入 DTO（第四周-Day3）：构建完成后由调度器组装，
    /// 供 Post 阶段校验器检查实体空引用、组件完整性、逻辑可达性。
    /// Entities 由构建器在 Finish 前填充（LevelBuildResult.BuiltObjects），
    /// ExpectedCount 对应 InstantiatedCount——数量不一致视为构建异常（多/漏实例化）。
    /// </summary>
    [Serializable]
    public class PostBuildData
    {
        /// <summary> 本次生成的全部实体（构建器 _instances 完整清单），可为 null（构建器异常路径） </summary>
        public List<GameObject> Entities;

        /// <summary> 期望实体数（= 构建结果 InstantiatedCount）；0 且空列表视为合法（无实体关卡） </summary>
        public int ExpectedCount;
    }
}
