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
using WeNote.App;
using WeNote.Core;

namespace WeNote.Tests;

internal static partial class Program
{
    private static async Task PolishChecks()
    {
        var folder = Path.Combine(root, "本地资料 有空格");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "说明.txt");
        File.WriteAllText(file, "测试文档");
        Check(NoteLinks.TryResolve(folder, out var folderUri) && folderUri.IsFile && NoteLinks.ShortName(folderUri) == "本地资料 有空格",
            "中文带空格的本地文件夹识别为 file 链接和短名称");
        Check(NoteLinks.TryResolve(file, out var fileUri) && NoteLinks.ShortName(fileUri) == "说明.txt" &&
            NoteLinks.LaunchInfo(folderUri).FileName == folder &&
            NoteLinks.LaunchInfo(folderUri).UseShellExecute, "文件夹交给系统打开，目标路径完整保留");
        var webUri = new Uri("https://example.com/a/long?name=中文");
        Check(NoteLinks.ShortName(webUri) == "example.com" && NoteLinks.LaunchInfo(webUri).FileName == webUri.AbsoluteUri,
            "网页显示域名，仍将完整网址交给默认浏览器");
        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var rich = controller.Notes.Values.Single();
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
            (string)links[0].ToolTip == NoteLinks.Tooltip(folderUri), "链接右键可修改或复制地址，悬停显示完整路径");
        var opened = 0;
        Uri? openedUri = null;
        rich.Editor.LinkOpener = uri => { opened++; openedUri = uri; };
        var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = links[0] };
        var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Source = links[0] };
        rich.Editor.Rich.RaiseEvent(down);
        rich.Editor.Rich.RaiseEvent(up);
        Check(opened == 1 && openedUri == folderUri && down.Handled && up.Handled,
            "实际鼠标事件单击链接只打开一次，不需要 Ctrl");
        rich.Editor.InsertPlain("\n普通正文");
        Check(rich.SaveNow() && store.LoadAll().Notes.Single().RichText is not null, "带编辑菜单的链接仍正常序列化保存");
        Capture(rich, Path.Combine(root, "short-links.png"));

        var dialog = new LinkWindow(rich.Data.Appearance, folder, "项目资料") { Owner = rich };
        _ = rich.Dispatcher.BeginInvoke(() =>
        {
            dialog.UpdateLayout();
            Capture(dialog, Path.Combine(root, "link-dialog.png"));
            dialog.SaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        });
        Check(dialog.ShowDialog() == true && dialog.TargetUri == folderUri && dialog.LinkName == "项目资料",
            "成熟主题的链接窗口可提交名称与本地地址");

        var pastedPath = LoadPaste(rich.Editor.PreparePaste(new DataObject(DataFormats.UnicodeText, folder)));
        Check(Elements(pastedPath).OfType<Hyperlink>().Single().NavigateUri == folderUri &&
            FormattedDocuments.PlainText(pastedPath) == "本地资料 有空格", "粘贴本地路径自动成为短名称链接");
        var pastedWeb = LoadPaste(rich.Editor.PreparePaste(new DataObject(DataFormats.UnicodeText, webUri.AbsoluteUri)));
        Check(FormattedDocuments.PlainText(pastedWeb) == "example.com", "粘贴裸网址自动显示域名");
        var fileMd = LoadPaste(rich.Editor.PreparePaste(new DataObject(DataFormats.UnicodeText, DocumentText.MarkdownLink("资料", folderUri))));
        Check(Elements(fileMd).OfType<Hyperlink>().Single().NavigateUri == folderUri, "粘贴只有本地链接的 Markdown 也能识别");
        var pastedMd = LoadPaste(rich.Editor.PreparePaste(new DataObject(DataFormats.UnicodeText, "# 标题\n\n**重点**\n\n- 第一项")));
        Check(HasBold(pastedMd) && Elements(pastedMd).OfType<System.Windows.Documents.List>().Any(),
            "富文本自动识别 Markdown 标题、粗体和列表");
        var forceMd = controller.CreateNote();
        forceMd.Editor.InsertMarkdown("这是 *斜体*");
        Check(Elements(forceMd.Editor.Rich.Document).Any(element => element.FontStyle == FontStyles.Italic),
            "显式按 Markdown 粘贴可处理不够明显的语法");
        Check(((Paragraph)forceMd.Editor.Rich.Document.Blocks.FirstBlock!).Margin.Bottom == 3,
            "富文本新段落间距已收紧");
        Check(rich.FormatConversionNotice(NoteFormat.PlainText) is not null &&
            rich.FormatConversionNotice(NoteFormat.Markdown) is not null, "可能丢排版的切换给出具体差异提示");

        _ = rich.Dispatcher.BeginInvoke(() =>
        {
            var warning = Application.Current.Windows.OfType<Wpf.Ui.Controls.MessageBox>().Single();
            warning.UpdateLayout();
            Capture(warning, Path.Combine(root, "format-warning.png"));
            ((Window)warning).Close();
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
            !plain.Editor.IsPreview && (string)plain.ModeButton.Content == "纯文本 ▾", "纯文本保留符号与换行，不自动转换 Markdown");
        var converted = controller.CreateNote();
        converted.Editor.InsertLink(folderUri, "资料");
        converted.ChangeNoteFormat(NoteFormat.PlainText);
        Check(converted.Editor.TextContent.Contains(folder) && converted.Editor.TextContent.Contains("资料") && converted.Snapshot().RichText is null,
            "转为纯文本去掉样式并保留链接实际地址");
        converted.ChangeNoteFormat(NoteFormat.Markdown);
        Check(converted.Editor.TextContent.Contains(folder), "纯文本转 Markdown 不擅自改写文字");
        forceMd.ChangeNoteFormat(NoteFormat.Markdown);
        Check(forceMd.Editor.TextContent.Contains("*斜体*"), "富文本导出常见斜体 Markdown 语法");
        await Task.Delay(600);
        var expected = controller.Notes.Values.Select(window => window.Snapshot()).ToArray();
        File.WriteAllText(Path.Combine(root, "expected.json"), JsonSerializer.Serialize(expected));
        Check(controller.RequestExit() && store.LoadAll().Notes.Count == expected.Length &&
            store.LoadAll().Notes.Single(note => note.Id == plain.Data.Id).Text == raw, "三种格式自动保存并正常退出，纯文本尾部换行不丢");
    }

    private static void PolishReload()
    {
        var expected = JsonSerializer.Deserialize<NoteData[]>(File.ReadAllText(Path.Combine(root, "expected.json")))!;
        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        Check(controller.Notes.Count == expected.Length, "新进程恢复三种格式的便签数量");
        foreach (var data in expected)
        {
            var window = controller.Notes[data.Id];
            Check(window.Editor.TextContent == data.Text && window.Editor.Format == data.Format,
                "重开恢复文字与编辑格式");
        }
        var rich = controller.Notes.Values.Single(window => window.Editor.TextContent.Contains("项目资料"));
        var link = Elements(rich.Editor.Rich.Document).OfType<Hyperlink>().First();
        var count = 0;
        rich.Editor.LinkOpener = _ => count++;
        link.RaiseEvent(new RequestNavigateEventArgs(link.NavigateUri!, "") { RoutedEvent = Hyperlink.RequestNavigateEvent });
        Check(count == 1 && link.NavigateUri!.IsFile && link.ContextMenu.Items.OfType<MenuItem>().Any(item => (string)item.Header == "修改链接"),
            "重开后链接导航与右键编辑仍有效");
        Check(controller.RequestExit(), "恢复后正常保存退出");
    }
}