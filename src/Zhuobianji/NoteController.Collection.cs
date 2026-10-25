using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Zhuobianji.Core;

namespace Zhuobianji;

internal sealed partial class NoteController
{
    internal NoteWindow? MainNote => Notes.Values.FirstOrDefault(window => window.Data.IsCollectionMain);
    internal IEnumerable<NoteWindow> RootNotes => Notes.Values.Where(window => window.Data.CollectedBy is null);
    internal IEnumerable<NoteWindow> CollectedNotes(NoteWindow main) =>
        Notes.Values.Where(window => window.Data.CollectedBy == main.Data.Id)
            .OrderBy(window => Array.IndexOf(main.Data.CollectionOrder, window.Data.Id) is var index && index >= 0 ? index : int.MaxValue)
            .ThenBy(window => window.Data.CreatedAt);

    internal bool MoveCollectedNote(NoteWindow main, Guid id, int insertionIndex)
    {
        var order = CollectedNotes(main).Select(window => window.Data.Id).ToList();
        var oldIndex = order.IndexOf(id);
        if (oldIndex < 0) return false;
        insertionIndex = Math.Clamp(insertionIndex, 0, order.Count);
        if (insertionIndex > oldIndex) insertionIndex--;
        if (insertionIndex == oldIndex) return true;
        order.RemoveAt(oldIndex);
        order.Insert(insertionIndex, id);
        var saved = main.TryCommit(data => data with { CollectionOrder = order.ToArray() });
        RefreshCollections();
        return saved;
    }

    internal void RefreshCollections()
    {
        foreach (var window in Notes.Values) window.RefreshCollectionState();
    }

    internal void NoteChanged() => MainNote?.RefreshCollectionState();

    internal bool SetMainNote(NoteWindow window)
    {
        if (MainNote is not null && MainNote != window || window.Data.CollectedBy is not null) return false;
        var saved = window.TryCommit(data => data with { IsCollectionMain = true, CollectedBy = null });
        RefreshCollections();
        return saved;
    }

    internal bool CollectNote(NoteWindow window)
    {
        if (MainNote is not { } main || main == window || window.Data.IsCollectionMain) return false;
        if (!window.TryCommit(data => data with { CollectedBy = main.Data.Id, IsHidden = true })) return false;
        window.Hide();
        ShowSaved(main, activate: true);
        RefreshCollections();
        return true;
    }

    // 子便签从标签那一侧抽出来：卡片右边贴着主便签的标签区，顶部对齐它的标签；左边放不下才放到主便签右边。
    // 窗口坐标含四周的阴影边距，卡片边缘 = 窗口边缘 ± ShadowMargin。
    private const double ExpandGap = 8;

    internal bool ExpandCollectedNote(NoteWindow window)
    {
        if (window.Data.CollectedBy is null || MainNote is not { } main) return false;
        ShowSaved(main, activate: false);
        var slide = 0.0;
        if (!window.IsVisible)
        {
            var area = SystemParameters.WorkArea;
            var margin = NoteWindow.ShadowMargin;
            var railLeft = main.Left + margin + (main.Data.ShowCollectionRail ? 0 : main.CollectionRailWidth);
            var x = railLeft - ExpandGap - window.Width + margin;
            slide = 16;
            if (x + margin < area.Left)
            {
                x = main.Left + main.Width - 2 * margin + ExpandGap;
                slide = -16;
            }
            window.Left = Math.Max(area.Left - margin, Math.Min(x, area.Right - window.Width + margin));
            window.Top = Math.Max(area.Top - margin, Math.Min(main.CollectionTabTop(window) - margin, area.Bottom - window.Height + margin));
            if (window.Data.IsOnDesktop && !window.IsDesktopAttached) SetDesktopMode(window, true);
        }
        ShowSaved(window, activate: true, slide);
        RefreshCollections();
        return true;
    }

    internal bool CollapseCollectedNote(NoteWindow window) =>
        window.Data.CollectedBy is not null && SaveAndHide(window);
    internal void ToggleCollectedNote(NoteWindow window)
    {
        if (window.IsVisible) CollapseCollectedNote(window);
        else ExpandCollectedNote(window);
    }

    internal bool ReleaseNote(NoteWindow window)
    {
        if (window.Data.CollectedBy is null) return true;
        if (!window.TryCommit(data => data with { CollectedBy = null, IsHidden = false })) return false;
        if (window.Data.IsOnDesktop && !window.IsDesktopAttached) SetDesktopMode(window, true);
        window.Reveal(activate: true);
        RefreshCollections();
        return true;
    }

    internal NoteWindow? CreateCollectedNote(NoteWindow main)
    {
        if (!main.Data.IsCollectionMain || !main.SaveNow()) return null;
        var window = CreateNote(main, collectedBy: main.Data.Id);
        ExpandCollectedNote(window);
        return window;
    }

}
