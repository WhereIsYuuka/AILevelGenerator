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
        /// <returns>生成结果（LevelData + Tasks + 校验错误/警告）</returns>
        Task<GenerationResult> GenerateAsync(GenerationRequest request);
    }
}