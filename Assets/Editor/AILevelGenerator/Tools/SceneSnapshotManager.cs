using System;
using System.IO;
using AILevelGenerator.Editor.Builders;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Utilities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
// Debug 歧义：System.Diagnostics.Debug 未使用，此处仅作防御性统一（与项目内其他 Editor 文件一致）
using Debug = UnityEngine.Debug;

namespace AILevelGenerator.Editor.Tools
{
    /// <summary>
    /// 场景级快照管理器（第四周-Day1）：全量回滚 = 场景文件级原子还原。
    /// 职责：
    ///   1. 生成前 CreateSnapshot：SaveScene(saveAsCopy) 保存场景副本到 Temp/GenerateSnapshot.unity，
    ///      零副作用（不改变当前场景路径、不清 dirty 标记——saveAsCopy 重载的官方语义）；
    ///   2. 全量回滚 RollbackToSnapshot：OpenScene(Single) 原子重载快照（生成物体随场景卸载全灭）
    ///      → NavMeshData 存在时自动重烘焙（快照数据是场景子资源、几何一致故结果一致，恢复"NavMesh 保留"）
    ///      → SaveScene 回写原场景文件（场景 path 从 Temp 归位，否则后续保存会覆盖快照文件）
    ///      → 删除临时快照文件（无残留）→ 恢复快照时刻 dirty 语义；
    ///   3. 丢弃快照 DiscardSnapshot：临时文件删除 + 状态归零（快照作废）。
    /// 与增量回滚（RollbackManager，物体级分帧删除）互为两级回滚体系。
    /// 单例语义：操作对象是"编辑器当前场景"这一全局资源；经 ServiceLocator 注册（窗口不 new 业务类）。
    /// 边界：播放模式一律拒绝；未保存场景（untitled）拒绝快照（SaveScene 可能弹对话框阻塞自动化）；
    /// 回滚前校验调度器非 IsBusy 由调用方（窗口/菜单）负责，本类不感知调度器。
    /// </summary>
    public class SceneSnapshotManager : ISceneSnapshotManager
    {
        /// <summary>
        /// 快照文件路径（项目相对路径）。
        /// 注意：必须位于 Assets/ 下 —— EditorSceneManager.OpenScene 只接受资产库内的场景文件
        /// （曾用 Temp/ 目录，SaveScene 能写但 OpenScene 抛 "Cannot open scene"）；Assets/Temp/ 已在 .gitignore 忽略。
        /// </summary>
        public const string SnapshotRelativePath = "Assets/Temp/GenerateSnapshot.unity";

        private static SceneSnapshotManager _instance;

        /// <summary> 全局单例（编辑器会话内唯一，操作全局场景资源） </summary>
        public static SceneSnapshotManager Instance => _instance ??= new SceneSnapshotManager();

        private readonly SnapshotStateTracker _tracker = new();

        private SceneSnapshotManager() { }

        /// <summary> 是否持有有效快照（状态登记 + 文件存在性双重校验，防外部误删后回滚坏文件） </summary>
        public bool HasSnapshot => _tracker.HasSnapshot && File.Exists(SnapshotRelativePath);

        public string SnapshotPath => _tracker.SnapshotPath;

        public string OriginalScenePath => _tracker.OriginalScenePath;

        public bool CreateSnapshot()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[快照] 播放模式中禁止创建快照");
                return false;
            }

            var active = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path))
            {
                Debug.LogWarning("[快照] 当前场景尚未保存过（untitled），无法创建快照，请先保存场景（生成仍可继续，取消/失败走增量回滚兜底）");
                return false;
            }

            try
            {
                // 目录防御创建（Assets/Temp 首次生成前不存在）
                var snapshotDir = Path.GetDirectoryName(SnapshotRelativePath);
                if (!string.IsNullOrEmpty(snapshotDir) && !Directory.Exists(snapshotDir))
                    Directory.CreateDirectory(snapshotDir);

                // saveAsCopy: true —— 只写文件副本，不改变当前场景路径、不清 dirty 标记（官方文档语义，快照零副作用）
                if (!EditorSceneManager.SaveScene(active, SnapshotRelativePath, true))
                {
                    Debug.LogError($"[快照] 保存场景副本失败：{SnapshotRelativePath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[快照] 保存场景副本异常：{ex.Message}");
                return false;
            }

            // 快照时刻是否有烘焙 NavMesh 数据（运行时注册，不入场景文件）——回滚时据此决定是否重烘焙
            var hasNavMesh = HasNavMeshData();
            var registered = _tracker.Create(active.path, SnapshotRelativePath, active.isDirty, hasNavMesh);
            if (registered)
                Debug.Log($"[快照] 已创建生成前快照：{active.path} → {SnapshotRelativePath}" +
                          (active.isDirty ? "（快照时刻场景含未保存修改，已一并入快照）" : "") +
                          (hasNavMesh ? "（快照时刻已有 NavMesh 数据，回滚后自动重烘焙）" : ""));
            return registered;
        }

        public bool RollbackToSnapshot(bool rebakeNavMesh = true)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[快照] 播放模式中禁止回滚，请先停止播放");
                return false;
            }
            if (!HasSnapshot)
            {
                Debug.LogWarning("[快照] 当前没有有效快照，无法回滚（快照已被消费/丢弃，或临时文件已不存在）");
                return false;
            }

            var originalPath = OriginalScenePath;
            if (string.IsNullOrEmpty(originalPath))
            {
                Debug.LogWarning("[快照] 快照时刻场景未保存过，无回写目标，回滚被拒绝");
                return false;
            }

            try
            {
                // 1. 原子重载快照（Single 替换当前场景）：生成物体、光照、层级随场景文件整体还原
                var loaded = EditorSceneManager.OpenScene(SnapshotPath, OpenSceneMode.Single);

                // 2. NavMesh 保留：烘焙数据是运行时 AddNavMeshData 注册的（不入场景文件），OpenScene 卸载后注册丢失。
                //    以快照时刻的检测结果（tracker.HasNavMeshData）为准——几何与快照时刻一致，重烘焙结果即快照时刻数据。
                if (rebakeNavMesh && _tracker.HasNavMeshData)
                {
                    EditorUtility.DisplayProgressBar("AI 关卡生成", "回滚后重烘焙 NavMesh（恢复快照时刻寻路数据）...", 0.99f);
                    try
                    {
                        new NavMeshBaker().BakeGlobal(new NavMeshBakeTracker());
                    }
                    finally
                    {
                        EditorUtility.ClearProgressBar();
                    }
                }

                // 3. 回写原场景文件：OpenScene 后场景 path 指向快照文件，必须归位（否则用户后续保存会覆盖快照文件）
                if (!EditorSceneManager.SaveScene(loaded, originalPath))
                {
                    Debug.LogError($"[快照] 回写原场景文件失败：{originalPath}");
                    EditorSceneManager.OpenScene(originalPath, OpenSceneMode.Single); // 兜底：原文件未被改写，直接恢复
                    return false;
                }

                // 4. 恢复快照时刻 dirty 语义（SaveScene 会清 dirty 标记，快照时刻若 dirty 则恢复，用户未保存修改不丢失标记）
                if (_tracker.WasSceneDirty) EditorSceneManager.MarkSceneDirty(loaded);

                // 5. 清理临时快照资产（无残留；DeleteAsset 同时删除 .meta 并刷新资产库）+ 状态归零
                if (!AssetDatabase.DeleteAsset(SnapshotRelativePath))
                    File.Delete(SnapshotRelativePath); // 兜底：资产库删除失败（理论上不发生）时直接删文件
                _tracker.TryRollback();

                Debug.Log($"[快照] 已回滚到生成前快照：{originalPath} 已恢复至快照时刻（临时文件已删除）");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[快照] 回滚异常：{ex.Message}\n{ex.StackTrace}");
                // 兜底恢复：原场景文件自快照创建后从未被改写，OpenScene 失败时仍完好，直接恢复
                try
                {
                    EditorSceneManager.OpenScene(originalPath, OpenSceneMode.Single);
                    Debug.Log("[快照] 已通过兜底路径恢复原场景（快照文件保留，可重试）");
                }
                catch (Exception inner)
                {
                    Debug.LogError($"[快照] 兜底恢复原场景也失败：{inner.Message}");
                }
                return false;
            }
        }

        public bool DiscardSnapshot()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[快照] 播放模式中禁止丢弃快照");
                return false;
            }
            if (!HasSnapshot)
            {
                Debug.LogWarning("[快照] 当前没有有效快照，无需丢弃");
                return false;
            }

            if (!AssetDatabase.DeleteAsset(SnapshotRelativePath))
                File.Delete(SnapshotRelativePath); // 兜底：资产库删除失败（理论上不发生）时直接删文件
            _tracker.TryDiscard();
            Debug.Log("[快照] 已丢弃快照（临时文件已删除，生成物体保留在场景中）");
            return true;
        }

        /// <summary>
        /// 检测当前是否有已烘焙的 NavMesh 数据。
        /// FindObjectsOfTypeAll 返回内存中全部已加载对象（含运行时注册、未挂 GameObject 的对象），
        /// 本项目烘焙经 NavMeshBaker 运行时 AddNavMeshData 注册 → 一定可被检测到。
        /// </summary>
        private static bool HasNavMeshData()
        {
            return Resources.FindObjectsOfTypeAll<NavMeshData>().Length > 0;
        }
    }
}
