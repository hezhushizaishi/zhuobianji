using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Zhuobianji;

// 标签拖动排序：被拖的标签跟着指针走，其他标签滑开让出插入位置；松手后从当前位置滑到新位置。
internal partial class NoteWindow
{
    private const string CollectionDragFormat = "Zhuobianji.CollectionTab";
    private Guid? collectionDragId;
    private Point collectionDragStart;
    private bool collectionDropped;

    private void OnCollectionDragStart(object sender, MouseButtonEventArgs e)
    {
        collectionDragId = (Guid)((FrameworkElement)sender).Tag;
        collectionDragStart = e.GetPosition(CollectionTabs);
    }

    private void OnCollectionDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || collectionDragId is not Guid id) return;
        var point = e.GetPosition(CollectionTabs);
        if (Math.Abs(point.X - collectionDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - collectionDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var tab = (ToggleButton)sender;
        collectionDragId = null;
        collectionDropped = false;
        e.Handled = true;
        // 先取消按钮的按下状态，拖动结束时就不会触发“展开／收起”。
        tab.ReleaseMouseCapture();
        Panel.SetZIndex(tab, 1);
        try { DragDrop.DoDragDrop(tab, new DataObject(CollectionDragFormat, id.ToString()), DragDropEffects.Move); }
        finally
        {
            Panel.SetZIndex(tab, 0);
            if (!collectionDropped) SettleCollectionTabs(TabPositions());
        }
    }

    private bool IsCollectionDrag(IDataObject data, out Guid id)
    {
        return Guid.TryParse(data.GetData(CollectionDragFormat) as string, out id) &&
            Data.IsCollectionMain && CollectionTabButtons.ContainsKey(id);
    }

    // 按布局位置算插入点，不受让位动画的位移影响。
    internal int CollectionInsertionIndex(double y)
    {
        for (var index = 0; index < CollectionTabs.Children.Count; index++)
        {
            var slot = LayoutInformation.GetLayoutSlot((FrameworkElement)CollectionTabs.Children[index]);
            if (y < slot.Top + tabHeight / 2) return index;
        }
        return CollectionTabs.Children.Count;
    }

    private void OnCollectionDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!IsCollectionDrag(e.Data, out var id)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Move;
        var pointer = e.GetPosition(CollectionScroll);
        if (pointer.Y < 16) CollectionScroll.LineUp();
        else if (pointer.Y > CollectionScroll.ActualHeight - 16) CollectionScroll.LineDown();
        var y = e.GetPosition(CollectionTabs).Y;
        var dragged = CollectionTabButtons[id];
        var count = CollectionTabs.Children.Count;
        var from = CollectionTabs.Children.IndexOf(dragged);
        var insertion = CollectionInsertionIndex(y);
        var target = insertion > from ? insertion - 1 : insertion;
        for (var index = 0; index < count; index++)
        {
            var tab = (UIElement)CollectionTabs.Children[index];
            if (tab == dragged)
            {
                // 被拖的标签中心跟着指针，不超出第一个和最后一个位置。
                var shift = (TranslateTransform)tab.RenderTransform;
                shift.BeginAnimation(TranslateTransform.YProperty, null);
                shift.Y = Math.Clamp(y - from * TabPitch - tabHeight / 2, -from * TabPitch, (count - 1 - from) * TabPitch);
                continue;
            }
            var offset = index > from && index <= target ? -TabPitch : index < from && index >= target ? TabPitch : 0;
            SlideTab(tab, TranslateTransform.YProperty, offset, NoteTheme.Fast);
        }
    }

    // 指针离开整个标签区才收回让位；在标签之间移动也会触发 DragLeave，不处理。
    private void OnCollectionDragLeave(object sender, DragEventArgs e)
    {
        if (new Rect(CollectionScroll.RenderSize).Contains(e.GetPosition(CollectionScroll))) return;
        foreach (UIElement tab in CollectionTabs.Children) SlideTab(tab, TranslateTransform.YProperty, 0, NoteTheme.Fast);
    }

    private void OnCollectionDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var before = TabPositions();
        if (!IsCollectionDrag(e.Data, out var id)) e.Effects = DragDropEffects.None;
        else e.Effects = controller.MoveCollectedNote(this, id, CollectionInsertionIndex(e.GetPosition(CollectionTabs).Y))
            ? DragDropEffects.Move : DragDropEffects.None;
        collectionDropped = true;
        SettleCollectionTabs(before);
    }

    // 每个标签现在看上去的位置（布局位置加让位位移）。
    private Dictionary<UIElement, double> TabPositions() => CollectionTabs.Children.Cast<UIElement>()
        .Select((tab, index) => (tab, y: index * TabPitch + ((TranslateTransform)tab.RenderTransform).Y))
        .ToDictionary(item => item.tab, item => item.y);

    // 排好序后，每个标签从刚才看上去的位置滑到自己的新位置。
    private void SettleCollectionTabs(Dictionary<UIElement, double> before)
    {
        for (var index = 0; index < CollectionTabs.Children.Count; index++)
        {
            var tab = CollectionTabs.Children[index];
            var shift = (TranslateTransform)tab.RenderTransform;
            var from = before.TryGetValue(tab, out var y) ? y - index * TabPitch : 0;
            shift.Y = 0;
            shift.BeginAnimation(TranslateTransform.YProperty,
                from == 0 ? null : new DoubleAnimation(from, 0, NoteTheme.Fast) { EasingFunction = NoteTheme.Ease });
        }
    }
}
