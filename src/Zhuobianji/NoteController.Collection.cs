using System;
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
            var area = ScreenArea.Of(main);
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

    // 取出没存上时收纳关系不变。子便签开着窗口时窗口底部会显示保存失败；没开着（还没建或收起了）就在这里说原因。
    internal bool ReleaseNote(NoteEntry note, bool showErrors = true)
    {
        if (note.Data.CollectedBy is null) return true;
        if (!book.Release(note))
        {
            if (showErrors && !IsShown(note)) Dialogs.Show($"“{note.Data.Preview}”没能取出，仍收在主便签里。\n\n{note.SaveError}", "取出");
            return false;
        }
        RevealReleased(note, activate: true);
        RefreshCollections();
        return true;
    }

    // 刚解除收纳的便签显示出来。正文读不了就不显示（WindowFor 已提示）；新建的根便签窗口 WindowFor 已经挂过桌面，
    // 原来就有窗口（展开过的子便签）的，记着贴桌面就在这里挂上。
    private void RevealReleased(NoteEntry note, bool activate)
    {
        var existed = WindowOf(note) is not null;
        if (WindowFor(note) is not { } window) return;
        if (existed && note.Data.IsOnDesktop && !window.IsDesktopAttached) SetDesktopMode(window, true);
        window.Reveal(activate);
    }

    internal NoteWindow? CreateCollectedNote(NoteWindow main)
    {
        if (!main.Data.IsCollectionMain || !main.SaveNow()) return null;
        var window = CreateNote(main, collectedBy: main.Data.Id);
        ExpandCollectedNote(window.Entry);
        return window;
    }
}
