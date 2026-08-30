using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Parsing;

namespace AILevelGenerator.Runtime.LLM
{
    /// <summary>
    /// DeepSeek Chat Completions API 客户端（OpenAI 兼容，零外部依赖）。
    /// - 构造注入 HttpClient（测试注入 stub HttpMessageHandler，单测不碰真实网络）
    /// - API key 构造注入，绝不落盘/打日志
    /// - 请求体手写序列化（结构固定）；响应体用自研 JsonParser 解析（自举，零依赖）
    /// - 异常四级分类（Network/HttpError/ApiError/Parse），中文错误提示直接可展示给用户
    /// </summary>
    public class DeepSeekClient : IDeepSeekClient
    {
        public const string DefaultBaseUrl = "https://api.deepseek.com/chat/completions";
        public const string DefaultModel = "deepseek-chat";

        private readonly HttpClient _http;
        private string _apiKey; // 非 readonly：支持窗口保存新 Key 后 UpdateApiKey 即时生效
        private readonly string _baseUrl;
        private readonly string _defaultModel;

        public DeepSeekClient(
            string apiKey,
            string baseUrl = DefaultBaseUrl,
            string defaultModel = DefaultModel,
            HttpClient httpClient = null,
            float timeoutSeconds = 60f)
        {
            _apiKey = apiKey ?? string.Empty;
            _baseUrl = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
            _defaultModel = string.IsNullOrEmpty(defaultModel) ? DefaultModel : defaultModel;
            _http = httpClient ?? CreateHttpClient();
            _http.Timeout = TimeSpan.FromSeconds(Math.Max(1f, timeoutSeconds));
        }

        /// <summary>
        /// 创建 HttpClient：优先读取环境变量 HTTPS_PROXY/HTTP_PROXY（Unity 进程默认系统代理解析不稳，
        /// 实测在带本地代理的 Windows 环境必须显式指定 WebProxy 才能访问外网）；无代理则直连。
        /// 测试注入 stub HttpMessageHandler 时绕过本方法。
        /// </summary>
        private static HttpClient CreateHttpClient()
        {
            var envProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY")
                ?? Environment.GetEnvironmentVariable("HTTP_PROXY");
            if (!string.IsNullOrEmpty(envProxy) && Uri.TryCreate(envProxy, UriKind.Absolute, out var proxyUri))
            {
                return new HttpClient(new HttpClientHandler
                {
                    Proxy = new WebProxy(proxyUri),
                    UseProxy = true
                });
            }
            return new HttpClient();
        }

        /// <summary> 更新 API Key（窗口保存新 Key 后即时生效，无需重载域） </summary>
        public void UpdateApiKey(string apiKey)
        {
            _apiKey = apiKey ?? string.Empty;
        }

        public async Task<DeepSeekChatResponse> ChatAsync(DeepSeekChatRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrEmpty(_apiKey))
                throw new DeepSeekException("未配置 DeepSeek API Key（请先在「API 设置」中保存）");

            var body = BuildRequestBody(request);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _baseUrl);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(httpRequest);
            }
            catch (HttpRequestException e)
            {
                throw new NetworkException($"网络请求失败：无法连接到 DeepSeek 服务（{e.Message}）。请检查网络连接或代理设置。", e);
            }
            catch (TaskCanceledException e)
            {
                throw new NetworkException("网络请求超时：DeepSeek 服务响应过慢，请稍后重试。", e);
            }

            using (response)
            {
                var bodyText = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpErrorException(
                        $"HTTP {(int)response.StatusCode}（{response.StatusCode}）：{Truncate(bodyText, 300)}",
                        (int)response.StatusCode);
                }
                return ParseResponse(bodyText);
            }
        }

        /// <summary> 手写序列化请求体（结构固定，避免引入序列化库依赖） </summary>
        private string BuildRequestBody(DeepSeekChatRequest request)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"model\":\"").Append(JsonParser.EscapeString(request.Model ?? _defaultModel)).Append('"');

            if (request.Messages != null && request.Messages.Count > 0)
            {
                sb.Append(",\"messages\":[");
                for (var i = 0; i < request.Messages.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var m = request.Messages[i];
                    sb.Append("{\"role\":\"").Append(JsonParser.EscapeString(m.Role))
                      .Append("\",\"content\":\"")
                      .Append(JsonParser.EscapeString(m.Content ?? string.Empty))
                      .Append("\"}");
                }
                sb.Append(']');
            }

            if (request.Temperature.HasValue)
                sb.Append(",\"temperature\":").Append(request.Temperature.Value.ToString(CultureInfo.InvariantCulture));
            if (request.MaxTokens.HasValue)
                sb.Append(",\"max_tokens\":").Append(request.MaxTokens.Value);

            if (request.Tools != null && request.Tools.Count > 0)
            {
                sb.Append(",\"tools\":[");
                for (var i = 0; i < request.Tools.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var f = request.Tools[i].Function;
                    sb.Append("{\"type\":\"").Append(JsonParser.EscapeString(request.Tools[i].Type))
                      .Append("\",\"function\":{")
                      .Append("\"name\":\"").Append(JsonParser.EscapeString(f.Name)).Append("\",")
                      .Append("\"description\":\"").Append(JsonParser.EscapeString(f.Description)).Append("\",")
                      .Append("\"parameters\":")
                      .Append(string.IsNullOrEmpty(f.ParametersJson) ? "{}" : f.ParametersJson)
                      .Append("}}");
                }
                sb.Append(']');
            }

            if (!string.IsNullOrEmpty(request.ToolChoiceJson))
                sb.Append(",\"tool_choice\":").Append(request.ToolChoiceJson);
            if (!string.IsNullOrEmpty(request.ResponseFormatJson))
                sb.Append(",\"response_format\":").Append(request.ResponseFormatJson);

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary> 响应体 → DTO（JsonParser 容错解析 + 手写映射，字段缺失容忍） </summary>
        private static DeepSeekChatResponse ParseResponse(string bodyText)
        {
            JsonValue root;
            try
            {
                root = JsonParser.Parse(bodyText);
            }
            catch (Exception e)
            {
                throw new ParseException($"DeepSeek 响应不是合法 JSON：{e.Message}", e);
            }

            // 兜底检查 API 业务错误（OpenAI 兼容格式通常在 HTTP 非 2xx，但双保险）
            var errorNode = root.Get("error");
            if (errorNode != null && errorNode.IsObject)
            {
                var code = errorNode.GetString("code", "");
                var message = errorNode.GetString("message", "未知错误");
                throw new ApiErrorException($"DeepSeek API 错误（{code}）：{message}", code);
            }

            var response = new DeepSeekChatResponse
            {
                Id = root.GetString("id", string.Empty),
                RawResponse = bodyText
            };

            var choicesNode = root.Get("choices");
            if (choicesNode != null && choicesNode.IsArray)
            {
                foreach (var c in choicesNode.ArrayValue)
                {
                    var choice = new DeepSeekChoice
                    {
                        Index = c.GetInt("index", 0),
                        FinishReason = c.GetString("finish_reason", string.Empty)
                    };
                    var messageNode = c.Get("message");
                    if (messageNode != null)
                    {
                        var message = new DeepSeekResponseMessage
                        {
                            Role = messageNode.GetString("role", string.Empty),
                            Content = messageNode.GetString("content", null) // 可能为 null（function calling 模式）
                        };
                        var toolCallsNode = messageNode.Get("tool_calls");
                        if (toolCallsNode != null && toolCallsNode.IsArray)
                        {
                            message.ToolCalls = new List<DeepSeekToolCall>();
                            foreach (var t in toolCallsNode.ArrayValue)
                            {
                                var functionNode = t.Get("function");
                                message.ToolCalls.Add(new DeepSeekToolCall
                                {
                                    Id = t.GetString("id", string.Empty),
                                    FunctionName = functionNode != null ? functionNode.GetString("name", string.Empty) : string.Empty,
                                    Arguments = functionNode != null ? functionNode.GetString("arguments", null) : null
                                });
                            }
                        }
                        choice.Message = message;
                    }
                    response.Choices.Add(choice);
                }
            }

            var usageNode = root.Get("usage");
            if (usageNode != null)
            {
                response.Usage = new DeepSeekUsage
                {
                    PromptTokens = usageNode.GetInt("prompt_tokens", 0),
                    CompletionTokens = usageNode.GetInt("completion_tokens", 0),
                    TotalTokens = usageNode.GetInt("total_tokens", 0)
                };
            }

            return response;
        }

        /// <summary> 截断长文本（错误体摘要防刷屏） </summary>
        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "…";
        }
    }
}
