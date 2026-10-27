using cpu_net.Model;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace cpu_net.Views.Pages
{
    /// <summary>
    /// HomePage.xaml 的交互逻辑
    /// </summary>
    public partial class HomePage : Page
    {
        private readonly SettingModel _settingData = new SettingModel();

        public MainWindow ParentWindow { get; set; }

        public HomePage()
        {
            InitializeComponent();
            this.Loaded += (s, e) =>
            {
                if (ParentWindow != null)
                {
                    this.DataContext = ParentWindow.DataContext;
                }
            };
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (_settingData.PathExist())
            {
                return;
            }

            var result = MessageBox.Show("请在设置中添加账号信息", "提示");
            if (result == MessageBoxResult.OK)
            {
                ToConf();
            }
        }

        private void ToConf()
        {
            if (!this.Dispatcher.CheckAccess())
            {
                this.Dispatcher.Invoke(DispatcherPriority.Send, new Action(ToConf));
            }
            else
            {
                this.ParentWindow.Conf_Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }

        private void textLog_TextChanged(object sender, TextChangedEventArgs e)
        {
            sv1?.ScrollToEnd();
        }

        private void Log_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control) return;
            CopyLog(textLog.SelectionLength > 0 ? textLog.SelectedText : textLog.Text);
            e.Handled = true;
        }

        private void CopySelectedLog_Click(object sender, RoutedEventArgs e) => CopyLog(textLog.SelectedText);

        private void CopyAllLog_Click(object sender, RoutedEventArgs e) => CopyLog(textLog.Text);

        private void SelectAllLog_Click(object sender, RoutedEventArgs e)
        {
            textLog.Focus();
            textLog.SelectAll();
        }

        private static void CopyLog(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); }
            catch (ExternalException)
            {
                MessageBox.Show("剪贴板暂时被其他程序占用，请稍后再复制。", "复制日志");
            }
        }
    }
}
