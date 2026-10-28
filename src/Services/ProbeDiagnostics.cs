using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace cpu_net.Services
{
    internal static class ProbeDiagnostics
    {
        public const int RetainedFileCount = 20;
        private static readonly object FileGate = new();

        public static string Save(string reason, string detail)
        {
            lock (FileGate)
            {
                string? path = null;
                try
                {
                    string directory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Diagnostics");
                    System.IO.Directory.CreateDirectory(directory);
                    var files = new System.IO.DirectoryInfo(directory).GetFiles("probe-*.txt")
                        .Where(f => Regex.IsMatch(f.Name, @"^probe-\d{8}T\d{9}Z-[a-f0-9]{32}\.txt$"))
                        .OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
                    // 先腾出一个位置；删除失败则停止写入，避免突破数量上限。
                    foreach (var file in files.Take(Math.Max(0, files.Count - RetainedFileCount + 1))) file.Delete();
                    path = System.IO.Path.Combine(directory, $"probe-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.txt");
                    using (var stream = new System.IO.FileStream(path, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write))
                    using (var writer = new System.IO.StreamWriter(stream, new System.Text.UTF8Encoding(false)))
                        writer.Write(detail);
                    return $"外网探测异常：{reason}；诊断文件：{System.IO.Path.GetFullPath(path)}";
                }
                catch (Exception ex)
                {
                    string cleanup = "";
                    if (path != null)
                    {
                        try { System.IO.File.Delete(path); }
                        catch (Exception cleanupError) { cleanup = $"；未完成文件清理失败：{FileFailure(cleanupError)}"; }
                    }
                    return $"外网探测异常：{reason}；诊断文件写入失败：{FileFailure(ex)}{cleanup}";
                }
            }
        }

        private static string FileFailure(Exception ex) => ex switch
        {
            UnauthorizedAccessException => "没有目录或文件写入/清理权限（UnauthorizedAccessException）",
            System.IO.IOException => $"文件读写或保留清理失败（IOException，HResult=0x{ex.HResult:X8}）",
            _ => $"{ex.GetType().Name}，HResult=0x{ex.HResult:X8}"
        };

        public static string BriefFailure(Exception ex) => ex switch
        {
            OperationCanceledException => "探测请求超时",
            HttpRequestException http => $"探测请求失败（{http.HttpRequestError}）",
            System.IO.IOException => "探测响应读取失败",
            _ => $"探测请求异常（{ex.GetType().Name}）"
        };
        public static string Address(Uri? uri)
        {
            if (uri == null || !uri.IsAbsoluteUri || (uri.Scheme != "http" && uri.Scheme != "https"))
                return "[无效或非HTTP地址已隐藏]";
            // 只显示主机与已知静态探测路径，不输出用户信息、查询、片段或动态路径。
            string path = uri.AbsolutePath is "/" or "/connecttest.txt" or "/ncsi.txt"
                ? uri.AbsolutePath : "/[路径已脱敏]";
            return $"{uri.Scheme}://{uri.IdnHost}:{uri.Port}{path}";
        }

        public static string Failure(Exception ex)
        {
            var parts = new List<string>();
            for (Exception? current = ex; current != null; current = current.InnerException)
            {
                string reason = current switch
                {
                    OperationCanceledException => "请求超时（5秒）",
                    HttpRequestException http => $"HTTP请求失败：{http.HttpRequestError}",
                    SocketException socket => $"网络错误：{socket.SocketErrorCode}，NativeError={socket.NativeErrorCode}",
                    UriFormatException => "响应跳转地址格式无效",
                    System.IO.IOException => "响应传输或正文读取失败",
                    _ => $"请求处理失败，HResult=0x{current.HResult:X8}"
                };
                // 不使用原始Message：可能包含完整URL、响应片段或身份信息。
                parts.Add($"{current.GetType().FullName}：{reason}");
            }
            return string.Join(" -> ", parts);
        }
    }
}
