namespace MirrorpondHUD.Cards
{
    /// <summary>内置卡片的默认内容模板（只属于宿主实现，不进插件契约程序集）。</summary>
    internal static class CardDefaults
    {
        /// <summary>系统信息卡：CPU / 内存 / GPU / 网络 / 磁盘，前置 ReiconFont 图标。</summary>
        public const string MetricsContent =
            "{cpu_icon} {cpu:0.0}%   {mem_icon} {mem_pct:0}%   {gpu_icon} {gpu:0.0}%\n" +
            "{net_down_icon} {net_down}   {net_up_icon} {net_up}   {disk_icon} {disk:0.0}%";

        /// <summary>时钟卡：时间、日期、星期（中文）、运行时长。</summary>
        public const string ClockContent =
            "{time}  {date}\n{weekday_zh}  {uptime}";

        /// <summary>按键卡：最近按键（键帽字形）。</summary>
        public const string KeysContent = "KEY {keys}";

        /// <summary>文本卡：计算机名与用户名。</summary>
        public const string TextContent = "{host} · {user}";
    }
}
