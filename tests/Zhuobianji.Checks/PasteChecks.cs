using System;
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
    // 粘贴识别本身（链接、网页、RTF、Markdown、路径、打开路由）在 documents 组；这里是真窗口里的：
    // 真 Ctrl+V 和撤销、存下再读、链接的悬停和右键、链接窗口改 Markdown 链接、真实打开所在文件夹。
    private static async Task PasteChecks()
    {
        const string sample = @"已更新到 0.6.1，原便签数据保留。双击 [微便签.exe]\(C:/Demo/便签/微便签.exe) 即可。";
        const string result = "已更新到 0.6.1，原便签数据保留。双击 微便签.exe 即可。";
        string Label(Hyperlink link) => new TextRange(link.ContentStart, link.ContentEnd).Text;
        var folder = Path.Combine(root, "测试资料 有空格");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "说明.txt");
        File.WriteAllText(file, "独立测试文件");
        var web = new Uri("https://example.com/reference?from=note");

        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var rich = controller.Windows.Values.Single();
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
        var sampleUri = Elements(rich.Editor.Rich.Document).OfType<Hyperlink>().Single().NavigateUri;
        var savedRich = rich.Snapshot();
        Check(rich.SaveNow(), "自动生成的正文链接正常保存");
        var restored = new NoteEditor();
        restored.Initialize(store.LoadAll().Notes.Single(note => note.Id == savedRich.Id));
        Check(restored.TextContent == savedRich.Text && Elements(restored.Rich.Document).OfType<Hyperlink>().Single().NavigateUri == sampleUri,
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
        Check(markdown.Editor.TextContent == sample && Label(mdLink) == "微便签.exe" && mdLink.NavigateUri == sampleUri,
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
        Check(controller.RequestExit(), "保存退出完成相关检查");
        // 对真实 Windows Shell 只调用一次，确认当前电脑能打开并选中测试文件。
        NoteLinks.OpenFileLocation(file);
        Check(true, "当前 Windows 的打开所在文件夹接口调用成功");
    }
}
