using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Zhuobianji;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

// 编辑器和文档转换：用单独的编辑器测粘贴识别、格式互转、链接名称、排版和主题色跟随，不开窗口。
// 要真窗口的（真 Ctrl+V 和撤销、链接窗口、链接卡片、格式提示框、截图、重开）留在 paste、links、formatting、editing 组。
internal static partial class Program
{
    private static string LinkLabel(Hyperlink link) => new TextRange(link.ContentStart, link.ContentEnd).Text;
    private static Hyperlink OnlyLink(FlowDocument document) => Elements(document).OfType<Hyperlink>().Single();

    private static NoteEditor Editor(NoteFormat format = NoteFormat.RichText, NoteAppearance? appearance = null)
    {
        var editor = new NoteEditor();
        editor.Initialize(new NoteData { Format = format, Appearance = appearance ?? new NoteAppearance() });
        return editor;
    }

    private static void DocumentsChecks()
    {
        PasteRecognition();
        LinkNames();
        FormatConversion();
        ReadingLayout();
    }

    // 粘贴识别：文字里的链接、网页、RTF、Markdown、本地路径，以及打开链接的路由。
    private static void PasteRecognition()
    {
        const string sample = @"已更新到 0.6.1，原便签数据保留。双击 [微便签.exe]\(C:/Demo/便签/微便签.exe) 即可。";
        const string result = "已更新到 0.6.1，原便签数据保留。双击 微便签.exe 即可。";
        var appearance = new NoteAppearance();
        var editor = Editor();
        var pasted = LoadPaste(editor.PreparePaste(new DataObject(DataFormats.UnicodeText, sample)));
        var sampleLink = OnlyLink(pasted);
        Check(FormattedDocuments.PlainText(pasted) == result && LinkLabel(sampleLink) == "微便签.exe" && sampleLink.NavigateUri!.IsFile,
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
        var rtfPaste = LoadPaste(editor.PreparePaste(new DataObject(DataFormats.Rtf, FormattedDocuments.Save(rtfSource, DataFormats.Rtf))));
        Check(HasBold(rtfPaste) && FormattedDocuments.PlainText(rtfPaste) == result && OnlyLink(rtfPaste).NavigateUri!.IsFile,
            "RTF 粘贴也自动处理文字链接并保留格式");
        var barePath = LoadPaste(editor.PreparePaste(new DataObject(DataFormats.UnicodeText, file)));
        Check(FormattedDocuments.PlainText(barePath) == "说明.txt" && OnlyLink(barePath).NavigateUri == fileUri,
            "单独粘贴有空格的本地路径显示文件名");
        var codeSource = "代码：" + (char)96 + sample + (char)96 + "\n\n" + new string((char)96, 3) + "\n" + sample + "\n" + new string((char)96, 3);
        var code = LoadPaste(editor.PreparePaste(new DataObject(DataFormats.UnicodeText, codeSource)));
        Check(!Elements(code).OfType<Hyperlink>().Any() && FormattedDocuments.PlainText(code).Contains(@"[微便签.exe]\("),
            "行内代码和代码块里的链接文字保持原样");
        var missing = new Uri(Path.Combine(folder, "还没有这个文件.txt"));
        var noCheckOnPaste = LoadPaste(editor.PreparePaste(new DataObject(DataFormats.UnicodeText, DocumentText.MarkdownLink("待补资料", missing))));
        Check(OnlyLink(noCheckOnPaste).NavigateUri == missing, "粘贴不检查文件存在性，先保留链接");

        // 网页剪贴板（CF_HTML）走真实的粘贴事件；明显的 Markdown 转成排版，普通文字和 RTF 照原样。
        var htmlClip = new DataObject();
        htmlClip.SetData(DataFormats.Html, "Version:1.0\r\nStartHTML:000000\r\n<html><body><!--StartFragment--><p><b>网页中文</b><br>第二行</p><a href='https://example.com/a'>链接</a><!--EndFragment--></body></html>");
        htmlClip.SetData(DataFormats.UnicodeText, "网页中文\n第二行");
        var pasteEvent = new DataObjectPastingEventArgs(htmlClip, false, DataFormats.Html) { RoutedEvent = DataObject.PastingEvent };
        editor.Rich.RaiseEvent(pasteEvent);
        Check(pasteEvent.FormatToApply == DataFormats.Xaml, "实际粘贴事件接入自动格式识别");
        var pastedHtml = LoadPaste(pasteEvent.DataObject);
        Check(FormattedDocuments.PlainText(pastedHtml).Contains("网页中文") && HasBold(pastedHtml) && Elements(pastedHtml).OfType<Hyperlink>().Any(),
            "网页剪贴板自动识别 CF_HTML，保留中文、粗体、换行和链接");
        Check(HasBold(LoadPaste(editor.PreparePaste(new DataObject(DataFormats.UnicodeText, "# 标题\n\n**重点**\n\n- 一项\n- 二项")))),
            "明显 Markdown 粘贴自动转为富文本排版");
        Check(FormattedDocuments.PlainText(LoadPaste(editor.PreparePaste(new DataObject(DataFormats.UnicodeText, "普通文字 - 含标点")))) == "普通文字 - 含标点",
            "普通文字保留原文，不误触发 Markdown 转换");
        var rtf = new DataObject(DataFormats.Rtf, "{\\rtf1\\ansi original}");
        rtf.SetData(DataFormats.Html, "<p>不应覆盖原生富文本</p>");
        Check(FormattedDocuments.PlainText(LoadPaste(editor.PreparePaste(rtf))) == "original", "RTF 内容优先保留并统一便签排版");
        var raw = Editor();
        raw.InsertPlain("**按原文保留**");
        Check(raw.TextContent == "**按原文保留**" && !HasBold(raw.Rich.Document), "按原文粘贴保留 Markdown 符号");

        var markdown = Editor(NoteFormat.Markdown);
        var mdHtml = markdown.PreparePaste(new DataObject(DataFormats.Html, "<p>阅读 <a href='" + web.AbsoluteUri + "'>参考网站</a></p>"));
        Check(((string)mdHtml.GetData(DataFormats.UnicodeText)).Contains(DocumentText.MarkdownLink("参考网站", web)),
            "Markdown 粘贴网页已有超链接时保留名称和目标");
        var original = new DataObject(DataFormats.UnicodeText, sample);
        Check(ReferenceEquals(Editor(NoteFormat.PlainText).PreparePaste(original), original), "纯文本不自动改写链接文字");

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
    }

    // 链接显示的名称：Markdown、网页、本地路径、长网址、0.8.x 的旧名称，以及复制成纯文字。
    private static void LinkNames()
    {
        var appearance = new NoteAppearance();
        var web = new Uri("https://example.com/reference/a?source=note");
        var folder = Path.Combine(root, "项目资料");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "说明.txt");
        File.WriteAllText(file, "独立测试文件");
        var fileUri = new Uri(file);
        var folderUri = new Uri(folder);

        var emptyMd = "[](" + web.AbsoluteUri + ")";
        var emptyLink = OnlyLink(FormattedDocuments.FromMarkdown(emptyMd, appearance));
        Check(LinkLabel(emptyLink) == "example.com/reference/a?source=note" && emptyLink.NavigateUri == web,
            "空名称 Markdown 链接显示域名和路径，保留完整目标");
        Check(LinkLabel(OnlyLink(LoadPaste(Editor().PreparePaste(new DataObject(DataFormats.UnicodeText, emptyMd))))) == NoteLinks.ShortName(web),
            "富文本粘贴空名称 Markdown 链接能识别并显示");
        var named = OnlyLink(FormattedDocuments.FromMarkdown("[参考资料](" + web.AbsoluteUri + ")", appearance));
        var explicitUrl = OnlyLink(FormattedDocuments.FromMarkdown("[" + web.AbsoluteUri + "](" + web.AbsoluteUri + ")", appearance));
        Check(LinkLabel(named) == "参考资料" && LinkLabel(explicitUrl) == web.AbsoluteUri,
            "已有名称保持原样，名称本身是网址也不擅自缩短");
        var auto = OnlyLink(FormattedDocuments.FromMarkdown(web.AbsoluteUri, appearance));
        var angleAuto = OnlyLink(FormattedDocuments.FromMarkdown("<" + web.AbsoluteUri + ">", appearance));
        Check(LinkLabel(auto) == NoteLinks.ShortName(web) && LinkLabel(angleAuto) == NoteLinks.ShortName(web) && auto.NavigateUri == web && angleAuto.NavigateUri == web,
            "Markdown 裸网址和自动链接预览显示域名和路径");
        var emptyFile = OnlyLink(FormattedDocuments.FromHtml("<p><a href=\"" + fileUri.AbsoluteUri + "\"></a></p>", appearance));
        var emptyFolder = OnlyLink(FormattedDocuments.FromHtml("<p><a href=\"" + folderUri.AbsoluteUri + "\"></a></p>", appearance));
        Check(LinkLabel(emptyFile) == "说明.txt" && LinkLabel(emptyFolder) == "项目资料",
            "无名称本地链接显示文件名或文件夹名");
        Check(LinkLabel(OnlyLink(FormattedDocuments.FromHtml("<p><a href=\"" + web.AbsoluteUri + "\">阅读资料</a></p>", appearance))) == "阅读资料",
            "网页粘贴保留原链接文字");
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
        Check(LinkLabel(legacyLinks[0]) == "github.com/hezhushizaishi/zhuobianji" && LinkLabel(legacyLinks[1]) == "我的项目" &&
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
    }

    // 地址识别和格式互转里只看文档本身的：路径和网址变链接、显式按 Markdown 插入、富文本导出 Markdown。
    // 界面上的切换、提示框和链接卡片在 formatting 组。
    private static void FormatConversion()
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
        Check(NoteLinks.ShortName(webUri) == "example.com/a/long?name=中文" && NoteLinks.LaunchInfo(webUri).FileName == webUri.AbsoluteUri,
            "网页显示域名和路径，仍将完整网址交给默认浏览器");
        var editor = Editor();
        var pastedPath = LoadPaste(editor.PreparePaste(new DataObject(DataFormats.UnicodeText, folder)));
        Check(OnlyLink(pastedPath).NavigateUri == folderUri && FormattedDocuments.PlainText(pastedPath) == "本地资料 有空格",
            "粘贴本地路径自动成为短名称链接");
        Check(FormattedDocuments.PlainText(LoadPaste(editor.PreparePaste(new DataObject(DataFormats.UnicodeText, webUri.AbsoluteUri)))) == "example.com/a/long?name=中文",
            "粘贴裸网址自动显示域名和路径");
        Check(OnlyLink(LoadPaste(editor.PreparePaste(new DataObject(DataFormats.UnicodeText, DocumentText.MarkdownLink("资料", folderUri))))).NavigateUri == folderUri,
            "粘贴只有本地链接的 Markdown 也能识别");
        var pastedMd = LoadPaste(editor.PreparePaste(new DataObject(DataFormats.UnicodeText, "# 标题\n\n**重点**\n\n- 第一项")));
        Check(HasBold(pastedMd) && Elements(pastedMd).OfType<System.Windows.Documents.List>().Any(),
            "富文本自动识别 Markdown 标题、粗体和列表");
        var forceMd = Editor();
        forceMd.InsertMarkdown("这是 *斜体*");
        Check(Elements(forceMd.Rich.Document).Any(element => element.FontStyle == FontStyles.Italic),
            "显式按 Markdown 粘贴可处理不够明显的语法");
        Check(((Paragraph)forceMd.Rich.Document.Blocks.FirstBlock!).Margin.Bottom == 3, "富文本新段落间距已收紧");
        forceMd.ChangeFormat(NoteFormat.Markdown);
        Check(forceMd.TextContent.Contains("*斜体*"), "富文本导出常见斜体 Markdown 语法");
    }

    // 排版：旧行距、粘贴的字体字号、富文本存储不写主题值、代码块和引用跟随深浅色、网页配色、Consolas 字体便签。
    private static void ReadingLayout()
    {
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
        var note = new NoteEditor();
        note.Initialize(original);
        var document = note.Rich.Document;
        Check(note.TextContent == original.Text && note.Format == original.Format, "旧行距富文本记录恢复后内容和格式不变");
        Check(Math.Abs(document.LineHeight - original.Appearance.FontSize * NoteTheme.LineHeightFactor) < 0.01 &&
            Elements(document).OfType<Block>().All(block => block.ReadLocalValue(Block.LineHeightProperty) == DependencyProperty.UnsetValue),
            "原保存内容的 1.7 倍行高已由当前行高统一接管");
        Check(Elements(document).OfType<System.Windows.Documents.List>().All(list => list.Margin.Bottom == NoteTheme.ListSpacing) &&
            Elements(document).OfType<Hyperlink>().Any(link => link.NavigateUri!.IsFile) && HasBold(document),
            "列表间距收紧，粗体和本地链接保留");
        var rtfSource = new FlowDocument(new Paragraph(new Bold(new Run("粗体正文"))) { Margin = new Thickness(0, 20, 0, 30), LineHeight = 80 })
            { FontFamily = new FontFamily("Times New Roman"), FontSize = 30 };
        var pastedRtf = LoadPaste(note.PreparePaste(new DataObject(DataFormats.Rtf, FormattedDocuments.Save(rtfSource, DataFormats.Rtf))));
        Check(Elements(pastedRtf).Where(element => element is Run).All(element =>
            element.FontFamily.Source == appearance.FontFamily && Math.Abs(element.FontSize - appearance.FontSize) < 0.01) &&
            HasBold(pastedRtf) && FormattedDocuments.PlainText(pastedRtf) == "粗体正文",
            "无链接 RTF 也采用便签字体字号，同时保留粗体与文字");
        var pastedHtml = LoadPaste(note.PreparePaste(new DataObject(DataFormats.Html,
            "<h2>标题</h2><p style='font-size:30px'>正文 <b>重点</b> <a href='https://example.com/a'>网站</a></p><ul><li>第一项</li><li>第二项</li></ul>")));
        var paragraphs = Elements(pastedHtml).OfType<Paragraph>().ToArray();
        Check(paragraphs[0].FontSize > appearance.FontSize && paragraphs[1].FontSize == appearance.FontSize &&
            HasBold(pastedHtml) && OnlyLink(pastedHtml).NavigateUri!.Host == "example.com",
            "HTML 正文沿用便签字号，标题、粗体和链接保留");
        var restored = new NoteEditor();
        restored.Initialize(note.Snapshot(original));
        Check(restored.TextContent == original.Text && restored.Rich.Document.LineHeight == document.LineHeight &&
            Elements(restored.Rich.Document).OfType<Hyperlink>().Count() == Elements(document).OfType<Hyperlink>().Count(),
            "存下再读入，正文、链接及新的行距恢复");

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
        Check(Shown(legacyRuns[0]) == dark.Text.Color && Shown(legacyRuns[1]) == red && Shown(OnlyLink(legacy.Rich.Document)) == dark.Link.Color,
            "旧数据写死的浅色主题色在深色模式里跟随主题，自选颜色保留");
        NoteTheme.DarkMode = false;
        legacy.ApplyAppearance(appearance);
        Check(Shown(Elements(legacy.Rich.Document).OfType<Run>().First()) == light.Text.Color && Shown(OnlyLink(legacy.Rich.Document)) == light.Link.Color,
            "切回浅色模式，打开着的便签里正文和链接一起换回浅色");
        var stored = legacy.Snapshot(new NoteData()).RichText!;
        Check(new[] { "303C49", "E4ECF5", "3D75B5", "83B7F0", "FontFamily=", "FontSize=" }.All(value => !stored.Contains(value)) &&
            stored.Contains("C95555"), "富文本存储不写主题色和便签字体字号，自选颜色照存");
        NoteTheme.DarkMode = true;
        var reloaded = new NoteEditor();
        reloaded.Initialize(new NoteData { Format = NoteFormat.RichText, Text = legacy.TextContent, RichText = stored, Appearance = appearance });
        Check(reloaded.TextContent == legacy.TextContent && Shown(Elements(reloaded.Rich.Document).OfType<Run>().First()) == dark.Text.Color &&
            Shown(OnlyLink(reloaded.Rich.Document)) == dark.Link.Color,
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
        var darkPaste = Editor(appearance: appearance);
        var webColors = LoadPaste(darkPaste.PreparePaste(new DataObject(DataFormats.Html,
            "<p><span style='color: rgb(31, 35, 40); background-color: rgb(255, 255, 255)'>网页黑字</span><span style='color: #C95555'>红字</span></p>")));
        var webRuns = Elements(webColors).OfType<Run>().Where(run => run.Text.Length > 0).ToArray();
        NoteTheme.DarkMode = false;
        Check(webRuns.Length == 2 && Shown(webRuns[0]) == dark.Text.Color && Shown(webRuns[1]) == red &&
            Elements(webColors).All(element => element.Background is null),
            "网页的黑字白底粘到深色模式的便签改为跟随主题，标红的字保留");

        // 便签字体本身是 Consolas 时，只有显式设成等宽字体的部分才算代码。
        var monoAppearance = appearance with { FontFamily = "Consolas" };
        var mono = new NoteEditor();
        mono.Initialize(new NoteData { Format = NoteFormat.RichText, Text = "等宽正文", Appearance = monoAppearance });
        var monoLink = LoadPaste(mono.PreparePaste(new DataObject(DataFormats.UnicodeText, "资料 https://example.com/mono 见上")));
        var monoHtml = LoadPaste(mono.PreparePaste(new DataObject(DataFormats.Html, "<p style='font-size:30px'>网页正文</p>")));
        Check(Elements(monoLink).OfType<Hyperlink>().Count() == 1 &&
            Elements(monoHtml).OfType<Paragraph>().All(paragraph => Math.Abs(paragraph.FontSize - monoAppearance.FontSize) < 0.01),
            "Consolas 字体的便签照常识别粘贴的网址，网页字号照常换成便签字号");
        Check(DocumentText.Markdown(FormattedDocuments.FromMarkdown("正文 `x`", monoAppearance)) == "正文 `x`",
            "Consolas 字体的便签导出 Markdown 时只给代码加反引号");
    }
}
