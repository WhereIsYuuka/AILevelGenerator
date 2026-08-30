using System;

namespace AILevelGenerator.Runtime.Data
{
    /// <summary> 构建结果状态 </summary>
    public enum LevelBuildStatus
    {
        /// <summary> 全部 Props 按预算实例化完成 </summary>
        Succeeded,

        /// <summary> 失败（数据为空/无合法预制体/异常） </summary>
        Failed,

        /// <summary> 被取消（本次已实例化物体已增量清理） </summary>
        Cancelled
    }

    /// <summary>
    /// 场景构建结果 DTO：调度器据此判定整条生成任务的成功/失败/取消。
    /// </summary>
    [Serializable]
    public class LevelBuildResult
    {
        public LevelBuildStatus Status;
        public int InstantiatedCount;   // 成功实例化的物体数
        public int SkippedCount;        // 跳过数（未命中资源映射/实例化失败的 Props）
        public string ErrorMessage;
        public float BuildTime;         // 构建耗时（秒）

        public bool IsSuccess => Status == LevelBuildStatus.Succeeded;

        public static LevelBuildResult Succeeded(int instantiated, int skipped, float buildTime) => new()
        {
            Status = LevelBuildStatus.Succeeded,
            InstantiatedCount = instantiated,
            SkippedCount = skipped,
            BuildTime = buildTime
        };

        public static LevelBuildResult Failed(string message, int instantiated = 0) => new()
        {
            Status = LevelBuildStatus.Failed,
            ErrorMessage = message,
            InstantiatedCount = instantiated
        };

        public static LevelBuildResult Cancelled(int instantiated) => new()
        {
            Status = LevelBuildStatus.Cancelled,
            InstantiatedCount = instantiated
        };
    }

    /// <summary>
    /// 构建选项：帧率自适应参数与根物体命名（默认值按 60fps 基准设计）。
    /// </summary>
    [Serializable]
    public class LevelBuildOptions
    {
        /// <summary> 生成根物体名前缀（增量取消/场景识别用，全部实例挂其下） </summary>
        public string RootNamePrefix = "[AI Generated] ";

        public int BudgetWindowSize = 10;    // 滑动平均窗口（帧）
        public float TargetFrameTimeMs = 8f; // 单帧耗时预算（毫秒）：60fps 单帧 16.7ms，留一半余量保证编辑器流畅
        public int BasePerFrame = 3;         // 基准速率：目标帧耗时下的每帧实例化数
        public int MinPerFrame = 1;          // 每帧下限（保证至少推进，长构建不致停滞）
        public int MaxPerFrame = 30;         // 每帧上限（防止极端提速拖垮编辑器）
    }
}
