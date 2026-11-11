using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards;

/// <summary>
/// 列表选择逻辑：行命中、Ctrl / Shift 多选、右键保留选择、Shift 连选范围。
/// 从 StationView 抽出后，视图只保留鼠标事件入口与拖出阈值判断。
/// </summary>
internal sealed class StationSelection
{
    readonly CardList _list;
    readonly IList<StationItem> _items;
    readonly Func<StationItem, bool> _matches;

    /// <summary>Shift 连选的锚点；过滤条件变化 / 删除条目时由视图清空。</summary>
    public StationItem Anchor;

    public StationSelection(CardList list, IList<StationItem> items, Func<StationItem, bool> matches)
    {
        _list = list;
        _items = items;
        _matches = matches;
    }

    public List<StationItem> SelectedItems()
    {
        var result = new List<StationItem>();
        foreach (object row in _list.Inner.SelectedItems)
        {
            StationItem item = ItemOf(row);
            if (item is not null && !result.Contains(item)) result.Add(item);
        }
        return result;
    }

    public StationItem Selected()
    {
        List<StationItem> selected = SelectedItems();
        return selected.Count == 0 ? null : selected[0];
    }

    public StationItem ItemOf(object row)
    {
        var element = row as FrameworkElement;
        return element is null ? null : element.Tag as StationItem;
    }

    public StationItem ItemAt(Point point, out FrameworkElement row)
    {
        row = null;
        ListBoxItem container = ContainerAt(point);
        if (container is null) return null;

        row = container.Content as FrameworkElement;
        return ItemOf(row);
    }

    public FrameworkElement RowOf(StationItem item)
    {
        foreach (object rowObject in _list.Items)
        {
            var row = rowObject as FrameworkElement;
            if (row is not null && ReferenceEquals(row.Tag, item)) return row;
        }
        return null;
    }

    public bool IsRowSelected(FrameworkElement row) => _list.Inner.SelectedItems.Contains(row);

    /// <summary>
    /// 行内左键的选中规则：Shift 连选（无 Ctrl）、Ctrl 切换、普通单击单选。
    /// 整卡拖动在窗口的冒泡处理里，视图在调用后自行把左键标记 Handled。
    /// </summary>
    public void ApplyRowClick(StationItem item, FrameworkElement row, bool ctrl, bool shift)
    {
        if (shift && !ctrl && Anchor is not null)
        {
            SelectRange(Anchor, item);
        }
        else if (ctrl)
        {
            if (_list.Inner.SelectedItems.Contains(row)) _list.Inner.SelectedItems.Remove(row);
            else _list.Inner.SelectedItems.Add(row);
            Anchor = item;
        }
        else
        {
            _list.Inner.SelectedItem = row;
            Anchor = item;
        }
    }

    /// <summary>右键点在已选中的行上保留多选，批量操作才有意义；点未选中的行则只选它。</summary>
    public void KeepSelection(StationItem item, FrameworkElement row)
    {
        if (_list.Inner.SelectedItems.Contains(row)) return;

        _list.Inner.SelectedItem = row;
        Anchor = item;
    }

    ListBoxItem ContainerAt(Point point)
    {
        DependencyObject hit = _list.Inner.InputHitTest(point) as DependencyObject;
        while (hit is not null && !(hit is ListBoxItem))
            hit = VisualTreeHelper.GetParent(hit);
        return hit as ListBoxItem;
    }

    /// <summary>Shift 连选：在可见（过滤后）列表里从锚点到目标整段选中。</summary>
    void SelectRange(StationItem anchor, StationItem target)
    {
        var visible = new List<StationItem>();
        foreach (StationItem item in _items)
            if (_matches(item)) visible.Add(item);

        int from = visible.IndexOf(anchor);
        int to = visible.IndexOf(target);
        if (from < 0 || to < 0)
        {
            FrameworkElement row = RowOf(target);
            if (row is not null) _list.Inner.SelectedItem = row;
            return;
        }

        if (from > to) { int swap = from; from = to; to = swap; }

        _list.Inner.SelectedItems.Clear();
        foreach (object rowObject in _list.Items)
        {
            StationItem item = ItemOf(rowObject);
            int index = visible.IndexOf(item);
            if (index >= from && index <= to) _list.Inner.SelectedItems.Add(rowObject);
        }
    }
}
