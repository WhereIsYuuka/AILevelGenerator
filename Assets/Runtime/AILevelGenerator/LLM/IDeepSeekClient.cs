using System.Threading.Tasks;

namespace AILevelGenerator.Runtime.LLM
{
    /// <summary>
    /// DeepSeek 客户端抽象（LLMGenerator 依赖此接口，不耦合具体实现）。
    /// 测试注入 stub/fake 即可覆盖全链路而不碰真实网络。
    /// </summary>
    public interface IDeepSeekClient
    {
        Task<DeepSeekChatResponse> ChatAsync(DeepSeekChatRequest request);
    }
}
