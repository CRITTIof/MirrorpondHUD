using System;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace RuntimeCardExample;

/// <summary>
/// 最小运行时卡片：不依赖任何宿主内部实现，只实现公开的 CardModuleBase。
/// 加载后应在「添加卡片」菜单出现；Kind = "example-clock"。
/// </summary>
public sealed class ExampleClockCard : CardModuleBase
{
    public override string Kind => "example-clock";
    public override string DisplayName => "示例插件时钟";
    public override string Icon => "\uE100";
    public override string DefaultContent => "插件时钟 {time}";
    public override bool SupportsContent => true;
    public override int Order => 10;

    public override int RefreshIntervalMs(CardSettings config) => 1000;

    public override string Render(CardRenderContext context) => $"插件时钟 · {DateTime.Now.ToString("HH:mm:ss")}";
}
