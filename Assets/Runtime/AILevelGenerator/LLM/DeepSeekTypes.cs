using System;
using System.Collections.Generic;

namespace AILevelGenerator.Runtime.LLM
{
    /// <summary> 聊天消息（OpenAI 兼容格式，DeepSeek 同构） </summary>
    public class DeepSeekMessage
    {
        public string Role;   // system / user / assistant
        public string Content;
    }

    /// <summary> Function Calling 工具函数定义（parameters 为原始 JSON Schema 文本，直接嵌入请求体） </summary>
    public class DeepSeekToolFunction
    {
        public string Name;
        public string Description;
        public string ParametersJson; // 原始 JSON Schema 字符串
    }

    /// <summary> Function Calling 工具条目 </summary>
    public class DeepSeekTool
    {
        public const string TypeFunction = "function";
        public string Type = TypeFunction;
        public DeepSeekToolFunction Function;
    }

    /// <summary>
    /// 聊天补全请求。可选字段（Temperature/MaxTokens/Tools/ToolChoiceJson/ResponseFormatJson）
    /// 未设置时不写入请求体（双重约束开关由调用方决定）。
    /// ToolChoiceJson / ResponseFormatJson 为原始 JSON 文本（如 {"type":"json_object"}），直接嵌入。
    /// </summary>
    public class DeepSeekChatRequest
    {
        public string Model; // 缺省时用客户端默认模型
        public List<DeepSeekMessage> Messages;
        public float? Temperature;
        public int? MaxTokens;
        public List<DeepSeekTool> Tools;   // Function Calling 工具
        public string ToolChoiceJson;      // 如 {"type":"function","function":{"name":"generate_level"}}
        public string ResponseFormatJson;  // 如 {"type":"json_object"}
    }

    /// <summary> 模型返回的工具调用（消息内） </summary>
    public class DeepSeekToolCall
    {
        public string Id;
        public string FunctionName;
        public string Arguments; // JSON 字符串（结构化生成的核心载体）
    }

    /// <summary> 响应消息（content 或 tool_calls 二选一） </summary>
    public class DeepSeekResponseMessage
    {
        public string Role;
        public string Content;
        public List<DeepSeekToolCall> ToolCalls;
    }

    public class DeepSeekChoice
    {
        public int Index;
        public DeepSeekResponseMessage Message;
        public string FinishReason;
    }

    public class DeepSeekUsage
    {
        public int PromptTokens;
        public int CompletionTokens;
        public int TotalTokens;
    }

    /// <summary> 聊天补全响应（含原始响应体供调试/回显） </summary>
    public class DeepSeekChatResponse
    {
        public string Id;
        public List<DeepSeekChoice> Choices = new();
        public DeepSeekUsage Usage;
        public string RawResponse;
    }

    // —— 异常分类（全部带中文 FriendlyMessage，网络/服务问题可直接提示用户） ——

    /// <summary> DeepSeek 调用异常基类 </summary>
    public class DeepSeekException : Exception
    {
        public DeepSeekException(string message, Exception innerException = null) : base(message, innerException)
        {
        }

        /// <summary> 面向用户的中文提示（默认即 Message，子类可细化） </summary>
        public virtual string FriendlyMessage => Message;
    }

    /// <summary> 网络层失败（连不上/超时） </summary>
    public class NetworkException : DeepSeekException
    {
        public NetworkException(string message, Exception innerException = null) : base(message, innerException)
        {
        }
    }

    /// <summary> HTTP 非 2xx（服务端拒绝，含状态码与错误体摘要） </summary>
    public class HttpErrorException : DeepSeekException
    {
        public int StatusCode { get; }

        public HttpErrorException(string message, int statusCode = 0, Exception innerException = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
        }
    }

    /// <summary> HTTP 2xx 但响应体含 error 字段（API 业务错误） </summary>
    public class ApiErrorException : DeepSeekException
    {
        public string ErrorCode { get; }

        public ApiErrorException(string message, string errorCode = "", Exception innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }

    /// <summary> 响应体不是合法 JSON 或结构不符合预期 </summary>
    public class ParseException : DeepSeekException
    {
        public ParseException(string message, Exception innerException = null) : base(message, innerException)
        {
        }
    }
}
