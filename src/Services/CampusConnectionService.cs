using cpu_net.Constants;
using cpu_net.Model;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;

namespace cpu_net.Services
{
    public enum InternetProbeState { Online, CampusPortal, Unknown }

    public sealed record InternetProbeResult(InternetProbeState State, int? PortalMode = null);
    public sealed record CampusLoginTarget(int Mode, string Address);

    public static class CampusConnectionService
    {
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan PrimaryProbeTimeout = TimeSpan.FromMilliseconds(2500);
        private static readonly HttpClient ProbeClient = new(PhysicalNetworkService.CreateHandler())
            { Timeout = Timeout.InfiniteTimeSpan };

        public static async Task<InternetProbeResult> ProbeInternetAsync(
            string url, string expectedBody, Action<string> report, CancellationToken cancellationToken = default,
            Action<string, string, string>? diagnostic = null)
        {
            void Publish(string key, string reason, string detail)
            {
                if (diagnostic != null) diagnostic(key, reason, detail);
                else report(ProbeDiagnostics.Save(reason, detail));
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(ProbeTimeout);
            if (url != NetworkConstants.DefaultProbeUrl || expectedBody != NetworkConstants.DefaultProbeBody)
                return (await ProbeAttemptAsync(url, expectedBody, report, cancellationToken, deadline.Token, Publish)).Result;

            (string Key, string Reason, string Detail)? primaryFailure = null;
            using var primaryDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            primaryDeadline.CancelAfter(PrimaryProbeTimeout);
            var primary = await ProbeAttemptAsync(url, expectedBody, report, cancellationToken, primaryDeadline.Token,
                (key, reason, detail) => primaryFailure = (key, reason, detail), deadline.Token);
            cancellationToken.ThrowIfCancellationRequested();
            if (!primary.CanFallback || deadline.IsCancellationRequested || primaryFailure is not { } failure)
            {
                if (primaryFailure is { } saved) Publish(saved.Key, saved.Reason, saved.Detail);
                return primary.Result;
            }

            var backup = await ProbeAttemptAsync(NetworkConstants.BackupProbeUrl, NetworkConstants.BackupProbeBody,
                report, cancellationToken, deadline.Token, (key, reason, detail) =>
                    Publish(failure.Key + key, $"默认探测与备用探测均未确认外网可达（{reason}）",
                        $"默认探测\n{failure.Detail}\n\n备用探测\n{detail}"));
            return backup.Result;
        }

        private static async Task<(InternetProbeResult Result, bool CanFallback)> ProbeAttemptAsync(
            string url, string expectedBody, Action<string> report, CancellationToken cancellationToken,
            CancellationToken attemptToken, Action<string, string, string> diagnostic, CancellationToken bodyToken = default)
        {
            var started = DateTimeOffset.Now;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Uri? probeUri = null;
            HttpResponseMessage? response = null;
            string? body = null;
            string locationText = "无";
            string mediaType = "未收到响应";
            string physicalRoute = "尚未选择物理出口";
            string phase = "选择物理出口或解析 DNS";
            void Report(string reason, string? failure = null, bool timedOut = false)
            {
                string address = ProbeDiagnostics.Address(probeUri);
                int? statusCode = (int?)response?.StatusCode;
                string failurePhase = Volatile.Read(ref phase);
                string metadata = $"检测地址={address}\n物理出口={Volatile.Read(ref physicalRoute)}\n阶段={failurePhase}\nHTTP={statusCode?.ToString() ?? "未收到响应"}\nContent-Type={mediaType}\nLocation={locationText}\n正文长度={(body == null ? "未知（未取得完整正文）" : body.Length + " 字符")}\n原因={reason}\n异常={failure ?? "无"}";
                // 地址轮换及取消异常包装只影响完整诊断，不拆分同一种超时。
                string stable = JsonSerializer.Serialize(new
                {
                    address, statusCode, mediaType, locationText, body, reason, failurePhase,
                    failure = timedOut ? null : failure
                });
                string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stable)));
                string detail = $"检测时间={started:yyyy-MM-dd HH:mm:ss.fff zzz}\n{metadata}\n耗时={timer.ElapsedMilliseconds} ms\n保留策略=最近{ProbeDiagnostics.RetainedFileCount}份；超出删除最旧文件\n完整响应正文（未脱敏、不截断）：\n{body ?? "[未取得正文]"}";
                diagnostic(key, reason, detail);
            }
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out probeUri) ||
                    (probeUri.Scheme != Uri.UriSchemeHttp && probeUri.Scheme != Uri.UriSchemeHttps))
                {
                    Report("检测地址格式无效，未发送请求");
                    return (new(InternetProbeState.Unknown), false);
                }
                using var request = new HttpRequestMessage(HttpMethod.Get, probeUri);
                request.Options.Set(PhysicalNetworkService.RouteReportKey, description =>
                {
                    Volatile.Write(ref physicalRoute, description);
                    if (description.StartsWith("物理出口：", StringComparison.Ordinal))
                        Volatile.Write(ref phase, "建立 TCP 连接");
                });
                request.Options.Set(PhysicalNetworkService.TcpConnectedKey,
                    () => Volatile.Write(ref phase, "TCP 已连接，等待 HTTP 响应头"));
                response = await ProbeClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attemptToken);
                Volatile.Write(ref phase, "读取 HTTP 正文");
                mediaType = response.Content.Headers.ContentType?.ToString() ?? "未提供";
                var location = response.Headers.Location;
                Uri? destination = null;
                if (location != null) Uri.TryCreate(probeUri, location, out destination);
                if (response.Headers.TryGetValues("Location", out var locations)) locationText = string.Join("\n", locations);
                string? bodyFailure = null;
                try { body = await response.Content.ReadAsStringAsync(bodyToken.CanBeCanceled ? bodyToken : attemptToken); }
                catch (Exception ex) when (!response.IsSuccessStatusCode && !cancellationToken.IsCancellationRequested)
                {
                    // 非成功响应原先只依赖状态码和Location；诊断读取失败不改变门户判定。
                    bodyFailure = ProbeDiagnostics.Failure(ex);
                }
                if (response.IsSuccessStatusCode && body!.Trim() == expectedBody)
                    return (new(InternetProbeState.Online), false);

                int status = (int)response.StatusCode;
                if (status == 200 && body != null &&
                    string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (PortalPageRecognizer.IsKnownCampusPage(body))
                    {
                        report("检测到已知校园网认证页面（HTTP 200 正文跳转）");
                        return (new(InternetProbeState.CampusPortal, 1), false);
                    }
                    if (PortalPageRecognizer.IsKnownDormitoryPage(body))
                    {
                        report("检测到已知宿舍网认证页面（HTTP 200 正文跳转）");
                        return (new(InternetProbeState.CampusPortal, 0), false);
                    }
                }
                if ((status == 301 || status == 302 || status == 303 || status == 307 || status == 308) &&
                    destination != null && (destination.Scheme == Uri.UriSchemeHttp || destination.Scheme == Uri.UriSchemeHttps))
                {
                    if (destination.Host == new Uri(NetworkConstants.BroadbandDrComUrl).Host)
                    {
                        report("检测到已知宿舍网认证页面（HTTP 重定向）");
                        return (new(InternetProbeState.CampusPortal, 0), false);
                    }
                    if (destination.Host == new Uri(NetworkConstants.CpuDrComUrl).Host)
                    {
                        Report("已知校园网认证跳转", bodyFailure);
                        return (new(InternetProbeState.CampusPortal, 1), false);
                    }
                }
                Report($"HTTP {status}，正文或跳转不符合联网判定", bodyFailure);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool timedOut = attemptToken.IsCancellationRequested || bodyToken.IsCancellationRequested;
                Report(timedOut ? "探测请求超时" : ProbeDiagnostics.BriefFailure(ex), ProbeDiagnostics.Failure(ex), timedOut);
                return (new(InternetProbeState.Unknown), response == null &&
                    (timedOut || ex is HttpRequestException { InnerException: not PhysicalNetworkException }));
            }
            finally { response?.Dispose(); }
            return (new(InternetProbeState.Unknown), false);
        }
        public static IEnumerable<CampusLoginTarget> ResolveTargets(int preferredNetwork, Action<string> report,
            int? detectedPortalMode = null, bool includeDormitory = true, CancellationToken cancellationToken = default)
        {
            if (preferredNetwork != 0 && preferredNetwork != 1)
            {
                report("连接优先顺序无效，请重新选择网络顺序。");
                yield break;
            }
            if (detectedPortalMode.HasValue && detectedPortalMode.Value != 0 && detectedPortalMode.Value != 1)
            {
                report("无法确定校园门户类型，本次不自动认证。");
                yield break;
            }

            int[] modeOrder = preferredNetwork == 0 ? new[] { 1, 0 } : new[] { 0, 1 };
            foreach (int mode in modeOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (mode == 0 && !includeDormitory)
                {
                    report("尚未选择有效运营商，跳过宿舍网连接。");
                    continue;
                }
                var target = ReadTarget(mode, report, cancellationToken);
                if (target != null) yield return target;
            }
        }

        private static CampusLoginTarget? ReadTarget(int mode, Action<string> report, CancellationToken cancellationToken)
        {
            string label = mode == 0 ? "宿舍网" : "校园网";
            try
            {
                string raw = NetworkService.HttpGetRequest(mode == 0
                    ? NetworkConstants.BroadbandDrComUrl : NetworkConstants.CpuDrComUrl, report, operationName: "状态查询",
                    cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var result = JsonSerializer.Deserialize<DrComIpResult>(
                    NetworkService.ExtractJson(raw, NetworkConstants.DrComCallback));
                if (IPAddress.TryParse(result?.ss5, out var address) && IsUsableIpv4(address))
                {
                    report($"{label}状态接口已提供认证 IP。");
                    return new CampusLoginTarget(mode, address.ToString());
                }
                report($"{label}状态接口未提供有效认证 IPv4，不使用默认路由或 TUN 地址代替。");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // 不输出原始响应及 JSON 片段，状态查询中可能带有身份信息。
                report($"{label}状态查询失败：{NetworkService.DescribeFailure(ex)}；请确认物理网卡已接入校园网络。");
            }
            return null;
        }

        private static bool IsUsableIpv4(IPAddress address)
        {
            if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) return false;
            byte[] bytes = address.GetAddressBytes();
            return bytes[0] != 0 && bytes[0] < 224 && !(bytes[0] == 169 && bytes[1] == 254);
        }
    }
}
