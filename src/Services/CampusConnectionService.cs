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
        private static readonly HttpClient ProbeClient = new HttpClient(new HttpClientHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false
        }) { Timeout = TimeSpan.FromSeconds(5) };

        public static async Task<InternetProbeResult> ProbeInternetAsync(
            string url, string expectedBody, Action<string> report, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var probeUri) ||
                    (probeUri.Scheme != Uri.UriSchemeHttp && probeUri.Scheme != Uri.UriSchemeHttps))
                {
                    report("外网状态无法确定：检测地址格式无效；重连由状态不明策略决定。");
                    return new InternetProbeResult(InternetProbeState.Unknown);
                }

                using var response = await ProbeClient.GetAsync(probeUri, cancellationToken);
                if (response.IsSuccessStatusCode &&
                    (await response.Content.ReadAsStringAsync(cancellationToken)).Trim() == expectedBody)
                    return new InternetProbeResult(InternetProbeState.Online);

                int status = (int)response.StatusCode;
                var location = response.Headers.Location;
                if ((status == 301 || status == 302 || status == 303 || status == 307 || status == 308) &&
                    location != null)
                {
                    var destination = location.IsAbsoluteUri ? location : new Uri(probeUri, location);
                    if (destination.Scheme == Uri.UriSchemeHttp || destination.Scheme == Uri.UriSchemeHttps)
                    {
                        if (destination.Host == new Uri(NetworkConstants.BroadbandDrComUrl).Host)
                            return new InternetProbeResult(InternetProbeState.CampusPortal, 0);
                        if (destination.Host == new Uri(NetworkConstants.CpuDrComUrl).Host)
                            return new InternetProbeResult(InternetProbeState.CampusPortal, 1);
                    }
                }
                report($"外网状态无法确定：HTTP {status}，正文或跳转不是已知联网/校园认证结果；重连由状态不明策略决定。");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                report($"外网状态无法确定：{NetworkService.DescribeFailure(ex)}；可能涉及 DNS、检测站点或 TUN 路由，重连由状态不明策略决定。");
            }
            return new InternetProbeResult(InternetProbeState.Unknown);
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
                var target = ReadTarget(mode, report);
                if (target != null) yield return target;
            }
        }

        private static CampusLoginTarget? ReadTarget(int mode, Action<string> report)
        {
            string label = mode == 0 ? "宿舍网" : "校园网";
            try
            {
                string raw = NetworkService.HttpGetRequest(mode == 0
                    ? NetworkConstants.BroadbandDrComUrl : NetworkConstants.CpuDrComUrl, report, operationName: "状态查询");
                var result = JsonSerializer.Deserialize<DrComIpResult>(
                    NetworkService.ExtractJson(raw, NetworkConstants.DrComCallback));
                if (IPAddress.TryParse(result?.ss5, out var address) && IsUsableIpv4(address))
                {
                    report($"{label}状态接口已提供认证 IP。");
                    return new CampusLoginTarget(mode, address.ToString());
                }
                report($"{label}状态接口未提供有效认证 IPv4，不使用默认路由或 TUN 地址代替。");
            }
            catch (Exception ex)
            {
                // 不输出原始响应及 JSON 片段，状态查询中可能带有身份信息。
                report($"{label}状态查询失败：{NetworkService.DescribeFailure(ex)}；请检查校园连接及认证地址的 TUN 路由排除。");
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
