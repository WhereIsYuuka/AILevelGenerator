using System.Collections.Generic;
using AILevelGenerator.Runtime.Interfaces.Templates;

namespace AILevelGenerator.Runtime.Interfaces
{
    /// <summary>
    /// 模板提供者 策划可扩展
    /// </summary>
    public interface ITemplateProvider
    {
        IReadOnlyList<LevelTemplate> GetLevelTemplates();
        LevelTemplate GetTemplateById(string id);
    }
}