using System;
using System.Linq;
using System.Windows;
using Zhuobianji.Core;

namespace Zhuobianji;

// 收纳：关系存在 NoteBook 里（Collect / Release / Move / SetMain），这里管窗口的收起、展开和标签刷新。
internal sealed partial class NoteController
{
    internal bool MoveCollectedNote(NoteWindow main, Guid id, int insertionIndex)
    {
        var saved = book.Move(main.Entry, id, insertionIndex);
        RefreshCollections();
        return saved;
    }

    internal void RefreshCollections()
    {
        foreach (var window in Windows.Values) window.RefreshCollectionState();
    }

    internal bool SetMainNote(NoteWindow window)
    {
        var saved = book.SetMain(window.Entry);
        RefreshCollections();
        return saved;
    }

    internal bool CollectNote(NoteWindow window)
    {
        if (!book.Collect(window.Entry)) return false;
        window.Hide();
        ShowSaved(book.Main!, activate: true);
        RefreshCollections();
        return true;
    }

    // 子便签从标签那一侧抽出来：卡片右边贴着主便签的标签区，顶部对齐它的标签；左边放不下才放到主便签右边。
    // 窗口坐标含四周的阴影边距，卡片边缘 = 窗口边缘 ± ShadowMargin。
    private const double ExpandGap = 8;

    internal bool ExpandCollectedNote(NoteEntry note)
    {
        if (note.Data.CollectedBy is null || book.Main is not { } mainNote) return false;
        ShowSaved(mainNote, activate: false);
        if (WindowOf(mainNote) is not { } main || WindowFor(note) is not { } window) return false;
        var slide = 0.0;
        if (!window.IsVisible)
        {
            var area = SystemParameters.WorkArea;
            var margin = NoteWindow.ShadowMargin;
            var railLeft = main.Left + margin + (mainNote.Data.ShowCollectionRail ? 0 : main.CollectionRailWidth);
            var x = railLeft - ExpandGap - window.Width + margin;
            slide = 16;
            if (x + margin < area.Left)
            {
                x = main.Left + main.Width - 2 * margin + ExpandGap;
                slide = -16;
            }
            window.Left = Math.Max(area.Left - margin, Math.Min(x, area.Right - window.Width + margin));
            window.Top = Math.Max(area.Top - margin, Math.Min(main.CollectionTabTop(note) - margin, area.Bottom - window.Height + margin));
            if (note.Data.IsOnDesktop && !window.IsDesktopAttached) SetDesktopMode(window, true);
        }
        ShowSaved(note, activate: true, slide);
        RefreshCollections();
        return true;
    }

    internal bool CollapseCollectedNote(NoteEntry note) =>
        note.Data.CollectedBy is not null && SaveAndHide(note);

    internal void ToggleCollectedNote(NoteEntry note)
    {
        if (IsShown(note)) CollapseCollectedNote(note);
        else ExpandCollectedNote(note);
    }

    internal bool ReleaseNote(NoteEntry note)
    {
        if (note.Data.CollectedBy is null) return true;
        if (!book.Release(note)) return false;
        // 取出时记下的关系已经存上；正文读不了就不显示（WindowFor 已提示）。新建的根便签窗口 WindowFor 已经挂过桌面。
        var existed = WindowOf(note) is not null;
        if (WindowFor(note) is { } window)
        {
            if (existed && note.Data.IsOnDesktop && !window.IsDesktopAttached) SetDesktopMode(window, true);
            window.Reveal(activate: true);
        }
        RefreshCollections();
        return true;
    }

    internal NoteWindow? CreateCollectedNote(NoteWindow main)
    {
        if (!main.Data.IsCollectionMain || !main.SaveNow()) return null;
        var window = CreateNote(main, collectedBy: main.Data.Id);
        ExpandCollectedNote(window.Entry);
        return window;
    }
}
