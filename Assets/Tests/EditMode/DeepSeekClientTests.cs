using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.LLM;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// DeepSeek 客户端单元测试：stub HttpMessageHandler 模拟 HTTP 层，单测不碰真实网络。
    /// 覆盖：请求体构造（认证头/消息/tools/response_format）、响应解析（content/tool_calls）、
    /// 异常四级分类（Network/HttpError/ApiError/Parse）与未配置 key 检查。
    /// </summary>
    public class DeepSeekClientTests
    {
        /// <summary> 可注入的 HttpMessageHandler stub：记录最后一次请求（body 在发送时捕获为字符串，
        /// 因为客户端会在 ChatAsync 返回后释放 StringContent）</summary>
        private class StubHttpMessageHandler : HttpMessageHandler
        {
            public HttpRequestMessage LastRequest;
            public string LastBody;
            public Func<HttpRequestMessage, HttpResponseMessage> Responder;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                LastBody = request.Content == null ? "" : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (Responder == null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                return Task.FromResult(Responder(request));
            }
        }

        private static StubHttpMessageHandler CreateStub(out DeepSeekClient client, string key = "test-key")
        {
            var stub = new StubHttpMessageHandler();
            client = new DeepSeekClient(key, httpClient: new HttpClient(stub), timeoutSeconds: 5f);
            return stub;
        }

        private static DeepSeekChatRequest SimpleRequest() => new()
        {
            Model = "deepseek-chat",
            Messages = new System.Collections.Generic.List<DeepSeekMessage>
            {
                new() { Role = "system", Content = "你是关卡设计师" },
                new() { Role = "user", Content = "设计一个森林营地" }
            },
            Temperature = 0.7f
        };

        // —— 请求体构造 ——

        [Test]
        public void 请求体_包含认证头与消息与温度()
        {
            var stub = CreateStub(out var client);
            stub.Responder = _ => OkResponse("{}");

            client.ChatAsync(SimpleRequest()).GetAwaiter().GetResult();

            Assert.AreEqual("Bearer test-key", stub.LastRequest.Headers.Authorization.ToString());
            Assert.AreEqual(HttpMethod.Post, stub.LastRequest.Method);
            var body = stub.LastBody;
            StringAssert.Contains("\"model\":\"deepseek-chat\"", body);
            StringAssert.Contains("\"role\":\"system\"", body);
            StringAssert.Contains("你是关卡设计师", body, "中文内容应原样写入请求体（UTF-8）");
            StringAssert.Contains("\"temperature\":0.7", body);
        }

        [Test]
        public void 请求体_包含tools与response_format与tool_choice()
        {
            var stub = CreateStub(out var client);
            stub.Responder = _ => OkResponse("{}");
            var request = SimpleRequest();
            request.Tools = new System.Collections.Generic.List<DeepSeekTool>
            {
                new() { Function = new DeepSeekToolFunction { Name = "generate_level", Description = "生成关卡", ParametersJson = "{\"type\":\"object\"}" } }
            };
            request.ToolChoiceJson = "{\"type\":\"function\",\"function\":{\"name\":\"generate_level\"}}";
            request.ResponseFormatJson = "{\"type\":\"json_object\"}";

            client.ChatAsync(request).GetAwaiter().GetResult();

            var body = stub.LastBody;
            StringAssert.Contains("\"tools\":[", body);
            StringAssert.Contains("\"name\":\"generate_level\"", body);
            StringAssert.Contains("\"parameters\":{\"type\":\"object\"}", body);
            StringAssert.Contains("\"tool_choice\":{\"type\":\"function\"", body);
            StringAssert.Contains("\"response_format\":{\"type\":\"json_object\"}", body);
        }

        [Test]
        public void 请求体_可选字段未设置_不写入请求体()
        {
            var stub = CreateStub(out var client);
            stub.Responder = _ => OkResponse("{}");

            client.ChatAsync(SimpleRequest()).GetAwaiter().GetResult();

            var body = stub.LastBody;
            Assert.IsFalse(body.Contains("tools"), "未设置 Tools 时不应写入 tools 字段");
            Assert.IsFalse(body.Contains("response_format"));
            Assert.IsFalse(body.Contains("max_tokens"));
        }

        [Test]
        public void 请求体_含特殊字符_正确转义()
        {
            var stub = CreateStub(out var client);
            stub.Responder = _ => OkResponse("{}");
            var request = SimpleRequest();
            request.Messages[1].Content = "含\"引号\"和\\反斜杠和\n换行";

            client.ChatAsync(request).GetAwaiter().GetResult();

            var body = stub.LastBody;
            StringAssert.Contains("含\\\"引号\\\"", body);
            StringAssert.Contains("\\\\反斜杠", body);
            StringAssert.Contains("\\n", body);
        }

        // —— 响应解析 ——

        [Test]
        public void 响应_解析content与usage()
        {
            var stub = CreateStub(out var client);
            stub.Responder = _ => OkResponse(
                "{\"id\":\"chatcmpl-1\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"{\\\"name\\\":\\\"营地\\\"}\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15}}");

            var response = client.ChatAsync(SimpleRequest()).GetAwaiter().GetResult();

            Assert.AreEqual("chatcmpl-1", response.Id);
            Assert.AreEqual(1, response.Choices.Count);
            Assert.AreEqual("{\"name\":\"营地\"}", response.Choices[0].Message.Content);
            Assert.AreEqual("stop", response.Choices[0].FinishReason);
            Assert.AreEqual(15, response.Usage.TotalTokens);
        }

        [Test]
        public void 响应_解析tool_calls_arguments为结构化载体()
        {
            var stub = CreateStub(out var client);
            stub.Responder = _ => OkResponse(
                "{\"id\":\"chatcmpl-2\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"generate_level\",\"arguments\":\"{\\\"levelName\\\":\\\"营地\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}");

            var response = client.ChatAsync(SimpleRequest()).GetAwaiter().GetResult();

            var message = response.Choices[0].Message;
            Assert.IsNull(message.Content, "function calling 模式 content 通常为 null");
            Assert.IsNotNull(message.ToolCalls);
            Assert.AreEqual(1, message.ToolCalls.Count);
            Assert.AreEqual("generate_level", message.ToolCalls[0].FunctionName);
            Assert.AreEqual("{\"levelName\":\"营地\"}", message.ToolCalls[0].Arguments);
            Assert.AreEqual("tool_calls", response.Choices[0].FinishReason);
        }

        [Test]
        public void 响应_choices为空_返回空列表不抛异常()
        {
            var stub = CreateStub(out var client);
            stub.Responder = _ => OkResponse("{\"id\":\"x\",\"choices\":[]}");

            var response = client.ChatAsync(SimpleRequest()).GetAwaiter().GetResult();

            Assert.IsEmpty(response.Choices);
        }

        // —— 异常分类 ——

        [Test]
        public void HTTP404_抛HttpErrorException_含状态码与错误体()
        {
            var stub = CreateStub(out var client);
            stub.Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"error\":{\"message\":\"模型不存在\"}}")
            };

            var ex = Assert.Throws<HttpErrorException>(() => client.ChatAsync(SimpleRequest()).GetAwaiter().GetResult());

            Assert.AreEqual(404, ex.StatusCode);
            StringAssert.Contains("404", ex.FriendlyMessage);
            StringAssert.Contains("模型不存在", ex.FriendlyMessage);
        }

        [Test]
        public void 响应含error字段_抛ApiErrorException()
        {
            var stub = CreateStub(out var client);
            stub.Responder = _ => OkResponse("{\"error\":{\"code\":\"rate_limit\",\"message\":\"请求过于频繁\"}}");

            var ex = Assert.Throws<ApiErrorException>(() => client.ChatAsync(SimpleRequest()).GetAwaiter().GetResult());

            Assert.AreEqual("rate_limit", ex.ErrorCode);
            StringAssert.Contains("rate_limit", ex.FriendlyMessage);
            StringAssert.Contains("请求过于频繁", ex.FriendlyMessage);
        }

        [Test]
        public void 响应非JSON_抛ParseException()
        {
            var stub = CreateStub(out var client);
            stub.Responder = _ => OkResponse("<html>502 Bad Gateway</html>");

            var ex = Assert.Throws<ParseException>(() => client.ChatAsync(SimpleRequest()).GetAwaiter().GetResult());

            StringAssert.Contains("不是合法 JSON", ex.FriendlyMessage);
        }

        [Test]
        public void 网络异常_抛NetworkException()
        {
            var stub = CreateStub(out var client);
            stub.Responder = _ => throw new HttpRequestException("connection refused");

            var ex = Assert.Throws<NetworkException>(() => client.ChatAsync(SimpleRequest()).GetAwaiter().GetResult());

            StringAssert.Contains("网络请求失败", ex.FriendlyMessage);
        }

        [Test]
        public void 超时_抛NetworkException()
        {
            var stub = CreateStub(out var client);
            stub.Responder = _ => throw new TaskCanceledException("timeout");

            var ex = Assert.Throws<NetworkException>(() => client.ChatAsync(SimpleRequest()).GetAwaiter().GetResult());

            StringAssert.Contains("超时", ex.FriendlyMessage);
        }

        [Test]
        public void 未配置key_抛DeepSeekException_提示在窗口设置()
        {
            var stub = CreateStub(out var client, key: "");
            stub.Responder = _ => OkResponse("{}");

            var ex = Assert.Throws<DeepSeekException>(() => client.ChatAsync(SimpleRequest()).GetAwaiter().GetResult());

            StringAssert.Contains("API Key", ex.FriendlyMessage);
        }

        // —— 辅助 ——

        private static HttpResponseMessage OkResponse(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
