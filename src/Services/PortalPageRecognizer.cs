using cpu_net.Constants;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace cpu_net.Services
{
    internal static class PortalPageRecognizer
    {
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
        private const RegexOptions HtmlOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private static readonly Regex Comments = new(@"<!--[\s\S]*?-->", HtmlOptions, MatchTimeout);
        // 只接受脚本块内单独的静态赋值，不执行脚本、不解释表达式或转义代码。
        private static readonly Regex Script = new(
            """<script\b[^>]*>\s*(?:window\.)?location\.href\s*=\s*(?:"(?<url>[^"<>\r\n]+)"|'(?<url>[^'<>\r\n]+)')\s*;?\s*</script\s*>""",
            RegexOptions.CultureInvariant, MatchTimeout);
        // 认证提示模板的链接：保守接受仅有href属性的链接，其他HTML不推断。
        private static readonly Regex Link = new(
            """<a\s+href\s*=\s*(?:"(?<url>[^"<>]+)"|'(?<url>[^'<>]+)')\s*>\s*here\s*</a\s*>""",
            HtmlOptions, MatchTimeout);

        public static bool IsKnownDormitoryPage(string body)
        {
            try
            {
                string html = Comments.Replace(body, "");
                foreach (Match match in Script.Matches(html))
                    if (IsKnownPortalUrl(match.Groups["url"].Value)) return true;

                if (!html.Contains("Authentication is required.", StringComparison.OrdinalIgnoreCase) ||
                    !html.Contains("to open the authentication page.", StringComparison.OrdinalIgnoreCase)) return false;
                foreach (Match match in Link.Matches(html))
                    if (IsKnownPortalUrl(WebUtility.HtmlDecode(match.Groups["url"].Value))) return true;
            }
            catch (RegexMatchTimeoutException) { /* 无法明确识别，仍按状态不明处理。 */ }
            return false;
        }

        private static bool IsKnownPortalUrl(string text)
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return false;
            var known = new Uri(NetworkConstants.BroadbandDrComUrl);
            if (uri.Scheme != known.Scheme || uri.Port != known.Port ||
                !string.Equals(uri.Host, known.Host, StringComparison.OrdinalIgnoreCase) ||
                uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
                uri.AbsolutePath != "/a79.htm") return false;

            string? accessControllerIp = null;
            string? accessControllerName = null;
            foreach (string part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = part.Split('=', 2);
                if (pair.Length != 2) return false;
                string key = Uri.UnescapeDataString(pair[0]);
                string value = Uri.UnescapeDataString(pair[1]);
                if (key == "wlanacip")
                {
                    if (accessControllerIp != null) return false;
                    accessControllerIp = value;
                }
                else if (key == "wlanacname")
                {
                    if (accessControllerName != null) return false;
                    accessControllerName = value;
                }
            }
            // wlanacip是接入控制器地址，仅作为模板特征，绝不返回给认证流程。
            return IPAddress.TryParse(accessControllerIp, out var address) &&
                address.AddressFamily == AddressFamily.InterNetwork &&
                !string.IsNullOrWhiteSpace(accessControllerName);
        }
    }
}
