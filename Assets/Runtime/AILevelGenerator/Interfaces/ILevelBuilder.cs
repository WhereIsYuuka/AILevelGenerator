using System;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;

namespace AILevelGenerator.Runtime.Interfaces
{
    /// <summary>
    /// 场景构建器接口：把生成结果（LevelData）分帧实例化为场景实体。
    /// 调度器在 LLM 生成成功后调用 BuildAsync，构建成功才算整条生成任务成功。
    /// 实现位于 Editor 程序集（SceneLevelBuilder，依赖 PrefabUtility / EditorApplication.update）。
    /// </summary>
    public interface ILevelBuilder
    {
        /// <summary> 是否正在构建（构建期间可安全 Cancel） </summary>
        bool IsBuilding { get; }

        /// <summary> 构建进度事件（0~1），UI 层订阅用于进度条显示（Day3） </summary>
        event Action<float> ProgressChanged;

        /// <summary>
        /// 分帧构建关卡：内部基于 EditorApplication.update 逐帧实例化，不阻塞主线程。
        /// 同一时间只允许一次构建，重复调用返回失败结果。
        /// </summary>
        /// <param name="levelData">LLM 生成的关卡数据</param>
        /// <param name="options">构建选项（帧预算等）；null 时使用实现默认值</param>
        /// <returns>构建结果（成功/失败/取消 + 实例化数量），永不清零，可安全丢弃</returns>
        Task<LevelBuildResult> BuildAsync(LevelData levelData, LevelBuildOptions options = null);

        /// <summary> 请求取消当前构建（增量删除本次已实例化物体，Day3 接入 UI 取消按钮） </summary>
        void Cancel();
    }
}
