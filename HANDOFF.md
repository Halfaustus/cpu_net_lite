# V1.1 正式交接

2026-10-29用户授权将当前修正作为正式V1.1提交推送，之后清理旧版本。主工程cpu_net.csproj，版本1.1.0；沿用master，origin为Halfaustus/cpu_net_lite。v1.1此前仅为本地归档标签，本次更新到最终提交并首次推送该标签，不改写远端历史。

包括增强模式、HTTP200宿舍门户识别、完整本地诊断及20份保留、已知宿舍门户只显示简短提示、配置读取失败重试、校园请求5秒全程超时与取消。首选已在线可能阻止另一目标、门户模板变化可能漏识别仅记录可能，不改既有规则。

正式程序artifacts/V1.1，保留原config.yaml、Log及Diagnostics。旧程序文件及portal-log-only、reconnect-resilience等阶段输出移入.local-archive；SDK/NuGet缓存保留。运行数据、构建输出及OpenWrt不提交，不创建GitHub Release/安装器，不自动启动。

AI仅Release编译/发布检查，行为由人工验收。详情见README、docs/DESIGN.md及docs/RUN-LOG.md；Git提交/推送实际结果以Git历史及交付记录为准。其他开发继续冻结。