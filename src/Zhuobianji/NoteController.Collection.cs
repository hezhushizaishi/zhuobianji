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
        var saved = main.SaveCollectionOrder(order.ToArray());
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
        var oldMain = window.Data.IsCollectionMain;
        window.SetCollectionState(true, null);
        if (!window.SaveNow()) { window.SetCollectionState(oldMain, null); RefreshCollections(); return false; }
        RefreshCollections();
        return true;
    }

    internal bool CollectNote(NoteWindow window)
    {
        if (MainNote is not { } main || main == window || window.Data.IsCollectionMain) return false;
        var oldParent = window.Data.CollectedBy;
        var oldHidden = window.Data.IsHidden;
        window.SetCollectionState(false, main.Data.Id);
        window.SetHidden(true);
        if (!window.SaveNow())
        { window.SetCollectionState(false, oldParent); window.SetHidden(oldHidden); return false; }
        window.Hide();
        main.SetHidden(false);
        main.SaveNow();
        main.Reveal(activate: true);
        RefreshCollections();
        return true;
    }

    internal bool ExpandCollectedNote(NoteWindow window)
    {
        if (window.Data.CollectedBy is null || MainNote is not { } main) return false;
        main.SetHidden(false);
        main.SaveNow();
        main.Reveal(activate: false);
        if (!window.IsVisible)
        {
            var area = SystemParameters.WorkArea;
            var index = CollectedNotes(main).TakeWhile(child => child != window).Count();
            var x = main.Left + main.Width + 10;
            if (x + window.Width > area.Right) x = main.Left - window.Width - 10;
            window.Left = Math.Max(area.Left, Math.Min(x, area.Right - window.Width));
            window.Top = Math.Max(area.Top, Math.Min(main.Top + index % 5 * 26, area.Bottom - window.Height));
            if (window.Data.IsOnDesktop && !window.IsDesktopAttached) SetDesktopMode(window, true);
        }
        window.SetHidden(false);
        window.SaveNow();
        window.Reveal(activate: true);
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
        var oldParent = window.Data.CollectedBy;
        var oldHidden = window.Data.IsHidden;
        window.SetCollectionState(false, null);
        window.SetHidden(false);
        if (!window.SaveNow())
        { window.SetCollectionState(false, oldParent); window.SetHidden(oldHidden); return false; }
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
