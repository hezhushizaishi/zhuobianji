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
    private static async Task LinksChecks()
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
        Check(Label(emptyLink) == "example.com/reference/a?source=note" && emptyLink.NavigateUri == web,
            "空名称 Markdown 链接显示域名和路径，保留完整目标");
        var named = Link(FormattedDocuments.FromMarkdown("[参考资料](" + web.AbsoluteUri + ")", appearance));
        var explicitUrl = Link(FormattedDocuments.FromMarkdown("[" + web.AbsoluteUri + "](" + web.AbsoluteUri + ")", appearance));
        Check(Label(named) == "参考资料" && Label(explicitUrl) == web.AbsoluteUri,
            "已有名称保持原样，名称本身是网址也不擅自缩短");
        var auto = Link(FormattedDocuments.FromMarkdown(web.AbsoluteUri, appearance));
        var angleAuto = Link(FormattedDocuments.FromMarkdown("<" + web.AbsoluteUri + ">", appearance));
        Check(Label(auto) == NoteLinks.ShortName(web) && Label(angleAuto) == NoteLinks.ShortName(web) && auto.NavigateUri == web && angleAuto.NavigateUri == web,
            "Markdown 裸网址和自动链接预览显示域名和路径");
        var emptyFile = Link(FormattedDocuments.FromHtml("<p><a href=\"" + fileUri.AbsoluteUri + "\"></a></p>", appearance));
        var emptyFolder = Link(FormattedDocuments.FromHtml("<p><a href=\"" + folderUri.AbsoluteUri + "\"></a></p>", appearance));
        Check(Label(emptyFile) == "说明.txt" && Label(emptyFolder) == "项目资料",
            "无名称本地链接显示文件名或文件夹名");
        var htmlNamed = Link(FormattedDocuments.FromHtml("<p><a href=\"" + web.AbsoluteUri + "\">阅读资料</a></p>", appearance));
        Check(Label(htmlNamed) == "阅读资料", "网页粘贴保留原链接文字");
        var longName = NoteLinks.ShortName(new Uri("https://www.example.com/" + new string('a', 50) + "/end.html"));
        Check(longName.Length == 40 && longName.StartsWith("example.com/aaa") && longName.Contains('…') && longName.EndsWith("end.html") &&
            NoteLinks.ShortName(new Uri("http://localhost:8080/api/")) == "localhost:8080/api",
            "长网址省略中间、保留结尾，省去 www，保留非默认端口");

        // 0.8.x 存下的自动链接名只有域名：读取时换成域名和路径，自己起的名称不动。
        var repo = new Uri("https://github.com/hezhushizaishi/zhuobianji");
        var legacyXaml = "<Section xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xml:space=\"preserve\"><Paragraph><Run>仓库 </Run>" +
            "<Hyperlink NavigateUri=\"" + repo.AbsoluteUri + "\"><Run>github.com</Run></Hyperlink><Run> 和 </Run>" +
            "<Hyperlink NavigateUri=\"" + repo.AbsoluteUri + "\"><Run>我的项目</Run></Hyperlink></Paragraph></Section>";
        var legacy = new NoteEditor();
        legacy.Initialize(new NoteData { Format = NoteFormat.RichText, Text = "仓库 github.com 和 我的项目", RichText = legacyXaml, Appearance = appearance });
        var legacyLinks = Elements(legacy.Rich.Document).OfType<Hyperlink>().ToArray();
        Check(Label(legacyLinks[0]) == "github.com/hezhushizaishi/zhuobianji" && Label(legacyLinks[1]) == "我的项目" &&
            legacy.TextContent == "仓库 github.com/hezhushizaishi/zhuobianji 和 我的项目",
            "旧版只显示域名的链接读取后显示域名和路径，自己起的名称不变");

        // 复制成纯文字：自动名称换成完整地址，自己起的名称后面括上地址；只选中半个链接时照原样。
        legacy.Rich.SelectAll();
        var copied = new DataObject(DataFormats.UnicodeText, legacy.Rich.Selection.Text);
        legacy.Rich.RaiseEvent(new DataObjectCopyingEventArgs(copied, false));
        Check(((string)copied.GetData(DataFormats.UnicodeText)!).TrimEnd() == "仓库 " + repo.AbsoluteUri + " 和 我的项目 (" + repo.AbsoluteUri + ")",
            "复制带链接的正文，纯文字里带完整地址");
        legacy.Rich.Selection.Select(legacyLinks[1].ContentStart.GetPositionAtOffset(2)!, legacyLinks[1].ContentEnd);
        var partial = new DataObject(DataFormats.UnicodeText, legacy.Rich.Selection.Text);
        legacy.Rich.RaiseEvent(new DataObjectCopyingEventArgs(partial, false));
        Check((string)partial.GetData(DataFormats.UnicodeText)! == "的项目", "只复制半个链接名称时不附加地址");

        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var rich = controller.Notes.Values.Single();
        rich.Left = 140; rich.Top = 120;
        var pasted = LoadPaste(rich.Editor.PreparePaste(new DataObject(DataFormats.UnicodeText, emptyMd)));
        Check(Label(Link(pasted)) == NoteLinks.ShortName(web), "富文本粘贴空名称 Markdown 链接能识别并显示");
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