using System;
using System.IO;
using System.Net;
using System.Text;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace cpu_net.Services
{
    /// <summary>校园状态查询与认证请求；总超时覆盖响应头及完整正文。</summary>
    public static class NetworkService
    {
        private static readonly HttpClient CampusClient = new(new HttpClientHandler
        {
            UseProxy = false,
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        // 保留现有工作线程调用接口，不引入额外认证并发或隐含重试。
        public static string HttpGetRequest(string url, Action<string>? diagnostic = null, string operationName = "认证",
            System.Threading.CancellationToken cancellationToken = default)
            => HttpGetRequestAsync(url, diagnostic, operationName, cancellationToken).GetAwaiter().GetResult();

        private static async System.Threading.Tasks.Task<string> HttpGetRequestAsync(string url,
            Action<string>? diagnostic, string operationName, System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var endpoint = new Uri(url).GetLeftPart(UriPartial.Path);
            var timer = Stopwatch.StartNew();
            diagnostic?.Invoke($"{operationName}请求：{endpoint}；直连；总超时 5000 ms");
            using var deadline = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            int? status = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await CampusClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    deadline.Token).ConfigureAwait(false);
                status = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"{endpoint}；直连；HTTP {status}；请求未成功（不跟随重定向）；耗时 {timer.ElapsedMilliseconds} ms");
                using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string content = await reader.ReadToEndAsync(deadline.Token).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
                diagnostic?.Invoke($"{operationName}响应：{endpoint}；HTTP {status}；耗时 {timer.ElapsedMilliseconds} ms；正文长度 {content.Length}");
                return content;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException($"{operationName}请求总超时（5000 ms，含正文读取）；HTTP={status?.ToString() ?? "未收到响应"}；耗时 {timer.ElapsedMilliseconds} ms");
            }
            catch (HttpRequestException ex)
            {
                // 不输出原始异常Message或完整认证URL。
                throw new InvalidOperationException($"{operationName}请求失败：{DescribeFailure(ex)}；耗时 {timer.ElapsedMilliseconds} ms");
            }
            catch (IOException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (IOException)
            {
                throw new InvalidOperationException($"{operationName}响应读取失败；HTTP={status?.ToString() ?? "未收到响应"}；耗时 {timer.ElapsedMilliseconds} ms");
            }
        }
        public static string DescribeFailure(Exception exception)
        {
            if (exception is OperationCanceledException)
                return "请求超时或被取消（检测超时设置为 5 秒）";
            if (exception is HttpRequestException http)
            {
                string reason = http.HttpRequestError switch
                {
                    HttpRequestError.NameResolutionError => "DNS 解析失败：检查测试域名及 DNS 配置",
                    HttpRequestError.ConnectionError => "连接失败：检查服务器端口、网络路由及系统代理",
                    HttpRequestError.ProxyTunnelError => "代理隧道失败：检查系统代理连接",
                    HttpRequestError.SecureConnectionError => "TLS 连接失败：检查证书及系统时间",
                    _ => "HTTP 请求失败"
                };
                return $"{reason}（{http.HttpRequestError}）；HTTP={http.StatusCode?.ToString() ?? "未收到响应"}" +
                    (http.InnerException is SocketException socket ? $"；Socket={socket.SocketErrorCode}" : "");
            }
            return exception.GetType().Name;
        }

        public static string ExtractJson(string response, string callback)
        {
            string text = response.Trim();
            if (text.StartsWith("{") || text == "null") return text;
            if (!text.StartsWith(callback, StringComparison.Ordinal))
                throw new JsonException("响应不是预期的 JSON 或 JSONP，可能是 HTML 页面或重定向响应");
            text = text.Substring(callback.Length).Trim();
            if (text.EndsWith(";")) text = text.Substring(0, text.Length - 1).TrimEnd();
            if (!text.StartsWith("(") || !text.EndsWith(")"))
                throw new JsonException("JSONP 括号不完整");
            return text.Substring(1, text.Length - 2).Trim();
        }

    }
}
