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
        var note = controller.Windows.Values.Single();
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

        // 0.8.x 存下的富文本：根 Section 写着浅色主题的字体、字号、行高和正文色，链接带浅色链接色。
        // 深浅色是整个程序的模式（NoteTheme.DarkMode），下面直接切模式，测完切回浅色。
        var light = NoteTheme.For(appearance.Palette, false);
        var dark = NoteTheme.For(appearance.Palette, true);
        var red = Color.FromRgb(0xC9, 0x55, 0x55);
        var legacyXaml = "<Section xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xml:space=\"preserve\" " +
            "LineHeight=\"24.65\" FontFamily=\"Microsoft YaHei UI\" FontSize=\"17\" Foreground=\"#FF303C49\"><Paragraph><Run>旧正文</Run>" +
            "<Run Foreground=\"#FFC95555\">红字</Run><Hyperlink NavigateUri=\"https://example.com/\" Foreground=\"#FF3D75B5\"><Run>链接</Run></Hyperlink></Paragraph></Section>";
        NoteTheme.DarkMode = true;
        var legacy = new NoteEditor();
        legacy.Initialize(new NoteData { Format = NoteFormat.RichText, Text = "旧正文红字链接", RichText = legacyXaml, Appearance = appearance });
        Color Shown(TextElement element) => ((SolidColorBrush)element.Foreground).Color;
        var legacyRuns = Elements(legacy.Rich.Document).OfType<Run>().ToArray();
        Check(Shown(legacyRuns[0]) == dark.Text.Color && Shown(legacyRuns[1]) == red &&
            Shown(Elements(legacy.Rich.Document).OfType<Hyperlink>().Single()) == dark.Link.Color,
            "旧数据写死的浅色主题色在深色模式里跟随主题，自选颜色保留");
        NoteTheme.DarkMode = false;
        legacy.ApplyAppearance(appearance);
        Check(Shown(Elements(legacy.Rich.Document).OfType<Run>().First()) == light.Text.Color &&
            Shown(Elements(legacy.Rich.Document).OfType<Hyperlink>().Single()) == light.Link.Color,
            "切回浅色模式，打开着的便签里正文和链接一起换回浅色");
        var stored = legacy.Snapshot(new NoteData()).RichText!;
        Check(new[] { "303C49", "E4ECF5", "3D75B5", "83B7F0", "FontFamily=", "FontSize=" }.All(value => !stored.Contains(value)) &&
            stored.Contains("C95555"), "富文本存储不写主题色和便签字体字号，自选颜色照存");
        NoteTheme.DarkMode = true;
        var reloaded = new NoteEditor();
        reloaded.Initialize(new NoteData { Format = NoteFormat.RichText, Text = legacy.TextContent, RichText = stored, Appearance = appearance });
        Check(reloaded.TextContent == legacy.TextContent && Shown(Elements(reloaded.Rich.Document).OfType<Run>().First()) == dark.Text.Color &&
            Shown(Elements(reloaded.Rich.Document).OfType<Hyperlink>().Single()) == dark.Link.Color,
            "不带主题值的富文本重开后按当前主题显示正文和链接");
        NoteTheme.DarkMode = false;
        var fromDark = new FlowDocument(new Paragraph(new Run("深色模式里复制的字") { Foreground = dark.Text }))
            { FontFamily = new FontFamily(appearance.FontFamily), FontSize = appearance.FontSize };
        var pastedAcross = LoadPaste(restored.PreparePaste(new DataObject(DataFormats.Rtf, FormattedDocuments.Save(fromDark, DataFormats.Rtf))));
        Check(FormattedDocuments.PlainText(pastedAcross) == "深色模式里复制的字" &&
            Elements(pastedAcross).OfType<Run>().All(run => Shown(run) == light.Text.Color),
            "深色模式下复制的文字在浅色模式里粘贴，改用浅色主题的正文色");

        // Markdown 转成的代码块、引用和表格：底色、引用文字色和边框跟随深浅色，存储后换个模式重开也跟随。
        var blocks = new NoteEditor();
        blocks.Initialize(new NoteData { Format = NoteFormat.Markdown, Text = "> 引用\n\n```\ncode\n```\n\n| a | b |\n| - | - |\n| 1 | 2 |", Appearance = appearance });
        blocks.ChangeFormat(NoteFormat.RichText);
        NoteTheme.DarkMode = true;
        blocks.ApplyAppearance(appearance);
        bool Follows(FlowDocument document, NoteTheme theme) =>
            Elements(document).OfType<Paragraph>().Any(paragraph => paragraph.Background is SolidColorBrush code && code.Color == theme.Code.Color) &&
            Elements(document).OfType<Section>().Any(quote => Shown(quote) == theme.Muted.Color && ((SolidColorBrush)quote.BorderBrush).Color == theme.Border.Color) &&
            Elements(document).OfType<TableCell>().Any() &&
            Elements(document).OfType<TableCell>().All(cell => ((SolidColorBrush)cell.BorderBrush).Color == theme.Border.Color);
        Check(Follows(blocks.Rich.Document, dark), "Markdown 转成的代码块、引用和表格换成深色后跟着变");
        var blocksStored = blocks.Snapshot(new NoteData()).RichText;
        NoteTheme.DarkMode = false;
        var blocksReloaded = new NoteEditor();
        blocksReloaded.Initialize(new NoteData { Format = NoteFormat.RichText, Text = blocks.TextContent, RichText = blocksStored, Appearance = appearance });
        Check(Follows(blocksReloaded.Rich.Document, light) && DocumentText.Markdown(blocksReloaded.Rich.Document).Contains("```"),
            "深色下存的代码块、引用和表格在浅色模式重开后按浅色显示，代码块仍认得出");

        // 网页的黑字白底粘到深色模式的便签：去掉原页面的配色，跟随主题；有颜色的字保留。
        NoteTheme.DarkMode = true;
        var darkPaste = new NoteEditor();
        darkPaste.Initialize(new NoteData { Format = NoteFormat.RichText, Text = "", Appearance = appearance });
        var webColors = LoadPaste(darkPaste.PreparePaste(new DataObject(DataFormats.Html,
            "<p><span style='color: rgb(31, 35, 40); background-color: rgb(255, 255, 255)'>网页黑字</span><span style='color: #C95555'>红字</span></p>")));
        var webRuns = Elements(webColors).OfType<Run>().Where(run => run.Text.Length > 0).ToArray();
        NoteTheme.DarkMode = false;
        Check(webRuns.Length == 2 && Shown(webRuns[0]) == dark.Text.Color && Shown(webRuns[1]) == red &&
            Elements(webColors).All(element => element.Background is null),
            "网页的黑字白底粘到深色模式的便签改为跟随主题，标红的字保留");

        // 便签字体本身是 Consolas 时，只有显式设成等宽字体的部分才算代码。
        var mono = new NoteEditor();
        var monoAppearance = appearance with { FontFamily = "Consolas" };
        mono.Initialize(new NoteData { Format = NoteFormat.RichText, Text = "等宽正文", Appearance = monoAppearance });
        var monoLink = LoadPaste(mono.PreparePaste(new DataObject(DataFormats.UnicodeText, "资料 https://example.com/mono 见上")));
        var monoHtml = LoadPaste(mono.PreparePaste(new DataObject(DataFormats.Html, "<p style='font-size:30px'>网页正文</p>")));
        Check(Elements(monoLink).OfType<Hyperlink>().Count() == 1 &&
            Elements(monoHtml).OfType<Paragraph>().All(paragraph => Math.Abs(paragraph.FontSize - monoAppearance.FontSize) < 0.01),
            "Consolas 字体的便签照常识别粘贴的网址，网页字号照常换成便签字号");
        Check(DocumentText.Markdown(FormattedDocuments.FromMarkdown("正文 `x`", monoAppearance)) == "正文 `x`",
            "Consolas 字体的便签导出 Markdown 时只给代码加反引号");
        Check(controller.RequestExit(), "局部排版检查完成并退出");
    }
}