using System.Runtime.CompilerServices;

// 宿主与契约分属两个程序集：Log / Json 仍是内部实现，只对宿主开放。
[assembly: InternalsVisibleTo("MirrorpondHUD")]
