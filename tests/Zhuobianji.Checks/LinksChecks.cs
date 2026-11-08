using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using Zhuobianji;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

internal static partial class Program
{
    // 链接名称的规则（Markdown、网页、本地路径、长网址、旧名称、复制）在 documents 组；这里是真窗口里的：
    // 底部插入链接和链接窗口、预览里插入、只改地址时名称跟着变、纯文本没有入口、存下再读、截图。
    private static async Task LinksChecks()
    {
        var web = new Uri("https://example.com/reference/a?source=note");
        var folder = Path.Combine(root, "项目资料");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "说明.txt");
        File.WriteAllText(file, "独立测试文件");
        var fileUri = new Uri(file);
        var folderUri = new Uri(folder);
        string Label(Hyperlink link) => new TextRange(link.ContentStart, link.ContentEnd).Text;
        Hyperlink Link(FlowDocument document) => Elements(document).OfType<Hyperlink>().Single();

        var emptyMd = "[](" + web.AbsoluteUri + ")";

        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var rich = controller.Windows.Values.Single();
        rich.Left = 140; rich.Top = 120;
        var word = new Run("项目资料");
        rich.Editor.Rich.Document = new FlowDocument(new Paragraph(word));
        rich.Editor.Rich.Selection.Select(word.ContentStart, word.ContentEnd);
        var richPrefilled = false;
        _ = rich.Dispatcher.BeginInvoke(() =>
        {
            var dialog = Application.Current.Windows.OfType<LinkWindow>().Single();
            richPrefilled = dialog.NameBox.Text == "项目资料";
            dialog.AddressBox.Text = folder;
            dialog.SaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        });
        rich.LinkButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var richLink = Link(rich.Editor.Rich.Document);
        Check(richPrefilled && Label(richLink) == "项目资料" && richLink.NavigateUri == folderUri,
            "富文本选中文字后从底部插入链接，保留文字并嵌入正文");

        var markdown = controller.CreateNote(format: NoteFormat.Markdown);
        markdown.Left = 620; markdown.Top = 120;
        markdown.Editor.TextContent = "参考资料";
        markdown.Editor.Source.SelectAll();
        var markdownPrefilled = false;
        _ = markdown.Dispatcher.BeginInvoke(() =>
        {
            var dialog = Application.Current.Windows.OfType<LinkWindow>().Single();
            markdownPrefilled = dialog.NameBox.Text == "参考资料";
            dialog.AddressBox.Text = web.AbsoluteUri;
            dialog.SaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        });
        markdown.LinkButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(markdownPrefilled && markdown.Editor.TextContent == DocumentText.MarkdownLink("参考资料", web),
            "Markdown 共用插入入口，选中文字成为标准链接名称");
        markdown.Editor.Source.CaretIndex = markdown.Editor.TextContent.Length;
        markdown.Editor.Source.SelectionLength = 0;
        markdown.SetMarkdownPreview(true);
        _ = markdown.Dispatcher.BeginInvoke(() =>
        {
            var dialog = Application.Current.Windows.OfType<LinkWindow>().Single();
            dialog.AddressBox.Text = file;
            dialog.NameBox.Text = "";
            dialog.SaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        });
        markdown.LinkButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(!markdown.Editor.IsPreview && markdown.Editor.TextContent.EndsWith(DocumentText.MarkdownLink("说明.txt", fileUri)),
            "预览中插入返回源码，名称留空时自动使用文件名");
        var source = markdown.Editor.TextContent + "\n" + emptyMd + "\n" + web.AbsoluteUri;
        markdown.Editor.TextContent = source;
        markdown.SetMarkdownPreview(true);
        Check(markdown.Editor.TextContent == source && Elements(markdown.Editor.Preview.Document).OfType<Hyperlink>().Count() == 4,
            "短名称仅影响 Markdown 预览，不改写原始源码");

        var autoNote = controller.CreateNote();
        autoNote.Left = 140; autoNote.Top = 520;
        autoNote.Editor.InsertLink(web);
        var autoLink = Link(autoNote.Editor.Rich.Document);
        var nextWeb = new Uri("https://example.org/next");
        _ = autoNote.Dispatcher.BeginInvoke(() =>
        {
            var dialog = Application.Current.Windows.OfType<LinkWindow>().Single();
            dialog.AddressBox.Text = nextWeb.AbsoluteUri;
            dialog.SaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        });
        autoLink.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "修改链接")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(autoLink.NavigateUri == nextWeb && Label(autoLink) == "example.org/next" &&
            ((string)autoLink.ToolTip).StartsWith(nextWeb.AbsoluteUri), "只改地址时，自动起的名称和悬停地址跟着更新");

        var plain = controller.CreateNote(format: NoteFormat.PlainText);
        plain.Editor.InsertPlain(file);
        Check(plain.LinkButton.Visibility == Visibility.Collapsed && plain.Editor.TextContent == file,
            "纯文本隐藏插入链接入口，保留实际路径");
        Check(rich.SaveNow() && markdown.SaveNow(), "正文内链接可以正常保存");
        var saved = store.LoadAll().Notes;
        var restoredRich = new NoteEditor();
        restoredRich.Initialize(saved.Single(note => note.Id == rich.Data.Id));
        var restoredMd = new NoteEditor();
        restoredMd.Initialize(saved.Single(note => note.Id == markdown.Data.Id));
        Check(Link(restoredRich.Rich.Document).NavigateUri == folderUri && Label(Link(restoredRich.Rich.Document)) == "项目资料" &&
            restoredMd.TextContent == source && Elements(restoredMd.Preview.Document).OfType<Hyperlink>().Count() == 4,
            "从文件恢复名称、目标和 Markdown 源码");
        await Task.Delay(100);
        Capture(rich, Path.Combine(root, "rich-inline-links.png"));
        Capture(markdown, Path.Combine(root, "markdown-inline-links.png"));
        Check(controller.RequestExit(), "完成局部检查并保存退出");
    }
}