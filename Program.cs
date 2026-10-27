using System;

namespace cpu_net
{
    /// <summary>
    /// 应用程序入口点
    /// </summary>
    public static class Program
    {
        public static bool IsPreview { get; private set; }

        [STAThread]
        public static void Main(string[] args)
        {
            IsPreview = Array.Exists(args, argument => argument == "--preview");
            // 启动 WPF 应用程序
            var app = new App();
            app.InitializeComponent();  // 加载 App.xaml 资源并设置 StartupUri
            app.Run();
        }
    }
}
