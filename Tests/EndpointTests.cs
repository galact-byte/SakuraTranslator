using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SakuraTranslate;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;

public class ContextProxy : DispatchProxy
{
    public Func<MethodInfo, object[], object> Handler;
    protected override object Invoke(MethodInfo method, object[] args) => Handler(method, args);
}

internal sealed class LocalBackend : IDisposable
{
    private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
    private readonly Task worker;
    private volatile bool stopping;
    public string Model = "Sakura-Galtransl-7B-v3.7-IQ4_XS.gguf";
    public string Response = "{\"choices\":[{\"message\":{\"content\":\"你好\"}}]}";
    public readonly List<(string Path, string Body)> Requests = new();
    public int ModelStatus = 200;
    public string ModelResponse;
    public bool MultipleModels;
    public int HeaderDelayMs;
    public int BodyDelayMs;

    public string Endpoint => "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/v1/chat/completions";

    public LocalBackend()
    {
        listener.Start();
        worker = Task.Run(async () => {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    using var stream = client.GetStream();
                    var headers = new StringBuilder();
                    var one = new byte[1];
                    while (!headers.ToString().EndsWith("\r\n\r\n"))
                    {
                        if (await stream.ReadAsync(one) == 0) throw new IOException("Incomplete HTTP header");
                        headers.Append((char)one[0]);
                    }
                    if (headers.ToString().Contains("100-continue", StringComparison.OrdinalIgnoreCase))
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"));
                    var lengthLine = headers.ToString().Split("\r\n").FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                    int length = lengthLine == null ? 0 : int.Parse(lengthLine.Split(':')[1]);
                    var bytes = new byte[length];
                    int read = 0;
                    while (read < length)
                    {
                        int count = await stream.ReadAsync(bytes.AsMemory(read));
                        if (count == 0) throw new IOException("Incomplete HTTP body");
                        read += count;
                    }
                    string path = headers.ToString().Split(' ')[1];
                    lock (Requests) Requests.Add((path, Encoding.UTF8.GetString(bytes)));
                    bool models = path.EndsWith("/models");
                    string data = models ? JsonSerializer.Serialize(new { data = MultipleModels
                        ? new[] { new { id = Model }, new { id = "other" } } : new[] { new { id = Model } } }) : Response;
                    if (models && ModelResponse != null) data = ModelResponse;
                    byte[] body = Encoding.UTF8.GetBytes(data);
                    int status = models ? ModelStatus : 200;
                    if (HeaderDelayMs > 0) await Task.Delay(HeaderDelayMs);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
                    if (BodyDelayMs > 0) await Task.Delay(BodyDelayMs);
                    await stream.WriteAsync(body);
                }
            }
            catch (Exception error) when (stopping || error is SocketException || error is ObjectDisposedException || error is IOException) { }
        });
    }

    public void Dispose() { stopping = true; listener.Stop(); if (!worker.Wait(3000)) throw new Exception("测试服务未退出"); }
}

internal static class EndpointTests
{
    private static SakuraTranslateEndpoint Initialize(LocalBackend backend, bool automatic = true, Dictionary<string, string> overrides = null)
    {
        var context = DispatchProxy.Create<IInitializationContext, ContextProxy>();
        ((ContextProxy)(object)context).Handler = (method, args) => {
            if (method.Name == "GetOrCreateSetting")
            {
                string key = (string)args[1];
                if (overrides != null && overrides.TryGetValue(key, out var value)) return value;
                if (key == "Endpoint") return backend.Endpoint;
                if (key == "ModelName" && !automatic) return "GalTransl";
                if (key == "ModelVersion" && !automatic) return "3";
                return args.Length == 3 ? args[2] : null;
            }
            return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
        };
        var endpoint = new SakuraTranslateEndpoint();
        endpoint.Initialize(context);
        return endpoint;
    }

    private static string Translate(SakuraTranslateEndpoint endpoint, double maxSeconds = 5)
    {
        string result = null;
        var context = DispatchProxy.Create<ITranslationContext, ContextProxy>();
        ((ContextProxy)(object)context).Handler = (method, args) => {
            if (method.Name == "get_UntranslatedText") return "こんにちは";
            if (method.Name == "Complete") result = (string)args[0];
            if (method.Name == "Fail") throw new Exception((string)args[0]);
            return null;
        };
        var operation = endpoint.Translate(context);
        try
        {
            var timer = Stopwatch.StartNew();
            while (operation.MoveNext())
            {
                if (timer.Elapsed.TotalSeconds > maxSeconds) throw new Exception("翻译协程未在测试期限内结束");
                Thread.Sleep(1);
            }
        }
        finally { (operation as IDisposable)?.Dispose(); }
        return result ?? throw new Exception("未提交翻译结果");
    }

    public static void VerifyLiveBackend(string endpoint)
    {
        using var placeholder = new LocalBackend();
        var plugin = Initialize(placeholder, true, new() { ["Endpoint"] = endpoint });
        Console.WriteLine("PASS live backend translation: " + Translate(plugin, 30));
    }

    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("自动识别并随后端换模型", () => {
            using var backend = new LocalBackend();
            var endpoint = Initialize(backend);
            if (Translate(endpoint) != "你好") throw new Exception("译文错误");
            backend.Model = "sakura-7b-qwen2.5-v1.0-iq4xs.gguf";
            Translate(endpoint);
            var requests = backend.Requests.Where(request => !request.Path.EndsWith("/models")).ToArray();
            using var first = JsonDocument.Parse(requests[0].Body);
            using var second = JsonDocument.Parse(requests[1].Body);
            if (!first.RootElement.GetProperty("model").GetString().Contains("v3.7")) throw new Exception("未使用实际模型 ID");
            if (!first.RootElement.GetProperty("messages")[0].GetProperty("content").GetString().Contains("视觉小说")) throw new Exception("GalTransl 模板错误");
            if (!second.RootElement.GetProperty("messages")[0].GetProperty("content").GetString().Contains("轻小说翻译模型")) throw new Exception("换模型后仍使用旧模板");
        });
        yield return ("手动配置不查询模型接口", () => {
            using var backend = new LocalBackend { ModelStatus = 404 };
            Translate(Initialize(backend, false));
            if (backend.Requests.Any(request => request.Path.EndsWith("/models"))) throw new Exception("手动模式不应依赖模型接口");
        });
        yield return ("保留引号、换行和字面反斜杠", () => {
            const string expected = "\"你好\"\n\\N[1] 与字面 \\n";
            using var backend = new LocalBackend { Response = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = expected } } } }) };
            if (Translate(Initialize(backend, false)) != expected) throw new Exception("响应发生二次反转义或引号丢失");
        });
        foreach (string identifier in new[] { "unknown", "Sakura-4B-Qwen3-Base-v2.gguf" })
            yield return ("未知模型不发送翻译 " + identifier, () => {
                using var backend = new LocalBackend { Model = identifier };
                try { Translate(Initialize(backend)); } catch (Exception) {
                    if (backend.Requests.Any(request => request.Path.EndsWith("/chat/completions"))) throw new Exception("未知模型仍发送了猜测的翻译请求");
                    return;
                }
                throw new Exception("未知模型应明确失败");
            });
        yield return ("取消协程中止尚未完成的识别请求", () => {
            using var backend = new LocalBackend { BodyDelayMs = 2000 };
            var endpoint = Initialize(backend);
            var context = DispatchProxy.Create<ITranslationContext, ContextProxy>();
            ((ContextProxy)(object)context).Handler = (method, args) => method.Name == "get_UntranslatedText" ? "こんにちは" : null;
            var operation = endpoint.Translate(context);
            var timer = Stopwatch.StartNew();
            while (true)
            {
                if (!operation.MoveNext()) throw new Exception("请求不应提前完成");
                lock (backend.Requests) if (backend.Requests.Count > 0) break;
                if (timer.Elapsed.TotalSeconds > 2) throw new Exception("测试服务没有收到请求");
                Thread.Sleep(1);
            }
            timer.Restart();
            ((IDisposable)operation).Dispose();
            if (timer.Elapsed.TotalSeconds > 1) throw new Exception("取消操作等待了网络响应");
            if (backend.Requests.Any(row => row.Path.EndsWith("/chat/completions"))) throw new Exception("取消后仍发起翻译");
        });
        yield return ("缺少统计时按 finish_reason 有界重试", () => {
            using var backend = new LocalBackend { Response = "{\"choices\":[{\"message\":{\"content\":\"你好\"},\"finish_reason\":\"length\"}]}" };
            Translate(Initialize(backend, false, new() { ["FixDegeneration"] = "True" }));
            var requests = backend.Requests.Where(row => row.Path.EndsWith("/chat/completions")).ToArray();
            if (requests.Length != 2) throw new Exception("退化重试次数不正确");
            using var last = JsonDocument.Parse(requests[1].Body);
            if (last.RootElement.GetProperty("frequency_penalty").GetDouble() != 0.2) throw new Exception("重试惩罚参数错误");
        });
        yield return ("后端停止时明确失败", () => {
            string stopped;
            using (var backend = new LocalBackend()) stopped = backend.Endpoint;
            using var placeholder = new LocalBackend();
            var endpoint = Initialize(placeholder, true, new() { ["Endpoint"] = stopped });
            try { Translate(endpoint); } catch (InvalidOperationException error) {
                if (!error.Message.Contains("翻译服务请求失败")) throw;
                return;
            }
            throw new Exception("已停止的后端不应成功");
        });
        foreach (string response in new[] { "", "invalid", "[]", "{}", "{\"choices\":[null]}", "{\"choices\":[{\"message\":null}]}", "{\"choices\":[{\"message\":{\"content\":42}}]}" })
            yield return ("拒绝异常翻译响应 " + response, () => {
                using var backend = new LocalBackend { Response = response };
                try { Translate(Initialize(backend, false)); } catch (InvalidOperationException) { return; }
                throw new Exception("应明确拒绝异常翻译响应");
            });
        foreach (string response in new[] { "", "[]", "{}", "{\"data\":[]}", "{\"data\":[null]}", "{\"data\":[\"text\"]}", "{\"data\":[{\"id\":42}]}" })
            yield return ("拒绝异常模型响应 " + response, () => {
                using var backend = new LocalBackend { ModelResponse = response };
                try { Translate(Initialize(backend)); } catch (InvalidOperationException) { return; }
                throw new Exception("应明确拒绝异常模型响应");
            });
        yield return ("原生 HTTP 超时与协程超时使用同一异常类型", () => {
            using var backend = new LocalBackend { HeaderDelayMs = 500 };
            var helper = typeof(SakuraTranslateEndpoint).Assembly.GetType("SakuraTranslate.Helpers.HttpRequestHelper");
            var send = helper.GetMethod("Send", BindingFlags.Public | BindingFlags.Static);
            var operation = (IEnumerator)send.Invoke(null, new object[] { backend.Endpoint.Replace("/chat/completions", "/models"), "GET", null, 100, (Action<string>)(_ => throw new Exception("慢速请求不应成功")) });
            try
            {
                operation.MoveNext();
                Thread.Sleep(250);
                try { operation.MoveNext(); } catch (TimeoutException) { return; }
                throw new Exception("没有返回统一的超时异常");
            }
            finally { ((IDisposable)operation).Dispose(); }
        });
        foreach (bool bodyOnly in new[] { false, true })
            yield return ("协程超时 " + (bodyOnly ? "正文" : "响应头"), () => {
                using var backend = new LocalBackend { HeaderDelayMs = bodyOnly ? 0 : 1500, BodyDelayMs = bodyOnly ? 1500 : 0 };
                var endpoint = Initialize(backend, true, new() { ["RequestTimeoutSeconds"] = "1" });
                var timer = Stopwatch.StartNew();
                try { Translate(endpoint); } catch (TimeoutException) {
                    if (timer.Elapsed.TotalSeconds > 2) throw new Exception("超时未生效，协程可能阻塞");
                    return;
                }
                throw new Exception("慢速服务没有触发超时");
            });
        yield return ("模型接口失败不猜测模板", () => {
            using var backend = new LocalBackend { ModelStatus = 404 };
            try { Translate(Initialize(backend)); } catch (InvalidOperationException error) {
                if (!error.Message.Contains("404")) throw;
                if (backend.Requests.Any(request => request.Path.EndsWith("/chat/completions"))) throw new Exception("识别失败后仍发送翻译请求");
                return;
            }
            throw new Exception("模型接口失败应报错");
        });
        yield return ("旧版手动 Sakura 配置保持有效", () => {
            using var backend = new LocalBackend { ModelStatus = 404 };
            Translate(Initialize(backend, false, new() { ["ModelName"] = "Sakura", ["ModelVersion"] = "1.0" }));
            if (backend.Requests.Any(request => request.Path.EndsWith("/models"))) throw new Exception("旧配置被自动模式覆盖");
        });
        foreach (string suffix in new[] { "", "/v1", "/v1/" })
            yield return ("补全服务地址 " + suffix, () => {
                using var backend = new LocalBackend();
                string address = backend.Endpoint.Replace("/v1/chat/completions", suffix);
                Translate(Initialize(backend, true, new() { ["Endpoint"] = address }));
                if (!backend.Requests.Any(request => request.Path == "/v1/models")) throw new Exception("模型接口路径不正确");
            });
        yield return ("Sakura 1.5 使用官方新版提示词", () => {
            using var backend = new LocalBackend { Model = "Sakura-14B-Qwen3-v1.5-Q4_K_M.gguf" };
            Translate(Initialize(backend));
            var request = backend.Requests.Single(row => row.Path.EndsWith("/chat/completions"));
            using var document = JsonDocument.Parse(request.Body);
            if (!document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString().Contains("日本二次元领域"))
                throw new Exception("没有使用官方 1.5 系统提示词");
        });
        yield return ("多模型不擅自选第一个", () => {
            using var backend = new LocalBackend { MultipleModels = true };
            try { Translate(Initialize(backend)); } catch (Exception) {
                if (backend.Requests.Any(request => request.Path.EndsWith("/chat/completions"))) throw new Exception("多个模型仍擅自发送翻译请求");
                return;
            }
            throw new Exception("多个模型应要求手动选择");
        });
    }
}
