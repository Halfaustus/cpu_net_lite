using cpu_net.Model;
using cpu_net.Constants;
using cpu_net.Services;
using cpu_net.ViewModel;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace cpu_net.Views.Pages
{
    public partial class ConfigurationPage : Page
    {
        private bool _isScrollingFromMenu = false;
        private bool _isLoadingSettings = true;

        public MainWindow ParentWindow { get; set; }

        public ConfigurationPage()
        {
            InitializeComponent();
            LoadSettingsToUi(new SettingModel(), isReset: true);
            _isLoadingSettings = false;
            VersionTextBlock.Text = $"版本: {typeof(App).Assembly.GetName().Version?.ToString(3) ?? "Unknown"}";

            MenuListBox.SelectionChanged += MenuListBox_SelectionChanged;
            MenuListBox.PreviewMouseLeftButtonDown += MenuListBox_PreviewMouseLeftButtonDown;
            ContentScrollViewer.ScrollChanged += ContentScrollViewer_ScrollChanged;
        }

        private void Code_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = Regex.IsMatch(e.Text, "[^0-9.-]+");
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer.exe", NetworkConstants.GitHubRepoUrl);
        }

        private void SaveModule(string name, Func<SettingModel, bool> update, Action? apply = null)
        {
            if (Program.IsPreview)
            {
                MessageBox.Show("预览模式仅展示界面，不保存设置。", "预览模式");
                return;
            }
            try
            {
                if (!new SettingsStore().Update(update)) return;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{name}保存失败：{ex.Message}", "保存设置", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            try
            {
                apply?.Invoke();
                MessageBox.Show($"{name}已保存", "保存设置");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{name}已保存，应用设置失败：{ex.Message}。请重启软件后重试。", "保存设置");
            }
        }

        private void SaveNetworkConfiguration_Click(object sender, RoutedEventArgs e)
        {
            SaveModule("网络配置", settings =>
            {
                if (settings.NetworkLoginEnabled &&
                    (string.IsNullOrWhiteSpace(code.Text) || string.IsNullOrEmpty(secret.Password)))
                {
                    MessageBox.Show("请输入学号和密码", "账号密码");
                    return false;
                }
                settings.Username = code.Text.Trim();
                settings.Password = secret.Password;
                var selectedCarrier = ResolveCarrier();
                settings.Carrier = selectedCarrier.Carrier;
                settings.Key = selectedCarrier.Key;
                return true;
            }, apply: () => (ParentWindow?.DataContext as MainViewModel)?.RestartNetworkTimer());
        }

        private void ApplyImmediateSetting(string name, Action<SettingModel> update,
            bool restartNetwork = false, Action? apply = null)
        {
            if (_isLoadingSettings) return;
            if (Program.IsPreview)
            {
                SettingsStatusText.Text = "预览模式不保存或应用设置。";
                return;
            }
            try
            {
                new SettingsStore().Update(settings => { update(settings); return true; });
            }
            catch (Exception ex)
            {
                SettingsStatusText.Text = $"{name}保存失败，修改尚未生效。";
                MessageBox.Show($"{name}保存失败：{NetworkService.DescribeFailure(ex)}", "设置");
                return;
            }
            try
            {
                apply?.Invoke();
                if (restartNetwork) (ParentWindow?.DataContext as MainViewModel)?.RestartNetworkTimer();
                SettingsStatusText.Text = $"{name}已保存。";
            }
            catch (Exception ex)
            {
                SettingsStatusText.Text = $"{name}已保存，但应用失败。";
                MessageBox.Show($"{name}已保存，但应用失败：{NetworkService.DescribeFailure(ex)}", "设置");
            }
        }

        private void ImmediateOption_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings || sender is not CheckBox option) return;
            bool enabled = option.IsChecked == true;
            switch (option.Name)
            {
                case "NetworkEnabledCheckBox":
                    ApplyImmediateSetting("网络登录开关", settings => settings.NetworkLoginEnabled = enabled, restartNetwork: true);
                    break;
                case "AutoRun":
                    ApplyImmediateSetting("开机启动", settings => settings.IsAutoRun = enabled,
                        apply: () => new AutoStart().SetMeAutoStart(enabled));
                    break;
                case "AutoLogin":
                    ApplyImmediateSetting("启动时登录", settings => settings.IsAutoLogin = enabled);
                    break;
                case "AutoMin":
                    ApplyImmediateSetting("启动时收起", settings => settings.IsAutoMin = enabled);
                    break;
                case "SetLogin":
                    ApplyImmediateSetting("定时检测", settings => settings.IsSetLogin = enabled, restartNetwork: true);
                    break;
                case "EnhancedModeCheckBox":
                    ApplyImmediateSetting("增强模式", settings => settings.EnhancedMode = enabled, restartNetwork: true);
                    break;
                case "PreventSleepCheckBox":
                    ApplyImmediateSetting("阻止系统休眠", settings => settings.PreventSystemSleep = enabled,
                        apply: ApplySavedPowerSettings);
                    break;
                case "PreventDisplaySleepCheckBox":
                    ApplyImmediateSetting("阻止屏幕关闭", settings => settings.PreventDisplaySleep = enabled,
                        apply: ApplySavedPowerSettings);
                    break;
            }
        }

        private void ApplySavedPowerSettings()
        {
            var settings = new SettingsStore().Read();
            (ParentWindow ?? throw new InvalidOperationException("主窗口尚未就绪。"))
                .ApplyPowerSettings(settings.PreventSystemSleep, settings.PreventDisplaySleep);
        }

        private void Priority_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings || sender is not RadioButton option || option.IsChecked != true) return;
            int priority = option.Name == "dormitoryFirst" ? 1 : 0;
            ApplyImmediateSetting("连接优先顺序", settings => settings.PreferredNetwork = priority, restartNetwork: true);
        }

        private void Interval_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingSettings) return;
            if (!int.TryParse(loginTime.Text, out int interval) || interval < 1 || interval > 86400)
            {
                IntervalValidationText.Text = "请输入 1—86400 的整数秒；当前继续使用上次有效间隔。";
                return;
            }
            IntervalValidationText.Text = string.Empty;
            ApplyImmediateSetting("检测间隔", settings => settings.LoginTime = interval, restartNetwork: true);
        }

        private void LogFileSize_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingSettings) return;
            if (!int.TryParse(LogFileSize.Text, out int size) || size < 1 || size > 100)
            {
                LogSizeValidationText.Text = "请输入 1～100 MB，当前输入尚未保存。";
                return;
            }
            LogSizeValidationText.Text = string.Empty;
            ApplyImmediateSetting("日志大小上限", settings => settings.LogFileSizeMb = size);
        }

        private void MenuListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindParent<ListBoxItem>(e.OriginalSource as DependencyObject) is ListBoxItem item && item.IsSelected)
            {
                e.Handled = true;
            }
        }

        private void MenuListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 由滚动联动触发的选中变更不应再执行滚动，避免循环重置
            if (_isScrollingFromMenu) return;
            if (MenuListBox?.SelectedItem is not ListBoxItem item) return;

            string tag = item.Tag?.ToString() ?? "network";

            FrameworkElement? target = tag switch
            {
                "network" => NetworkPanel,
                "logging" => LoggingPanel,
                "about" => AboutPanel,
                _ => null
            };

            ScrollToSection(target);
        }

        private void ScrollToSection(FrameworkElement? target)
        {
            if (target == null || ContentScrollViewer == null) return;
            if (!target.IsLoaded)
            {
                // 控件尚未加载到可视树，延迟到布局完成后重试
                Dispatcher.BeginInvoke(() => ScrollToSection(target), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }

            _isScrollingFromMenu = true;
            try
            {
                var point = target.TransformToVisual(ContentScrollViewer).Transform(new Point(0, 0));
                ContentScrollViewer.ScrollToVerticalOffset(ContentScrollViewer.VerticalOffset + point.Y);
            }
            catch (InvalidOperationException)
            {
                // 目标控件与 ScrollViewer 暂不在同一可视树中（页面布局尚未完成）
            }
            finally
            {
                Dispatcher.BeginInvoke(() => _isScrollingFromMenu = false, DispatcherPriority.Background);
            }
        }

        private void ContentScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isScrollingFromMenu) return;
            if (ContentScrollViewer == null) return;

            var sections = new (FrameworkElement? Element, int Index)[]
            {
                (NetworkPanel, 0),
                (LoggingPanel, 1),
                (AboutPanel, 2)
            };

            // 底部边界处理：当滚动到最底部时，强制选中最后一个标签
            if (ContentScrollViewer.ScrollableHeight > 0 &&
                ContentScrollViewer.VerticalOffset >= ContentScrollViewer.ScrollableHeight - 0.5)
            {
                int lastIndex = sections.Length - 1;
                if (MenuListBox.SelectedIndex != lastIndex)
                {
                    _isScrollingFromMenu = true;
                    MenuListBox.SelectedIndex = lastIndex;
                    Dispatcher.BeginInvoke(() => _isScrollingFromMenu = false, DispatcherPriority.Background);
                }
                return;
            }

            // 顶部阈值算法：从后往前找到第一个顶部在判定线以上的面板
            double threshold = 40;
            int bestIndex = 0;

            for (int i = sections.Length - 1; i >= 0; i--)
            {
                var (element, index) = sections[i];
                if (element == null) continue;
                var point = element.TransformToVisual(ContentScrollViewer).Transform(new Point(0, 0));
                if (point.Y <= threshold)
                {
                    bestIndex = index;
                    break;
                }
            }

            if (MenuListBox.SelectedIndex != bestIndex)
            {
                _isScrollingFromMenu = true;
                MenuListBox.SelectedIndex = bestIndex;
                Dispatcher.BeginInvoke(() => _isScrollingFromMenu = false, DispatcherPriority.Background);
            }
        }

        #region 辅助方法

        private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent) return parent;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private void LoadSettingsToUi(SettingModel data, bool isReset)
        {
            bool hasConfig = isReset && data.PathExist();
            if (hasConfig)
            {
                data = data.Read();
            }

            // 网络设置
            NetworkEnabledCheckBox.IsChecked = !hasConfig || data.NetworkLoginEnabled;
            code.Text = hasConfig ? data.Username : string.Empty;
            secret.Password = hasConfig ? data.Password : string.Empty;
            loginTime.Text = data.LoginTime.ToString();
            LogFileSize.Text = data.LogFileSizeMb.ToString();
            EnhancedModeCheckBox.IsChecked = data.EnhancedMode;
            AutoRun.IsChecked = hasConfig && data.IsAutoRun;
            AutoLogin.IsChecked = hasConfig && data.IsAutoLogin;
            AutoMin.IsChecked = hasConfig && data.IsAutoMin;
            SetLogin.IsChecked = hasConfig && data.IsSetLogin;
            PreventSleepCheckBox.IsChecked = data.PreventSystemSleep;
            PreventDisplaySleepCheckBox.IsChecked = data.PreventDisplaySleep;

            carrier.SelectedIndex = hasConfig ? data.Carrier switch
            {
                "cmcc" => 1,
                "unicom" => 2,
                "telecom" => 3,
                _ => 0
            } : 0;

            campusFirst.IsChecked = !hasConfig || data.PreferredNetwork != 1;
            dormitoryFirst.IsChecked = hasConfig && data.PreferredNetwork == 1;

        }

        private (string Carrier, int Key) ResolveCarrier()
        {
            return carrier.SelectedIndex switch
            {
                1 => ("cmcc", 1),
                2 => ("unicom", 2),
                3 => ("telecom", 3),
                _ => (string.Empty, 0)
            };
        }

        #endregion
    }
}
