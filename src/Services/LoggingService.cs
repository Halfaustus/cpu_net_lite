using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;

namespace cpu_net.Services
{
    /// <summary>
    /// 日志服务：提供线程安全的日志写入与读取功能
    /// </summary>
    public static class LoggingService
    {
        private static readonly SemaphoreSlim LogSemaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 日志根目录，固定为应用程序所在目录，避免相对路径导致写入系统目录的风险
        /// </summary>
        private static readonly string LogBaseDir = AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// 写入异常日志到 ErrorLog 目录（按天分文件）
        /// </summary>
        public static void WriteErrorLog(Exception ex)
        {
            string errorLogDir = Path.Combine(LogBaseDir, "ErrorLog");

            var now = DateTime.Now;
            string fileName = $"{now.Year}{now.Month:D2}{now.Day:D2}.log";
            string logPath = Path.Combine(errorLogDir, fileName);

            var log = Environment.NewLine + "----------------------" + DateTime.Now + " --------------------------" + Environment.NewLine
                      + ex.Message
                      + Environment.NewLine
                      + ex.InnerException
                      + Environment.NewLine
                      + ex.StackTrace
                      + Environment.NewLine + "----------------------footer--------------------------" + Environment.NewLine;

            LogSemaphore.Wait();
            try
            {
                Directory.CreateDirectory(errorLogDir);
                File.AppendAllText(logPath, log);
            }
            catch (Exception failure)
            {
                WriteFallback(log, failure);
            }
            finally
            {
                LogSemaphore.Release();
            }
        }

        /// <summary>
        /// 写入运行时日志（同步入口，内部转异步，不阻塞调用方）
        /// </summary>
        public static void WriteTextLog(string log, string logName)
        {
            _ = WriteTextLogAsync(log, logName);
        }

        /// <summary>
        /// 异步写入运行时日志
        /// </summary>
        // testMode 仅保留历史调用签名；主应用不再使用第二套日志。
        public static async Task WriteTextLogAsync(string log, string logName, bool testMode = false)
        {

            string logDir = Path.Combine(LogBaseDir, logName);

            string logPath = Path.Combine(logDir, "runtime.log");
            var formattedLog = $"{DateTime.Now:M-d HH:mm:ss}  {log}{Environment.NewLine}";

            await LogSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(logDir);
                long limit = Math.Clamp(new SettingsStore().Read().LogFileSizeMb, 1, 100) * 1024L * 1024;
                if (File.Exists(logPath) && new FileInfo(logPath).Length + Encoding.UTF8.GetByteCount(formattedLog) > limit)
                {
                    // 仅轮转本版本专用文件，不触碰旧版按月保存的日志。
                    for (int index = 3; index >= 1; index--)
                    {
                        string source = Path.Combine(logDir, $"runtime.{index}.log");
                        if (File.Exists(source)) File.Move(source, Path.Combine(logDir, $"runtime.{index + 1}.log"), true);
                    }
                    File.Move(logPath, Path.Combine(logDir, "runtime.1.log"), true);
                }
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        await File.AppendAllTextAsync(logPath, formattedLog).ConfigureAwait(false);
                        break;
                    }
                    catch (IOException ex) when (attempt < 3 &&
                        ((ex.HResult & 0xffff) == 32 || (ex.HResult & 0xffff) == 33))
                    {
                        await Task.Delay(100 * (attempt + 1)).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception failure)
            {
                WriteFallback(formattedLog, failure);
            }
            finally
            {
                LogSemaphore.Release();
            }
        }

        /// <summary>
        /// 读取日志文件最后 N 行
        /// </summary>
        public static string ReadLogText(string logName, int maxLines = 200)
        {
            var now = DateTime.Now;
            string fileName = $"{now.Year}{now.Month:D2}.log";
            string directory = Path.Combine(LogBaseDir, logName);

            LogSemaphore.Wait();
            try
            {
                if (maxLines <= 0) return string.Empty;
                var paths = new List<string> { Path.Combine(directory, "runtime.log") };
                for (int index = 1; index <= 4; index++) paths.Add(Path.Combine(directory, $"runtime.{index}.log"));
                paths.Add(Path.Combine(directory, fileName));
                var lines = new List<string>();
                foreach (string path in paths)
                {
                    if (File.Exists(path)) lines.InsertRange(0, ReadTail(path, maxLines - lines.Count));
                    if (lines.Count >= maxLines) break;
                }
                return string.Join(Environment.NewLine, lines);
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                LogSemaphore.Release();
            }
        }

        private static string[] ReadTail(string path, int maxLines)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long end = stream.Length;
            long start = end;
            int lineBreaks = 0;
            byte[] buffer = new byte[4096];
            // 限制单文件尾部读取量，避免损坏文件中的超长单行耗尽内存。
            long minimum = Math.Max(0, end - 1024 * 1024);
            while (start > minimum && lineBreaks <= maxLines)
            {
                int count = (int)Math.Min(buffer.Length, start - minimum);
                start -= count;
                stream.Position = start;
                stream.ReadExactly(buffer.AsSpan(0, count));
                for (int index = 0; index < count; index++) if (buffer[index] == (byte)'\n') lineBreaks++;
            }
            stream.Position = start;
            byte[] tail = new byte[(int)(end - start)];
            stream.ReadExactly(tail);
            int offset = 0;
            if (start > 0)
            {
                int newline = Array.IndexOf(tail, (byte)'\n');
                if (newline < 0) return Array.Empty<string>();
                offset = newline + 1;
            }
            return Encoding.UTF8.GetString(tail, offset, tail.Length - offset)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .TakeLast(maxLines).Select(line => line.TrimEnd('\r')).ToArray();
        }

        // 独立的按进程备用文件，日志失败时不递归调用日志服务。
        private static void WriteFallback(string entry, Exception failure)
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData), "cpu_net", "FallbackLogs");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, $"{DateTime.Now:yyyyMMdd}-{Environment.ProcessId}.log"),
                    $"{DateTime.Now:O} 日志写入失败：{failure.GetType().Name} (0x{failure.HResult:X8}){Environment.NewLine}{entry}");
            }
            catch
            {
                Debug.WriteLine("CPU_NET 日志及备用日志写入失败。");
            }
        }
    }
}

