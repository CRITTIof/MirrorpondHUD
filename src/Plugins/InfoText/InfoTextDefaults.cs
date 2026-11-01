namespace MirrorpondHUD.Cards
{
    /// <summary>信息与文本插件的默认内容（跟宿主建新卡时用的默认模板保持一致）。</summary>
    internal static class InfoTextDefaults
    {
        public const string MetricsContent =
            "{cpu_icon} {cpu:0.0}%   {mem_icon} {mem_pct:0}%   {gpu_icon} {gpu:0.0}%\n" +
            "{net_down_icon} {net_down}   {net_up_icon} {net_up}   {disk_icon} {disk:0.0}%";

        public const string ClockContent =
            "{time}  {date}\n{weekday_zh}  {uptime}";

        public const string KeysContent = "KEY {keys}";
        public const string TextContent = "{host} · {user}";
    }
}
