namespace cpu_net.Constants
{
    /// <summary>
    /// 网络相关常量定义
    /// </summary>
    public static class NetworkConstants
    {

        // DrCOM 状态查询
        public const string DrComCallback = "dr1002";
        public const string LoginCallback = "dr1004";

        // 宽带模式 (Mode 0)
        public const string BroadbandDrComUrl = "http://172.17.253.3/drcom/chkstatus?callback=dr1002";
        public const string BroadbandLoginBaseUrl = "http://172.17.253.3:801/eportal/portal/login?";

        // CPU 模式 (Mode 1)
        public const string CpuDrComUrl = "http://192.168.199.21/drcom/chkstatus?callback=dr1002";
        public const string CpuLoginBaseUrl = "http://192.168.199.21:801/eportal/?c=Portal&a=login&callback=dr1004&login_method=1";

        // 外部页面
        public const string SelfServiceUrl = "http://192.168.199.70:8080/Self/Dashboard";
        public const string GitHubRepoUrl = "https://github.com/Halfaustus/cpu_net_lite";

    }
}

