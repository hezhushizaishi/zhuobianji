using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WeNote.App;
using WeNote.Core;

namespace WeNote.Tests;

internal static partial class Program
{
    private static Rect OnCard(NoteWindow window, FrameworkElement element) =>
        element.TransformToAncestor(window.NoteCard).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static bool Overlaps(Rect first, Rect second)
    {
        first.Intersect(second);
        return !first.IsEmpty && first.Width > 0 && first.Height > 0;
    }

    private static async Task CloseNoteSeed()
    {
        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var note = controller.Notes.Values.Single();
        note.Left = 200; note.Top = 120;
        note.UpdateLayout();
        Check(OnCard(note, note.CloseButton).Top < 42 &&
            OnCard(note, note.DeleteButton).Top > note.NoteCard.ActualHeight / 2 &&
            note.DeleteButton.Content is Wpf.Ui.Controls.SymbolIcon { Symbol: Wpf.Ui.Controls.SymbolRegular.Delete24 },
            "右上角是关闭，底部是 Fluent 垃圾桶图标，两者位置分开");
        var icon = (Wpf.Ui.Controls.SymbolIcon)note.DeleteButton.Content;
        var font = new System.Windows.Media.Typeface(icon.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Check(font.TryGetGlyphTypeface(out var glyphs) && glyphs.CharacterToGlyphMap.ContainsKey((int)Wpf.Ui.Controls.SymbolRegular.Delete24),
            "WPF UI 内嵌图标字体已加载，包含垃圾桶字形");
        note.SaveNow();
        var beforeClose = JsonSerializer.Deserialize<NoteData>(File.ReadAllText(store.GetPath(note.Data.Id)))!;
        note.Editor.InsertPlain("普通便签关闭前刚写的文字");
        note.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(!note.IsVisible && note.Data.IsHidden && controller.Notes.ContainsKey(note.Data.Id) &&
            store.LoadAll().Notes.Single(data => data.Id == note.Data.Id).Text.Contains("关闭前刚写") &&
            !Application.Current.Windows.OfType<Wpf.Ui.Controls.MessageBox>().Any(),
            "普通便签 × 立即保存并隐藏，不弹删除框、不删除数据");
        var closeBackup = JsonSerializer.Deserialize<NoteData>(File.ReadAllText(store.GetPath(note.Data.Id) + ".bak"));
        Check(SameNoteData(closeBackup, beforeClose),
            "关闭将待写正文与隐藏状态一次提交，上一版副本保持关闭前的成功记录");
        controller.ShowAll();
        Check(note.IsVisible && note.Editor.TextContent.Contains("关闭前刚写"),
            "托盘显示全部的链路能找回已关闭的普通便签");

        var main = controller.CreateNote(note);
        main.SetMainItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var first = controller.CreateNote(main);
        first.Editor.InsertPlain("收纳记录 A");
        controller.CollectNote(first);
        var second = controller.CreateNote(main, NoteFormat.Markdown);
        second.Editor.TextContent = "# 收纳记录 B";
        controller.CollectNote(second);
        controller.ShowNote(first);
        controller.ShowNote(second);
        first.Editor.InsertPlain("\n子便签关闭前新增");
        first.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(!first.IsVisible && first.Data.IsHidden && first.Data.CollectedBy == main.Data.Id &&
            main.IsVisible && second.IsVisible &&
            store.LoadAll().Notes.Single(data => data.Id == first.Data.Id).Text.Contains("关闭前新增"),
            "子便签 × 保存并收回标签，保留关系，其他窗口保持可见");
        main.CollectionTabButtons[first.Data.Id].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(first.IsVisible && first.Editor.TextContent.Contains("关闭前新增"),
            "收回后点原标签可重新展开原内容");
        main.UpdateLayout();
        var menuDelete = main.MoreButton.ContextMenu.Items.OfType<MenuItem>()
            .Single(item => Equals(item.Header, "删除这张便签"));
        var labelDelete = main.CollectionTabButtons[first.Data.Id].ContextMenu.Items.OfType<MenuItem>()
            .Single(item => Equals(item.Header, "删除这张"));
        Check(menuDelete.Icon is Wpf.Ui.Controls.SymbolIcon { Symbol: Wpf.Ui.Controls.SymbolRegular.Delete24 } && labelDelete.Icon is Wpf.Ui.Controls.SymbolIcon { Symbol: Wpf.Ui.Controls.SymbolRegular.Delete24 },
            "更多菜单和标签右键的删除项都有同款垃圾桶图标");

        second.Width = second.MinWidth;
        second.UpdateLayout();
        var trashBounds = OnCard(second, second.DeleteButton);
        var statusBounds = OnCard(second, second.SaveStatus);
        Check(new FrameworkElement[] { second.ModeButton, second.LinkButton, second.MarkdownButtons }
            .All(element => !Overlaps(OnCard(second, element), trashBounds) && !Overlaps(OnCard(second, element), statusBounds)),
            "最小宽度 Markdown 便签底部工具、保存状态和垃圾桶不重叠");
        Capture(second, Path.Combine(root, "trash-footer-narrow.png"));
        main.Editor.InsertPlain("主便签关闭前新增");
        second.Editor.InsertPlain("\n主便签关闭前的待保存文字");
        main.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(!main.IsVisible && !first.IsVisible && !second.IsVisible && controller.MainNote == main &&
            controller.CollectedNotes(main).Count() == 2 && first.Data.CollectedBy == main.Data.Id &&
            second.Data.CollectedBy == main.Data.Id && controller.Notes.Count == 4,
            "主便签 × 收起已展开子窗口并隐藏主窗口，收纳关系和数据保留");
        var saved = store.LoadAll().Notes;
        Check(saved.Single(data => data.Id == main.Data.Id).Text.Contains("主便签关闭前新增") &&
            saved.Single(data => data.Id == second.Data.Id).Text.Contains("待保存文字"),
            "关闭主便签前保存主正文和子便签的待写内容");
        controller.ShowAll();
        main.UpdateLayout();
        Check(main.IsVisible && !first.IsVisible && !second.IsVisible && main.CollectionTabButtons.Count == 2,
            "找回主便签时恢复标签，收纳项保持收起");
        Capture(main, Path.Combine(root, "close-and-trash.png"));
        note.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        main.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        File.WriteAllText(Path.Combine(root, "close-main-id.json"), JsonSerializer.Serialize(main.Data.Id));
        Check(controller.RequestExit(), "关闭后仍能正常保存退出");
        await Task.CompletedTask;
    }

    private static async Task CloseNoteReload()
    {
        var mainId = JsonSerializer.Deserialize<Guid>(File.ReadAllText(Path.Combine(root, "close-main-id.json")));
        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        controller.ShowAll();
        var main = controller.Notes[mainId];
        var first = controller.CollectedNotes(main).Single(window => window.Data.Text.Contains("记录 A"));
        Check(controller.Notes.Count == 4 && main.IsVisible && main.Editor.TextContent.Contains("主便签关闭前新增") &&
            controller.CollectedNotes(main).All(child => !child.IsVisible),
            "独立进程找回关闭的便签，数量、正文和收纳关系未丢失");
        main.CollectionTabButtons[first.Data.Id].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(first.IsVisible && first.Editor.TextContent.Contains("子便签关闭前新增"),
            "重开后标签仍能找回已保存的子便签");
        Check(controller.RequestExit(), "恢复关闭数据后正常退出");
        await Task.CompletedTask;
    }
}