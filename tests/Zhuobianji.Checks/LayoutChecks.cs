using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Zhuobianji;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

internal static partial class Program
{
    private static async Task LayoutChecks()
    {
        using var store = new NoteStore(Path.Combine(root, "Data"));
        var appearance = new NoteAppearance();
        var oldLayout = new FlowDocument
        {
            Blocks =
            {
                new Paragraph(new Bold(new Run("原来保存的粗体正文")))
                    { LineHeight = appearance.FontSize * 1.7, Margin = new Thickness(0, 0, 0, 10) },
                new System.Windows.Documents.List(new ListItem(new Paragraph(
                    new Hyperlink(new Run("本地资料")) { NavigateUri = new Uri(Path.Combine(root, "资料.txt")) })
                    { LineHeight = appearance.FontSize * 1.7 }))
                    { Margin = new Thickness(0, 0, 0, 12) }
            },
            FontFamily = new FontFamily(appearance.FontFamily), FontSize = appearance.FontSize,
            LineHeight = appearance.FontSize * 1.7
        };
        var original = new NoteData
        {
            Text = FormattedDocuments.PlainText(oldLayout), Format = NoteFormat.RichText, Appearance = appearance,
            RichText = FormattedDocuments.Save(oldLayout, DataFormats.Xaml)
        };
        store.Save(original);
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var note = controller.Notes.Values.Single();
        note.Left = 140; note.Top = 120;
        var document = note.Editor.Rich.Document;
        Check(note.Editor.TextContent == original.Text && note.Editor.Format == original.Format,
            "旧行距富文本记录恢复后内容和格式不变");
        Check(Math.Abs(document.LineHeight - original.Appearance.FontSize * NoteTheme.LineHeightFactor) < 0.01 &&
            Elements(document).OfType<Block>().All(block => block.ReadLocalValue(Block.LineHeightProperty) == DependencyProperty.UnsetValue),
            "原保存内容的 1.7 倍行高已由当前行高统一接管");
        Check(Elements(document).OfType<System.Windows.Documents.List>().All(list => list.Margin.Bottom == NoteTheme.ListSpacing) &&
            Elements(document).OfType<Hyperlink>().Any(link => link.NavigateUri!.IsFile) && HasBold(document),
            "列表间距收紧，粗体和本地链接保留");
        await Task.Delay(80);
        Capture(note, Path.Combine(root, "restored-note-layout.png"));
        var rtfSource = new FlowDocument(new Paragraph(new Bold(new Run("粗体正文")))
        { Margin = new Thickness(0, 20, 0, 30), LineHeight = 80 })
        { FontFamily = new FontFamily("Times New Roman"), FontSize = 30 };
        var rtf = note.Editor.PreparePaste(new DataObject(DataFormats.Rtf, FormattedDocuments.Save(rtfSource, DataFormats.Rtf)));
        var pastedRtf = LoadPaste(rtf);
        Check(Elements(pastedRtf).Where(element => element is Run).All(element =>
            element.FontFamily.Source == appearance.FontFamily && Math.Abs(element.FontSize - appearance.FontSize) < 0.01) &&
            HasBold(pastedRtf) && FormattedDocuments.PlainText(pastedRtf) == "粗体正文",
            "无链接 RTF 也采用便签字体字号，同时保留粗体与文字");
        var html = note.Editor.PreparePaste(new DataObject(DataFormats.Html,
            "<h2>标题</h2><p style='font-size:30px'>正文 <b>重点</b> <a href='https://example.com/a'>网站</a></p><ul><li>第一项</li><li>第二项</li></ul>"));
        var pastedHtml = LoadPaste(html);
        var paragraphs = Elements(pastedHtml).OfType<Paragraph>().ToArray();
        Check(paragraphs[0].FontSize > appearance.FontSize && paragraphs[1].FontSize == appearance.FontSize &&
            HasBold(pastedHtml) && Elements(pastedHtml).OfType<Hyperlink>().Single().NavigateUri!.Host == "example.com",
            "HTML 正文沿用便签字号，标题、粗体和链接保留");
        Check(note.SaveNow(), "排版调整后正常保存");
        var saved = store.LoadAll().Notes.Single();
        var restored = new NoteEditor();
        restored.Initialize(saved);
        Check(restored.TextContent == original.Text && restored.Rich.Document.LineHeight == document.LineHeight &&
            Elements(restored.Rich.Document).OfType<Hyperlink>().Count() == Elements(document).OfType<Hyperlink>().Count(),
            "重开后正文、链接及新的行距恢复");
        Check(controller.RequestExit(), "局部排版检查完成并退出");
    }
}