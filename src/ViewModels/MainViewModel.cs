using CommunityToolkit.Mvvm.Input;
using cpu_net.Constants;
using cpu_net.Model;
using cpu_net.Services;
using cpu_net.ViewModel.Base;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.IO;
using System.Windows.Threading;

namespace cpu_net.ViewModel
{
    public class MainViewModel : ViewModelBase
    {
        private readonly SettingModel _settingData = new SettingModel();
        private CancellationTokenSource? _networkCancellation;
        private readonly FileSystemWatcher? _settingsWatcher;
        private int _restartPending;
        private bool _stopped;
        private string? _lastProbeMessage;
        private string? _lastProbeDisplay;
        private long _lastProbeReport;
        private int _repeatedProbeCount;
        private long _lastLoginStart = -30000;
        private const int LoginCooldownMilliseconds = 30000;
        private readonly DispatcherTimer _logRefreshTimer;
        private int _logDirty;
        private int _checkingNetwork;
        private int _loggingIn;
        private readonly object _loginCancellationGate = new();
        private CancellationTokenSource? _activeLoginCancellation;
        private int _lastDetectionInterval = 10;

        // 内存日志缓冲区（即时显示，不依赖文件IO）
        private readonly Queue<string> _txtLogQueue = new Queue<string>();
        private bool _logCleared;
        private const int MaxLogLines = 500;

        public MainViewModel()
        {
            Debug.WriteLine("MainViewModel constructor called.");
            _logRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _logRefreshTimer.Tick += (_, _) =>
            {
                _logRefreshTimer.Stop();
                if (Application.Current.MainWindow?.IsVisible == false) return;
                if (Interlocked.Exchange(ref _logDirty, 0) != 0) OnPropertyChanged(nameof(TxtLog));
                _logRefreshTimer.Stop();
            };
            if (!Program.IsPreview)
            {
                _settingsWatcher = new FileSystemWatcher(AppDomain.CurrentDomain.BaseDirectory, "config.yaml");
                _settingsWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
                _settingsWatcher.Changed += (_, _) => ScheduleRestart();
                _settingsWatcher.Created += (_, _) => ScheduleRestart();
                _settingsWatcher.Deleted += (_, _) => ScheduleRestart();
                _settingsWatcher.Renamed += (_, _) => ScheduleRestart();
                _settingsWatcher.EnableRaisingEvents = true;
            }
            _ = LoadHistoryLogsAsync();
            RestartNetworkTimer();
        }

        /// <summary>
        /// 启动时异步加载历史日志到内存
        /// </summary>
        private async Task LoadHistoryLogsAsync()
        {
            var txtLog = await Task.Run(() => LoggingService.ReadLogText("Log", MaxLogLines));

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                lock (_txtLogQueue)
                {
                    // 用户已清空时，不让稍后完成的启动加载重新填入旧日志。
                    if (_logCleared) return;
                    foreach (var line in txtLog.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
                        _txtLogQueue.Enqueue(line);
                    while (_txtLogQueue.Count > MaxLogLines) _txtLogQueue.Dequeue();
                }

                OnPropertyChanged(nameof(TxtLog));
            });
        }

        public void RestartNetworkTimer()
        {
            if (Program.IsPreview || _stopped) return;
            _networkCancellation?.Cancel();
            lock (_loginCancellationGate) _activeLoginCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _networkCancellation = cancellation;
            _ = RunNetworkLoopAsync(cancellation);
        }

        private async void ScheduleRestart()
        {
            if (Interlocked.Exchange(ref _restartPending, 1) != 0) return;
            try
            {
                await Task.Delay(250);
                if (!_stopped) await Application.Current.Dispatcher.InvokeAsync(RestartNetworkTimer);
            }
            catch (Exception ex) { Debug.WriteLine(ex.GetType().Name); }
            finally { Volatile.Write(ref _restartPending, 0); }
        }

        private static bool CanCheck(SettingModel setting) => setting.NetworkLoginEnabled && setting.IsSetLogin &&
            !string.IsNullOrWhiteSpace(setting.Username) && !string.IsNullOrEmpty(setting.Password);

        private async Task RunNetworkLoopAsync(CancellationTokenSource cancellation)
        {
            var token = cancellation.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    SettingModel setting;
                    try
                    {
                        setting = new SettingsStore().ReadForNetwork();
                        _lastDetectionInterval = Math.Clamp(setting.LoginTime, 1, 86400);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is YamlDotNet.Core.YamlException)
                    {
                        ReportProbe($"配置读取失败（{ex.GetType().Name}），本轮不认证，{_lastDetectionInterval} 秒后重试。");
                        await Task.Delay(TimeSpan.FromSeconds(_lastDetectionInterval), token);
                        continue;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(_lastDetectionInterval), token);
                    if (!CanCheck(setting)) continue;
                    if (Interlocked.Exchange(ref _checkingNetwork, 1) != 0) continue;
                    try { await CheckNetworkAsync(token); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception ex) { ReportProbe($"网络检测异常：{NetworkService.DescribeFailure(ex)}"); }
                    finally { Volatile.Write(ref _checkingNetwork, 0); }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { Info($"定时检测已停止：{NetworkService.DescribeFailure(ex)}"); }
            finally
            {
                if (ReferenceEquals(_networkCancellation, cancellation)) _networkCancellation = null;
                cancellation.Dispose();
            }
        }

        public void StopBackgroundWork()
        {
            _stopped = true;
            _settingsWatcher?.Dispose();
            _networkCancellation?.Cancel();
            lock (_loginCancellationGate) _activeLoginCancellation?.Cancel();
            _logRefreshTimer.Stop();
        }

        private void ReportProbe(string message)
            => ReportProbeCore(message, () => message);

        private void ReportProbeDiagnostic(string key, string reason, string detail)
            => ReportProbeCore(key, () => ProbeDiagnostics.Save(reason, detail));

        private void ReportProbeCore(string key, Func<string> createMessage)
        {
            long now = Environment.TickCount64;
            if (_lastProbeMessage == key)
            {
                _repeatedProbeCount++;
                if (now - _lastProbeReport < 300000) return;
                Info($"{_lastProbeDisplay}（过去 5 分钟重复 {_repeatedProbeCount} 次）");
            }
            else
            {
                if (_repeatedProbeCount > 0) Info($"上一探测状态额外重复 {_repeatedProbeCount} 次，现已变化。");
                _lastProbeDisplay = createMessage();
                Info(_lastProbeDisplay);
            }
            _lastProbeMessage = key;
            _lastProbeReport = now;
            _repeatedProbeCount = 0;
        }

        private async Task CheckNetworkAsync(CancellationToken token)
        {
            if (!_settingData.PathExist()) return;
            var setting = new SettingsStore().ReadForNetwork();
            if (!setting.NetworkLoginEnabled || !setting.IsSetLogin ||
                string.IsNullOrWhiteSpace(setting.Username) || string.IsNullOrEmpty(setting.Password)) return;

            var probe = await CampusConnectionService.ProbeInternetAsync(setting.TestUrl, setting.TestCode, ReportProbe, token,
                ReportProbeDiagnostic);
            token.ThrowIfCancellationRequested();
            if (probe.State == InternetProbeState.Online)
            {
                if (_lastProbeMessage != null) ReportProbe($"外网探测已恢复正常，恢复时间={DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}。");
                _lastProbeMessage = null;
            }
            if (probe.State == InternetProbeState.CampusPortal && probe.PortalMode.HasValue)
            {
                await Task.Run(() => LoginOnline(probe.PortalMode, token), token);
            }
            else if (probe.State == InternetProbeState.Unknown && setting.EnhancedMode)
            {
                token.ThrowIfCancellationRequested();
                await Task.Run(() => LoginOnline(null, token, unknownStateRetry: true), token);
            }
        }

        #region 日志属性与命令

        public string TxtLog
        {
            get { lock (_txtLogQueue) return string.Join(Environment.NewLine, _txtLogQueue); }
        }

        private RelayCommand? _clearLogCommand;
        public RelayCommand ClearLogCommand => _clearLogCommand ??= new RelayCommand(() =>
        {
            lock (_txtLogQueue)
            {
                _logCleared = true;
                _txtLogQueue.Clear();
            }
            OnPropertyChanged(nameof(TxtLog));
        });

        public void Info(string message)
        {
            var line = $"{DateTime.Now:M-d HH:mm:ss}  {message}";
            lock (_txtLogQueue)
            {
                _txtLogQueue.Enqueue(line);
                while (_txtLogQueue.Count > MaxLogLines) _txtLogQueue.Dequeue();
            }
            QueueLogRefresh();
            LoggingService.WriteTextLog(message, "Log");
        }

        private void QueueLogRefresh()
        {
            if (Interlocked.Exchange(ref _logDirty, 1) != 0) return;
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_stopped) _logRefreshTimer.Start();
            }));
        }

        public void ResumeLogRefresh()
        {
            if (!_stopped && Volatile.Read(ref _logDirty) != 0) _logRefreshTimer.Start();
        }

        private AsyncRelayCommand? _loginButtonClick;
        public AsyncRelayCommand LoginButton_Click => _loginButtonClick ??= new AsyncRelayCommand(async () =>
        {
            await Task.Run(() => LoginOnline());
        });

        private RelayCommand? _bindButtonClick;
        public RelayCommand BindButton_Click => _bindButtonClick ??= new RelayCommand(() => BindOnline());

        #endregion

        public int LoginOnline(int? detectedPortalMode = null, CancellationToken cancellationToken = default,
            bool unknownStateRetry = false, bool isStartupLogin = false)
        {
            if (Program.IsPreview)
            {
                Info("预览模式不执行校园网认证。");
                return 0;
            }
            if (Interlocked.Exchange(ref _loggingIn, 1) != 0)
            {
                if (!detectedPortalMode.HasValue && !unknownStateRetry && !isStartupLogin)
                    Info("当前登录流程尚未完成，请等待结束后再手动登录。");
                return 0;
            }
            try
            {
                lock (_loginCancellationGate)
                {
                    if (_stopped) return 0;
                    _activeLoginCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cancellationToken = _activeLoginCancellation.Token;
                }
                int result = LoginCore(detectedPortalMode, cancellationToken, unknownStateRetry, isStartupLogin);
                if (unknownStateRetry && (result == 1 || result == 2))
                {
                    Info("校园认证已成功或已在线，本轮结束；继续探测外网，若仍状态不明，增强模式将在冷却结束后继续尝试。");
                }
                return result == 2 ? 0 : result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return 0; }
            catch (Exception ex)
            {
                Info($"登录流程无法继续：{NetworkService.DescribeFailure(ex)}");
                return 0;
            }
            finally
            {
                lock (_loginCancellationGate)
                {
                    _activeLoginCancellation?.Dispose();
                    _activeLoginCancellation = null;
                }
                Volatile.Write(ref _loggingIn, 0);
            }
        }

        private int LoginCore(int? detectedPortalMode, CancellationToken cancellationToken, bool unknownStateRetry,
            bool isStartupLogin)
        {
            bool automatic = detectedPortalMode.HasValue || unknownStateRetry;
            cancellationToken.ThrowIfCancellationRequested();
            if (!_settingData.PathExist())
            {
                Info("No Config Found");
                return 0;
            }

            var setting = new SettingsStore().ReadForNetwork();
            if (!setting.NetworkLoginEnabled ||
                (automatic && !setting.IsSetLogin) ||
                (unknownStateRetry && !setting.EnhancedMode)) return 0;
            if (string.IsNullOrWhiteSpace(setting.Username) || string.IsNullOrEmpty(setting.Password))
            {
                Info("请先设置账号和密码，本次未认证。");
                return 0;
            }

            // 各入口更新同一时间戳，但只有自动入口受冷却限制；手动只受正在执行的串行锁限制。
            long now = Environment.TickCount64;
            long remaining = LoginCooldownMilliseconds - (now - _lastLoginStart);
            if ((automatic || isStartupLogin) && remaining > 0)
            {
                return 0;
            }
            cancellationToken.ThrowIfCancellationRequested();
            _lastLoginStart = now;
            if (unknownStateRetry)
            {
                Info("外网状态不明，增强模式开始本轮重连；先确认校园认证 IP。");
            }

            bool hasCarrier = setting.Carrier == "cmcc" || setting.Carrier == "unicom" || setting.Carrier == "telecom";
            var targets = CampusConnectionService.ResolveTargets(setting.PreferredNetwork, Info,
                detectedPortalMode, includeDormitory: hasCarrier, cancellationToken: cancellationToken);
            bool foundTarget = false;
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foundTarget = true;
                var latest = new SettingsStore().ReadForNetwork(force: true);
                if (!latest.NetworkLoginEnabled || (automatic && !latest.IsSetLogin) ||
                    (unknownStateRetry && !latest.EnhancedMode) ||
                    latest.PreferredNetwork != setting.PreferredNetwork || latest.Username != setting.Username ||
                    latest.Password != setting.Password || latest.Carrier != setting.Carrier ||
                    (automatic &&
                        (latest.TestUrl != setting.TestUrl || latest.TestCode != setting.TestCode)))
                {
                    Info("认证相关设置已变更，本次停止，请按新设置重新检测或登录。");
                    return 0;
                }
                Info(target.Mode == 0 ? "开始尝试宿舍网登录。" : "开始尝试校园网登录。");
                cancellationToken.ThrowIfCancellationRequested();
                var outcome = AuthenticateTarget(target, latest, cancellationToken);
                if (outcome == LoginAttemptOutcome.Success) return 1;
                if (outcome == LoginAttemptOutcome.AlreadyOnline) return 2;
            }
            Info(foundTarget ? "本轮可用网络均未确认登录成功，请查看各次认证记录。"
                : "无法确认校园认证 IP，本次未认证；可检查 TUN 中校园认证地址的直连/路由排除配置。");
            return 0;
        }

        private enum LoginAttemptOutcome { Failed, Success, AlreadyOnline }

        private LoginAttemptOutcome AuthenticateTarget(CampusLoginTarget target, SettingModel setting, CancellationToken cancellationToken)
        {
            try
            {
                string loginUrl = BuildLoginUrl(target.Mode, setting, target.Address);
                string responseText = NetworkService.HttpGetRequest(loginUrl, Info, cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                // 去除首尾包裹（如 dr1004(...) ）
                if (string.IsNullOrWhiteSpace(responseText))
                {
                    Info("登录失败：认证接口返回空正文，请检查上方 HTTP 状态及耗时。");
                    return LoginAttemptOutcome.Failed;
                }

                var json = NetworkService.ExtractJson(responseText, NetworkConstants.LoginCallback);
                var loginResult = JsonSerializer.Deserialize<LoginResult>(json);

                if (loginResult == null)
                {
                    Info("登录失败：认证接口返回 JSON null。");
                    return LoginAttemptOutcome.Failed;
                }

                if (loginResult.result == 1)
                {
                    Info("登录成功");
                    return LoginAttemptOutcome.Success;
                }

                // result == 0，解析具体错误
                var errorCode = JsonSerializer.Deserialize<LoginErrorCode>(json);
                if (errorCode?.ret_code == 2)
                {
                    Info("本设备已在线，请勿重复登录");
                    return LoginAttemptOutcome.AlreadyOnline;
                }

                Info("登录失败");
                Info($"认证结果：result={loginResult.result}；ret_code={errorCode?.ret_code}");
                return LoginAttemptOutcome.Failed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (System.Net.Http.HttpRequestException e)
            {
                Info("登录失败");
                Info(NetworkService.DescribeFailure(e));
                return LoginAttemptOutcome.Failed;
            }
            catch (JsonException)
            {
                Info("登录响应解析失败，未确认认证成功。");
                return LoginAttemptOutcome.Failed;
            }
            catch (Exception e)
            {
                Info($"登录请求失败：{(e is InvalidOperationException ? e.Message : NetworkService.DescribeFailure(e))}");
                return LoginAttemptOutcome.Failed;
            }
        }

        /// <summary>
        /// 构建登录请求 URL
        /// </summary>
        private static string BuildLoginUrl(int mode, SettingModel setting, string localIp)
        {
            return mode switch
            {
                1 => $"{NetworkConstants.CpuLoginBaseUrl}&user_account=%2C0%2C{Uri.EscapeDataString(setting.Username)}&user_password={Uri.EscapeDataString(setting.Password)}" +
                     $"&wlan_user_ip={localIp}&wlan_user_ipv6=&wlan_user_mac=000000000000&wlan_ac_ip=&wlan_ac_name=&jsVersion=3.3.3&v=1954",
                _ => $"{NetworkConstants.BroadbandLoginBaseUrl}callback={NetworkConstants.LoginCallback}&login_method=1&user_account=%2C0%2C{Uri.EscapeDataString(setting.Username)}%40{Uri.EscapeDataString(setting.Carrier)}" +
                     $"&user_password={Uri.EscapeDataString(setting.Password)}&wlan_user_ip={localIp}&wlan_user_ipv6=&wlan_user_mac=000000000000&wlan_ac_ip=&wlan_ac_name=&jsVersion=4.2.2&terminal_type=1&lang=zh-cn&v=9745&lang=zh"
            };
        }

        private static void BindOnline()
        {
            System.Diagnostics.Process.Start("explorer.exe", NetworkConstants.SelfServiceUrl);
        }
    }
}


