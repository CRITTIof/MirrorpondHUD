using System;
using System.Collections.Generic;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    public sealed class PresetInfo
    {
        public string Name { get; set; }
        public string Content { get; set; }

        public override string ToString() { return Name; }
    }

    public sealed class TokenInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }

        public string Display { get { return "{" + Name + "}"; } }
    }

    public static class Catalog
    {
        public static readonly TokenInfo[] Tokens =
        {
            new TokenInfo { Name = "cpu", Description = "CPU 总占用率（%）" },
            new TokenInfo { Name = "cpu_bar", Description = "CPU 占用条" },
            new TokenInfo { Name = "mem_pct", Description = "内存占用率（%）" },
            new TokenInfo { Name = "mem_used", Description = "已用内存（GB）" },
            new TokenInfo { Name = "mem_total", Description = "内存总量（GB）" },
            new TokenInfo { Name = "mem_bar", Description = "内存占用条" },
            new TokenInfo { Name = "gpu", Description = "GPU 占用率（%）" },
            new TokenInfo { Name = "gpu_bar", Description = "GPU 占用条" },
            new TokenInfo { Name = "gpu_name", Description = "显卡名称" },
            new TokenInfo { Name = "gpu_temp", Description = "GPU 温度（℃）" },
            new TokenInfo { Name = "gpu_power", Description = "GPU 功耗（W）" },
            new TokenInfo { Name = "vram_used", Description = "显存已用（MB）" },
            new TokenInfo { Name = "vram_total", Description = "显存总量（MB）" },
            new TokenInfo { Name = "disk", Description = "磁盘活动率（%）" },
            new TokenInfo { Name = "disk_bar", Description = "磁盘活动条" },
            new TokenInfo { Name = "disk_read", Description = "磁盘读取速度" },
            new TokenInfo { Name = "disk_write", Description = "磁盘写入速度" },
            new TokenInfo { Name = "net_down", Description = "网络下载速度" },
            new TokenInfo { Name = "net_up", Description = "网络上传速度" },
            new TokenInfo { Name = "net_adapter", Description = "当前网卡名" },
            new TokenInfo { Name = "free_C", Description = "C 盘空闲空间（GB）" },
            new TokenInfo { Name = "total_C", Description = "C 盘总容量（GB）" },
            new TokenInfo { Name = "used_pct_C", Description = "C 盘已用百分比" },
            new TokenInfo { Name = "keys", Description = "最近按键（连续重复会折叠）" },
            new TokenInfo { Name = "key_count", Description = "累计按键次数" },
            new TokenInfo { Name = "time", Description = "当前时间" },
            new TokenInfo { Name = "date", Description = "当前日期" },
            new TokenInfo { Name = "uptime", Description = "系统运行时长" },
            new TokenInfo { Name = "host", Description = "计算机名" },
            new TokenInfo { Name = "user", Description = "用户名" }
        };

        public static readonly PresetInfo[] Presets =
        {
            new PresetInfo
            {
                Name = "标准面板",
                Content = Settings.DefaultMetricsContent
            },
            new PresetInfo
            {
                Name = "极简一行",
                Content = "CPU {cpu:0.0}%   MEM {mem_pct:0}%   ↓{net_down}   ↑{net_up}"
            },
            new PresetInfo
            {
                Name = "游戏性能",
                Content = "{gpu_name}  {gpu:0.0}%\n" +
                          "温度 {gpu_temp:0}℃   功耗 {gpu_power:0}W\n" +
                          "显存 {vram_used:0}/{vram_total:0} MB\n" +
                          "CPU {cpu:0.0}%   MEM {mem_pct:0}%"
            },
            new PresetInfo
            {
                Name = "键盘直播",
                Content = "按键记录\n{keys}\n累计 {key_count} 次"
            },
            new PresetInfo
            {
                Name = "磁盘与网络",
                Content = "磁盘 {disk:0.0}%  读 {disk_read}  写 {disk_write}\n" +
                          "C: {free_C:0.0}GB 可用 / {total_C:0.0}GB\n" +
                          "网络 ↓{net_down}  ↑{net_up}"
            },
            new PresetInfo
            {
                Name = "迷你状态条",
                Content = "{cpu_bar} {cpu:0}%   {mem_bar} {mem_pct:0}%   {gpu_bar} {gpu:0}%"
            }
        };
    }
}
