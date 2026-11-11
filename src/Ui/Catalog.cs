using System;
using System.Collections.Generic;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

public sealed class PresetInfo
{
    public string Name { get; set; }
    public string Content { get; set; }

    public override string ToString() => Name;
}

public sealed class TokenInfo
{
    public string Name { get; set; }
    public string Description { get; set; }

    public string Display => "{" + Name + "}";
}

public static class Catalog
{
    public static readonly TokenInfo[] Tokens =
    {
        new() { Name = "cpu", Description = "CPU 总占用率（%）" },
        new() { Name = "cpu_bar", Description = "CPU 占用条" },
        new() { Name = "mem_pct", Description = "内存占用率（%）" },
        new() { Name = "mem_used", Description = "已用内存（GB）" },
        new() { Name = "mem_total", Description = "内存总量（GB）" },
        new() { Name = "mem_bar", Description = "内存占用条" },
        new() { Name = "gpu", Description = "GPU 占用率（%）" },
        new() { Name = "gpu_bar", Description = "GPU 占用条" },
        new() { Name = "gpu_name", Description = "显卡名称" },
        new() { Name = "gpu_temp", Description = "GPU 温度（℃）" },
        new() { Name = "gpu_power", Description = "GPU 功耗（W）" },
        new() { Name = "vram_used", Description = "显存已用（MB）" },
        new() { Name = "vram_total", Description = "显存总量（MB）" },
        new() { Name = "disk", Description = "磁盘活动率（%）" },
        new() { Name = "disk_bar", Description = "磁盘活动条" },
        new() { Name = "disk_read", Description = "磁盘读取速度" },
        new() { Name = "disk_write", Description = "磁盘写入速度" },
        new() { Name = "net_down", Description = "网络下载速度" },
        new() { Name = "net_up", Description = "网络上传速度" },
        new() { Name = "net_adapter", Description = "当前网卡名" },
        new() { Name = "free_C", Description = "C 盘空闲空间（GB）" },
        new() { Name = "total_C", Description = "C 盘总容量（GB）" },
        new() { Name = "used_pct_C", Description = "C 盘已用百分比" },
        new() { Name = "keys", Description = "最近按键（连续重复会折叠）" },
        new() { Name = "key_count", Description = "累计按键次数" },
        new() { Name = "time", Description = "当前时间" },
        new() { Name = "date", Description = "当前日期" },
        new() { Name = "weekday_zh", Description = "星期（中文，星期一）" },
        new() { Name = "weekday_zh_short", Description = "星期（中文简写，周一）" },
        new() { Name = "weekday_en", Description = "星期（英文，Monday）" },
        new() { Name = "weekday_en_short", Description = "星期（英文简写，Mon）" },
        new() { Name = "weekday", Description = "星期（跟随系统区域，旧配置沿用）" },
        new() { Name = "uptime", Description = "系统运行时长" },
        new() { Name = "host", Description = "计算机名" },
        new() { Name = "user", Description = "用户名" }
    };

    public static readonly PresetInfo[] Presets =
    {
        new() {
            Name = "标准面板",
            // 这是「模板目录」里的一个可选项，与默认卡片解耦：默认卡由
            // ICardDefaultProvider 提供，这里不再引用宿主模板常量。
            Content = "{cpu_icon} {cpu:0.0}%   {mem_icon} {mem_pct:0}%   {gpu_icon} {gpu:0.0}%\n" +
                      "{net_down_icon} {net_down}   {net_up_icon} {net_up}   {disk_icon} {disk:0.0}%"
        },
        new() {
            Name = "极简一行",
            Content = "CPU {cpu:0.0}%   MEM {mem_pct:0}%   ↓{net_down}   ↑{net_up}"
        },
        new() {
            Name = "游戏性能",
            Content = "{gpu_name}  {gpu:0.0}%\n" +
                      "温度 {gpu_temp:0}℃   功耗 {gpu_power:0}W\n" +
                      "显存 {vram_used:0}/{vram_total:0} MB\n" +
                      "CPU {cpu:0.0}%   MEM {mem_pct:0}%"
        },
        new() {
            Name = "键盘直播",
            Content = "按键记录\n{keys}\n累计 {key_count} 次"
        },
        new() {
            Name = "磁盘与网络",
            Content = "磁盘 {disk:0.0}%  读 {disk_read}  写 {disk_write}\n" +
                      "C: {free_C:0.0}GB 可用 / {total_C:0.0}GB\n" +
                      "网络 ↓{net_down}  ↑{net_up}"
        },
        new() {
            Name = "迷你状态条",
            Content = "{cpu_bar} {cpu:0}%   {mem_bar} {mem_pct:0}%   {gpu_bar} {gpu:0}%"
        }
    };
}
