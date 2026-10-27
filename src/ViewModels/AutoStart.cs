using Microsoft.Win32;
using System;

namespace cpu_net.ViewModel
{
    public class AutoStart
    {
        private const string AppName = "CPU_NET_Lite";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // 仅在用户修改开机启动开关并保存时调用；不清理旧版启动项。
        public void SetMeAutoStart(bool onOff = true)
        {
            if (onOff)
            {
                string appPath = Environment.ProcessPath
                    ?? throw new InvalidOperationException("无法确定当前程序路径。");
                using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                    ?? throw new InvalidOperationException("无法打开当前用户启动项。");
                key.SetValue(AppName, $"\"{appPath}\"", RegistryValueKind.String);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                key?.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
    }
}
