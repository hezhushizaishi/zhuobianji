using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;
using Zhuobianji;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

internal static partial class Program
{
    // 地址识别、粘贴识别和文档里的格式互转在 documents 组；这里是真窗口里的：插入链接后的悬停和右键、
    // 不按 Ctrl 单击不打开、链接卡片、链接窗口、格式提示框、切换格式后编辑区和格式键跟着换、重开。
    private static async Task FormattingChecks()
    {
        var folder = Path.Combine(root, "本地资料 有空格");
        Directory.CreateDirectory(folder);
        NoteLinks.TryResolve(folder, out var folderUri);
        var webUri = new Uri("https://example.com/a/long?name=中文");
        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var rich = controller.Windows.Values.Single();
        rich.Left = 140; rich.Top = 120;
        rich.Editor.InsertLink(folderUri, "项目资料");
        rich.Editor.InsertPlain("\n");
        rich.Editor.InsertLink(webUri, "参考网页");
        await Task.Delay(100);
        var links = Elements(rich.Editor.Rich.Document).OfType<Hyperlink>().ToArray();
        Check(links.Length == 2 && links[0].NavigateUri == folderUri && links[1].NavigateUri == webUri &&
            rich.Editor.TextContent.Contains("项目资料") && !rich.Editor.TextContent.Contains(folder),
            "插入短名称链接，正文不堆长路径");
        Check(rich.Editor.TextContent == "项目资料\n参考网页", "连续插入内容保留换行，粘贴后光标移到末尾");
        Check(links[0].ContextMenu.Items.OfType<MenuItem>().Any(item => (string)item.Header == "修改链接") &&
            (string)links[0].ToolTip == folder + "\nCtrl+单击打开文件夹", "链接右键可修改或复制地址，悬停显示完整路径和 Ctrl+单击");
        var opened = 0;
        Uri? openedUri = null;
        rich.Editor.LinkOpener = uri => { opened++; openedUri = uri; };
        foreach (var routed in new[] { UIElement.PreviewMouseLeftButtonDownEvent, UIElement.MouseLeftButtonDownEvent,
            UIElement.PreviewMouseLeftButtonUpEvent, UIElement.MouseLeftButtonUpEvent })
            links[0].RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routed });
        Check(opened == 0, "不按 Ctrl 单击链接只放光标，不会打开");

        // 光标进入链接时出现卡片：完整地址、打开、复制、修改、取消；离开链接收起。
        rich.Activate();
        rich.Editor.Rich.Focus();
        var insideWeb = links[1].ContentStart.GetPositionAtOffset(2)!;
        rich.Editor.Rich.Selection.Select(insideWeb, insideWeb);
        await Task.Delay(150);
        Check(rich.Editor.LinkCard.IsOpen && rich.Editor.LinkCardAddress.Text == "https://example.com/a/long?name=中文" &&
            (string)rich.Editor.LinkCardOpen.Content == "打开", "光标停在链接里，下方卡片显示可读的完整地址和操作");
        Capture(rich, Path.Combine(root, "link-card-note.png"));
        if (rich.Editor.LinkCard.Child is FrameworkElement card) SavePng(card, Path.Combine(root, "link-card.png"));
        var clipboardBefore = Clipboard.GetDataObject();
        try
        {
            rich.Editor.LinkCardCopy.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(Clipboard.GetText() == webUri.AbsoluteUri && (string)rich.Editor.LinkCardCopy.Content == "已复制",
                "卡片一键复制完整地址");
        }
        finally { if (clipboardBefore is not null) Clipboard.SetDataObject(clipboardBefore, true); }
        rich.Editor.LinkCardOpen.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(opened == 1 && openedUri == webUri && !rich.Editor.LinkCard.IsOpen, "卡片打开链接后收起");
        var insideFolder = links[0].ContentStart.GetPositionAtOffset(2)!;
        rich.Editor.Rich.Selection.Select(insideFolder, insideFolder);
        await Task.Delay(150);
        Check(rich.Editor.LinkCard.IsOpen && rich.Editor.LinkCardAddress.Text == folder &&
            (string)rich.Editor.LinkCardOpen.Content == "打开文件夹" && (string)rich.Editor.LinkCardCopy.Content == "复制地址",
            "光标换到另一个链接，卡片跟着换成它的地址");
        rich.Editor.Rich.CaretPosition = links[1].ElementEnd;
        await Task.Delay(150);
        Check(!rich.Editor.LinkCard.IsOpen, "光标离开链接，卡片收起");
        rich.Editor.InsertPlain("\n普通正文");
        Check(rich.SaveNow() && store.LoadAll().Notes.Single().RichText is not null, "带编辑菜单的链接仍正常序列化保存");
        Capture(rich, Path.Combine(root, "short-links.png"));

        var dialog = new LinkWindow(rich.Data.Appearance, folder, "项目资料") { Owner = rich };
        _ = rich.Dispatcher.BeginInvoke(() =>
        {
            CaptureDialog(dialog, Path.Combine(root, "link-dialog.png"));
            dialog.SaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        });
        Check(dialog.ShowDialog() == true && dialog.TargetUri == folderUri && dialog.LinkName == "项目资料",
            "链接窗口可提交名称与本地地址");

        Check(rich.FormatConversionNotice(NoteFormat.PlainText) is not null &&
            rich.FormatConversionNotice(NoteFormat.Markdown) is not null, "可能丢排版的切换给出具体差异提示");

        _ = rich.Dispatcher.BeginInvoke(() =>
        {
            var warning = Application.Current.Windows.OfType<MessageDialog>().Single();
            CaptureDialog(warning, Path.Combine(root, "format-warning.png"));
            warning.Close();
        });
        rich.MarkdownFormatItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Task.Delay(100);
        Check(rich.Editor.Format == NoteFormat.RichText, "实际切换菜单弹出说明，取消时保留原格式");
        rich.ChangeNoteFormat(NoteFormat.Markdown);
        Check(rich.Data.Format == NoteFormat.Markdown && rich.Editor.Source.IsVisible && !rich.Editor.Rich.IsVisible &&
            rich.Editor.TextContent.Contains("file:///") && rich.Editor.TextContent.Contains("项目资料") &&
            rich.Editor.TextContent.Contains(webUri.AbsoluteUri), "富文本转 Markdown 保留链接名称与完整目标");
        rich.SetMarkdownPreview(true);
        Check(Elements(rich.Editor.Preview.Document).OfType<Hyperlink>().Any(link => link.NavigateUri == folderUri),
            "Markdown 预览保留可打开的本地链接");
        Capture(rich, Path.Combine(root, "markdown-links.png"));
        rich.ChangeNoteFormat(NoteFormat.RichText);
        Check(Elements(rich.Editor.Rich.Document).OfType<Hyperlink>().Any(link => link.NavigateUri == folderUri),
            "Markdown 转回富文本保留本地链接");
        var plain = controller.CreateNote(format: NoteFormat.PlainText);
        const string raw = "**保持原文**\n下一行\n";
        plain.Editor.InsertPlain(raw);
        Check(plain.Editor.TextContent == raw && plain.Editor.Source.IsVisible && !plain.Editor.Rich.IsVisible &&
            !plain.Editor.IsPreview && plain.ModeLabel.Text == "纯文本", "纯文本保留符号与换行，不自动转换 Markdown");
        Check(plain.ModeIcon.Symbol == Wpf.Ui.Controls.SymbolRegular.TextT20 && rich.ModeIcon.Symbol == Wpf.Ui.Controls.SymbolRegular.DocumentText20 &&
            plain.PlainFormatItem.Icon is Wpf.Ui.Controls.SymbolIcon { Symbol: Wpf.Ui.Controls.SymbolRegular.TextT20 },
            "格式键和格式菜单用图标区分富文本、Markdown 和纯文本");
        var converted = controller.CreateNote();
        converted.Editor.InsertLink(folderUri, "资料");
        converted.ChangeNoteFormat(NoteFormat.PlainText);
        Check(converted.Editor.TextContent.Contains(folder) && converted.Editor.TextContent.Contains("资料") && converted.Snapshot().RichText is null,
            "转为纯文本去掉样式并保留链接实际地址");
        converted.ChangeNoteFormat(NoteFormat.Markdown);
        Check(converted.Editor.TextContent.Contains(folder), "纯文本转 Markdown 不擅自改写文字");
        await Task.Delay(600);
        var expected = controller.Windows.Values.Select(window => window.Snapshot()).ToArray();
        File.WriteAllText(Path.Combine(root, "expected.json"), JsonSerializer.Serialize(expected));
        Check(controller.RequestExit() && store.LoadAll().Notes.Count == expected.Length &&
            store.LoadAll().Notes.Single(note => note.Id == plain.Data.Id).Text == raw, "三种格式自动保存并正常退出，纯文本尾部换行不丢");
    }

    private static void FormattingReload()
    {
        var expected = JsonSerializer.Deserialize<NoteData[]>(File.ReadAllText(Path.Combine(root, "expected.json")))!;
        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        Check(controller.Book.Notes.Count == expected.Length, "新进程恢复三种格式的便签数量");
        foreach (var data in expected)
        {
            var window = WindowOf(controller, data.Id);
            Check(window.Editor.TextContent == data.Text && window.Editor.Format == data.Format,
                "重开恢复文字与编辑格式");
        }
        var rich = controller.Windows.Values.Single(window => window.Editor.TextContent.Contains("项目资料"));
        var link = Elements(rich.Editor.Rich.Document).OfType<Hyperlink>().First();
        var count = 0;
        rich.Editor.LinkOpener = _ => count++;
        link.RaiseEvent(new RequestNavigateEventArgs(link.NavigateUri!, "") { RoutedEvent = Hyperlink.RequestNavigateEvent });
        Check(count == 1 && link.NavigateUri!.IsFile && link.ContextMenu.Items.OfType<MenuItem>().Any(item => (string)item.Header == "修改链接"),
            "重开后链接导航与右键编辑仍有效");
        Check(controller.RequestExit(), "恢复后正常保存退出");
    }
}