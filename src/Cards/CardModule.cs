using System;
using System.Collections.Generic;
using System.Globalization;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 一种卡片 = 一个模块。新增功能只要加一个实现这个接口的 public 类，
    /// 构造时由 <see cref="CardRegistry"/> 反射发现，不用改宿主、工程文件或设置页代码。
    /// </summary>
    public interface ICardModule
    {
        /// <summary>稳定的种类标识，写进配置文件的 Kind 字段。</summary>
        string Kind { get; }

        /// <summary>设置页里显示的名字（同种卡片可以有多个，名字可重名）。</summary>
        string DisplayName { get; }

        /// <summary>ReiconFont 图标字符（U+E100 / U+E200 / U+E300 区）。</summary>
        string Icon { get; }

        /// <summary>内容模板为空时使用的默认内容。</summary>
        string DefaultContent { get; }

        /// <summary>是否允许编辑内容模板（命令行卡片是内置界面，不需要）。</summary>
        bool SupportsContent { get; }

        /// <summary>是否是独占键盘输入的卡片（命令行）。</summary>
        bool IsInteractive { get; }

        /// <summary>新卡片的默认「常驻」状态。</summary>
        bool DefaultResident { get; }

        /// <summary>新卡片的默认呼出热键；空 = 不预置。</summary>
        string DefaultHotkey { get; }

        /// <summary>「添加卡片」菜单里的排序，小的在前。</summary>
        int Order { get; }

        /// <summary>把卡片内容渲染成一行文本（多行用 \n）。</summary>
        string Render(CardRenderContext context);
    }

    /// <summary>渲染一张卡片时需要的共享数据。变量表按需构建，同一帧内的多张卡片可以复用。</summary>
    public sealed class CardRenderContext
    {
        Dictionary<string, object> _variables;

        public CardRenderContext(Settings settings, MetricsSnapshot snapshot, KeyHistory keys, CardSettings config)
        {
            Settings = settings ?? new Settings();
            Snapshot = snapshot ?? new MetricsSnapshot();
            Keys = keys;
            Config = config;
        }

        public Settings Settings { get; private set; }
        public MetricsSnapshot Snapshot { get; private set; }
        public KeyHistory Keys { get; private set; }
        public CardSettings Config { get; private set; }

        public IDictionary<string, object> Variables
        {
            get
            {
                if (_variables == null)
                    _variables = Core.Variables.Build(Snapshot, Settings, Keys);
                return _variables;
            }
        }

        public string Render(string template)
        {
            if (string.IsNullOrEmpty(template)) return "";
            return TemplateEngine.Render(template, Variables);
        }

        /// <summary>内容模板为空时回退到卡片默认内容。</summary>
        public string RenderOrDefault(string defaultContent)
        {
            string content = Config == null ? null : Config.Content;
            return Render(string.IsNullOrWhiteSpace(content) ? defaultContent : content);
        }
    }

    /// <summary>卡片模块的公共基类：只提供默认实现，子类覆盖需要的部分。</summary>
    public abstract class CardModuleBase : ICardModule
    {
        public abstract string Kind { get; }
        public abstract string DisplayName { get; }
        public abstract string Icon { get; }

        public virtual string DefaultContent { get { return ""; } }
        public virtual bool SupportsContent { get { return true; } }
        public virtual bool IsInteractive { get { return false; } }
        public virtual bool DefaultResident { get { return true; } }
        public virtual string DefaultHotkey { get { return ""; } }
        public virtual int Order { get { return 50; } }

        public abstract string Render(CardRenderContext context);

        public override string ToString() { return DisplayName; }
    }
}
