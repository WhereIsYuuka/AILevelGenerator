using System.Collections;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Utilities;
using UnityEngine;

namespace AILevelGenerator.Editor.Builders
{
    /// <summary>
    /// 回滚管理器（Day3 最小实现）：增量取消 —— 分帧销毁被追踪的生成根物体。
    /// 复用 EditorCoroutine 分帧机制（每帧最多销毁一批子物体），删除过程不阻塞编辑器（验收：取消无卡顿）。
    /// 场景原有内容完全保留（只删登记过的生成根，不涉及快照）。
    /// 第四周扩展：场景快照保存/恢复作为全量回滚兜底（本类届时补充）。
    /// </summary>
    public class RollbackManager : IRollbackManager
    {
        /// <summary> 单帧销毁子物体上限（超过则让出一帧，保证编辑器流畅） </summary>
        private const int MaxDestroyPerFrame = 4;

        private readonly RollbackTracker _tracker = new();
        private EditorCoroutine _coroutine;

        public bool IsRollingBack => _coroutine != null;

        public void TrackRoot(GameObject root) => _tracker.Track(root);

        public void RollbackLastGeneration()
        {
            if (IsRollingBack) return; // 删除进行中忽略重复请求
            var root = _tracker.TakeLast();
            if (root == null) return; // 无登记：安全空操作

            _coroutine = EditorCoroutine.Start(DestroyRoutine(root));
            if (_coroutine == null)
            {
                // 分帧启动失败兜底：同步删除（理论上不发生，保底不残留）
                Object.DestroyImmediate(root);
                _coroutine = null;
            }
        }

        /// <summary>
        /// 增量删除全部已登记生成根（第四周-Day1）：分帧逐个删除（每帧一批子物体）。
        /// 与 RollbackLastGeneration（取消：仅最近一次）并存 —— 用于"清空全部生成"或全量回滚前的补充清理；
        /// 场景级快照回滚（SceneSnapshotManager.OpenScene）是更彻底的全量手段，三者互为两级回滚体系。
        /// </summary>
        public void RollbackAllGenerated()
        {
            if (IsRollingBack) return; // 删除进行中忽略重复请求
            if (_tracker.Count == 0) return; // 无登记：安全空操作

            _coroutine = EditorCoroutine.Start(DestroyAllRoutine());
            if (_coroutine == null)
            {
                // 分帧启动失败兜底：同步删除全部（理论上不发生，保底不残留）
                while (_tracker.Count > 0)
                {
                    var root = _tracker.TakeLast();
                    if (root != null) Object.DestroyImmediate(root);
                }
                _coroutine = null;
            }
        }

        public void ClearAllTracked() => _tracker.Clear();

        /// <summary>
        /// 分帧销毁单个根：先自后向前逐个销毁子物体（每帧一批，yield 让出），
        /// 最后销毁根 —— 大数量删除按帧摊薄，避免一次性 DestroyImmediate 卡顿。
        /// </summary>
        private IEnumerator DestroyRoutine(GameObject root)
        {
            var destroyed = 0;
            while (root != null) // 已销毁对象经 UnityEngine.Object 的 == 重载判空
            {
                if (destroyed >= MaxDestroyPerFrame)
                {
                    destroyed = 0;
                    yield return null; // 让出一帧：删除过程分帧推进
                }
                if (DestroyOneBatch(root, ref destroyed)) break;
            }
            _coroutine = null;
        }

        /// <summary>
        /// 分帧销毁全部已登记根：逐根处理（每根复用单帧批量销毁），全部完成后复位协程标记。
        /// 与 DestroyRoutine 共用 DestroyOneBatch，删除节奏一致（每帧上限 MaxDestroyPerFrame 个）。
        /// </summary>
        private IEnumerator DestroyAllRoutine()
        {
            while (_tracker.Count > 0)
            {
                var root = _tracker.TakeLast();
                if (root == null) continue; // 已被外部销毁的登记：跳过

                var destroyed = 0;
                while (root != null) // 已销毁对象经 UnityEngine.Object 的 == 重载判空
                {
                    if (destroyed >= MaxDestroyPerFrame)
                    {
                        destroyed = 0;
                        yield return null; // 让出一帧：删除过程分帧推进
                    }
                    if (DestroyOneBatch(root, ref destroyed)) break;
                }
            }
            _coroutine = null;
        }

        /// <summary>
        /// 单根单帧销毁一批：有子物体时自后向前销毁一个子物体（未销毁完返回 false），
        /// 无子物体时销毁根（返回 true）。供两个删除协程共用，保证删除节奏一致。
        /// </summary>
        private static bool DestroyOneBatch(GameObject root, ref int destroyed)
        {
            var childCount = root.transform.childCount;
            if (childCount > 0)
            {
                Object.DestroyImmediate(root.transform.GetChild(childCount - 1).gameObject);
                destroyed++;
                return false;
            }
            Object.DestroyImmediate(root); // 无子物体后销毁根
            return true;
        }
    }
}
