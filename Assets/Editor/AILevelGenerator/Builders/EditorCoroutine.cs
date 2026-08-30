using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AILevelGenerator.Editor.Builders
{
    /// <summary>
    /// 轻量分帧执行器：基于 EditorApplication.update 驱动 IEnumerator（不引官方 EditorCoroutine 包）。
    /// 用法：EditorCoroutine.Start(MyRoutine())；
    /// 支持 yield return null（下一帧继续）与 yield return new EditorWaitForSeconds(t)（实时等待）。
    /// 协程内异常被捕获、记录 Error 后自动停止，保证编辑器不因单次故障被中断。
    /// </summary>
    public sealed class EditorCoroutine
    {
        private static readonly List<EditorCoroutine> Active = new();
        private static bool _hooked;

        private readonly IEnumerator _routine;

        /// <summary> 是否仍在运行（Stop 或自然结束后为 false） </summary>
        public bool IsRunning { get; private set; }

        private EditorCoroutine(IEnumerator routine) => _routine = routine;

        /// <summary> 启动协程；routine 为空返回 null，调用方自行判空 </summary>
        public static EditorCoroutine Start(IEnumerator routine)
        {
            if (routine == null) return null;
            EnsureTickHooked();
            var coroutine = new EditorCoroutine(routine) { IsRunning = true };
            Active.Add(coroutine);
            return coroutine;
        }

        /// <summary> 停止协程（从驱动列表中移除，后续不再推进） </summary>
        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            Active.Remove(this);
        }

        private static void EnsureTickHooked()
        {
            if (_hooked) return;
            EditorApplication.update += Tick;
            _hooked = true;
            // 编辑器退出时注销，避免退出流程中驱动残留协程
            EditorApplication.quitting += () =>
            {
                EditorApplication.update -= Tick;
                _hooked = false;
            };
        }

        /// <summary>
        /// 每帧驱动全部活跃协程。倒序遍历：Stop 会从列表移除，倒序避免索引越界。
        /// 自定义等待指令（EditorWaitForSeconds）时间未到则本帧不推进。
        /// </summary>
        private static void Tick()
        {
            for (var i = Active.Count - 1; i >= 0; i--)
            {
                var coroutine = Active[i];
                var current = coroutine._routine.Current;
                if (current is EditorWaitForSeconds wait && EditorApplication.timeSinceStartup < wait.EndTime)
                    continue; // 等待中，本帧不推进

                try
                {
                    if (!coroutine._routine.MoveNext())
                        coroutine.Stop(); // 协程自然结束
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AI Generator] EditorCoroutine 异常终止：{ex.Message}\n{ex.StackTrace}");
                    coroutine.Stop();
                }
            }
        }
    }

    /// <summary>
    /// 协程等待指令：基于 EditorApplication.timeSinceStartup 的实时等待
    /// （编辑器暂停/切后台也会计时，符合编辑器工具预期）。
    /// </summary>
    public sealed class EditorWaitForSeconds
    {
        /// <summary> 等待截止时间（EditorApplication.timeSinceStartup 时间轴） </summary>
        public float EndTime { get; }

        public EditorWaitForSeconds(float seconds) => EndTime = (float)EditorApplication.timeSinceStartup + seconds;
    }
}
