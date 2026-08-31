namespace AILevelGenerator.Runtime.Utilities
{
    /// <summary>
    /// 场景快照生命周期追踪器（纯逻辑，不依赖编辑器 API，可独立单元测试）。
    /// 记录"当前是否有可用快照"及其元数据（原场景路径/快照路径/快照时刻 dirty 标记）。
    /// 与 RollbackTracker 同模式：数据在 Runtime 可测，真实文件操作（SaveScene/OpenScene）在 Editor 侧执行。
    /// 语义：重复 Create = 覆盖（生成前总是创建新快照）；Rollback/Discard 成功后状态归零（快照文件由 Editor 侧负责删除）。
    /// </summary>
    public class SnapshotStateTracker
    {
        /// <summary> 是否持有有效快照（Rollback/Discard 成功后归零） </summary>
        public bool HasSnapshot { get; private set; }

        /// <summary> 快照时刻的活动场景路径（回滚后 SaveScene 回写目标；空 = 快照时刻场景未保存过） </summary>
        public string OriginalScenePath { get; private set; }

        /// <summary> 快照文件路径（相对项目根，如 Temp/GenerateSnapshot.unity） </summary>
        public string SnapshotPath { get; private set; }

        /// <summary> 快照时刻场景是否处于 dirty（未保存修改）状态，回滚回写后据此恢复标记语义 </summary>
        public bool WasSceneDirty { get; private set; }

        /// <summary>
        /// 快照时刻场景是否已有烘焙 NavMesh 数据（运行时 AddNavMeshData 注册，不入场景文件）。
        /// 回滚时据此刻意重烘焙：烘焙数据不随 OpenScene 加载，几何一致故重烘焙结果与快照时刻一致。
        /// </summary>
        public bool HasNavMeshData { get; private set; }

        /// <summary>
        /// 登记新快照。已存在快照时视为覆盖（重新登记，不报错）。
        /// 参数校验失败（路径为空）返回 false 且不改变状态。
        /// </summary>
        public bool Create(string originalScenePath, string snapshotPath, bool wasSceneDirty, bool hasNavMeshData = false)
        {
            if (string.IsNullOrEmpty(snapshotPath)) return false;

            OriginalScenePath = originalScenePath ?? string.Empty;
            SnapshotPath = snapshotPath;
            WasSceneDirty = wasSceneDirty;
            HasNavMeshData = hasNavMeshData;
            HasSnapshot = true;
            return true;
        }

        /// <summary> 回滚消费快照：仅 HasSnapshot 时成功（状态归零并返回 true），否则返回 false </summary>
        public bool TryRollback()
        {
            if (!HasSnapshot) return false;
            ResetState();
            return true;
        }

        /// <summary> 丢弃快照：仅 HasSnapshot 时成功（状态归零并返回 true），否则返回 false </summary>
        public bool TryDiscard()
        {
            if (!HasSnapshot) return false;
            ResetState();
            return true;
        }

        private void ResetState()
        {
            HasSnapshot = false;
            OriginalScenePath = null;
            SnapshotPath = null;
            WasSceneDirty = false;
            HasNavMeshData = false;
        }
    }
}
