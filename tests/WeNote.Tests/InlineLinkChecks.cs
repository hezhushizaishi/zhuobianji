using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using WeNote.App;
using WeNote.Core;

namespace WeNote.Tests;

internal static partial class Program
{
    private static async Task InlineLinkChecks()
    {
        var appearance = new NoteAppearance();
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
        var emptyLink = Link(FormattedDocuments.FromMarkdown(emptyMd, appearance));
        Check(Label(emptyLink) == "example.com" && emptyLink.NavigateUri == web,
            "空名称 Markdown 链接显示域名，保留完整目标");
        var named = Link(FormattedDocuments.FromMarkdown("[参考资料](" + web.AbsoluteUri + ")", appearance));
        var explicitUrl = Link(FormattedDocuments.FromMarkdown("[" + web.AbsoluteUri + "](" + web.AbsoluteUri + ")", appearance));
        Check(Label(named) == "参考资料" && Label(explicitUrl) == web.AbsoluteUri,
            "已有名称保持原样，名称本身是网址也不擅自缩短");
        var auto = Link(FormattedDocuments.FromMarkdown(web.AbsoluteUri, appearance));
        var angleAuto = Link(FormattedDocuments.FromMarkdown("<" + web.AbsoluteUri + ">", appearance));
        Check(Label(auto) == "example.com" && Label(angleAuto) == "example.com" && auto.NavigateUri == web && angleAuto.NavigateUri == web,
            "Markdown 裸网址和自动链接预览显示域名");
        var emptyFile = Link(FormattedDocuments.FromHtml("<p><a href=\"" + fileUri.AbsoluteUri + "\"></a></p>", appearance));
        var emptyFolder = Link(FormattedDocuments.FromHtml("<p><a href=\"" + folderUri.AbsoluteUri + "\"></a></p>", appearance));
        Check(Label(emptyFile) == "说明.txt" && Label(emptyFolder) == "项目资料",
            "无名称本地链接显示文件名或文件夹名");
        var htmlNamed = Link(FormattedDocuments.FromHtml("<p><a href=\"" + web.AbsoluteUri + "\">阅读资料</a></p>", appearance));
        Check(Label(htmlNamed) == "阅读资料", "网页粘贴保留原链接文字");

        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var rich = controller.Notes.Values.Single();
        rich.Left = 140; rich.Top = 120;
        var pasted = LoadPaste(rich.Editor.PreparePaste(new DataObject(DataFormats.UnicodeText, emptyMd)));
        Check(Label(Link(pasted)) == "example.com", "富文本粘贴空名称 Markdown 链接能识别并显示");
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