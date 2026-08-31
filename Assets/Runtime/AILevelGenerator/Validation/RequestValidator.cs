using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;

namespace AILevelGenerator.Runtime.Validation
{
    /// <summary>
    /// 请求级前置校验（输入合法性）：Prompt 非空/不超长、模板存在、至少一个生成开关开启。
    /// 校验失败时调度器直接拦截本次生成（100% 拦截非法输入，不进入生成链路）。
    /// </summary>
    public class RequestValidator : BaseValidator<GenerationRequest>
    {
        private const int MaxPromptLength = 2000;

        public override ValidationResult Validate(GenerationRequest data, ValidationContext context)
        {
            var result = new ValidationResult();
            if (data == null)
            {
                AddError(result, "REQUEST_NULL", "生成请求为空，已取消");
                return result;
            }

            // Prompt：非空且非超长（消息含"缺少描述"字样，与调度器无注册表时的内联提示同族，便于日志断言复用）
            if (string.IsNullOrWhiteSpace(data.Prompt))
                AddError(result, "REQUEST_PROMPT_EMPTY", "生成请求缺少描述（Prompt 为空或仅空白字符），已取消", "prompt");
            else if (data.Prompt.Length > MaxPromptLength)
                AddError(result, "REQUEST_PROMPT_TOO_LONG", $"生成描述过长（{data.Prompt.Length} 字符），超过上限 {MaxPromptLength} 字符", "prompt");

            // 模板存在性：仅当指定了 TemplateId 且模板提供者已注入时校验（未注入降级跳过，不误报）
            if (!string.IsNullOrEmpty(data.TemplateId) && context?.TemplateProvider != null)
            {
                if (context.TemplateProvider.GetTemplateById(data.TemplateId) == null)
                    AddError(result, "REQUEST_TEMPLATE_NOT_FOUND", $"模板不存在：{data.TemplateId}，请检查模板配置", "templateId");
            }

            // 三个生成开关全关：无可生成内容，属于调用方配置错误
            if (!data.GenerateTerrain && !data.GenerateProps && !data.GenerateTasks)
                AddError(result, "REQUEST_NO_CONTENT", "地形/道具/任务三个生成开关均为关闭状态，无可生成内容", "generateTerrain|generateProps|generateTasks");

            return result;
        }
    }
}
