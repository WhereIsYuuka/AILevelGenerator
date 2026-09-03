using AILevelGenerator.Runtime.Utilities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AILevelGenerator.Editor.Builders
{
    /// <summary>
    /// NavMesh 播放模式守卫（第三周-Day5）：进入播放模式时自动重新烘焙全局 NavMesh。
    /// 背景：NavMesh.AddNavMeshData 注册的是**运行时数据**，域重载（脚本重编译/进出播放模式）
    /// 会将其全部清空——若用户「生成关卡 → 直接进播放模式」，agent 初始化时无 NavMesh，
    /// 控制台报 "Failed to create agent because there is no valid NavMesh"。
    /// 修复：EnteredPlayMode 回调里用 NavMeshBaker 重新烘焙（同样排除本次生成实体），
    /// 保证播放模式下寻路永远可用。
    /// </summary>
    [InitializeOnLoad]
    public static class NavMeshPlayModeGuard
    {
        static NavMeshPlayModeGuard()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;

            try
            {
                var tracker = new NavMeshBakeTracker();
                var ok = new NavMeshBaker().BakeGlobal(tracker, FindGeneratedRoot());
                if (ok)
                    Debug.Log($"[AI Generator] 播放模式守卫：已重新烘焙全局 NavMesh（{tracker.Message}），实体可正常寻路");
                else
                    Debug.LogWarning($"[AI Generator] 播放模式守卫：{tracker.Message}（场景实体仍可生成，仅寻路不可用）");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[AI Generator] 播放模式守卫烘焙异常：{ex.Message}");
            }
        }

        /// <summary> 查找场景中的生成根（[AI Generated] 前缀、无父物体），排除其下实体参与烘焙 </summary>
        private static Transform FindGeneratedRoot()
        {
            foreach (var go in Object.FindObjectsOfType<GameObject>())
            {
                if (go.transform.parent == null && go.name.StartsWith("[AI Generated]"))
                    return go.transform;
            }
            return null;
        }
    }
}
