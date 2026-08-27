using System;

namespace AILevelGenerator.RunTime.Data
{
    /// <summary>
    /// 生成请求 DTO
    /// </summary>
    [Serializable]
    public class GenerationRequest
    {
        public string Prompt;   // 自然语言描述
        public string TemplateId;   // 模板唯一标识
        public int RandomSeed;
        public bool GenerateTerrain = true;    // 地形
        public bool GenerateProps = true;  // 道具
        public bool GenerateTasks = true;
    }
}