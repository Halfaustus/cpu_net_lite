# CPU_NET_Lite V1.2 当前设计

2026-10-27，用户已确认当前功能正常，授权整理唯一最终工程、设为V1.0、保存本地Git并推送origin。主进程执行，不使用子进程，不引入新功能；CLI、一键安装、凭据存储迁移及依赖替换继续冻结。

## 工程边界

主工程cpu_net.csproj，解决方案cpu_net.sln，仅包含WPF主应用，版本1.2.0，net10.0-windows。App/Program负责入口与单实例；MainWindow负责窗口/电源；配置页负责设置保存；MainViewModel负责调度和认证协调；CampusConnectionService/NetworkService负责外网探测、认证IP和校园接口；SettingsStore负责YAML缓存及原子替换；LoggingService负责日志。历史回归代码保留原断言，目标框架统一net10.0，不自动执行功能验收。

旧安装器/卸载器工程、发布配置、用户IDE文件、旧图片和阶段构建移入本地忽略的.local-archive/pre-v1.0，历史配置日志随原目录保留，不上传。OpenWrt相关内容保持原位置并忽略。默认项目扫描明确排除归档、artifacts、OpenWrt和测试目录。

## 行为契约

账号/密码/运营商显式一起保存，其余设置单字段即时保存；初始化和预览不保存或执行系统操作。配置仍在程序旁config.yaml，不迁移凭据，不改用户配置。缓存返回独立副本，保存及认证发送前仍读最新配置。

校园网对应192.168.199.21，宿舍网对应172.17.253.3，端点沿用上游4.2.0。按用户优先顺序逐个查询认证IP并登录，成功或已在线停止，无有效运营商跳过宿舍网。单次请求5秒超时，应用层直连，不改系统TUN/路由。

检测默认10秒（1～86400），本轮结束后等待；有效开关及凭据缺失时不调度。所有登录入口共用串行防重入；任一轮次开始后30秒内限制自动登录，手动登录不受冷却限制并刷新自动冷却时间。增强模式（EnhancedMode）默认关闭；开启后外网状态不明时不限次数继续重连，成功或已在线仅结束当前轮，不锁止后续轮次。关闭时未知状态仅继续探测；外网正常时不触发重连。旧UnknownReconnectAttempts仅兼容保留，不参与决策，也不自动映射为增强模式。设置变化及外网恢复不重置自动冷却。已知门户及手动/启动登录沿用原规则。

日志显示500行，250ms合并刷新，隐藏暂停；同类探测异常5分钟汇总。runtime日志5文件轮转，默认每个10MB、可设1～100MB；不删除旧月度、错误和备用日志。尾部读取单文件最多1MB。清空按钮只清显示。电源两个独立开关保持既有默认值。

## 交付与检查

Design Ready / Dev Ready：已有Windows/.NET10及固定依赖可用；无产品决策待确认。允许修改版本、工程组织、说明和忽略规则，归档旧产物，不更改已确认功能。正式输出artifacts/V1.0，保持依赖框架部署；不发布安装器或新GitHub Release。提交到现有master并推送origin/master，不强推；使用v1.0标签标识本次版本。

AI检查仅Release编译/发布，不执行真实认证或独立功能/性能验收。版本显示、最终目录启动及所有功能由人工确认。执行证据见RUN-LOG.md；此前细节保留在历史运行记录和本地设计归档。

## 增强模式专项（2026-10-28）
Design Ready / Dev Ready：由主进程修改SettingModel、MainViewModel及配置页，移除未知状态的计数、上限提示和完成锁止。新开关单字段即时保存并重启检测调度，不带入账号/密码/运营商草稿；初始化和预览不保存、不认证。每轮开始及发送前读取增强模式，关闭后不再开始未知状态认证；已经发出的同步请求不强行撤回。保留30秒自动冷却、手动登录刷新冷却、串行锁、凭据与开关校验、IP确认及目标优先顺序。
用户提供日志背景解释停止尝试的机制，不推断最初断网原因，不开展网络故障诊断。仅Release C#/XAML编译，临时产物artifacts/enhanced-mode，不替换artifacts/V1.0。人工确认开关持久化/草稿隔离、连续未知状态在成功或已在线后继续尝试、关闭及正常外网停止触发、冷却/串行与认证保护。其他冻结计划不恢复，不提交推送。

### 外网探测异常诊断追加（2026-10-28，历史方案，以下独立诊断文件方案覆盖）
仅扩展探测诊断，不改变“HTTP成功且Trim后正文等于TestCode”的外网正常标准、已知门户主机/状态码规则及增强模式调度。HttpClient保持禁止自动重定向；读取响应头后在同一个5秒期限内读取正文。非成功响应正文读取失败仍按原状态码/Location识别门户，并注明读取异常。
诊断记录检测开始时间（含日期与偏移）、去查询/片段/用户信息且动态路径遮蔽的地址、状态码、Content-Type媒体类型（不记录参数，未知类型遮蔽）、耗时、完整解码正文字符数、Location及摘要。未收到响应明确标注；仅收到响应头时正文长度未知、不生成正文摘要。异常记录类型链、HttpRequestError/SocketError/NativeError/HResult和固定原因描述，不输出可能含URL或身份数据的原始异常Message。
正文先遮蔽已保存账号密码，再使用保守词表仅保留常见错误词/HTML结构词及符号，其他词全部遮蔽；不会保留原始门户自由文本、表单值或脚本值。摘要内容最多1024字符，另附截断标记。未知词可能包含有用诊断文本，故也会遮蔽；不记录Cookie、认证头或其他任意响应头。
重复判定使用脱敏元数据、正文长度及完整脱敏正文的摘要哈希，排除时间/耗时；首次及可见异常内容变化立即记录，相同异常持续时每5分钟汇总并附最新详情。被遮蔽且长度相同的身份值变化不单独触发日志。去重标识不持久化原始正文/身份哈希。恢复正常记录完整恢复时间。日志继续走Info与原LoggingService轮转，不新增独立日志文件通道。
人工验收：不同HTTP/Content-Type/Location、长正文及脱敏、异常类型/无响应、首次/变化/5分钟汇总、恢复时间，以及增强模式行为。AI仅编译，不发起实际探测/认证或执行功能断言。

设置页说明精简（2026-10-28）：按用户要求将详细解释集中到README，配置页只保留设置名称、单位/范围、必要错误提示和简短操作反馈，去掉逐项说明及详细ToolTip。本次UI编译输出artifacts/enhanced-mode-ui，未替换此前启动目录。

### 独立完整诊断文件（2026-10-28，当前方案）
用户授权完整保存外网探测响应正文及原始Location，不脱敏、不截断；不扩大到认证请求、Cookie或本地配置。CampusConnectionService提供完整诊断与独立简短原因；MainViewModel按排除检测时间/耗时的SHA256键去重，仅首次/变化时调用ProbeDiagnostics.Save，重复5分钟汇总引用上次简短消息，不重新建文件。键涵盖完整正文、Content-Type、Location、状态、原因等，只在内存保留，不将正文写入运行日志/界面。恢复正常保留完整时间提示。
Diagnostics位于AppDomain.CurrentDomain.BaseDirectory下，UTC毫秒时间加GUID命名、CreateNew写入UTF-8。最多20份符合本程序固定命名格式的文件，新建前清除最旧文件腾出位置；不限制单份容量，不截断正文，不清理其他命名文件。清理失败则不新建；写入失败尽力移除未完成文件，记录异常类型、原因/错误码，不返回失败路径；相同失败通过既有5分钟汇总反馈，不中断探测/重连。下一次异常变化或恢复后可重新尝试写入。
检测地址沿用去查询/用户信息/动态路径处理；响应头仅取Content-Type和Location，未收到响应/完整正文明确注明，不虚构内容。异常原因保留结构化类型链及网络错误码，不直接转储可能带请求凭据的原始异常Message。未增加上传功能，Diagnostics加入Git忽略。
AI只编译。完整正文、去重变化、20份保留/删除、写入失败与界面路径均待人工验收；联网判定、增强模式、自动30秒冷却及认证保护保持既有逻辑。其余计划冻结，无提交推送。

### HTTP 200 已知宿舍网认证页面（2026-10-28）
Design Ready / Dev Ready：主进程限定修改探测判定及新增PortalPageRecognizer，复用现有CampusPortal自动登录分支。输入为用户提供的HTTP 200、text/html、无Location、location.href/认证链接指向宿舍门户的实测页面；不访问该URL，不执行脚本。其他未提交修改保留，其他开发继续冻结。
正常判定仍先执行HTTP成功且Trim正文等于TestCode。未满足正常判定时，仅HTTP 200且Content-Type媒体类型text/html尝试新识别。移除HTML注释后，静态匹配脚本块内单独location.href（可带window.）的带引号URL赋值；或同时包含固定Authentication is required./to open the authentication page.提示及仅href属性、here文本的认证链接。链接HTML实体解码，不解释JS表达式/转义，不跟随地址。正则单次匹配有100ms超时，超时不确认门户。
完整URL以Uri解析，精确核对BroadbandDrComUrl现有常量的scheme/host/port，路径必须/a79.htm，不接受userinfo/fragment。查询需具有唯一wlanacip（有效IPv4）和唯一非空wlanacname。wlanacip仅验证门户模板特征，不向认证流程传递任何页面IP。当前仅扩大实测宿舍门户模板，未根据猜测扩大校园门户路径；其他内容维持Unknown。地址子串、纯提示文字或未知主机均不构成此规则的确认依据。
命中返回CampusPortal/0，并通过原完整诊断文件链记录“检测到已知宿舍网认证页面（HTTP 200 正文跳转）”。界面只显示简短原因和成功文件路径，Diagnostics保留20份及去重/写入失败处理不变。该状态不依赖EnhancedMode，但仍需网络登录、定时检测和有效凭据。
ResolveTargets仍按PreferredNetwork排序、逐个访问状态接口确认设备认证IP；门户标记不覆盖优先顺序。既有串行锁、自动30秒冷却、取消/设置变更检查和手动规则不变。成功或已在线只结束当前登录轮；只有正常外网探测才记录恢复，后续持续门户按冷却再尝试。
人工验收：实测模板及链接分支、未知/伪装URL及纯提示文字、增强模式关闭时的门户重连、用户排序/IP来源、持续门户与30秒冷却、诊断文件和恢复判定。AI仅Release编译，不执行上述功能断言或真实探测/认证；独立产物artifacts/http200-portal。

### V1.1本地归档（2026-10-28）
用户授权将当前版本保存到本地Git。版本1.1.0，沿用master，创建本地v1.1标签；本次不推送或发布，覆盖此前交付章节中的V1.0推送安排。包含手动冷却修正、增强模式、完整诊断文件、设置说明精简及HTTP200门户识别。独立编译artifacts/V1.1，不覆盖旧运行目录。此次归档不表示用户已确认新增功能，仍待人工验收。其他计划冻结。

2026-10-28追加：用户要求检测到已知宿舍认证页面不生成诊断文件。HTTP200正文识别及HTTP重定向宿舍门户均改走简短ReportProbe提示，沿用5分钟重复汇总，不调用诊断保存；其余异常及校园门户原诊断逻辑不变。独立编译artifacts/portal-log-only，未覆盖当前V1.1。

### 配置读取恢复与校园请求总时限（2026-10-29）
用户授权先处理只读分析的前两项，其余两项仅标记可能。主进程实现，范围SettingsStore、MainViewModel、NetworkService和CampusConnectionService；保留已知宿舍门户不生成诊断文件的未提交成果。
设计/实现：新增仅供联网流程使用的ReadForNetwork，配置格式错误不回退成关闭功能的默认配置，向调用层报告异常。调度循环内捕获IO/权限/YAML读取异常，本轮不认证，以最后一次有效检测间隔重试（尚未取得有效配置时10秒）；RestartNetworkTimer不再先读取配置，避免重启时读失败留下已取消的循环。有效关闭开关/缺少凭据/不存在配置时仅等待并重读配置，不进行外网探测或认证。启动/界面原有Read行为未扩展修改，不改写配置或沿用旧凭据认证。
校园状态查询/认证的HttpGetRequest保留工作线程同步调用接口，内部使用共享HttpClient及ConfigureAwait(false)异步IO。每次请求的关联CancellationTokenSource设置5秒总时限，覆盖响应头、流及ReadToEndAsync正文读取；不隐含重试，不跟随重定向、不启用代理/Cookie，保留UTF8读取和gzip/deflate解压。用户关闭/修改设置触发RestartNetworkTimer或退出时取消当前登录请求；外部取消在状态查询和认证解析层继续传播，最终释放串行锁。超时是当前目标的请求失败，仍按原顺序决定是否尝试后续目标；不改变每轮开始至少30秒的自动冷却，手动规则不变。
取消只能停止客户端正在等待的请求，不能撤销服务器已处理的认证。总时限不是实时系统的毫秒级保证。配置恢复及慢响应/取消行为需人工确认，AI不运行真实接口或功能断言。

#### 仅标记的潜在局限（不在本次修复范围）
1. 首选网络每轮均返回已在线而外网仍异常时，按既有成功/已在线即结束当前轮契约，另一网络可能始终不被尝试。是否放行另一目标涉及产品规则，暂不修改，也不认定已实际发生。
2. HTTP200门户静态匹配限定实测模板，其他脚本写法、链接属性或提示文字变化可能漏识别；增强模式关闭时不会按Unknown认证。继续按新实测页面扩展，不扩大为任意网页触发，暂不修改识别器。
建议先人工观察配置临时读失败恢复、慢响应超时和取消，再根据日志决定是否需要后两项变更。

### V1.1正式提交与推送（2026-10-29）
用户授权将当前修正正式作为1.1提交推送，并在完成后清理旧版本。本条覆盖此前仅本地归档、不推送和不替换V1.1的阶段限制。保持版本1.1.0，沿用master；已核对origin/master仍为51653e1、远端不存在v1.1，允许更新本地v1.1到最终提交后正常推送，不强推。发布先输出artifacts/v1.1-release，推送后更新artifacts/V1.1程序文件，保留原配置/日志/诊断；旧程序及阶段输出归档。无进程运行，不自动启动。前两项修正仅经编译，后两项潜在局限不变；其他计划冻结。

### 状态查询具体异常说明修复（2026-11-04）
用户日志显示两个状态接口约5秒后仅报InvalidOperationException。只读定位确认：NetworkService原先将超时/HTTP/读取失败转换为带安全说明的InvalidOperationException，但ReadTarget经DescribeFailure处理后仅留下类型名。本次用户授权修复这一信息丢失，不推断实际网络故障原因。
NetworkService增加私有CampusRequestFailureException，仅该类型可由DescribeFailure显示本服务构造的说明；记录操作、等待响应头/读取正文阶段、实际已取得的HTTP状态或未收到响应头、原始异常类型/结构化网络原因及耗时。HTTP非成功单独标明；正文IO异常在总时限已取消时按超时记录。外部取消继续传播，5秒限制及认证IP保护不变。不输出任意异常Message、认证查询参数或原始响应。
认证失败提示统一使用DescribeFailure，去掉任意InvalidOperationException直接显示Message的分支；状态查询与认证均保留安全说明。仅编译到artifacts/request-error-detail，未更新正式运行目录；实际超时/HTTP错误/读取失败提示待人工验收。

### 自动校园网络记忆与事件恢复（历史方案，已被下文物理出口方案替代）

用户要求直接修改连接逻辑并明确采用自动记忆校园网络、校外停止重查、网络变化时恢复的方案。当前工作包由主进程串行完成，范围为 CampusNetworkService、MainViewModel、运行状态文件忽略规则及对应说明，复用现有 CampusConnectionService。输入为现有校园状态接口及本机网络信息，输出为自动学习、未知网络暂停和原认证流程。Design Ready / Dev Ready，无新增依赖，检查限定 Windows x64 Release C#/XAML 编译，实际识别、切网及 TUN 共存由人工验收。已撤销的登记界面和开关继续保持撤销状态。

定时流程在外网探测前按原优先顺序查询校园状态接口，取得首个有效认证 IPv4 后继续原外网状态判定；外网 Online 仍不触发自动登录，CampusPortal 或启用增强模式后的 Unknown 沿原规则登录。有效状态响应是校园网络识别依据，本机网络配置 ID 不再作为登记名单。首个已识别目标在同轮传入认证流程，避免重复查询，也不重查识别期间已不可用的高优先目标；首个目标认证成功/已在线即结束，只有其认证失败才继续原顺序查询下个目标。无运营商仍跳过宿舍目标。

首次遇到未知网络时按原顺序识别一次，没有可用目标则标记该连接已尝试，停止后续自动校园查询、外网探测和认证，不再运行 30/60/120/240/300 秒退避。暂停后定时循环只维持配置读取，识别服务在没有网络事件、目标条件变化或手动强制时直接返回，不再枚举网络。网络事件会先重读物理连接快照，快照未变且已尝试的未知网络继续暂停；快照确实变化时允许新连接识别一次。无可用物理连接同样等待事件。本机信息暂不可读时仅按原间隔重试本地读取，不发送校园请求。手动登录始终可重新识别；重启程序后未知连接会获得一次新的识别机会。接口失败只说明尚未确认为校园网，不推断确定的校外地理位置。

NLM 网络配置 ID、适配器 ID、本地 IPv4 和网关构成本机连接快照，用来识别连接变化与丢弃过期结果。复用已知 TUN 名称判定，过滤常见虚拟适配器；名称过滤不保证识别所有第三方虚拟适配器。MainViewModel 正常模式订阅 NetworkChange.NetworkAddressChanged/NetworkAvailabilityChanged，事件只增加版本号并唤醒原调度循环，不直接发送请求；退出时解除订阅。多次事件由容量为 1 的信号合并，同一物理快照的虚拟网络事件不会重启未知网络的 HTTP 查询。识别期间连接变化则丢弃结果。每个后续目标查询前和认证提交前再次核对连接，发送前继续读取最新凭据和设置，保留原 5 秒总时限及取消。已识别目标超过 15 秒或目标顺序/运营商条件变化时重新识别。未识别到校园网络的流程不更新自动登录冷却；实际进入认证轮次仍共享串行限制和 30 秒自动冷却。状态提示只在变化时记录，识别失败的该次详细请求说明随状态变化输出。

校园状态接口提供有效认证 IP，且识别前后连接快照一致时自动学习。优先将认证 IP 与物理适配器 IPv4 对应，能够唯一对应网络配置 ID 时记忆该 ID；只有一个候选网络配置时允许接入网 NAT。多网络且来源无法唯一确定时只确认当前会话，不持久化任何候选 ID。正向记忆保存在程序旁 campus-networks.json（GUID 数组），不含账号、密码、IP、网关或网络名称，已加入 .gitignore。惰性加载，无记录时首次识别；写入使用同目录临时文件后原子替换。文件读取失败保留原文件，本进程只在内存学习；保存失败同样保留内存识别，失败不会中断当前认证。该应用正常模式已有单实例互斥，本服务内部由信号量串行化读取/学习。未知网络的失败状态仅保留在内存。

任何活动物理连接的配置 ID 已被记住，或完整快照已在本会话确认时，继续原周期识别，即使接口临时失败也不将该连接判为校外。Windows 重建配置、移动程序目录未带上记录文件时重新学习。只修改 TUN 路由且物理快照不变时，已暂停的未知网络需要手动登录重查。网络变化恢复依赖系统通知，未发出地址/可用性通知的纯配置标识变化可通过手动登录重新识别。

登记界面、开关及登记字典快照逻辑已撤销。SettingModel 仅保留两个旧测试版字段的兼容入口，读入后丢弃，序列化时省略，避免已经保存过登记的测试配置变成不可读配置。既有真实配置保持原样，未执行迁移。预览模式继续不执行自动任务。

官方接口依据为 [INetwork::GetNetworkId](https://learn.microsoft.com/en-us/windows/win32/api/netlistmgr/nf-netlistmgr-inetwork-getnetworkid)、[INetworkConnection::GetAdapterId](https://learn.microsoft.com/en-us/windows/win32/api/netlistmgr/nf-netlistmgr-inetworkconnection-getadapterid)、[NetworkChange](https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.networkchange?view=net-10.0) 及本机 Windows SDK 10.0.26100.0 的 netlistmgr.h。本次仅编译到 artifacts/campus-network-memory，COM 枚举、旧测试配置兼容、记忆文件读写、Wi-Fi/网线识别、系统网络事件、认证和 TUN 下暂停恢复均待人工验证。

### 请求自动绑定物理网卡（2026-11-05，当前方案）

用户明确目标为程序自动绕开 TUN、仅使用物理网卡。当前工作包由主进程串行实现，范围为 PhysicalNetworkService、PhysicalDnsMessage、NetworkService、CampusConnectionService、ProbeDiagnostics，以及移除前一方案的 CampusNetworkService 和 MainViewModel 识别/记忆门控。输入为 Windows 活动硬件接口与只读路由信息，输出为绑定物理接口的 DNS/TCP 流。无新增设置或依赖。Design Ready / Dev Ready，运行检查限 Windows x64 Release 编译及 Windows SDK 原生结构布局编译，具体 TUN 下行为由人工验收。

PhysicalNetworkService 创建共用 SocketsHttpHandler 配置，用 ConnectCallback 接管探测、状态查询及认证的建连。读取 GetIfEntry2 的 HardwareInterface/FilterInterface 标志，只接受活动硬件且具有可用地址的接口，不依赖适配器名称判断物理性。对每个目标分别在候选物理接口上调用 GetBestRoute2，并通过 GetIpInterfaceEntry 读取接口跃点；按最长前缀、路由与接口合计跃点、接口索引排序，选择一个出口。IPv4 使用网络字节序的 IP_UNICAST_IF，IPv6 使用主机字节序的 IPV6_UNICAST_IF，同时 Bind 到原生 API 选出的该卡源地址。找不到出口或建连失败时返回错误，不回退默认/TUN 出口，不重试其他物理接口。每次 HTTP 请求重建连接并重新选路，保持原 HTTP/TLS 主机名和证书验证。

域名探测不使用系统 DNS 缓存或默认出口解析，以防沿用 TUN 的 Fake-IP。选择物理适配器配置且在该接口可达的 DNS 服务器，按物理路由合计跃点选首个出口。UDP 查询与 TC 截断后的 TCP 查询均复用接口/源地址绑定，支持标准压缩域名、IDN、A/AAAA 及最多 8 次 CNAME 后续查询；优先 A，没有可路由 A 时再查询 AAAA。该解析器用于 HTTP 主机地址解析，不实现系统 hosts、搜索后缀、NRPT/DoH 策略或 DNS 缓存。全部操作贯通原请求取消和 5 秒总时限；没有 HTTP/认证隐含重试。直接 IP 的两个校园端点无需 DNS。

校园请求日志增加“物理出口”及网卡名、接口索引、本地和目标 IP；探测诊断增加物理出口元数据，界面保留原简短提示和诊断路径策略。只允许本服务构造的 PhysicalNetworkException 原因出现在普通日志，其他异常继续使用结构化安全描述。恢复原外网探测和串行认证入口、优先顺序、自动 30 秒冷却、增强模式、发送前最新配置及状态接口认证 IP 校验。删除网络记忆服务及事件门控，不读取或删除旧 campus-networks.json；.gitignore 仍忽略旧运行数据。旧测试版登记配置字段继续兼容，界面没有登记开关。

该方案指定应用 socket 的物理出站接口，不修改系统代理、路由、网卡或防火墙。严格路由/WFP/其他系统策略仍可能阻断连接，能否在用户当前 Clash Verge Rev TUN 配置下实际完成请求尚未验证。多张物理网卡都只有默认路由时，按跃点自动选择，无法凭接口类型知道哪张接入校园。外部浏览器打开的自助服务页由浏览器自行处理网络。

原生结构使用本机 Windows SDK 10.0.26100.0 netioapi.h 的 x64 布局，编译期 static_assert 核对 MIB_IF_ROW2、MIB_IPINTERFACE_ROW、MIB_IPFORWARD_ROW2、SOCKADDR_INET 的大小及使用字段偏移。官方依据为 [Windows 出站接口选项](https://learn.microsoft.com/en-us/windows/win32/winsock/ipproto-ip-socket-options)、[GetBestRoute2](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-getbestroute2)、[MIB_IF_ROW2](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_if_row2)、[ConnectCallback](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.connectcallback) 和 [DNS 报文及传输规范](https://www.rfc-editor.org/rfc/rfc1035)。本次输出 artifacts/physical-nic-direct，实际网卡枚举、选路、socket 选项、DNS、TLS、认证、切网及 Clash TUN 场景待人工验证。
### 校园门户识别与默认探测超时处理（2026-11-18）

本工作包由主进程完成，修改范围是门户识别、默认外网探测和诊断。依据为物理网卡测试版保留的 20 份诊断，其中 18 份为默认探测约 5 秒未收到响应头，2 份为 HTTP 200 的校园网认证页。日志显示已解析目标并选择 WLAN，旧记录无法区分 TCP 建连和建连后等待 HTTP，也没有证据可确定超时由 TUN 造成。

HTTP 200 的 HTML 同时包含已知认证提示、静态跳转及链接，且两者均精确指向无查询参数的校园网入口 `http://192.168.199.21/a79.htm` 时，按校园门户处理。沿用状态接口确认认证 IP 和用户设置的认证顺序。原宿舍网模板保持独立。

默认探测在 2.5 秒内未收到响应头并超时，或此前遇到符合回退条件的 HTTP 传输错误时，可尝试微软 NCSI 的 `http://www.msftncsi.com/ncsi.txt`，校验预期正文 `Microsoft NCSI`。两次共享 5 秒总期限及同一取消来源，均绑定物理网卡。主地址已收到响应头时继续按原规则处理正文和门户，不用备用结果覆盖。自定义探测仍用单地址和 5 秒期限。两个地址都未确认时完整诊断记录各自结果，取消包装或 CDN 地址轮换不单独触发新的超时诊断。
