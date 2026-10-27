using cpu_net.Model;
using cpu_net.ViewModel;
using cpu_net.Views.Pages;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace cpu_net
{
    public partial class MainWindow : Window
    {
        [DllImport("kernel32.dll")]
        static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

        [Flags]
        enum EXECUTION_STATE : uint
        {
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
        }

        private static readonly Brush DarkBlueBrush = new SolidColorBrush(Colors.DarkBlue);
        private static readonly Brush WhiteBrush = new SolidColorBrush(Colors.White);
        private HomePage _cachedHomePage = new HomePage();
        private ConfigurationPage _cachedConfigurationPage = new ConfigurationPage();
        private readonly MainViewModel _vm = new MainViewModel();

        public MainWindow()
        {
            InitializeComponent();
            IsVisibleChanged += (_, _) => { if (IsVisible) _vm.ResumeLogRefresh(); };
            _cachedHomePage.ParentWindow = this;
            _cachedConfigurationPage.ParentWindow = this;
            ChangePage("home");
            DataContext = _vm;

            if (Program.IsPreview)
            {
                Title += "（预览模式）";
                _vm.Info("预览模式：可查看窗口与设置，不认证、不保存设置、不执行自动任务。");
                return;
            }

            _vm.Info("CPU_NET_Lite 已以正常模式启动。");
            var settingData = new SettingModel().Read();
            try { ApplyPowerSettings(settingData.PreventSystemSleep, settingData.PreventDisplaySleep); }
            catch (InvalidOperationException ex) { _vm.Info(ex.Message); }
            if (settingData.PathExist())
            {
                if (settingData.NetworkLoginEnabled && settingData.IsAutoLogin)
                {
                    LoginAtStartup();
                }
                if (settingData.IsAutoMin)
                {
                    this.Visibility = Visibility.Hidden;
                }
            }
        }

        public void ApplyPowerSettings(bool preventSystemSleep, bool preventDisplaySleep)
        {
            if (Program.IsPreview) return;
            // 电源请求属于调用线程，启动和切换均在窗口线程执行。
            Dispatcher.VerifyAccess();
            var flags = EXECUTION_STATE.ES_CONTINUOUS;
            if (preventSystemSleep) flags |= EXECUTION_STATE.ES_SYSTEM_REQUIRED;
            if (preventDisplaySleep) flags |= EXECUTION_STATE.ES_DISPLAY_REQUIRED;
            if (SetThreadExecutionState(flags) == 0)
                throw new InvalidOperationException("电源选项应用失败，请重新切换或重启客户端。");
        }

        private void AllowSleep()
        {
            SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
        }

        private void ChangePage(string name)
        {
            Home_Button.BorderBrush = WhiteBrush;
            Conf_Button.BorderBrush = WhiteBrush;

            switch (name)
            {
                case "home":
                    Home_Button.BorderBrush = DarkBlueBrush;
                    PageFrame.Content = _cachedHomePage;
                    break;
                case "conf":
                    Conf_Button.BorderBrush = DarkBlueBrush;
                    PageFrame.Content = _cachedConfigurationPage;
                    break;
            }
        }

        private async void LoginAtStartup()
        {
            await Task.Run(() => _vm.LoginOnline());
            ChangePage("home");
        }

        public void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            MessageBoxResult result = System.Windows.MessageBox.Show("是否退出？", "询问", MessageBoxButton.YesNo, MessageBoxImage.Question);
            e.Cancel = result != MessageBoxResult.Yes;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.Visibility = Visibility.Hidden;
            }
        }

        private void Home_Button_Click(object sender, RoutedEventArgs e)
        {
            ChangePage("home");
        }

        private void Conf_Button_Click(object sender, RoutedEventArgs e)
        {
            ChangePage("conf");
        }

        protected override void OnClosed(EventArgs e)
        {
            _vm.StopBackgroundWork();
            if (!Program.IsPreview) AllowSleep();
            base.OnClosed(e);
        }
    }
}


