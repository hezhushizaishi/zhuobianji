using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WeNote.App;
using WeNote.Core;

namespace WeNote.Tests;

internal static partial class Program
{
    private static IEnumerable<TextElement> Elements(FlowDocument document)
    {
        for (var p = document.ContentStart; p is not null && p.CompareTo(document.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart && p.GetAdjacentElement(LogicalDirection.Forward) is TextElement element) yield return element;
    }

    private static FlowDocument LoadPaste(IDataObject data)
    {
        var document = new FlowDocument();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes((string)data.GetData(DataFormats.Xaml)!));
        new TextRange(document.ContentStart, document.ContentEnd).Load(stream, DataFormats.Xaml);
        return document;
    }

    private static bool HasBold(FlowDocument document) => Elements(document).Any(e => e.FontWeight == FontWeights.Bold || e.FontWeight == FontWeights.SemiBold);

    private static async Task ModernSeed()
    {
        var store = new NoteStore(Path.Combine(root, "modern", "Data"));
        var legacy = new NoteData { Version = 1, Text = "旧版中文 📝\r\n第二行\r\n", Left = 120, Top = 130, Width = 380, Height = 360 };
        store.Save(legacy);
        var oldJson = JsonNode.Parse(File.ReadAllText(store.GetPath(legacy.Id)))!.AsObject();
        foreach (var field in new[] { "Format", "RichText", "PreviewMarkdown", "Appearance" }) oldJson.Remove(field);
        File.WriteAllText(store.GetPath(legacy.Id), oldJson.ToJsonString());
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var old = controller.Notes.Values.Single();
        Check(old.Editor.TextContent == legacy.Text && old.Snapshot().Text == legacy.Text && old.Editor.Format == NoteFormat.RichText,
            "新版自身 M1/M2 纯文本记录无需导入，原文字和换行完整保留");
        var rich = controller.CreateNote();
        Check(rich.Editor.Format == NoteFormat.RichText, "默认新建富文本便签");
        rich.Left = 180; rich.Top = 150;
        rich.Editor.Rich.Document = FormattedDocuments.FromHtml("<h2>把想法留在这里</h2><p>轻便的 <b>中文富文本 📝</b>，留一点呼吸。</p><ul><li>写下今天的重点</li><li><del>已经完成的事</del></li></ul><p><a href='https://example.com/'>参考链接</a> · <span style='color:#C95555'>重点颜色</span></p>", rich.Data.Appearance);
        var richText = rich.Editor.TextContent;
        Check(richText.Contains("中文富文本 📝") && HasBold(rich.Editor.Rich.Document) && Elements(rich.Editor.Rich.Document).OfType<Hyperlink>().Single().NavigateUri!.Host == "example.com",
            "富文本保留中文、粗体、列表、删除线和链接");
        rich.UpdateLayout();
        rich.FormatBar.Visibility = Visibility.Visible;
        rich.UpdateLayout();
        var boldButton = ((System.Windows.Controls.WrapPanel)rich.FormatBar.Child).Children.OfType<Button>().First();
        Check(ReferenceEquals(boldButton.CommandTarget, rich.Editor.Rich), "格式按钮实际绑定到原生编辑器");
        Capture(rich, Path.Combine(root, "rich-preview.png"));

        var html = new DataObject();
        html.SetData(DataFormats.Html, "Version:1.0\r\nStartHTML:000000\r\n<html><body><!--StartFragment--><p><b>网页中文</b><br>第二行</p><a href='https://example.com/a'>链接</a><!--EndFragment--></body></html>");
        html.SetData(DataFormats.UnicodeText, "网页中文\n第二行");
        var pasteEvent = new DataObjectPastingEventArgs(html, false, DataFormats.Html) { RoutedEvent = DataObject.PastingEvent };
        rich.Editor.Rich.RaiseEvent(pasteEvent);
        Check(pasteEvent.FormatToApply == DataFormats.Xaml, "实际粘贴事件接入自动格式识别");
        var pastedHtml = LoadPaste(pasteEvent.DataObject);
        Check(FormattedDocuments.PlainText(pastedHtml).Contains("网页中文") && HasBold(pastedHtml) && Elements(pastedHtml).OfType<Hyperlink>().Any(),
            "网页剪贴板自动识别 CF_HTML，保留中文、粗体、换行和链接");
        var mdPaste = new DataObject(DataFormats.UnicodeText, "# 标题\n\n**重点**\n\n- 一项\n- 二项");
        Check(HasBold(LoadPaste(rich.Editor.PreparePaste(mdPaste))), "明显 Markdown 粘贴自动转为富文本排版");
        var regular = new DataObject(DataFormats.UnicodeText, "普通文字 - 含标点");
        Check(FormattedDocuments.PlainText(LoadPaste(rich.Editor.PreparePaste(regular))) == "普通文字 - 含标点", "普通文字保留原文，不误触发 Markdown 转换");
        var rtf = new DataObject(DataFormats.Rtf, "{\\rtf1\\ansi original}");
        rtf.SetData(DataFormats.Html, "<p>不应覆盖原生富文本</p>");
        Check(FormattedDocuments.PlainText(LoadPaste(rich.Editor.PreparePaste(rtf))) == "original", "RTF 内容优先保留并统一便签排版");
        var raw = new NoteEditor(); raw.Initialize(new NoteData()); raw.InsertPlain("**按原文保留**");
        Check(raw.TextContent == "**按原文保留**" && !HasBold(raw.Rich.Document), "按原文粘贴保留 Markdown 符号");

        var markdown = controller.CreateNote(format: NoteFormat.Markdown);
        markdown.Left = 660; markdown.Top = 150; markdown.Width = 440; markdown.Height = 570;
        const string source = "# 今天，慢一点\n\n留下 **重点**，也留一点空白。\n\n- 读完一章\n- 写下灵感\n\n> 想法不必一次写完。\n\n```csharp\nvar note = \"中文\";\n```\n\n| 项目 | 状态 |\n| --- | --- |\n| 便签 | 可用 |\n\n[参考链接](https://example.com/)\n";
        markdown.Editor.TextContent = source;
        markdown.SetMarkdownPreview(true);
        var preview = markdown.Editor.Preview.Document;
        Check(markdown.Editor.TextContent == source && HasBold(preview) && Elements(preview).OfType<System.Windows.Documents.Table>().Any() && Elements(preview).OfType<Hyperlink>().Any(),
            "Markdown 标题、列表、引用、代码和表格预览，源码不被改写");
        Capture(markdown, Path.Combine(root, "markdown-preview.png"));
        markdown.SetMarkdownPreview(false);
        Check(markdown.Editor.Source.Text == source, "源码与预览切换保留原始文本");
        markdown.SetMarkdownPreview(true);
        var dark = new NoteAppearance { Palette = "mist", Dark = true, FontFamily = "Microsoft YaHei UI", FontSize = 19, Opacity = 0.9 };
        rich.ApplyAppearance(dark); rich.MarkDirty();
        Check(rich.Editor.TextContent == richText && Elements(rich.Editor.Rich.Document).OfType<Hyperlink>().Any() && controller.SaveDefaults(dark),
            "改外观不改文字或链接，并保存新便签默认外观");
        Check(Elements(rich.Editor.Rich.Document).Any(e => e.Foreground is SolidColorBrush brush && brush.Color == NoteTheme.For(dark).Text.Color),
            "深色模式同步富文本默认文字颜色");
        var next = controller.CreateNote(); next.Editor.TextContent = "新便签继承默认外观";
        Check(next.Data.Appearance == dark && next.Editor.Format == NoteFormat.RichText && store.LoadAll().Errors.Count == 0,
            "新便签继承外观，设置文件不会被当成便签");
        Capture(rich, Path.Combine(root, "dark-preview.png"));
        await Task.Delay(650);
        Check(!rich.IsDirty && !markdown.IsDirty && store.LoadAll().Notes.Single(n => n.Id == rich.Data.Id).RichText is not null,
            "防抖自动保存富文本及 Markdown");
        var saved = File.ReadAllBytes(store.GetPath(rich.Data.Id));
        using (var locked = new FileStream(store.GetPath(rich.Data.Id), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            rich.Editor.Rich.Selection.Select(rich.Editor.Rich.Document.ContentEnd, rich.Editor.Rich.Document.ContentEnd);
            rich.Editor.InsertPlain("退出前的新内容");
            controller.HideNote(rich);
            Check(rich.IsDirty && rich.SaveError is not null && rich.IsVisible && !rich.Data.IsHidden && saved.SequenceEqual(File.ReadAllBytes(store.GetPath(rich.Data.Id))),
                "富文本保存失败保留旧文件和未保存状态，阻止隐藏");
        }
        Check(rich.SaveNow(), "保存失败后可正常重试");
        next.Editor.TextContent += "\n退出前待保存内容";
        var expected = controller.Notes.Values.Select(w => w.Snapshot()).ToArray();
        File.WriteAllText(Path.Combine(root, "modern-expected.json"), JsonSerializer.Serialize(expected));
        Check(controller.RequestExit(), "退出保存两种格式的待写内容");
    }

    private static void ModernReload()
    {
        var expected = JsonSerializer.Deserialize<NoteData[]>(File.ReadAllText(Path.Combine(root, "modern-expected.json")))!;
        using var store = new NoteStore(Path.Combine(root, "modern", "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        Check(controller.Notes.Count == expected.Length && controller.Preferences.Appearance.Dark, "独立进程恢复便签数量及默认外观");
        foreach (var data in expected)
        {
            var window = controller.Notes[data.Id];
            Check(window.Editor.TextContent == data.Text && window.Editor.Format == data.Format && window.Editor.IsPreview == data.PreviewMarkdown &&
                window.Data.Appearance == data.Appearance && window.Opacity == data.Appearance.Opacity && SameBounds(data, window.Snapshot()),
                "独立进程恢复内容、格式、预览状态、外观及位置");
        }
        var rich = controller.Notes.Values.First(w => w.Editor.TextContent.Contains("中文富文本"));
        Check(HasBold(rich.Editor.Rich.Document) && Elements(rich.Editor.Rich.Document).OfType<System.Windows.Documents.List>().Any() &&
            Elements(rich.Editor.Rich.Document).OfType<Hyperlink>().Single().NavigateUri!.Host == "example.com" &&
            Elements(rich.Editor.Rich.Document).OfType<Span>().Any(s => s.TextDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough)),
            "重启保留富文本粗体、列表、删除线和链接目标");
        Check(Elements(rich.Editor.Rich.Document).OfType<Hyperlink>().All(h => ((SolidColorBrush)h.Foreground).Color == NoteTheme.For(rich.Data.Appearance).Link.Color),
            "深色外观及重启后链接仍清晰可读");
        var markdown = controller.Notes.Values.Single(w => w.Editor.Format == NoteFormat.Markdown);
        Check(Elements(markdown.Editor.Preview.Document).OfType<System.Windows.Documents.Table>().Any(), "重启恢复 Markdown 预览排版");
        var created = controller.CreateNote();
        Check(created.Data.Appearance == controller.Preferences.Appearance && created.Editor.Format == NoteFormat.RichText, "重启后新建便签仍使用默认富文本和保存的外观");
        Check(controller.RequestExit(), "新版恢复后正常退出");
    }
}
