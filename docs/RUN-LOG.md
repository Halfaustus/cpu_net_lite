# 运行可行性记录（2026-10-26）

执行者：主进程，未启动子进程。

范围：在现有 4.2.0 源码上实现 TUN 共存，并按用户追加要求移除电费查询、邮件与系统气泡通知、外观定制。本次不代表完整 Lite 已实现，也未升级原 net8.0-windows 工程或重打包旧安装程序。

## 环境

- 工作目录：`C:\Projects\CPU_NET_Lite\cpu_net-master`
- Windows x64，版本 10.0.26200；PowerShell。
- 起初 `dotnet --info` / `dotnet --list-sdks` 显示无 SDK；用户自行安装后重新检查，实际 SDK 为 10.0.403，MSBuild 18.9.15。
- 已有 Microsoft.WindowsDesktop.App 8.0.20 / 10.0.14 等运行时；实际工程目标仍是 net8.0-windows。
- 源码分支 master，基线 `2a2a148bf025fd026cdd6de153567c4d6dd3dd30`；修改未提交，未推送。

## 执行记录与异常处理

1. 首次执行下方构建命令（尚无 RestoreConfigFile 参数）：依赖还原失败，无法读取用户目录 `C:\Users\30731\AppData\Roaming\NuGet\NuGet.Config`，退出 1。尚未进入源码编译，属于环境权限限制。
2. 创建项目内 `artifacts/NuGet.Config`，清空源列表并仅使用 `https://api.nuget.org/v3/index.json`，缓存和产物均设置在项目内；增加 RestoreConfigFile 后重试，仍因同一用户配置目录的访问限制退出 1。
3. 通过平台权限机制执行构建，还原现有固定版本依赖成功；TUN 修改的 Release 构建退出 0，0 错误、90 警告。随后首页说明更新后的 --no-restore 编译也退出 0。
4. 用户追加移除功能后，按下方命令重新还原并构建最终源码：退出 0，0 错误、64 警告，耗时约 1.98 秒。警告包括引用可空性、旧 WebRequest API 和未使用事件；未扩展为代码质量评价或无关整改。

最终构建参数（此前的 --no-restore 检查仅额外添加该选项）：

```powershell
$env:DOTNET_CLI_HOME = 'C:\Projects\CPU_NET_Lite\cpu_net-master\artifacts\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_HTTP_CACHE_PATH = 'C:\Projects\CPU_NET_Lite\cpu_net-master\artifacts\nuget-http-cache'
$env:NUGET_PLUGINS_CACHE_PATH = 'C:\Projects\CPU_NET_Lite\cpu_net-master\artifacts\nuget-plugins-cache'
dotnet build .\cpu_net.csproj -c Release --nologo `
  -p:BaseOutputPath=C:/Projects/CPU_NET_Lite/cpu_net-master/bin/ `
  -p:BaseIntermediateOutputPath=C:/Projects/CPU_NET_Lite/cpu_net-master/obj/ `
  -p:MSBuildProjectExtensionsPath=C:/Projects/CPU_NET_Lite/cpu_net-master/obj/ `
  -p:RestorePackagesPath=C:/Projects/CPU_NET_Lite/cpu_net-master/artifacts/nuget-packages `
  -p:RestoreConfigFile=C:/Projects/CPU_NET_Lite/cpu_net-master/artifacts/NuGet.Config `
  -p:NuGetAudit=false *> .\artifacts\build-release.log
```

首次 SDK 调用还设置了 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1；SDK 仍输出初始化说明、工具 PATH 添加失败提示和开发证书已安装提示。后续明确关闭证书生成和工具 PATH 添加。未调用证书信任命令。

最终构建覆盖：项目 C# 编译与 WPF XAML 编译，包括新增 CampusConnectionService、改动的 MainViewModel、主页、设置页及主窗口。产物：`bin/Release/net8.0-windows/cpu_net.exe`；完整最终输出：`artifacts/build-release.log`。没有新增测试脚本。

源码变更清单读取时，`git status --short --branch` 成功；`git diff --stat` 因部分克隆缺失基线对象而尝试按需取回，遭遇 schannel SEC_E_NO_CREDENTIALS，统计未取得，未继续重试。该限制不影响已执行的源码构建，也没有创建提交或推送。

## 结论与未验证范围

**运行可行性检查通过，其他项目待人工验收。范围仅为最终 Release 编译与 XAML 编译。** 已发现的两次构建环境权限错误通过平台授权后重试解决，未发现编译错误。

未启动原应用，因为其启动路径仍包含更新服务、启动项修复、公告联网和防休眠设置等本次未修改的行为。不执行真实认证、TUN 切换、路由修改、开机启动设置、电费查询或邮件发送。

以下均为**未验证**：应用启动/初始化、实际认证、TUN 共存、门户跳转形式、定时重连、取消/设置变化、精简后的界面交互及旧配置读取/保存。没有执行发布或生成 ZIP，根目录旧安装程序仍为原文件。

既有 Regression 程序包含功能断言及旧 TUN/电费/邮件/外观用例，本次没有运行或修改，不能据此作当前产品的功能验收。旧配置模型字段和历史用户文件作为兼容数据保留，对应功能服务及调度入口已删除。功能正确性、UI、性能、安全、架构及最终接受与否全部待人工验收。

## 后续预览与界面调整记录

以上“未启动”是最初功能精简阶段的记录。用户随后要求启动查看，已加入 `--preview` 隔离自动任务：跳过更新初始化/用户数据恢复、启动项修复、联网公告、防休眠、登录和重连；不保存设置或执行手动认证。启动前逐次检查构建目录不存在 config.yaml。正常运行模式、真实认证及设置操作仍未执行。

- 预览入口 Release 编译退出 0，64 警告、0 错误。首次沙箱启动的进程未在用户桌面显示，关闭该次进程后通过平台授权启动桌面预览，曾返回主窗口及首页控件树；未作 UI 质量评价。
- 用户随后要求删除教程及上方文本框，按钮改为上方宽带登录/下方运营商绑定：对应命令、静态文案与公告加载均删除。重新编译退出 0，64 警告、0 错误，并重启预览；用户按 Esc 后停止 Computer Use，未继续界面操作。
- 开发者署名改为 Halfaustus、代码链接改为用户仓库，Authors/RepositoryUrl 与 README 维护入口同步，原来源说明和 LICENSE 保留。编译退出 0，64 警告、0 错误，已重启预览；用户再次按 Esc 后停止 Computer Use。
- 用户要求所有确认由其人工完成：后续只报告实际执行记录，不进行自动界面确认或给出验收结论。
- 应用显示名称统一为 CPU_NET_Lite（关于、窗口、托盘、Title/Product、README）。使用上述构建参数并增加 `--no-restore` 编译最终源码，退出 0，64 警告、0 错误，耗时约 1.43 秒；输出记录为 `artifacts/build-preview.log`。
- 显示名称修改后，仅关闭此前已记录且路径匹配的本次预览进程，重新执行 `Start-Process -FilePath .\bin\Release\net8.0-windows\cpu_net.exe -ArgumentList '--preview' -WindowStyle Normal -PassThru`；实际工作目录为可执行文件目录。返回 PID 28540、HasExited=False。进程记录保存在 `artifacts/preview-process-id.txt`。未继续点击、截图或确认当前窗口的显示结果，由用户查看。

没有创建提交、推送、发布或重打包旧安装程序。最新预览结果、界面内容与最终接受与否待用户人工确认。

## 移除自动更新与更新代理的执行记录

用户明确授权移除自动更新和更新代理。实际修改：

- 删除设置页代理导航/面板、代理配置加载/保存和关于页检查更新按钮；关于页版本直接读取程序集元数据。
- 删除托盘检查更新命令、启动更新初始化、后台检查、Velopack 入口及更新备份恢复/缓存清理调用。
- 删除 UpdateService、UpdateProgressWindow 两个文件和 Velopack 包引用；该服务中的进程默认代理/代理环境变量修改随之删除。
- 删除旧 `.github/workflows/release.yml`，并从 cpu_net.sln 移除旧安装器项目关联；保留旧安装器/卸载器源码及旧安装程序，不打包、发布或删除历史用户文件。
- 历史 UpdateProxy 配置字段仅作被动兼容，README、设计和交接记录已同步。

执行者仍为主进程，无子进程。环境仍为 Windows x64 / .NET SDK 10.0.403 / net8.0-windows 工程。使用本文件前述最终构建命令与环境参数（此次没有 --no-restore，重新还原移除 Velopack 后的依赖图），输出改为 `artifacts/build-remove-update.log`。实际退出 0，64 警告、0 错误，耗时约 2.97 秒；未出现编译错误，无运行错误修复事项。

仅终止此前记录且路径匹配的本次预览进程，随后以同一 `--preview` / WindowStyle Normal 命令重启；启动前 config.yaml 不存在，返回 PID 29212、HasExited=False，进程记录已更新。未进行自动点击、截图或界面确认，此版本主窗口显示、托盘、设置和正常模式行为均由用户人工确认；真实认证及其他未执行路径仍未验证。

## 用户请求正常模式启动

用户明确要求打开客户端正常运行，供其自行配置和进行真实环境确认。启动前仍未发现构建目录的 config.yaml，没有已保存凭据或开启的自动登录/重连。

为遵守 Lite 启动项边界，移除 App 正常启动时清理旧版启动项的调用；AutoStart 改为只管理当前用户 Run 键下的 CPU_NET_Lite 项，不再清理旧注册表项或启动文件夹快捷方式。网络设置仅在开机启动值发生变化时调用该服务。没有实际执行该服务或写入启动项。正常模式新增启动状态日志。

执行者：主进程。环境和项目路径同前。使用前述本地路径参数并增加 --no-restore 执行 Release 编译，日志为 `artifacts/build-normal-launch.log`；退出 0，64 警告、0 错误，耗时约 3.29 秒。没有编译错误，无相关运行错误修复记录。

仅关闭此前记录且路径匹配的本次预览进程，然后不带 --preview 执行：

```powershell
Start-Process -FilePath 'C:\Projects\CPU_NET_Lite\cpu_net-master\bin\Release\net8.0-windows\cpu_net.exe' `
  -WorkingDirectory 'C:\Projects\CPU_NET_Lite\cpu_net-master\bin\Release\net8.0-windows' `
  -WindowStyle Normal -PassThru
```

返回 PID 27560、HasExited=False；保存在 `artifacts/normal-process-id.txt`。仅记录启动命令返回情况，不自动点击、截图或确认界面。账号填写、保存、手动登录、TUN 共存及重连由用户操作确认；AI 未发送真实认证请求，实际认证路径仍未验证。此处更新了此前“正常模式未启动”的历史记录。

## 登录日志诊断、优先顺序与按钮位置

用户自行尝试登录未成功，随后授权读取日志。读取本地 `bin/Release/net8.0-windows/Log/202610.log` 的最近记录，输出前使用已保存配置的身份字段进行脱敏，没有展示账号、密码或完整认证参数。日志显示：14:12:53 尚未有配置；14:13:27 宽带和 CPU 状态接口均 HTTP 200，分别耗时约 245 / 185 ms，并均提供有效认证 IP；旧自动模式因两个接口都可用而停止，未发出登录认证请求。该记录不能证明密码错误或 TUN 路由故障，没有崩溃记录可据以归为运行异常。

用户追加要求：保存按钮移至账号信息下方；无法判断时依次尝试两个接口；进一步取消自动模式，改为校园网/宿舍网优先顺序。已修改设置页、主页、SettingModel、CampusConnectionService、MainViewModel 和说明。PreferredNetwork=0 表示校园网优先，1 表示宿舍网优先，默认0；旧 Mode 仅保留兼容数据。候选认证在同一个登录互斥内串行执行，成功或已在线停止；自动重连已有门户依据时只尝试对应网络。无有效运营商时跳过宿舍网。状态接口无有效地址的目标仍跳过。保存按钮仍保存全部网络设置。

当前配置读取仅输出了非敏感的模式/开关信息：旧 Mode=2，启动登录、定时检测、开机启动均为 true。AI 没有改写该配置、切换开关或写启动项。编译前只关闭此前记录的正常进程（PID 27560，路径匹配当前应用），没有扫描或退出其他程序。

执行者：主进程；Windows x64、SDK 10.0.403、net8.0-windows 工程。以本文件前述项目内构建路径参数执行 `dotnet build .\cpu_net.csproj -c Release --no-restore --nologo ... -p:NuGetAudit=false`，完整输出为 `artifacts/build-network-priority.log`；退出 0，64 警告、0 错误，耗时约 2.42 秒。未出现编译错误，未运行功能断言或发送真实认证请求。

因保存的自动行为已开启，编译后未自动重启，留给用户手动打开 `bin/Release/net8.0-windows/cpu_net.exe`。新版启动、界面、候选认证成功/已在线/失败切换、TUN 共存及重连均未执行，由用户人工确认；编译记录不构成上述验收结论。

## 即时设置、逐个查询与日志复制

用户反馈宿舍网优先仍看到校园网记录。脱敏日志显示：14:24:33 确实先校园网；14:24:40—41 用户启动记录包含宿舍网“登录成功”；14:25:01 的实际认证先宿舍网并返回已在线，校园网条目是预先查询第二个状态接口。当前非敏感配置 PreferredNetwork=1，启动登录/定时检测/开机启动均 true，间隔600秒。以上均为用户操作产生的日志，不是 AI 执行的功能测试，不推定 TUN 或其他功能已验收。

已按追加要求修改：保存按钮仅保存账号密码；其他设置修改时只保存各自字段，不能连带保存未提交的账号密码。初始化与预览不保存或操作系统设置。启动时选项保存后用于下次启动；开机启动仅响应用户切换。间隔无效输入保留上次有效值并提示。状态查询/认证改为按顺序逐个执行，首选成功/已在线后不访问另一目标；日志明确标为状态查询，已知门户仅作为自动重连触发依据，不覆盖优先顺序。右侧日志恢复选区高亮、支持 Ctrl+C（无选区复制全部）、右键复制选中/全部及全选；剪贴板占用异常提示。

主进程未扫描或终止用户手动打开的进程，先编译至项目内暂存目录。用户回复“已退出”后，已替换11项运行产物，明确保留 config.yaml、Log、RecordLog、ErrorLog、Images。追加日志复制后再次编译并替换。没有启动客户端、切换开关、操作剪贴板或发起真实认证。

环境仍为 Windows x64 / SDK 10.0.403 / net8.0-windows；使用前述 --no-restore 和项目内路径参数，额外指定 OutputPath=C:/Projects/CPU_NET_Lite/cpu_net-master/artifacts/immediate-settings/、AppendTargetFrameworkToOutputPath=false。即时设置版本退出0、64警告/0错误，约23.86秒；追加复制后的最终版本退出0、64警告/0错误，约23.72秒。最终构建日志 artifacts/build-immediate-settings.log，暂存产物 artifacts/immediate-settings/ 已复制至 bin/Release/net8.0-windows/。

未出现编译错误，无新增运行异常修复记录。最新启动、初始化事件抑制、设置即时应用、账号密码独立保存、排序、选区/快捷键/右键复制及剪贴板占用路径均未执行，待用户人工确认；编译退出0不构成功能或界面验收结论。

## 2026-10-26 根目录整理与 .NET 10 第一阶段

执行者：主进程；无子进程。用户明确要求先完成本阶段，CLI 和一键安装包等待确认后再开发。

迁移：先核对源目录 C:\Projects\CPU_NET_Lite\cpu_net-master、目标根目录及全部子项不存在同名冲突，然后使用 PowerShell Move-Item 将源目录所有子项（含 .git、源码、artifacts、bin、obj、历史配置和日志）迁到根目录；仅在源目录为空后移除空目录。原根目录 AGENTS.md、HANDOFF.md、旧安装程序保留。Git 仍为 master、既有远端不变；没有提交、推送、重置或执行安装器。

代码：主 cpu_net.csproj 的 TargetFramework 从 net8.0-windows 改为 net10.0-windows，移除两个 D 盘 BaseOutputPath/BaseIntermediateOutputPath，让 SDK 使用项目内 bin/obj。现有固定依赖、认证逻辑和配置格式保持。用户先要求保留防休眠，随后明确不需要阻止屏幕关闭：MainWindow 仅请求 ES_CONTINUOUS | ES_SYSTEM_REQUIRED，关闭时恢复 ES_CONTINUOUS；删除未使用的显示/离开模式标志。未实际执行电源 API。

环境：Windows x64 / PowerShell；dotnet --list-sdks 返回 10.0.403；dotnet --list-runtimes 确认 Microsoft.WindowsDesktop.App 10.0.14 已安装。工作目录为 C:\Projects\CPU_NET_Lite。

实际构建命令：

```powershell
$env:DOTNET_CLI_HOME = 'C:\Projects\CPU_NET_Lite\artifacts\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_HTTP_CACHE_PATH = 'C:\Projects\CPU_NET_Lite\artifacts\nuget-http-cache'
$env:NUGET_PLUGINS_CACHE_PATH = 'C:\Projects\CPU_NET_Lite\artifacts\nuget-plugins-cache'
dotnet build .\cpu_net.csproj -c Release --nologo -p:RestorePackagesPath=C:/Projects/CPU_NET_Lite/artifacts/nuget-packages -p:RestoreConfigFile=C:/Projects/CPU_NET_Lite/artifacts/NuGet.Config -p:NuGetAudit=false *> .\artifacts\build-net10.log
```

结果：退出0，64警告、0错误，44.65秒。覆盖主应用依赖还原、C# 和 WPF XAML 编译；产物 bin/Release/net10.0-windows/cpu_net.exe。警告主要为已有可空性、旧 WebRequest API、未使用事件；不扩大为质量整改。本阶段没有发现编译错误，无运行错误修复事项。

**运行可行性检查通过，其他项目待人工验收。范围仅为主应用 .NET 10 Release 编译。**

未验证：新版启动/加载、真实认证、TUN/自动重连、设置交互、防系统休眠和屏幕关闭行为。未执行任何应用启动、功能断言或真实网络认证，不读取输出真实凭据，不修改用户自动登录、重连或启动项。历史 bin/Release/net8.0-windows/config.yaml 和日志原样保留，新版输出目录未复制用户配置。由于目录迁移，旧快捷方式/启动项可能指向失效路径，需用户后续手动调整。本阶段不发布分发文件夹/ZIP、不生成安装器；旧 SetupBootstrapper/uninstaller 未迁移目标框架，也未参与主项目编译。

## 默认检测间隔3600秒（2026-10-26）

主进程仅修改 src/Models/SettingModel.cs：LoginTime 缺省值由5改为3600；已有配置的显式值不变。README及DESIGN同步。保存按钮下方运营商小字来自即时保存的 SettingsStatusText，本次解释其来源，未改动提示。未启动子进程，未修改用户配置。

环境：Windows x64 / SDK 10.0.403 / net10.0-windows，工作目录 C:\Projects\CPU_NET_Lite。设置 DOTNET_CLI_HOME 为项目 artifacts/dotnet-home，并设置 DOTNET_CLI_TELEMETRY_OPTOUT=1、DOTNET_GENERATE_ASPNET_CERTIFICATE=false、DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false。

首次命令：dotnet build .\cpu_net.csproj -c Release --no-restore --nologo -p:NuGetAudit=false -p:CopyRetryCount=0。退出1，64警告、1错误，23.94秒。MSB3021：bin/Release/net10.0-windows/cpu_net.exe 被其他进程占用，apphost.exe 无法复制；记录 artifacts/build-default-interval.log。没有终止用户进程。

改为暂存目录重试：dotnet build .\cpu_net.csproj -c Release --no-restore --nologo -p:NuGetAudit=false -p:OutputPath=C:/Projects/CPU_NET_Lite/artifacts/default-interval/ -p:AppendTargetFrameworkToOutputPath=false。退出0，64警告、0错误，24.62秒；记录 artifacts/build-default-interval-staged.log。产物 artifacts/default-interval/。

运行可行性检查通过，其他项目待人工验收。范围仅为暂存目录 Release C#/XAML 编译。原运行目录覆盖仍受文件占用阻塞，待用户退出后替换，不将首次失败构建的输出视为完整交付。未启动、认证或改写真实配置，默认显示和实际调度路径未验证；已有显式间隔不随新默认值改变。CLI和安装包仍暂停。

## 保存账号、密码和运营商的边界修正（2026-10-26）

用户澄清三项一起手动保存，下方其他配置才即时保存。主进程修改 ConfigurationPage.xaml/.cs：按钮及事件改为保存网络配置；在同一次 SettingsStore.Update 中写入 Username、Password、Carrier、Key；移除运营商 SelectionChanged 即时保存事件。成功写入后重置检测计时，不主动认证；其余设置仍仅保存各自字段。提示、README、AGENTS和HANDOFF同步，默认3600秒保留。没有改写真实用户配置，没有启动子进程。

环境 Windows x64 / .NET SDK 10.0.403 / net10.0-windows；工作目录 C:\Projects\CPU_NET_Lite。沿用上一节项目内 DOTNET_CLI_HOME 和禁用遥测、证书生成、工具PATH添加的环境参数。
实际命令：dotnet build .\cpu_net.csproj -c Release --no-restore --nologo -p:NuGetAudit=false -p:OutputPath=C:/Projects/CPU_NET_Lite/artifacts/default-interval/ -p:AppendTargetFrameworkToOutputPath=false。
退出0，64警告、0错误，24.30秒；日志 artifacts/build-network-config-save.log。运行可行性检查通过，其他项目待人工验收。范围仅为暂存输出的Release C#/XAML编译，未发现编译错误。未启动客户端、触发认证或操作设置验证。原运行目录此前被用户客户端占用，尚未替换完整产物，等待用户退出后更新并保留配置和日志；界面、三项共同保存和其他设置独立保存待人工确认。CLI及安装包继续暂停。

## 精简阶段一完成记录（2026-10-26）

执行者：主进程，未使用子进程。用户采纳精简建议后按阶段实施。

实际修改：删除无主程序调用的UserModel、LoginInfoViewModel、LoginPasswordBoxHelper、Utils、CommandBase及空Properties/Settings生成文件；移除NetworkService未使用POST代码与专用响应分支、无引用Google DNS/IP前缀/备用HTTPS常量；移除MainViewModel的RecordLog队列、历史加载、属性及Record入口，LoggingService不再保留该专用日志分支，历史文件不删除。旧testMode参数仅保留用于历史测试调用签名兼容。运营商改为固定只读列表/初始化后不变的选项，删除重复配置读取和未绑定ComboxItem。SettingModel改用ViewModelBase实现属性通知，保留兼容字段和现有序列化格式。移除主应用及历史Regression项目的Prism引用，后者仅增加ViewModelBase源码链接，不改测试断言、不运行历史测试。

环境：Windows x64，SDK 10.0.403，主应用net10.0-windows；工作目录C:\Projects\CPU_NET_Lite。沿用此前项目内DOTNET_CLI_HOME、NuGet缓存路径和关闭遥测/证书生成/工具PATH修改的环境参数。

实际命令：
```powershell
dotnet build .\cpu_net.csproj -c Release --nologo -p:RestorePackagesPath=C:/Projects/CPU_NET_Lite/artifacts/nuget-packages -p:RestoreConfigFile=C:/Projects/CPU_NET_Lite/artifacts/NuGet.Config -p:NuGetAudit=false -p:OutputPath=C:/Projects/CPU_NET_Lite/artifacts/cleanup-stage1/ -p:AppendTargetFrameworkToOutputPath=false
```
输出重定向至artifacts/build-cleanup-stage1.log。构建在消息间隔期间结束，恢复轮询时旧工具进程句柄已失效，未取得最终shell退出码；保存的完整MSBuild日志报告生成成功、20警告/0错误、25.17秒，产物artifacts/cleanup-stage1/cpu_net.exe存在。未发现编译错误，无代码运行错误修复事项。obj/project.assets.json中包列表现为CommunityToolkit.Mvvm/8.3.2、Hardcodet.NotifyIcon.Wpf/2.0.0、YamlDotNet/16.3.0。

运行可行性检查通过，其他项目待人工验收。范围仅主项目依赖还原及Release C#/XAML编译。未启动、认证、操作真实配置或覆盖被占用的运行目录；历史Regression项目编译/执行未验证。属性通知、配置兼容、运营商保存、日志、真实认证及界面待人工确认，不能由警告数量变化推定质量或性能收益。

3600秒默认间隔、账号密码运营商统一保存、下方设置即时保存、防系统休眠且允许熄屏及认证保护均保留。后续配置迁移、剩余依赖替换、网络异步化、界面简化、旧发布/安装源码整理、CLI和安装包尚未实施。新增OpenWrt.txt及openwrt目录非本工作包成果，未修改。无提交、推送或发布。

## 用户要求启动人工验收（2026-10-26）
用户明确要求启动当前阶段程序供人工验收。启动前确认artifacts/cleanup-stage1/cpu_net.exe存在，该目录config.yaml不存在；未复制旧凭据或配置。主进程执行Start-Process -FilePath C:\Projects\CPU_NET_Lite\artifacts\cleanup-stage1\cpu_net.exe -WorkingDirectory C:\Projects\CPU_NET_Lite\artifacts\cleanup-stage1 -WindowStyle Normal -PassThru（正常模式），返回PID41616；等待2秒后HasExited=False。PID记录于artifacts/cleanup-stage1-process-id.txt。没有关闭用户已有实例、操作界面、修改启动项或发起认证；正常模式按既有代码请求防系统休眠、允许屏幕关闭。
本次证据仅为启动命令成功返回且2秒后进程未退出，未自动确认主窗口/功能，不能据此推定无初始化异常。用户自行填写网络配置、保存并进行认证和其他人工验收。此前“未启动”由本条更新，其他未执行路径仍未验证。

用户要求再次启动：此前记录进程已不存在，暂存目录仍无config.yaml。以相同路径和正常模式Start-Process重新启动，PID=30936，2秒后HasExited=False。未关闭其他进程、未操作界面或发起认证，人工验收由用户进行。

用户报告启动错误弹窗已手动关闭，并要求再次重启。核对PID22432及暂存版路径后停止该次进程，正常模式重新启动PID=8700。启动前仍无config.yaml，未自动认证或操作界面。前次进程存活不能作为启动成功证据；错误弹窗内容尚未取得，启动路径未验证，等待用户保留报错。

## 运营商绑定启动错误修复及窗口紧凑布局（2026-10-26）

用户截图和暂存版ErrorLog确认启动异常：无法对CarrierViewModel只读属性ComboxList进行TwoWay/OneWayToSource绑定。原精简遗漏了ItemsSource的绑定方向，属于本次修改引入的运行错误，与此前猜测的权限原因不同。修复ConfigurationPage.xaml为Mode=OneWay。仅停止此前由AI启动且路径一致的PID8700，构建至artifacts/cleanup-stage1/：dotnet build .\cpu_net.csproj -c Release --no-restore --nologo -p:NuGetAudit=false -p:OutputPath=C:/Projects/CPU_NET_Lite/artifacts/cleanup-stage1/ -p:AppendTargetFrameworkToOutputPath=false。退出0、20警告/0错误、4.79秒，日志artifacts/build-carrier-binding-fix.log。

首次正常启动被自动审批拒绝，理由为可能从其他目录加载真实配置。随后只读核对SettingsStore/SettingModel均仅从应用基目录config.yaml读取，无用户目录回退；MainWindow和定时检查无配置时不认证，再次确认暂存目录无config.yaml。携带源码证据申请后启动获准。Start-Process正常启动返回PID32560；3秒后HasExited=False、MainWindowTitle=CPU_NET_Lite、Responding=True，ErrorLog文件尺寸/时间未变，普通日志产生正常启动记录。用户随后明确确认正常启动，功能暂未发现问题。原启动错误已在此次路径消失；这不是其他功能的全面验收。

用户追加要求窗口缩放和减少留白：MainWindow默认改640×420、最小480×320、CanResizeWithGrip；首页按钮列按内容占宽且靠上、日志填充剩余空间，移除比例留白网格；设置页侧栏100，表单输入列改为*，缩减内边距和关于区空白。仅修改三份XAML，不改认证及保存逻辑。

Windows x64 / SDK10.0.403 / net10.0-windows，在C:\Projects\CPU_NET_Lite设置项目内DOTNET_CLI_HOME及关闭遥测/证书生成/工具PATH添加，执行dotnet build .\cpu_net.csproj -c Release --no-restore --nologo -p:NuGetAudit=false -p:OutputPath=C:/Projects/CPU_NET_Lite/artifacts/compact-layout/ -p:AppendTargetFrameworkToOutputPath=false，退出0、20警告/0错误、4.46秒；日志artifacts/build-compact-layout.log。没有编译错误，无新增运行错误修复事项。

运行可行性检查通过，其他项目待人工验收。新布局范围仅Release C#/XAML编译；新布局启动及各尺寸交互未验证。未重启正在人工验收的PID32560，未复制/修改用户配置、未认证或操作界面，未生成安装器/CLI。新产物artifacts/compact-layout/，等待用户退出后替换实际验收目录程序文件并保留配置日志。

用户明确已退出并要求启动新布局。将compact-layout的7项应用文件复制到cleanup-stage1，保留config.yaml及所有日志。只读取非敏感开关，确认启动登录/定时检测/启动收起已开启，启动前已告知用户会按现有配置认证并收起托盘；依用户启动指令正常启动，PID=20120，2秒后HasExited=False。未自行点击认证、修改配置或进行功能/界面验收，认证结果不读取、不推定成功。

## 人工确认与本地Git归档（2026-10-26）
用户在新布局启动后明确回复“我已确认，保存至本地git，后续开发计划冻结”。据此记录当前版本已获用户确认，不将其扩展为AI进行的全面功能/质量验收。执行本地提交，不推送、不发布；后续开发和原计划估测均冻结。本轮仅更新冻结记录和Git归档，未修改程序逻辑、未重新编译或执行测试，沿用前述实际构建与启动记录。当前验收目录仍为artifacts/cleanup-stage1，配置和日志保留在磁盘；OpenWrt相关文件不属于本次提交范围。

## 电源选项专项修改（2026-10-27）
用户先要求系统休眠可选，随后追加屏幕关闭可选。本项明确解冻，其他计划继续冻结。主进程修改SettingModel、MainWindow.xaml.cs、ConfigurationPage.xaml/.cs，增加PreventSystemSleep（缺省true）和PreventDisplaySleep（缺省false），在定时检测下方依次增加复选框。每次仅写入自身字段，保存后读取两项已保存值并在UI线程组合调用SetThreadExecutionState，启动同样读取保存值；退出释放。返回0时提示应用失败。预览及初始化UI事件不执行保存或系统操作。README/设计/交接同步。无新增包，无子进程。

运行环境Windows x64 / SDK10.0.403 / net10.0-windows，项目C:\Projects\CPU_NET_Lite；环境参数沿用项目artifacts/dotnet-home及关闭遥测/证书生成/工具PATH添加。命令：dotnet build .\cpu_net.csproj -c Release --no-restore --nologo -p:NuGetAudit=false -p:OutputPath=C:/Projects/CPU_NET_Lite/artifacts/sleep-option/ -p:AppendTargetFrameworkToOutputPath=false。
仅系统休眠初版：退出0，20警告/0错误，5.51秒，artifacts/build-sleep-option.log。
加入独立屏幕开关后的最终版：退出0，20警告/0错误，4.12秒，artifacts/build-power-options.log。

运行可行性检查通过，其他项目待人工验收。范围仅最终Release C#/XAML编译。未发现编译错误；未启动或重启客户端，未操作用户配置/真实电源API/认证。新开关加载、四种组合、即时生效、重启保存与实际休眠/熄屏路径未验证，待人工确认。产物artifacts/sleep-option/，尚未替换用户当前验收目录。未创建Git提交或推送；此前本地归档因.git写权限与自动审批额度错误未完成。

## 主页清空日志按钮（2026-10-27）
主进程按用户追加要求修改HomePage.xaml、MainViewModel.cs：绑定按钮下新增清空日志命令，清空内存队列并通知文本更新，不调用磁盘删除；启动历史回填在用户已清空时跳过，队列读取/清空/回填使用同一锁，历史回填遵守既有500行上限。后续新日志照常显示并持久写入。工具提示说明重启仍加载历史。没有启动子进程，其他计划继续冻结。
Windows x64 / SDK10.0.403 / net10.0-windows，在C:\Projects\CPU_NET_Lite设置项目artifacts/dotnet-home及关闭遥测/证书生成/工具PATH添加后，执行dotnet build .\cpu_net.csproj -c Release --no-restore --nologo -p:NuGetAudit=false -p:OutputPath=C:/Projects/CPU_NET_Lite/artifacts/sleep-option/ -p:AppendTargetFrameworkToOutputPath=false。
退出0，20警告、0错误，43.81秒；日志artifacts/build-clear-log.log。运行可行性检查通过，其他项目待人工验收。范围仅最终Release C#/XAML编译，未发现编译错误。未启动程序、点击清空、改写用户配置、操作剪贴板或发送认证；新按钮加载及清空交互未验证。产物artifacts/sleep-option/包含此前两个电源开关，尚未替换当前验收目录，README/设计/交接已更新，无提交或推送。

2026-10-27用户要求启动电源开关及清空日志版本供人工验收。启动前未发现cpu_net运行进程；将sleep-option中7项程序文件更新至cleanup-stage1，保留配置及日志。仅读取非敏感开关并告知用户启动登录/检测/收起已开启；依用户指令正常启动PID=18836，2秒后HasExited=False。未点击认证、清空日志、电源开关或操作剪贴板，不推定人工验收结论。

## 短间隔检测优化（2026-10-27）
执行者：主进程，无子进程。实现范围和人工验收清单见DESIGN同名章节。保留原未提交成果、真实配置、历史日志及OpenWrt相关文件，未创建提交或发布。
环境：Windows x64，dotnet --version实际返回10.0.403，主工程net10.0-windows。
命令：dotnet build .\cpu_net.csproj -c Release -o .\artifacts\short-interval --no-restore
首次沙箱执行因无法访问C:\Users\30731\.dotnet而失败，属于环境权限阻断；随后通过平台授权在沙箱外重跑同一编译。退出0，20警告、0错误，编译报告耗时3.07秒。输出日志artifacts/build-short-interval.log；产物artifacts/short-interval/。
运行可行性检查通过，其他项目待人工验收。实际范围仅Release C#/XAML编译，未发现编译错误；没有启动客户端、执行认证、功能测试或性能测试，没有操作真实设置及历史日志。启动与新增运行路径未验证。未替换cleanup-stage1中的用户运行版本。

## 日志配置侧栏跳转（2026-10-27）
用户要求增加日志配置快速跳转。主进程在ConfigurationPage.xaml新增“日志配置”菜单及LoggingPanel锚点，在ConfigurationPage.xaml.cs补充跳转映射和滚动同步索引。日志设置仍即时保存，无新增保存按钮，不改变探测策略。无子进程。
Windows x64 / .NET SDK10.0.403，执行dotnet build .\cpu_net.csproj -c Release -o .\artifacts\short-interval --no-restore，退出0，20警告/0错误，耗时1.47秒，日志artifacts/build-short-interval.log。
运行可行性检查通过，其他项目待人工验收。范围仅Release编译；未启动或认证，侧栏跳转及滚动交互未验证。未替换当前运行版本、未提交或推送。
2026-10-27 用户要求启动一次：启动前未发现 cpu_net 进程，将 short-interval 的7项程序文件复制至 cleanup-stage1，保留目标配置和日志；通过平台授权正常启动，PID=28548，2秒后 HasExited=False，MainWindowTitle 为空。未操作认证或设置，未据此推定主窗口加载及功能验收通过；如已保存启动时收起，请从托盘打开。

## 状态不明有限重连（2026-10-27）
主进程实现可配置UnknownReconnectAttempts（默认3，0～100）及有限重连流程，详情见DESIGN同名章节。无子进程、无提交或推送。已有配置未改写，当前运行实例未停止或替换。
Windows x64 / SDK10.0.403，执行dotnet build .\cpu_net.csproj -c Release -o .\artifacts\short-interval --no-restore，退出0，20警告/0错误，1.55秒。日志artifacts/build-short-interval.log。
运行可行性检查通过，其他项目待人工验收。范围仅Release C#/XAML编译。未启动新版、未进行认证或功能/性能测试；新增保存、计数、冷却和重连路径未验证。

## 统一30秒登录冷却（2026-10-27）
主进程实现所有登录入口共用30秒轮次开始间隔，有限重连计数移动到获准开始轮次后；无子进程。实际环境Windows x64/.NET SDK10.0.403。
首次dotnet build .\cpu_net.csproj -c Release -o .\artifacts\short-interval --no-restore退出0，23警告/0错误，11.95秒，其中3条为cpu_net.exe(PID4696)占用导致的复制重试，最终复制成功。未主动停止进程；该次构建更新了short-interval输出文件，不能声称未触及用户运行目录。
随后改到独立输出执行dotnet build .\cpu_net.csproj -c Release -o .\artifacts\shared-cooldown --no-restore，退出0，增量构建0警告/0错误，1.34秒，日志artifacts/build-shared-cooldown.log。增量0警告不表示历史警告已修复。
运行可行性检查通过，其他项目待人工验收。实际仅Release编译，未启动/重启程序，未改用户配置或发送认证，新冷却及交互路径未验证。未提交或推送。
2026-10-27 用户要求重启：执行前未发现cpu_net运行进程。将shared-cooldown的7项程序文件更新到最近实际使用的short-interval目录，保留该目录配置及日志，正常启动PID30212；2秒后HasExited=False，窗口标题为空。未操作配置或主动点击认证；程序按保存设置自行运行，未推定功能验收通过。

## V1.0 工程归档与发布编译（2026-10-27）
用户明确确认当前运行正常，授权整理最终工程、保存Git并推送GitHub。主进程执行，无子进程。版本统一1.0.0（Assembly/File 1.0.0.0），解决方案仅含主应用；历史回归工程目标统一net10.0，未改动或执行功能断言。
旧安装器/卸载器源码、旧发布配置、IDE用户文件、无引用旧图片、旧bin及7个阶段构建目录移至.local-archive/pre-v1.0，保留原配置和日志。忽略归档、运行配置和OpenWrt内容，MSBuild排除这些非工程目录。README/DESIGN/HANDOFF整理为当前版本说明；历史设计备份本地归档，运行记录保留。
Windows x64 / SDK10.0.403，执行dotnet publish .\cpu_net.csproj -c Release --no-restore --self-contained false -o .\artifacts\V1.0，退出0，20条警告、无编译错误；日志artifacts/build-v1.0.log。输出7项程序文件，不包含配置、日志、旧安装器。
运行可行性检查通过，其他项目待人工验收。范围仅整理后的Release构建/发布；没有启动或发送认证，没有改写用户配置或启动项。用户已确认的是整理前功能，不能以发布编译替代整理后人工验收。
提交前fetch确认origin/master与本地HEAD相同（2a2a148），无需合并或强推。计划以master推送origin并创建v1.0标签；实际提交/推送结果见交付信息与Git历史。CLI及安装器继续冻结。

## 手动登录冷却范围修正（2026-10-27）
用户澄清30秒只限制自动登录，手动登录仅需等待当前登录流程结束。主进程修改MainViewModel：冷却仅检查自动入口，每个实际开始的轮次继续刷新共享时间戳；手动遇到正在执行的流程时提示等待。MainWindow显式标记启动登录，维持启动登录独立于定时检测开关。同步配置页说明、README及DESIGN，不修改次数预算、凭据或其他认证保护。
Windows x64/.NET SDK10.0.403；dotnet build .\cpu_net.csproj -c Release -o .\artifacts\manual-login-fix --no-restore，退出0，20警告/0错误，7.12秒。运行可行性检查通过，其他项目待人工验收。范围仅Release编译，未启动或认证；手动连续操作、自动冷却、忙碌提示待人工验证。没有提交/推送，不移动已发布v1.0标签，产物独立保存以待人工确认。
2026-10-27 用户要求合并至主要位置：源码本已在主工程，现执行dotnet publish .\cpu_net.csproj -c Release --no-restore --self-contained false -o .\artifacts\V1.0，退出0，本次增量发布未报告警告/错误。保留V1.0已有config.yaml和Log，清理manual-login-fix目录中的7个纯编译文件及空目录。运行可行性检查通过，其他项目待人工验收；仅发布，未启动/认证，未提交推送，v1.0标签不变。

## 增强模式专项（2026-10-28）
执行者：主进程，未启动子进程。修改前读取AGENTS.md、当前源码、DESIGN、RUN-LOG及Git状态，分支master；保留原7个文件中的未提交成果，尤其手动登录不受冷却限制及启动登录显式标记。未读取真实凭据或开展网络故障诊断。
修改：配置页以增强模式复选框取代次数输入，EnhancedMode默认false、即时单字段保存，初始化/预览沿用禁止保存及系统操作保护。移除未知状态次数、上限和成功/已在线后的完成锁止；增强模式开启时持续按探测结果及30秒自动冷却重连，成功/已在线仅结束当前轮。旧UnknownReconnectAttempts仅兼容保留，不参与决策、不映射新开关。保留认证IP确认、串行锁、优先顺序、发送前最新设置检查及手动登录冷却修正。同步README和DESIGN。
环境：Windows x64、PowerShell、.NET SDK10.0.403、net10.0-windows，工作目录C:\Projects\CPU_NET_Lite。设置DOTNET_CLI_TELEMETRY_OPTOUT=1、DOTNET_GENERATE_ASPNET_CERTIFICATE=false、DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false。
实际命令：dotnet build .\cpu_net.csproj -c Release -o .\artifacts\enhanced-mode --no-restore
退出0，20警告/0错误，10.34秒；日志artifacts/build-enhanced-mode.log，产物artifacts/enhanced-mode。未发现编译错误，无运行错误修复事项。
运行可行性检查通过，其他项目待人工验收。范围仅Release C#/XAML编译。未启动客户端、发送认证、改写真实配置或更新artifacts/V1.0；开关加载/保存、持续重连、冷却、关闭后的停止及其他实际运行路径未验证，人工事项见DESIGN。未执行功能测试、修改测试断言、提交或推送，其他开发计划继续冻结。

### 增强模式：外网探测异常诊断追加（2026-10-28）
主进程实现，无子进程。保留已有未提交成果。新增ProbeDiagnostics，修改CampusConnectionService探测诊断、MainViewModel日志去重及恢复提示；同步设计和README。具体字段、保守脱敏与去重边界见DESIGN。未改变正常判定表达式及已知门户判定规则，未开展网络故障诊断或功能验收。
环境Windows x64/PowerShell/.NET SDK10.0.403/net10.0-windows，目录C:\Projects\CPU_NET_Lite；沿用关闭遥测/开发证书生成/工具PATH修改的环境参数。实际命令：dotnet build .\cpu_net.csproj -c Release -o .\artifacts\enhanced-mode --no-restore。退出0，20警告/0错误，3.62秒，日志artifacts/build-enhanced-mode-diagnostics.log。未发现编译错误，无运行错误修复事项。
运行可行性检查通过，其他项目待人工验收。范围仅Release C#/XAML编译。产物artifacts/enhanced-mode包含增强模式及此次诊断追加；未替换V1.0、未启动程序、未发起外网探测或真实认证、未改写用户配置、未提交推送。异常日志内容/去重/恢复及增强模式实际行为均未验证，交由用户人工验收。

2026-10-28 用户明确要求启动。启动前未发现cpu_net.exe进程，artifacts/enhanced-mode/config.yaml不存在；未复制凭据。正常模式启动artifacts/enhanced-mode/cpu_net.exe，Start-Process返回PID=42576，即时HasExited=False。本条仅记录启动命令返回，不推定初始化或功能验收通过；未操作界面、认证或设置，由用户人工验收。

### 设置说明精简（2026-10-28）
用户要求详细解释仅保留在README。主进程移除配置页保存方式、优先顺序、增强模式、TUN与日志轮转的常驻说明，移除电源和间隔详细ToolTip；保留名称、单位、范围、错误提示及简短保存反馈。详细行为说明已在README保留，不改变设置/认证逻辑。
Windows x64/.NET SDK10.0.403，执行dotnet build .\cpu_net.csproj -c Release -o .\artifacts\enhanced-mode-ui --no-restore，退出0，20警告/0错误，3.13秒；日志artifacts/build-enhanced-mode-ui.log。
运行可行性检查通过，其他项目待人工验收。范围仅Release C#/XAML编译。独立输出避免覆盖此前启动的增强模式程序；未重启、未认证、未改用户配置，UI待人工验收，无提交推送。

### 外网完整诊断文件（2026-10-28）
主进程按用户追加授权实现，无子进程。保留既有增强模式、界面精简和手动冷却未提交成果。CampusConnectionService改为产生完整探测诊断，响应正文/Location原样保存；ProbeDiagnostics实现运行目录Diagnostics下唯一文件与20份数量保留；MainViewModel仅首次/异常内容变化写文件，5分钟重复汇总不新建，界面/运行日志只显示简短原因与成功文件的绝对路径。写入失败返回简短错误，不抛出中断检测，不显示失败路径。不读取/附带认证请求凭据、Cookie或本地配置，不上传。更新README、DESIGN及Git忽略。
环境Windows x64/PowerShell/.NET SDK10.0.403/net10.0-windows，目录C:\Projects\CPU_NET_Lite；沿用关闭遥测、证书生成和工具PATH修改的环境参数。实际命令：dotnet build .\cpu_net.csproj -c Release -o .\artifacts\enhanced-mode-ui --no-restore。退出0，20警告/0错误，2.77秒，日志artifacts/build-diagnostic-files.log。未发现编译错误，无运行错误修复事项。
运行可行性检查通过，其他项目待人工验收。范围仅Release C#/XAML编译。未启动、未探测或认证、未生成真实诊断文件、未执行功能测试、未替换V1.0、未提交推送。文件内容、20份保留、重复合并、失败处理、界面显示及增强模式实际路径均未验证，由用户人工验收；其他计划继续冻结。

2026-10-28 用户确认已退出并明确要求重新启动。启动前未发现cpu_net.exe进程，artifacts/enhanced-mode-ui/config.yaml不存在；未复制或修改配置。正常启动最新artifacts/enhanced-mode-ui/cpu_net.exe，Start-Process返回PID=32060，即时HasExited=False。未操作设置或认证；仅记录启动命令返回，初始化、完整诊断文件及增强模式行为由用户人工验收。

### HTTP 200 正文门户识别专项（2026-10-28）
执行者主进程，无子进程。修改前读取根AGENTS.md、Git状态、当前探测/认证协调及设计/运行记录；分支master，已有未提交成果均保留。首次读取常量时使用src/Constants/NetworkConstants.cs路径不存在，随后通过rg定位并读取src/Core/Constants/NetworkConstants.cs中的现有常量，不影响实现/编译。
新增PortalPageRecognizer静态识别实测宿舍认证模板；CampusConnectionService在既有正常正文判定之后增加HTTP200/text-html分支，命中返回CampusPortal/0并记录指定简短原因。沿用既有认证/排序/冷却及完整诊断文件流程，未更改MainViewModel或认证接口。README、DESIGN及AGENTS同步说明。
环境Windows x64/PowerShell/.NET SDK10.0.403/net10.0-windows，目录C:\Projects\CPU_NET_Lite。设置DOTNET_CLI_TELEMETRY_OPTOUT=1、DOTNET_GENERATE_ASPNET_CERTIFICATE=false、DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false。
实际命令：dotnet build .\cpu_net.csproj -c Release -o .\artifacts\http200-portal --no-restore
退出0，20警告/0错误，2.79秒；完整构建日志artifacts/build-http200-portal.log，独立产物artifacts/http200-portal。未发现编译错误，无运行错误修复事项。
运行可行性检查通过，其他项目待人工验收。范围仅Release C#/XAML编译；未执行页面识别功能测试、未启动程序、未探测/认证/访问页面中的地址、未改写用户配置或系统设置、未覆盖当前运行目录、未提交推送。页面识别与后续重连实际行为未验证，由用户人工验收。其他开发计划继续冻结。

### V1.1本地Git归档（2026-10-28）
用户明确授权“先将目前版本保存本地git作为V1.1”。主进程将工程版本设为1.1.0，Assembly/File为1.1.0.0，更新README/DESIGN/HANDOFF；归档此前所有专项源码和文档成果。计划在master提交并创建本地v1.1标签，不推送、不发布，不移动v1.0；实际提交标识以Git历史为准。真实配置、日志、Diagnostics、构建输出、归档与OpenWrt不纳入。
Windows x64/.NET SDK10.0.403/PowerShell；沿用关闭遥测、证书生成和工具PATH修改的环境参数，执行dotnet build .\cpu_net.csproj -c Release -o .\artifacts\V1.1 --no-restore。退出0，20警告/0错误，3.21秒，日志artifacts/build-v1.1.log。
运行可行性检查通过，其他项目待人工验收。范围仅版本号调整后的Release C#/XAML编译；未发现编译错误。独立输出artifacts/V1.1，未启动/认证、未覆盖旧运行目录或改写用户配置，未开展功能测试。用户本次归档授权不视为新增功能已经人工验收，其他开发继续冻结。
