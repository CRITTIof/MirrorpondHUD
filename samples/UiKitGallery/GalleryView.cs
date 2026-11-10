using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui;
using MirrorpondHUD.Ui.Controls;

namespace UiKitGallery
{
    /// <summary>
    /// 画廊视图：一个固定宽高的可滚动面板，按中文分区实例化 UiKit 公共控件。
    ///
    /// 为什么固定宽高：内容是 <see cref="ScrollViewer"/>，没有确定高度就不会出现滚动条；
    /// 520 × 600 在常见小屏上也放得下，超出的部分由内部滚动查看。
    ///
    /// 颜色纪律：只从 <see cref="StyleSettings"/> 经 <see cref="ThemeResolver"/> 取令牌；
    /// 前景色用公开的 <see cref="ColorCode.TryParse"/> 解析，失败时从 Muted 令牌的 RGB 反推，
    /// 不写死任何颜色常量。字号同理，标题 / 说明只在 style.FontSize 上做加减。
    ///
    /// 交互纪律：按钮 / 输入 / 滑块 / 列表 / 表格 / 树 / 时间线的状态全部留在本对象里，
    /// 不读写配置文件、不碰宿主内部类型；宿主重建内容时这些演示状态自然归零。
    /// </summary>
    internal sealed class GalleryView : ScrollViewer
    {
        readonly Settings _settings;
        readonly StyleSettings _style;
        readonly ThemeTokens _tokens;
        readonly Color _foreground;
        readonly double _fontSize;
        readonly FontFamily _font;
        readonly double _corner;
        readonly CliSettings _cli;
        readonly StackPanel _root;

        CardStatus _status;
        CardInput _input;
        CardTag _tag;
        CardSparkline _sparkline;
        CardLineChart _lineChart;
        CardBarChart _barChart;
        int _chartSeed;
        int _clickCount;
        bool _tableSorted;

        /// <summary>由宿主 CreateContent 调用；settings 为空时按默认设置搭界面。</summary>
        public GalleryView(Settings settings)
        {
            _settings = settings == null ? new Settings() : settings;
            _style = _settings.Style == null ? new StyleSettings() : _settings.Style;
            _tokens = ThemeResolver.Resolve(_style);
            _foreground = ResolveForeground(_style, _tokens);
            _fontSize = Math.Max(12, _style.FontSize);
            _font = ResolveFont(_style.FontFamily);
            _corner = Math.Max(0, Math.Min(24, _settings.Overlay == null ? 10 : _settings.Overlay.CornerRadius));
            _cli = _settings.Cli == null ? new CliSettings() : _settings.Cli;

            Width = 520;
            Height = 600;
            Background = Brushes.Transparent;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Padding = new Thickness(2);

            _root = new StackPanel { Orientation = Orientation.Vertical };
            Content = _root;
            BuildSections();
        }

        // =================================================================================
        // 页面骨架
        // =================================================================================

        void BuildSections()
        {
            _root.Children.Add(BuildHeader());

            TextBlock intro = MakeText(
                "这是 UiKitGallery 示例插件，不是内置卡。下面每个控件都是 UiKit 公共 API 的真实实例；" +
                "颜色与字号跟随「设置 → 外观」，交互只改内存状态，不写配置。",
                _fontSize - 1, Muted(0.62), FontWeights.Normal);
            intro.Margin = new Thickness(0, 4, 0, 10);
            _root.Children.Add(intro);

            _root.Children.Add(BuildMetricsSection());
            _root.Children.Add(BuildBadgeSection());
            _root.Children.Add(BuildListSection());
            _root.Children.Add(BuildTableSection());
            _root.Children.Add(BuildChartSection());
            _root.Children.Add(BuildTreeTimelineSection());
            _root.Children.Add(BuildFormSection());
            _root.Children.Add(BuildInputSection());
            _root.Children.Add(BuildDropSection());

            TextBlock footer = MakeText(
                "控件覆盖清单与安装步骤见 samples/UiKitGallery/README.md。",
                Math.Max(9, _fontSize - 3), Muted(0.42), FontWeights.Normal);
            footer.Margin = new Thickness(0, 2, 0, 6);
            _root.Children.Add(footer);
        }

        FrameworkElement BuildHeader()
        {
            // CardHeader：标题 + 徽标 + 右侧 CardToolbar；画廊顶部的两个动作按钮由工具栏动态添加。
            var header = new CardHeader("UiKit 控件画廊");
            header.Badge = "27 个控件";
            header.Margin = new Thickness(0, 0, 0, 6);

            // CardToolbar 由 CardHeader.Actions 提供：刷新图表 / 重置演示。
            header.Actions.Add("刷新图表", delegate { RefreshCharts(); }, CardButtonTone.Primary,
                "重新生成示例图表数据");
            header.Actions.Add("重置演示", delegate { ResetDemo(); }, CardButtonTone.Normal,
                "清空本地演示状态");

            header.ApplyStyle(_style, _corner);
            return header;
        }

        // =================================================================================
        // 指标 / 徽标 / 状态
        // =================================================================================

        FrameworkElement BuildMetricsSection()
        {
            var wrap = new WrapPanel();

            // CardMetricTile：数值 + 单位 + 趋势 / 说明，语气色由 CardBadgeTone 决定。
            wrap.Children.Add(MakeMetric("CPU", "23.4", "%", "+2.1", "演示数据", "◈",
                CardBadgeTone.Accent, CardMetricTrend.Up));
            wrap.Children.Add(MakeMetric("内存", "9.8", "GB", "-0.4", "演示数据", "▣",
                CardBadgeTone.Info, CardMetricTrend.Down));

            // CardGauge：仪表盘，演示 Value / Minimum / Maximum / Label / Unit 的组合。
            var gauge = new CardGauge
            {
                Value = 62,
                Label = "CPU",
                Unit = "%",
                Width = 132,
                Height = 92,
                Margin = new Thickness(0, 0, 8, 8)
            };
            gauge.ApplyStyle(_style, _corner);
            wrap.Children.Add(gauge);

            // CardProgressRing：环形进度，演示 Value / Label / ShowValue。
            var ring = new CardProgressRing
            {
                Value = 76,
                Label = "完成",
                Width = 104,
                Height = 104,
                Margin = new Thickness(0, 0, 8, 8)
            };
            ring.ApplyStyle(_style, _corner);
            wrap.Children.Add(ring);

            return MakeSection("指标与数据展示", "gauge",
                "CardMetricTile 指标块（趋势 / 语气）、CardGauge 仪表盘、CardProgressRing 环形进度；数据都是本地演示值。",
                wrap);
        }

        FrameworkElement BuildBadgeSection()
        {
            var wrap = new WrapPanel();

            // CardBadge：五种语气的小徽标，颜色由 ThemeTokens 的 Accent / Danger / Muted 派生。
            wrap.Children.Add(MakeBadge("运行中", CardBadgeTone.Success));
            wrap.Children.Add(MakeBadge("只读", CardBadgeTone.Neutral));
            wrap.Children.Add(MakeBadge("注意", CardBadgeTone.Warning));
            wrap.Children.Add(MakeBadge("失败", CardBadgeTone.Danger));
            wrap.Children.Add(MakeBadge("强调", CardBadgeTone.Accent));

            // CardTag：可移除标签，演示 Removable 与 Removed 事件。
            var tag = new CardTag("可移除标签（点 ×）", CardBadgeTone.Warning);
            tag.Removable = true;
            tag.Margin = new Thickness(0, 0, 6, 6);
            tag.ApplyStyle(_style, _corner);
            _tag = tag;
            tag.Removed += delegate(CardTag removed)
            {
                removed.Visibility = Visibility.Collapsed;
                SetStatus("CardTag.Removed：标签已从布局里移除（演示，不写配置）", CardStatusKind.Info);
            };
            wrap.Children.Add(tag);

            // CardStatus：状态行，演示文字 + 语气 +（可选的）进度条。
            var status = new CardStatus();
            status.ApplyStyle(_style);
            status.SetText("CardStatus：状态行支持普通 / 信息 / 成功 / 警告 / 错误五种语气。",
                CardStatusKind.Info);
            _status = status;

            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(wrap);
            stack.Children.Add(status);
            return MakeSection("徽标、标签与状态", "badge-check",
                "CardBadge 小徽标、CardTag 可移除标签、CardStatus 状态行；后续所有交互都会更新这一行状态。",
                stack);
        }

        // =================================================================================
        // 列表 / 表格
        // =================================================================================

        FrameworkElement BuildListSection()
        {
            // CardList：主题化列表，演示空状态、选中、双击 / 回车激活；条目内容由卡片自己决定。
            var list = new CardList { Height = 140, Margin = new Thickness(0, 0, 0, 4) };
            list.SetEmptyText("列表为空（CardList 的空状态）");
            list.Items.Add("第一行：试试选中 / 双击");
            list.Items.Add("第二行：列表只提供容器与交互");
            list.Items.Add("第三行：条目内容由卡片自己决定");
            list.ApplyStyle(_style, _cli);
            list.SelectedIndex = 0;

            list.SelectionChanged += delegate
            {
                SetStatus("CardList 选中：" + Describe(list.SelectedItem), CardStatusKind.Info);
            };
            list.ItemActivated += delegate(object item)
            {
                SetStatus("CardList 激活（双击 / 回车）：" + Describe(item), CardStatusKind.Success);
            };

            return MakeSection("列表", "list",
                "CardList：主题化行、空状态、选中、双击 / 回车激活；高度固定后由内部滚动。", list);
        }

        FrameworkElement BuildTableSection()
        {
            // CardTable：列宽 / 对齐 + 行数据 + 选中 + 排序；表头自带点击排序，这里额外演示 SortBy。
            var table = new CardTable { Height = 170, Margin = new Thickness(0, 0, 0, 4) };
            table.AddColumn("名称", null, null, new GridLength(1, GridUnitType.Star));
            table.AddColumn("大小", null, null, new GridLength(82), TextAlignment.Right);
            table.AddRow("alpha.txt", "12 KB");
            table.AddRow("beta.log", "4 KB");
            table.AddRow("gamma.png", "84 KB");
            table.ApplyStyle(_style, _corner);
            table.SelectedIndex = 0;

            var sort = MakeButton("按大小切换排序", CardButtonTone.Normal, delegate
            {
                _tableSorted = !_tableSorted;
                table.SortBy(1, _tableSorted);
                SetStatus("CardTable.SortBy(1, " + (_tableSorted ? "true" : "false") + ")：按第二列排序",
                    CardStatusKind.Info);
            });
            sort.Margin = new Thickness(0, 6, 0, 0);

            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(table);
            stack.Children.Add(sort);
            return MakeSection("表格", "table",
                "CardTable：列宽 / 对齐、行数据、选中、排序；表头自身支持点击排序，这里演示代码调用 SortBy。",
                stack);
        }

        // =================================================================================
        // 图表
        // =================================================================================

        FrameworkElement BuildChartSection()
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical };

            // CardSparkline：迷你趋势线，演示 SetValues / Fill / 自动缩放。
            TextBlock sparklineCaption = MakeText("CardSparkline：迷你趋势线，空值 / NaN 会断线。",
                Math.Max(10, _fontSize - 2), Muted(0.58), FontWeights.Normal);
            sparklineCaption.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(sparklineCaption);

            _sparkline = new CardSparkline
            {
                Width = 455,
                Height = 52,
                Fill = true,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _sparkline.SetValues(Wave(24, 0.0, 45.0, 20.0));
            _sparkline.ApplyStyle(_style, _corner);
            stack.Children.Add(_sparkline);

            // CardLineChart：多序列折线，演示 Series / Labels / Fill / 图例与网格。
            TextBlock lineCaption = MakeText("CardLineChart：多序列折线 + 图例 + 网格 + 横轴标签。",
                Math.Max(10, _fontSize - 2), Muted(0.58), FontWeights.Normal);
            lineCaption.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(lineCaption);

            _lineChart = new CardLineChart
            {
                Width = 455,
                Height = 180,
                Fill = true,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _lineChart.Series = InitialLineSeries();
            _lineChart.Labels = MakeLabels(8);
            _lineChart.ApplyStyle(_style, _corner);
            stack.Children.Add(_lineChart);

            // CardBarChart：分组柱状图，演示多序列并排与横轴标签。
            TextBlock barCaption = MakeText("CardBarChart：分组柱状图，单序列时就是普通柱状图。",
                Math.Max(10, _fontSize - 2), Muted(0.58), FontWeights.Normal);
            barCaption.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(barCaption);

            _barChart = new CardBarChart { Width = 455, Height = 180 };
            _barChart.Series = InitialBarSeries();
            _barChart.Labels = new List<string> { "Q1", "Q2", "Q3", "Q4", "Q5" };
            _barChart.ApplyStyle(_style, _corner);
            stack.Children.Add(_barChart);

            return MakeSection("图表", "chart-line",
                "三种图表都由 UiKit 自绘；点标题栏「刷新图表」会用另一组正弦波数据重画。", stack);
        }

        IList<CardChartSeries> InitialLineSeries()
        {
            var series = new List<CardChartSeries>();
            series.Add(new CardChartSeries("CPU") { Values = Wave(8, 0.0, 45.0, 22.0) });
            series.Add(new CardChartSeries("内存") { Values = Wave(8, 1.2, 55.0, 12.0) });
            return series;
        }

        IList<CardChartSeries> InitialBarSeries()
        {
            var series = new List<CardChartSeries>();
            series.Add(new CardChartSeries("示例") { Values = Wave(5, 0.0, 40.0, 20.0) });
            return series;
        }

        void RefreshCharts()
        {
            _chartSeed++;

            if (_sparkline != null)
            {
                _sparkline.SetValues(Wave(24, _chartSeed * 2.0, 45.0, 20.0));
            }

            if (_lineChart != null)
            {
                var series = new List<CardChartSeries>();
                series.Add(new CardChartSeries("CPU") { Values = Wave(8, _chartSeed * 1.7, 45.0, 22.0) });
                series.Add(new CardChartSeries("内存") { Values = Wave(8, _chartSeed * 1.7 + 1.2, 55.0, 12.0) });
                _lineChart.Series = series;
                _lineChart.Labels = MakeLabels(8);
            }

            if (_barChart != null)
            {
                var series = new List<CardChartSeries>();
                series.Add(new CardChartSeries("示例") { Values = Wave(5, _chartSeed * 0.9, 40.0, 20.0) });
                _barChart.Series = series;
                _barChart.Labels = new List<string> { "Q1", "Q2", "Q3", "Q4", "Q5" };
            }

            SetStatus("示例图表已刷新（第 " + _chartSeed + " 次）", CardStatusKind.Success);
        }

        // =================================================================================
        // 树 / 时间线
        // =================================================================================

        FrameworkElement BuildTreeTimelineSection()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // CardTree：根节点 / 子节点 / 展开收起 / 选中事件。
            var tree = new CardTree { Height = 170, Margin = new Thickness(0, 0, 6, 0) };
            tree.EmptyText = "树为空";
            CardTreeItem root = tree.AddRoot("示例项目");
            root.Add("控件演示").Add("CardIcon");
            root.Add("容器演示");
            tree.AddRoot("第二个根节点", "tag");
            tree.ExpandAll();
            tree.ApplyStyle(_style, _corner);
            tree.SelectionChanged += delegate(CardTreeItem item)
            {
                SetStatus("CardTree 选中：" + (item == null ? "（空）" : item.Text), CardStatusKind.Info);
            };
            Grid.SetColumn(tree, 0);
            grid.Children.Add(tree);

            // CardTimeline：时间线，演示时间 / 标题 / 详情 / 语气 / 选中事件。
            var timeline = new CardTimeline { Height = 170, Margin = new Thickness(6, 0, 0, 0) };
            timeline.EmptyText = "时间线为空";
            timeline.Add("10:00", "创建画廊", "示例插件启动", CardTimelineTone.Normal);
            timeline.Add("10:05", "挂载控件", "25 个以上公共控件", CardTimelineTone.Accent);
            timeline.Add("10:10", "等待主题", "颜色 / 字号跟随卡片外观", CardTimelineTone.Success);
            timeline.ApplyStyle(_style, _corner);
            timeline.SelectionChanged += delegate(CardTimelineItem item)
            {
                SetStatus("CardTimeline 选中：" + (item == null ? "（空）" : item.Title), CardStatusKind.Info);
            };
            Grid.SetColumn(timeline, 1);
            grid.Children.Add(timeline);

            return MakeSection("树与时间线", "list-tree",
                "CardTree 展开 / 收起 / 选中、CardTimeline 时间线圆点与语气；两边都有自己的空状态。", grid);
        }

        // =================================================================================
        // 表单
        // =================================================================================

        FrameworkElement BuildFormSection()
        {
            // CardSettingsForm：声明式表单容器；下面用 AddRow 把控件装进统一的行布局。
            var form = new CardSettingsForm();

            // CardSlider：带标签与数值显示的滑块。
            var slider = new CardSlider(null, 0, 100, 42);
            form.AddRow("CardSlider（0–100）", slider);

            // CardToggle：开关（CheckBox 基类，演示 Checked / Unchecked）。
            var toggle = new CardToggle(null);
            toggle.IsChecked = true;
            form.AddRow("CardToggle（默认开启）", toggle);

            // CardSelect：下拉选择，演示 ItemsSource / SelectedItem / SelectionChanged。
            var select = new CardSelect(null, new[] { "选项 A", "选项 B", "选项 C" });
            select.SelectedItem = "选项 B";
            form.AddRow("CardSelect", select);

            // CardNumberInput：数字输入，演示 Minimum / Maximum / Value 与 ValueChanged。
            var number = new CardNumberInput(null, 7, 1, 20);
            form.AddRow("CardNumberInput（1–20）", number);

            // CardColorPicker：颜色输入，初值取自当前主题的强调色令牌（不写死颜色）。
            var color = new CardColorPicker(null, ToHex(_tokens.Accent));
            form.AddRow("CardColorPicker（令牌强调色）", color);

            // CardSegmentedControl：分段选择，演示选项集合与 SelectedIndex。
            var segment = new CardSegmentedControl(null, "横向", "纵向");
            form.AddRow("CardSegmentedControl", segment);
            form.AddDescription("以上控件由 CardSettingsForm 统一排版；改值只更新状态行，不写配置文件。");
            form.AddDivider();

            slider.ValueChanged += delegate(double value)
            {
                SetStatus("CardSlider 值：" + value.ToString("0"), CardStatusKind.Info);
            };
            toggle.Checked += delegate
            {
                SetStatus("CardToggle：已开启", CardStatusKind.Success);
            };
            toggle.Unchecked += delegate
            {
                SetStatus("CardToggle：已关闭", CardStatusKind.Normal);
            };
            select.SelectionChanged += delegate(string item)
            {
                SetStatus("CardSelect 选中：" + Describe(item), CardStatusKind.Info);
            };
            number.ValueChanged += delegate(double value)
            {
                SetStatus("CardNumberInput 值：" + value.ToString("0.##"), CardStatusKind.Info);
            };
            color.ValueChanged += delegate(string value)
            {
                SetStatus("CardColorPicker 值：" + value, CardStatusKind.Info);
            };
            segment.SelectionChanged += delegate(int index, string item)
            {
                SetStatus("CardSegmentedControl 选中：" + index + " / " + Describe(item), CardStatusKind.Info);
            };

            form.ApplyStyle(_style, _corner);

            // 再单独演示一次 CardFormRow：表单内部每一行也是这个类型。
            var badge = new CardBadge("自定义行内容", CardBadgeTone.Accent);
            var row = new CardFormRow("CardFormRow", badge) { Margin = new Thickness(0, 8, 0, 0) };
            row.ApplyStyle(_style, _corner);

            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(form);
            stack.Children.Add(row);
            return MakeSection("表单控件", "sliders-horizontal",
                "CardSettingsForm / CardSlider / CardToggle / CardSelect / CardNumberInput / CardColorPicker / " +
                "CardSegmentedControl / CardFormRow。", stack);
        }

        // =================================================================================
        // 输入与按钮
        // =================================================================================

        FrameworkElement BuildInputSection()
        {
            // CardToolbar：一排统一间距 / 换肤的 CardButton。
            var toolbar = new CardToolbar();
            toolbar.Add("普通按钮", delegate
            {
                _clickCount++;
                SetStatus("CardButton 普通按钮：第 " + _clickCount + " 次点击", CardStatusKind.Info);
            }, CardButtonTone.Normal, "CardToolbar.Add");
            toolbar.Add("主操作", delegate
            {
                _clickCount++;
                SetStatus("CardButton 主操作：第 " + _clickCount + " 次点击", CardStatusKind.Success);
            }, CardButtonTone.Primary, "Primary 语气");
            toolbar.Add("危险示例", delegate
            {
                SetStatus("CardButton 危险语气：示例不会真的删除任何东西", CardStatusKind.Warning);
            }, CardButtonTone.Danger, "Danger 语气");
            CardButton disabled = toolbar.Add("禁用态", null, CardButtonTone.Normal, "IsEnabled = false");
            disabled.IsEnabled = false;
            toolbar.ApplyStyle(_style, _corner);

            // CardInput：单行输入 + 占位符 + 回车提交（内部用 CardTextBox）。
            _input = new CardInput(false)
            {
                Placeholder = "CardInput：输入文字后回车提交",
                MinHeight = 34
            };
            _input.ApplyStyle(_style, _corner);
            _input.Submitted += delegate(string text)
            {
                SetStatus(string.IsNullOrWhiteSpace(text) ? "CardInput：提交了空内容" : "CardInput 提交：" + text,
                    CardStatusKind.Success);
            };

            var submit = MakeButton("提交", CardButtonTone.Primary, delegate { _input.Submit(); });
            var clear = MakeButton("清空", CardButtonTone.Normal, delegate
            {
                _input.Clear();
                SetStatus("CardInput 已清空", CardStatusKind.Normal);
            });
            clear.Margin = new Thickness(6, 0, 0, 0);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
            buttons.Children.Add(submit);
            buttons.Children.Add(clear);

            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_input, 0);
            Grid.SetColumn(buttons, 1);
            row.Children.Add(_input);
            row.Children.Add(buttons);

            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(toolbar);
            stack.Children.Add(row);
            return MakeSection("输入与按钮", "text-cursor-input",
                "CardToolbar / CardButton（三种语气 + 禁用态）/ CardInput（回车提交、清空）；状态只在内存里。",
                stack);
        }

        // =================================================================================
        // 附加：拖放提示层
        // =================================================================================

        FrameworkElement BuildDropSection()
        {
            var host = new Grid { Height = 76 };
            var hint = MakeText("CardDropOverlay：拖入文件时的提示层，这里用按钮演示 Show / Hide。",
                Math.Max(10, _fontSize - 2), Muted(0.5), FontWeights.Normal);
            hint.TextAlignment = TextAlignment.Center;
            hint.VerticalAlignment = VerticalAlignment.Center;
            host.Children.Add(hint);

            // CardDropOverlay：覆盖在宿主网格上的提示层，Show(3) 显示“松开以添加 3 项”。
            var overlay = new CardDropOverlay();
            overlay.ApplyStyle(_style, _corner);
            host.Children.Add(overlay);

            var show = MakeButton("显示「松开以添加 3 项」", CardButtonTone.Normal, delegate
            {
                overlay.Show(3);
                SetStatus("CardDropOverlay.Show(3)", CardStatusKind.Info);
            });
            var hide = MakeButton("隐藏", CardButtonTone.Normal, delegate
            {
                overlay.Hide();
                SetStatus("CardDropOverlay.Hide()", CardStatusKind.Normal);
            });
            hide.Margin = new Thickness(6, 0, 0, 0);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(show);
            row.Children.Add(hide);

            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(host);
            stack.Children.Add(row);
            return MakeSection("拖放提示（附加）", "layers",
                "真实拖放由宿主通过 ICardDropTarget / ICardDropFeedback 注册；控件本身只负责高亮提示。",
                stack);
        }

        // =================================================================================
        // 组件工厂 / 主题辅助
        // =================================================================================

        Border MakeSection(string title, string iconName, string description, FrameworkElement content)
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical };

            var head = new StackPanel { Orientation = Orientation.Horizontal };

            // CardIcon：Lucide 图标（名字走 LucideIcons.Get），每个分区标题左侧都有一枚。
            var icon = new CardIcon(LucideIcons.Get(iconName), Math.Max(14, _fontSize + 2));
            icon.ApplyStyle(_style, _corner);
            head.Children.Add(icon);

            TextBlock titleText = MakeText(title, _fontSize + 1, _foreground, FontWeights.SemiBold);
            titleText.Margin = new Thickness(6, 0, 0, 0);
            head.Children.Add(titleText);
            panel.Children.Add(head);

            if (!string.IsNullOrEmpty(description))
            {
                TextBlock desc = MakeText(description, Math.Max(10, _fontSize - 2), Muted(0.58), FontWeights.Normal);
                desc.Margin = new Thickness(0, 2, 0, 6);
                panel.Children.Add(desc);
            }

            panel.Children.Add(content);

            return new Border
            {
                Background = new SolidColorBrush(_tokens.Panel),
                BorderBrush = new SolidColorBrush(_tokens.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(_corner),
                Padding = new Thickness(10, 8, 10, 10),
                Margin = new Thickness(0, 0, 0, 10),
                Child = panel
            };
        }

        TextBlock MakeText(string text, double size, Color color, FontWeight weight)
        {
            return new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = _font,
                FontSize = Math.Max(9, size),
                FontWeight = weight,
                Foreground = new SolidColorBrush(color)
            };
        }

        CardMetricTile MakeMetric(string title, string value, string unit, string delta, string caption,
            string icon, CardBadgeTone tone, CardMetricTrend trend)
        {
            // CardMetricTile：一次设置标题 / 数值 / 单位 / 趋势文字 / 说明，再统一套主题。
            var tile = new CardMetricTile(title, value);
            tile.Unit = unit;
            tile.Delta = delta;
            tile.Caption = caption;
            tile.Icon = icon;
            tile.Tone = tone;
            tile.Trend = trend;
            tile.Margin = new Thickness(0, 0, 8, 8);
            tile.ApplyStyle(_style, _corner);
            return tile;
        }

        CardBadge MakeBadge(string text, CardBadgeTone tone)
        {
            // CardBadge：所有徽标共用同一套语气色，这里统一加右 / 下间距。
            var badge = new CardBadge(text, tone);
            badge.Margin = new Thickness(0, 0, 6, 6);
            badge.ApplyStyle(_style, _corner);
            return badge;
        }

        CardButton MakeButton(string text, CardButtonTone tone, RoutedEventHandler click)
        {
            // CardButton：统一套当前卡片字体 / 字号 / 前景色，再挂点击回调。
            var button = new CardButton(text, tone);
            button.ApplyStyle(_style, _corner);
            if (click != null)
            {
                button.Click += click;
            }
            return button;
        }

        void SetStatus(string text, CardStatusKind kind)
        {
            if (_status == null) return;
            _status.SetText(text, kind);
        }

        void ResetDemo()
        {
            _clickCount = 0;
            _chartSeed = 0;
            _tableSorted = false;
            if (_tag != null) _tag.Visibility = Visibility.Visible;
            if (_input != null) _input.Clear();
            RefreshCharts();
            SetStatus("演示状态已重置（不写配置文件）", CardStatusKind.Normal);
        }

        static string Describe(object value)
        {
            return value == null ? "（无）" : value.ToString();
        }

        static List<double> Wave(int count, double phase, double middle, double amplitude)
        {
            var values = new List<double>(count);
            for (int i = 0; i < count; i++)
            {
                values.Add(middle + amplitude * Math.Sin((i + phase) * 0.55));
            }
            return values;
        }

        static IList<string> MakeLabels(int count)
        {
            var labels = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                labels.Add("T" + (i + 1));
            }
            return labels;
        }

        static Color MutedColor(Color foreground, double alpha)
        {
            return ThemeResolver.WithAlpha(foreground, alpha);
        }

        Color Muted(double alpha)
        {
            return MutedColor(_foreground, alpha);
        }

        static Color ResolveForeground(StyleSettings style, ThemeTokens tokens)
        {
            byte a, r, g, b;
            string code = style == null ? null : style.Foreground;
            if (ColorCode.TryParse(code, out a, out r, out g, out b))
            {
                // 正文始终按不透明绘制；alpha 只在软色令牌里使用。
                return Color.FromArgb(255, r, g, b);
            }

            // 解析失败时用主题令牌 Muted 的 RGB 兜底，避免写死默认前景色常量。
            return Color.FromRgb(tokens.Muted.R, tokens.Muted.G, tokens.Muted.B);
        }

        static FontFamily ResolveFont(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return new FontFamily("Microsoft YaHei UI");
            }
            return new FontFamily(name);
        }

        static string ToHex(Color color)
        {
            return "#" + color.A.ToString("X2") + color.R.ToString("X2") +
                   color.G.ToString("X2") + color.B.ToString("X2");
        }
    }
}
