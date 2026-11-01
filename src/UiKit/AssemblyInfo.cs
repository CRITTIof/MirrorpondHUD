using System.Runtime.CompilerServices;

// 宿主卡片与自检要访问 CliPanelView 的内部绑定状态；插件只使用公开控件 API。
[assembly: InternalsVisibleTo("MirrorpondHUD")]
