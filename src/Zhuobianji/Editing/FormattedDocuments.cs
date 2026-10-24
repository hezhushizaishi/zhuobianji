using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Zhuobianji.Core;

namespace Zhuobianji;

// Markdown 预览与网页粘贴共用一条 HTML → WPF 文档链路，不引入浏览器运行环境。
internal static class FormattedDocuments
{
    private static readonly MarkdownPipeline PastePipeline = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().UseAutoLinks().UseSoftlineBreakAsHardlineBreak().Build();
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseEmphasisExtras().UseAutoLinks().Build();

    internal static FlowDocument Create(NoteAppearance appearance)
    {
        var theme = NoteTheme.For(appearance);
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0), FontFamily = new FontFamily(appearance.FontFamily),
            FontSize = appearance.FontSize, Foreground = theme.Text, LineHeight = appearance.FontSize * 1.45
        };
        var paragraphStyle = new Style(typeof(Paragraph));
        paragraphStyle.Setters.Add(new Setter(System.Windows.Documents.Block.MarginProperty, new Thickness(0, 0, 0, 3)));
        document.Resources.Add(typeof(Paragraph), paragraphStyle);
        return document;
    }
    internal static FlowDocument FromMarkdown(string text, NoteAppearance appearance, bool pasted = false)
    {
        var pipeline = pasted ? PastePipeline : Pipeline;
        var document = Markdig.Markdown.Parse(PastedLinks.NormalizeCopiedSyntax(text), pipeline);
        // 自动网址没有用户起的名称；只调整渲染树，不改 Markdown 源码。
        foreach (var auto in document.Descendants<AutolinkInline>().ToArray())
        {
            var address = auto.IsEmail ? "mailto:" + auto.Url : auto.Url;
            if (!NoteLinks.TryResolve(address, out var uri)) continue;
            var named = new LinkInline { Url = uri.AbsoluteUri };
            named.AppendChild(new LiteralInline(NoteLinks.ShortName(uri)));
            auto.ReplaceBy(named);
        }
        foreach (var auto in document.Descendants<LinkInline>().Where(link => link.IsAutoLink).ToArray())
        {
            if (!NoteLinks.TryResolve(auto.Url, out var uri)) continue;
            auto.Clear();
            auto.AppendChild(new LiteralInline(NoteLinks.ShortName(uri)));
        }
        return FromHtml(Markdig.Markdown.ToHtml(document, pipeline), appearance);
    }
    internal static void ApplyReadingLayout(FlowDocument document, NoteAppearance appearance, bool pasted = false)
    {
        document.FontFamily = new FontFamily(appearance.FontFamily);
        document.FontSize = appearance.FontSize;
        document.LineHeight = appearance.FontSize * 1.45;
        foreach (var element in DocumentText.Elements(document).ToArray())
        {
            var code = element.FontFamily.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase) ||
                element.FontFamily.Source.Contains("Courier", StringComparison.OrdinalIgnoreCase);
            if (pasted && !code)
            {
                // 标题的相对大小保留，正文不继承网页或其他程序的字体、字号。
                var heading = element is Paragraph && element.FontWeight >= FontWeights.SemiBold &&
                    element.FontSize > appearance.FontSize * 1.15;
                element.ClearValue(TextElement.FontFamilyProperty);
                if (!heading) element.ClearValue(TextElement.FontSizeProperty);
            }
            if (element is System.Windows.Documents.Block block) block.ClearValue(System.Windows.Documents.Block.LineHeightProperty);
            if (element is Paragraph paragraph && !code) paragraph.Margin = new Thickness(0, 0, 0, 3);
            if (element is System.Windows.Documents.List list)
            { list.Margin = new Thickness(0, 0, 0, 6); list.Padding = new Thickness(20, 0, 0, 0); }
        }
    }
    internal static bool LooksLikeMarkdown(string text) => Regex.IsMatch(PastedLinks.NormalizeCopiedSyntax(text),
        @"(?m)^\s{0,3}(#{1,6}\s+\S|```|~~~|>\s+\S|[-*+]\s+\S|\d+\.\s+\S)|\*\*[^*\r\n]+\*\*|\[[^\]\r\n]*\]\(\s*<?(?:https?://|file:|[A-Za-z]:[\\/]|\\\\)|\|[ \t]*:?-{3,}:?[ \t]*\|");

    internal static string HtmlFragment(string html)
    {
        const string start = "<!--StartFragment-->", end = "<!--EndFragment-->";
        var a = html.IndexOf(start, StringComparison.OrdinalIgnoreCase);
        var b = html.IndexOf(end, StringComparison.OrdinalIgnoreCase);
        if (a >= 0 && b > a) return html[(a + start.Length)..b];
        var tag = html.IndexOf('<');
        return tag >= 0 ? html[tag..] : html;
    }

    internal static FlowDocument FromHtml(string html, NoteAppearance appearance)
    {
        var document = Create(appearance);
        using var dom = new HtmlParser().ParseDocument(HtmlFragment(html));
        if (dom.Body is not null) AddBlocks(dom.Body.ChildNodes, document.Blocks, appearance);
        if (document.Blocks.Count == 0) document.Blocks.Add(new Paragraph());
        PastedLinks.Apply(document, appearance);
        return document;
    }

    private static void AddBlocks(INodeList nodes, BlockCollection blocks, NoteAppearance appearance)
    {
        Paragraph? pending = null;
        void Flush() { if (pending is not null) { blocks.Add(pending); pending = null; } }
        foreach (var node in nodes)
        {
            if (node is IElement element && IsBlock(element.LocalName))
            {
                Flush();
                switch (element.LocalName)
                {
                    case "p": case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                        var paragraph = Paragraph();
                        if (element.LocalName.StartsWith('h'))
                        {
                            paragraph.FontWeight = FontWeights.SemiBold;
                            paragraph.FontSize = appearance.FontSize * (element.LocalName is "h1" or "h2" ? 1.4 : 1.15);
                        }
                        AddInlines(element.ChildNodes, paragraph.Inlines, appearance);
                        ApplyInlineStyle(element, paragraph);
                        blocks.Add(paragraph);
                        break;
                    case "pre":
                        blocks.Add(new Paragraph(new Run(element.TextContent.TrimEnd('\r', '\n')))
                        {
                            FontFamily = new FontFamily("Consolas"), FontSize = appearance.FontSize * 0.9,
                            Background = NoteTheme.For(appearance).Code, Padding = new Thickness(10),
                            Margin = new Thickness(0, 0, 0, 12)
                        });
                        break;
                    case "ul": case "ol":
                        var list = new System.Windows.Documents.List
                        {
                            MarkerStyle = element.LocalName == "ol" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                            Padding = new Thickness(20, 0, 0, 0), Margin = new Thickness(0, 0, 0, 6)
                        };
                        if (int.TryParse(element.GetAttribute("start"), out var index)) list.StartIndex = Math.Max(1, index);
                        foreach (var item in element.Children.Where(e => e.LocalName == "li"))
                        {
                            var listItem = new ListItem();
                            AddBlocks(item.ChildNodes, listItem.Blocks, appearance);
                            if (listItem.Blocks.Count == 0) listItem.Blocks.Add(new Paragraph());
                            list.ListItems.Add(listItem);
                        }
                        blocks.Add(list);
                        break;
                    case "blockquote":
                        var quote = new Section
                        {
                            BorderBrush = NoteTheme.For(appearance).Border, BorderThickness = new Thickness(3, 0, 0, 0),
                            Padding = new Thickness(12, 0, 0, 0), Margin = new Thickness(0, 0, 0, 12),
                            Foreground = NoteTheme.For(appearance).Muted
                        };
                        AddBlocks(element.ChildNodes, quote.Blocks, appearance);
                        blocks.Add(quote);
                        break;
                    case "table":
                        var table = new System.Windows.Documents.Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 12) };
                        var group = new TableRowGroup();
                        table.RowGroups.Add(group);
                        foreach (var row in element.QuerySelectorAll("tr"))
                        {
                            var tableRow = new TableRow();
                            foreach (var sourceCell in row.Children.Where(e => e.LocalName is "td" or "th"))
                            {
                                var cell = new TableCell
                                {
                                    BorderBrush = NoteTheme.For(appearance).Border, BorderThickness = new Thickness(0.5),
                                    Padding = new Thickness(6), FontWeight = sourceCell.LocalName == "th" ? FontWeights.SemiBold : FontWeights.Normal
                                };
                                AddBlocks(sourceCell.ChildNodes, cell.Blocks, appearance);
                                if (cell.Blocks.Count == 0) cell.Blocks.Add(new Paragraph());
                                tableRow.Cells.Add(cell);
                            }
                            group.Rows.Add(tableRow);
                        }
                        blocks.Add(table);
                        break;
                    case "hr":
                        blocks.Add(new Paragraph { BorderBrush = NoteTheme.For(appearance).Border, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 8, 0, 12) });
                        break;
                    default:
                        AddBlocks(element.ChildNodes, blocks, appearance);
                        break;
                }
            }
            else
            {
                if (pending is null && string.IsNullOrWhiteSpace(node.TextContent)) continue;
                pending ??= Paragraph();
                AddInline(node, pending.Inlines, appearance);
            }
        }
        Flush();
    }

    private static Paragraph Paragraph() => new() { Margin = new Thickness(0, 0, 0, 3) };

    private static bool IsBlock(string tag) => tag is "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
        or "pre" or "ul" or "ol" or "blockquote" or "table" or "hr" or "div" or "section" or "article";

    private static void AddInlines(INodeList nodes, InlineCollection inlines, NoteAppearance appearance)
    { foreach (var node in nodes) AddInline(node, inlines, appearance); }

    private static void AddInline(INode node, InlineCollection inlines, NoteAppearance appearance)
    {
        if (node is IText text)
        {
            var value = Regex.Replace(text.Data, @"\s+", " ");
            if (inlines.LastInline is LineBreak) value = value.TrimStart();
            inlines.Add(new Run(value));
            return;
        }
        if (node is not IElement element) return;
        if (element.LocalName is "script" or "style" or "iframe" or "object" or "head") return;
        if (element.LocalName == "br") { inlines.Add(new LineBreak()); return; }
        if (element.LocalName == "img")
        {
            var alt = element.GetAttribute("alt");
            if (!string.IsNullOrEmpty(alt)) inlines.Add(new Run("[图片：" + alt + "]"));
            return;
        }
        Span span = element.LocalName switch
        {
            "strong" or "b" => new Bold(), "em" or "i" => new Italic(), "u" or "ins" => new Underline(),
            "a" => CreateLink(element.GetAttribute("href"), appearance), _ => new Span()
        };
        if (element.LocalName is "s" or "del" or "strike") span.TextDecorations = TextDecorations.Strikethrough;
        if (element.LocalName == "mark") span.Background = NoteTheme.B("#F1DC83");
        if (element.LocalName == "code") { span.FontFamily = new FontFamily("Consolas"); span.Background = NoteTheme.For(appearance).Code; }
        ApplyInlineStyle(element, span);
        AddInlines(element.ChildNodes, span.Inlines, appearance);
        if (span is Hyperlink hyperlink && hyperlink.NavigateUri is Uri target)
        {
            var label = new TextRange(hyperlink.ContentStart, hyperlink.ContentEnd).Text.Trim();
            if (string.IsNullOrWhiteSpace(label))
            {
                hyperlink.Inlines.Clear();
                hyperlink.Inlines.Add(new Run(NoteLinks.ShortName(target)));
            }
        }
        inlines.Add(span);
    }

    private static Span CreateLink(string? address, NoteAppearance appearance)
    {
        if (!NoteLinks.TryResolve(address, out var uri)) return new Span();
        return new Hyperlink
        {
            NavigateUri = uri, ToolTip = NoteLinks.Tooltip(uri),
            Cursor = System.Windows.Input.Cursors.Hand, Foreground = NoteTheme.For(appearance).Link
        };
    }
    internal static FlowDocument FromText(string text, NoteAppearance appearance, bool detectLinks = false)
    {
        var document = Create(appearance);
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var paragraph = Paragraph();
            paragraph.Inlines.Add(new Run(line));
            document.Blocks.Add(paragraph);
        }
        if (detectLinks) PastedLinks.Apply(document, appearance);
        return document;
    }
    private static void ApplyInlineStyle(IElement element, TextElement target)
    {
        var declarations = (element.GetAttribute("style") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var declaration in declarations)
        {
            var pair = declaration.Split(':', 2);
            if (pair.Length != 2) continue;
            var key = pair[0].Trim().ToLowerInvariant();
            var value = pair[1].Trim();
            if (key == "font-weight" && (value == "bold" || int.TryParse(value, out var weight) && weight >= 600))
                target.FontWeight = FontWeights.Bold;
            else if (key == "font-style" && value == "italic") target.FontStyle = FontStyles.Italic;
            else if (key == "color" || key == "background-color")
            {
                try
                {
                    var rgb = Regex.Match(value, @"^rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)$");
                    var brush = rgb.Success
                        ? new SolidColorBrush(Color.FromRgb(byte.Parse(rgb.Groups[1].Value), byte.Parse(rgb.Groups[2].Value), byte.Parse(rgb.Groups[3].Value)))
                        : NoteTheme.B(value);
                    if (key == "color") target.Foreground = brush; else target.Background = brush;
                }
                catch (Exception ex) when (ex is FormatException or OverflowException or NotSupportedException) { }
            }
            else if (key == "font-size" && double.TryParse(value.TrimEnd('p', 'x', 't'), NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size is >= 6 and <= 72)
                target.FontSize = value.EndsWith("pt", StringComparison.OrdinalIgnoreCase) ? size * 4 / 3 : size;
            else if (key == "text-decoration" && target is Span span)
            {
                if (value.Contains("line-through")) span.TextDecorations = TextDecorations.Strikethrough;
                else if (value.Contains("underline")) span.TextDecorations = TextDecorations.Underline;
            }
        }
    }

    internal static string Save(FlowDocument document, string format)
    {
        using var stream = new MemoryStream();
        new TextRange(document.ContentStart, document.ContentEnd).Save(stream, format);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static string PlainText(FlowDocument document)
    {
        var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
        if (text.EndsWith("\r\n", StringComparison.Ordinal)) text = text[..^2];
        return text.Replace("\r\n", "\n");
    }
}
