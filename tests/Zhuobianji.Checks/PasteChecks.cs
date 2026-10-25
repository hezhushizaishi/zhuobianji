using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using Zhuobianji;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

internal static partial class Program
{
    private static async Task PasteChecks()
    {
        const string sample = @"已更新到 0.6.1，原便签数据保留。双击 [微便签.exe]\(C:/Demo/便签/微便签.exe) 即可。";
        const string result = "已更新到 0.6.1，原便签数据保留。双击 微便签.exe 即可。";
        var appearance = new NoteAppearance();
        var editor = new NoteEditor();
        editor.Initialize(new NoteData());
        string Label(Hyperlink link) => new TextRange(link.ContentStart, link.ContentEnd).Text;
        var pasted = LoadPaste(editor.PreparePaste(new DataObject(DataFormats.UnicodeText, sample)));
        var sampleLink = Elements(pasted).OfType<Hyperlink>().Single();
        Check(FormattedDocuments.PlainText(pasted) == result && Label(sampleLink) == "微便签.exe" && sampleLink.NavigateUri!.IsFile,
            "用户整段复制样例自动变成正文链接，无需手动填写");
        var standard = LoadPaste(editor.PreparePaste(new DataObject(DataFormats.UnicodeText, sample.Replace(@"]\(", "](") + "\n第二行")));
        Check(FormattedDocuments.PlainText(standard) == result + "\n第二行", "标准链接、周围正文和单行换行保留");

        var folder = Path.Combine(root, "测试资料 有空格");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "说明.txt");
        File.WriteAllText(file, "独立测试文件");
        var fileUri = new Uri(file);
        var folderUri = new Uri(folder);
        var web = new Uri("https://example.com/reference?from=note");
        var html = new DataObject(DataFormats.Html, "<p><b>已有粗体</b><br>双击 <b>[说明.txt]</b>\\(" + file.Replace('\\', '/') + ") 即可。<br><a href='" + web.AbsoluteUri + "'>参考网站</a></p>");
        var htmlPaste = LoadPaste(editor.PreparePaste(html));
        Check(HasBold(htmlPaste) && FormattedDocuments.PlainText(htmlPaste) == "已有粗体\n双击 说明.txt 即可。\n参考网站" &&
            Elements(htmlPaste).OfType<Hyperlink>().Count() == 2, "HTML 粘贴跨排版片段识别链接，原粗体和已有超链接保留");
        var rtfSource = FormattedDocuments.FromText(sample, appearance);
        rtfSource.Blocks.FirstBlock!.FontWeight = FontWeights.Bold;
        var rtf = new DataObject(DataFormats.Rtf, FormattedDocuments.Save(rtfSource, DataFormats.Rtf));
        var rtfPaste = LoadPaste(editor.PreparePaste(rtf));
        Check(HasBold(rtfPaste) && FormattedDocuments.PlainText(rtfPaste) == result &&
            Elements(rtfPaste).OfType<Hyperlink>().Single().NavigateUri!.IsFile, "RTF 粘贴也自动处理文字链接并保留格式");
        var barePath = LoadPaste(editor.PreparePaste(new DataObject(DataFormats.UnicodeText, file)));
        Check(FormattedDocuments.PlainText(barePath) == "说明.txt" && Elements(barePath).OfType<Hyperlink>().Single().NavigateUri == fileUri,
            "单独粘贴有空格的本地路径显示文件名");
        var codeSource = "代码：" + (char)96 + sample + (char)96 + "\n\n" + new string((char)96, 3) + "\n" + sample + "\n" + new string((char)96, 3);
        var code = LoadPaste(editor.PreparePaste(new DataObject(DataFormats.UnicodeText, codeSource)));
        Check(!Elements(code).OfType<Hyperlink>().Any() && FormattedDocuments.PlainText(code).Contains(@"[微便签.exe]\("),
            "行内代码和代码块里的链接文字保持原样");
        var missing = new Uri(Path.Combine(folder, "还没有这个文件.txt"));
        var noCheckOnPaste = LoadPaste(editor.PreparePaste(new DataObject(DataFormats.UnicodeText, DocumentText.MarkdownLink("待补资料", missing))));
        Check(Elements(noCheckOnPaste).OfType<Hyperlink>().Single().NavigateUri == missing, "粘贴不检查文件存在性，先保留链接");

        string? selected = null, error = null;
        ProcessStartInfo? launched = null;
        void Open(Uri target) => NoteLinks.Open(target, info => launched = info, path => selected = path, message => error = message);
        Open(fileUri);
        Check(selected == file && launched is null && error is null, "本地文件路由到所在文件夹并选中，不执行文件");
        Open(folderUri);
        Check(launched?.FileName == folder && launched.UseShellExecute, "本地文件夹交给系统直接打开");
        launched = null;
        Open(web);
        Check(launched?.FileName == web.AbsoluteUri && launched.UseShellExecute, "网页完整地址交给默认浏览器");
        Open(missing);
        Check(error == "文件或文件夹不存在，可能已移动或删除。", "目标不存在时给出简短错误");
        error = null;
        NoteLinks.Open(new Uri("https://example.com/"), _ => throw new Win32Exception("测试启动失败"), _ => { }, message => error = message);
        Check(error == "无法打开：测试启动失败", "系统打开失败被接住并显示原因");

        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var rich = controller.Notes.Values.Single();
        rich.Left = 140; rich.Top = 120; rich.Width = 500;
        rich.Editor.TextContent = "原来的正文\n";
        rich.Editor.Rich.CaretPosition = rich.Editor.Rich.Document.ContentEnd;
        rich.Editor.Rich.IsUndoEnabled = false;
        rich.Editor.Rich.IsUndoEnabled = true;
        var before = rich.Editor.TextContent;
        var oldClipboard = Clipboard.GetDataObject();
        try
        {
            Clipboard.SetText(sample);
            rich.Editor.Rich.Focus();
            ApplicationCommands.Paste.Execute(null, rich.Editor.Rich);
            Check(rich.Editor.TextContent.TrimEnd('\n') == (before + result).TrimEnd('\n') && Elements(rich.Editor.Rich.Document).OfType<Hyperlink>().Count() == 1,
                "真实 Ctrl+V 命令自动粘贴整段文字并生成链接");
            ApplicationCommands.Undo.Execute(null, rich.Editor.Rich);
            Check(rich.Editor.TextContent == before && !Elements(rich.Editor.Rich.Document).OfType<Hyperlink>().Any(),
                "一次撤销恢复粘贴前正文");
            ApplicationCommands.Paste.Execute(null, rich.Editor.Rich);
        }
        finally { if (oldClipboard is not null) Clipboard.SetDataObject(oldClipboard, true); }
        await Task.Delay(60);
        var savedRich = rich.Snapshot();
        Check(rich.SaveNow(), "自动生成的正文链接正常保存");
        var restored = new NoteEditor();
        restored.Initialize(store.LoadAll().Notes.Single(note => note.Id == savedRich.Id));
        Check(restored.TextContent == savedRich.Text && Elements(restored.Rich.Document).OfType<Hyperlink>().Single().NavigateUri == sampleLink.NavigateUri,
            "从文件恢复粘贴结果及完整目标");
        Capture(rich, Path.Combine(root, "automatic-rich-paste.png"));
        var richLink = Elements(rich.Editor.Rich.Document).OfType<Hyperlink>().Single();
        Check(((string)richLink.ToolTip).EndsWith("Ctrl+单击打开文件夹") &&
            richLink.ContextMenu.Items.OfType<MenuItem>().Any(item => (string)item.Header == "取消链接"),
            "悬停说明 Ctrl+单击打开文件夹，右键可取消链接");
        richLink.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "取消链接")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(rich.Editor.TextContent == savedRich.Text && !Elements(rich.Editor.Rich.Document).OfType<Hyperlink>().Any(),
            "取消富文本链接只去掉跳转，保留显示文字");

        var markdown = controller.CreateNote(format: NoteFormat.Markdown);
        markdown.Left = 680; markdown.Top = 120; markdown.Width = 500;
        markdown.Editor.TextContent = sample;
        markdown.SetMarkdownPreview(true);
        var mdLink = Elements(markdown.Editor.Preview.Document).OfType<Hyperlink>().Single();
        Check(markdown.Editor.TextContent == sample && Label(mdLink) == "微便签.exe" && mdLink.NavigateUri == sampleLink.NavigateUri,
            "Markdown 同样识别复制链接，预览可跳转且源码保留");
        _ = markdown.Dispatcher.BeginInvoke(() =>
        {
            var dialog = Application.Current.Windows.OfType<LinkWindow>().Single();
            dialog.NameBox.Text = "参考资料";
            dialog.AddressBox.Text = web.AbsoluteUri;
            dialog.SaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        });
        mdLink.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "修改链接")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var edited = Elements(markdown.Editor.Preview.Document).OfType<Hyperlink>().Single();
        Check(edited.NavigateUri == web && Label(edited) == "参考资料" && markdown.Editor.TextContent.Contains(web.AbsoluteUri),
            "Markdown 预览右键修改同步保存源码");
        Capture(markdown, Path.Combine(root, "automatic-markdown-paste.png"));
        edited.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "取消链接")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(!Elements(markdown.Editor.Preview.Document).OfType<Hyperlink>().Any() && markdown.Editor.TextContent.Contains("参考资料"),
            "Markdown 取消链接保留名称并更新源码");
        var mdHtml = markdown.Editor.PreparePaste(new DataObject(DataFormats.Html, "<p>阅读 <a href='" + web.AbsoluteUri + "'>参考网站</a></p>"));
        Check(((string)mdHtml.GetData(DataFormats.UnicodeText)).Contains(DocumentText.MarkdownLink("参考网站", web)),
            "Markdown 粘贴网页已有超链接时保留名称和目标");
        var plain = controller.CreateNote(format: NoteFormat.PlainText);
        var original = new DataObject(DataFormats.UnicodeText, sample);
        Check(ReferenceEquals(plain.Editor.PreparePaste(original), original), "纯文本不自动改写链接文字");
        Check(controller.RequestExit(), "保存退出完成相关检查");
        // 对真实 Windows Shell 只调用一次，确认当前电脑能打开并选中测试文件。
        NoteLinks.OpenFileLocation(file);
        Check(true, "当前 Windows 的打开所在文件夹接口调用成功");
    }
}
