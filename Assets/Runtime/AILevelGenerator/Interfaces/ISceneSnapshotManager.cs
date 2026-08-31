namespace AILevelGenerator.Runtime.Interfaces
{
    /// <summary>
    /// 场景级快照服务（第四周-Day1）：生成前保存场景副本，全量回滚时原子重载。
    /// 与增量回滚（IRollbackManager，物体级分帧删除）互为两级回滚体系：
    ///   增量 = 轻量清理本次生成物体；全量 = 场景文件级原子还原（快照时刻 100% 恢复）。
    /// 实现体在 Editor 程序集（需 EditorSceneManager），经 ServiceLocator 获取（窗口不 new 业务类）。
    /// </summary>
    public interface ISceneSnapshotManager
    {
        /// <summary> 当前是否持有有效快照（含文件存在性校验） </summary>
        bool HasSnapshot { get; }

        /// <summary> 快照文件路径（相对项目根，如 Temp/GenerateSnapshot.unity） </summary>
        string SnapshotPath { get; }

        /// <summary> 快照时刻的活动场景路径（回滚后 SaveScene 回写目标；空 = 快照时刻场景未保存过） </summary>
        string OriginalScenePath { get; }

        /// <summary>
        /// 创建生成前快照：SaveScene(saveAsCopy) 保存当前场景副本到 Temp/GenerateSnapshot.unity，
        /// 零副作用（不改变场景路径、不清 dirty 标记）。已存在快照时覆盖。
        /// 播放模式中一律拒绝。失败返回 false（不阻塞生成，增量回滚兜底仍在）。
        /// </summary>
        bool CreateSnapshot();

        /// <summary>
        /// 全量回滚：OpenScene 原子重载快照 → 回写原场景文件（场景路径归位）→ 删除临时快照文件。
        /// 场景含 NavMeshData 时自动重烘焙（快照数据在运行时注册层不随场景加载，几何一致故结果一致）。
        /// 前置校验：非播放模式、非生成中（IsBusy）、快照有效。失败时尝试 OpenScene 原场景兜底恢复。
        /// </summary>
        bool RollbackToSnapshot(bool rebakeNavMesh = true);

        /// <summary> 丢弃快照：删除临时文件并清零状态（快照作废，不可再回滚） </summary>
        bool DiscardSnapshot();
    }
}
