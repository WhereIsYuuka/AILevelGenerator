using AILevelGenerator.Editor.UI;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Utilities;
using UnityEditor;
using UnityEngine;
// Debug 歧义统一（与项目内其他 Editor 文件一致）
using Debug = UnityEngine.Debug;

namespace AILevelGenerator.Editor.Tools
{
    /// <summary>
    /// 场景级快照菜单（第四周-Day1）：创建/回滚/丢弃三个入口 + 回滚成功后联动调度器与窗口复位。
    /// 菜单路径挂 "Tools/AI Level Generator Tests/"（兄弟菜单，不与窗口叶子项 "Tools/AI Level Generator" 冲突——见 AccuracyTestRunner 注释）。
    /// 回滚链路与窗口内「回滚到快照」按钮完全一致：校验（非播放/非生成中/快照有效）→ 管理器全量还原 → 调度器 ResetToReady → 窗口 ResetUiState。
    /// </summary>
    public static class SceneSnapshotMenu
    {
        [MenuItem("Tools/AI Level Generator Tests/创建生成前快照")]
        public static void CreateSnapshot()
        {
            SceneSnapshotManager.Instance.CreateSnapshot();
            RefreshWindow(reset: false);
        }

        [MenuItem("Tools/AI Level Generator Tests/回滚到生成前快照（场景级）")]
        public static void RollbackToSnapshot()
        {
            var snapshot = SceneSnapshotManager.Instance;
            if (!snapshot.HasSnapshot)
            {
                Debug.LogWarning("[快照] 当前没有有效快照，无法回滚（先点击「创建生成前快照」或窗口「生成关卡」）");
                return;
            }

            // 生成中禁止回滚：生成/构建协程未结束前换场景会破坏构建（协程会在新场景上继续实例化）
            var scheduler = ServiceLocator.Get<IGeneratorScheduler>();
            if (scheduler != null && scheduler.IsBusy)
            {
                Debug.LogWarning("[快照] 生成进行中禁止回滚，请等待完成或先取消");
                return;
            }

            Debug.Log("[快照] 正在回滚到生成前快照（场景将整体恢复到快照时刻）...");
            if (snapshot.RollbackToSnapshot())
            {
                scheduler?.ResetToReady(); // 状态机强制复位（窗口事件链自动刷新状态行/进度条/按钮）
                RefreshWindow(reset: true, "已回滚到生成前快照：场景已恢复至快照时刻，无残留");
            }
            else
            {
                Debug.LogError("[快照] 回滚失败（原场景文件未被改写，可继续工作），详见 Console 日志");
            }
        }

        [MenuItem("Tools/AI Level Generator Tests/丢弃快照")]
        public static void DiscardSnapshot()
        {
            SceneSnapshotManager.Instance.DiscardSnapshot();
            RefreshWindow(reset: false);
        }

        [MenuItem("Tools/AI Level Generator Tests/创建生成前快照", true)]
        public static bool ValidateCreate() => !EditorApplication.isPlaying;

        [MenuItem("Tools/AI Level Generator Tests/回滚到生成前快照（场景级）", true)]
        public static bool ValidateRollback() => !EditorApplication.isPlaying && SceneSnapshotManager.Instance.HasSnapshot;

        [MenuItem("Tools/AI Level Generator Tests/丢弃快照", true)]
        public static bool ValidateDiscard() => !EditorApplication.isPlaying && SceneSnapshotManager.Instance.HasSnapshot;

        /// <summary>
        /// 快照操作后联动窗口（已打开才刷新，避免 GetWindow 隐式创建）：
        /// reset=true → 完整复位界面（清日志 + 状态重放 + 提示，回滚成功路径用）；
        /// reset=false → 仅刷新回滚按钮可用状态（创建/丢弃快照后按钮随快照存在性联动）。
        /// </summary>
        private static void RefreshWindow(bool reset, string message = null)
        {
            if (!EditorWindow.HasOpenInstances<AILevelGeneratorWindow>()) return;
            var window = EditorWindow.GetWindow<AILevelGeneratorWindow>();
            if (window == null) return;
            if (reset) window.ResetUiState(message);
            else window.RefreshSnapshotButton();
        }
    }
}
