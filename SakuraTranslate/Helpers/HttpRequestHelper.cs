using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace SakuraTranslate.Helpers
{
    internal static class HttpRequestHelper
    {
        public static IEnumerator Send(string endpoint, string method, string payload, int timeoutMs, Action<string> completed)
        {
            var request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Method = method;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            var timer = Stopwatch.StartNew();
            Stream output = null;
            Stream input = null;
            WebResponse response = null;
            try
            {
                if (payload != null)
                {
                    var bytes = Encoding.UTF8.GetBytes(payload);
                    request.ContentType = "application/json";
                    request.ContentLength = bytes.Length;
                    var opening = request.BeginGetRequestStream(null, null);
                    foreach (var step in WaitFor(opening, timer, timeoutMs)) yield return step;
                    output = request.EndGetRequestStream(opening);
                    var writing = output.BeginWrite(bytes, 0, bytes.Length, null, null);
                    foreach (var step in WaitFor(writing, timer, timeoutMs)) yield return step;
                    output.EndWrite(writing);
                    output.Close();
                    output = null;
                }
                var receiving = request.BeginGetResponse(null, null);
                foreach (var step in WaitFor(receiving, timer, timeoutMs)) yield return step;
                response = FinishResponse(request, receiving);
                input = response.GetResponseStream();
                using (var collected = new MemoryStream())
                {
                    var buffer = new byte[4096];
                    while (true)
                    {
                        var reading = input.BeginRead(buffer, 0, buffer.Length, null, null);
                        foreach (var step in WaitFor(reading, timer, timeoutMs)) yield return step;
                        int count = input.EndRead(reading);
                        if (count == 0) break;
                        collected.Write(buffer, 0, count);
                    }
                    completed(Encoding.UTF8.GetString(collected.ToArray()));
                }
            }
            finally
            {
                if (output != null) output.Close();
                if (input != null) input.Close();
                if (response != null) response.Close();
                request.Abort();
            }
        }

        private static IEnumerable<object> WaitFor(IAsyncResult operation, Stopwatch timer, int timeoutMs)
        {
            while (!operation.IsCompleted)
            {
                if (timer.ElapsedMilliseconds >= timeoutMs)
                    throw new TimeoutException("翻译服务请求超时，请检查后端状态，必要时调整 RequestTimeoutSeconds。");
                yield return null;
            }
        }

        private static WebResponse FinishResponse(HttpWebRequest request, IAsyncResult operation)
        {
            try { return request.EndGetResponse(operation); }
            catch (WebException error)
            {
                var response = error.Response as HttpWebResponse;
                string reason = response == null ? error.Status.ToString() : "HTTP " + (int)response.StatusCode;
                if (error.Response != null) error.Response.Close();
                throw new InvalidOperationException("翻译服务请求失败（" + reason + "）。请检查服务、Endpoint 地址和后端日志；自动模式需要 /v1/models 接口。");
            }
        }
    }
}
