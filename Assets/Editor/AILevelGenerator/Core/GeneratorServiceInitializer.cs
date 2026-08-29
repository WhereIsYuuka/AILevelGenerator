using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Scheduling;
using AILevelGenerator.Runtime.Utilities;
using UnityEditor;

namespace AILevelGenerator.Editor.Core
{
    /// <summary>
    /// 编辑器启动时把核心服务注册进 ServiceLocator（域加载时执行，必然早于窗口 CreateGUI）。
    /// 当前注册 MockGenerator 占位；Day6 接入真实 LLM 时仅需替换此处注册的生成器实现。
    /// </summary>
    [InitializeOnLoad]
    public static class GeneratorServiceInitializer
    {
        static GeneratorServiceInitializer()
        {
            var generator = new MockGenerator();
            ServiceLocator.Register<IGenerator>(generator);
            ServiceLocator.Register<IGeneratorScheduler>(new GeneratorScheduler(generator));
        }
    }
}
