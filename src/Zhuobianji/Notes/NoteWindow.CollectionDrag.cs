using System;
using System.Windows;
using System.Windows.Input;

namespace Zhuobianji;

internal partial class NoteWindow
{
    private const string CollectionDragFormat = "Zhuobianji.CollectionTab";
    private Guid? collectionDragId;
    private Point collectionDragStart;

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
        var tab = (FrameworkElement)sender;
        collectionDragId = null;
        e.Handled = true;
        // 先取消按钮的按下状态，拖动结束时就不会触发“展开／收起”。
        tab.ReleaseMouseCapture();
        tab.Opacity = 0.6;
        try { DragDrop.DoDragDrop(tab, new DataObject(CollectionDragFormat, id.ToString()), DragDropEffects.Move); }
        finally
        {
            tab.Opacity = 1;
            CollectionDropMarker.Visibility = Visibility.Collapsed;
        }
    }

    private bool IsCollectionDrag(IDataObject data, out Guid id)
    {
        return Guid.TryParse(data.GetData(CollectionDragFormat) as string, out id) &&
            Data.IsCollectionMain && CollectionTabButtons.ContainsKey(id);
    }

    internal int CollectionInsertionIndex(double y)
    {
        for (var index = 0; index < CollectionTabs.Children.Count; index++)
        {
            var tab = (FrameworkElement)CollectionTabs.Children[index];
            if (y < tab.TranslatePoint(new Point(), CollectionTabs).Y + tab.ActualHeight / 2) return index;
        }
        return CollectionTabs.Children.Count;
    }

    private void OnCollectionDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!IsCollectionDrag(e.Data, out _)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Move;
        var pointer = e.GetPosition(CollectionScroll);
        if (pointer.Y < 16) CollectionScroll.LineUp();
        else if (pointer.Y > CollectionScroll.ActualHeight - 16) CollectionScroll.LineDown();
        var index = CollectionInsertionIndex(e.GetPosition(CollectionTabs).Y);
        var y = index < CollectionTabs.Children.Count
            ? ((FrameworkElement)CollectionTabs.Children[index]).TranslatePoint(new Point(), CollectionTabs).Y - 3
            : CollectionTabs.ActualHeight - 3;
        y = Math.Clamp(y - CollectionScroll.VerticalOffset, 0, Math.Max(0, CollectionScroll.ActualHeight - 2));
        CollectionDropMarker.Margin = new Thickness(2, y, 2, 0);
        CollectionDropMarker.Visibility = Visibility.Visible;
    }

    private void OnCollectionDragLeave(object sender, DragEventArgs e) =>
        CollectionDropMarker.Visibility = Visibility.Collapsed;

    private void OnCollectionDrop(object sender, DragEventArgs e)
    {
        CollectionDropMarker.Visibility = Visibility.Collapsed;
        e.Handled = true;
        if (!IsCollectionDrag(e.Data, out var id)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = controller.MoveCollectedNote(this, id, CollectionInsertionIndex(e.GetPosition(CollectionTabs).Y))
            ? DragDropEffects.Move : DragDropEffects.None;
    }
}