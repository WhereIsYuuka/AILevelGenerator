using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;

namespace AILevelGenerator.Runtime.Interfaces
{
    public interface IGenerator
    {
        /// <summary>
        /// 生成器接口,屏蔽具体 LLM 实现
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        Task<GenerationRequest> GenerateAsync(GenerationRequest request);
    }
}