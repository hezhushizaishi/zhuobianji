using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Zhuobianji;

// 保留常见文档结构及链接目标；字体、颜色等显示样式不进入 Markdown。
internal static class DocumentText
{
    internal static IEnumerable<TextElement> Elements(FlowDocument document)
    {
        for (var p = document.ContentStart; p is not null && p.CompareTo(document.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart && p.GetAdjacentElement(LogicalDirection.Forward) is TextElement element) yield return element;
    }

    // 代码只看元素自己或上级显式设置的等宽字体；便签字体本身是 Consolas 时，正文不算代码。
    internal static bool IsCode(TextElement element)
    {
        for (DependencyObject? current = element; current is TextElement item; current = item.Parent)
            if (item.ReadLocalValue(TextElement.FontFamilyProperty) is FontFamily family)
                return family.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase) ||
                    family.Source.Contains("Courier", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    internal static string Markdown(FlowDocument document) => Blocks(document.Blocks, true).TrimEnd('\n');
    internal static string Plain(FlowDocument document) => Blocks(document.Blocks, false);
    internal static string MarkdownLink(string name, Uri uri) => $"[{Escape(name)}](<{uri.AbsoluteUri}>)";
    private static string Escape(string value) => Regex.Replace(value, @"([\\*_\[\]<>|\x60])", @"\$1");

    private static string Blocks(BlockCollection blocks, bool markdown) =>
        string.Join(markdown ? "\n\n" : "\n", blocks.Cast<Block>().Select(block => BlockText(block, markdown)));

    private static string BlockText(Block block, bool markdown)
    {
        switch (block)
        {
            case Paragraph paragraph:
                var value = Inlines(paragraph.Inlines, markdown);
                if (!markdown) return value;
                if (IsCode(paragraph) && paragraph.Background is not null)
                {
                    var code = Inlines(paragraph.Inlines, false);
                    var fence = new string((char)96, code.Contains(new string((char)96, 3)) ? 4 : 3);
                    return fence + "\n" + code + "\n" + fence;
                }
                var baseSize = (paragraph.Parent as FlowDocument)?.FontSize ?? 17;
                if (paragraph.FontSize > baseSize * 1.1 && paragraph.FontWeight >= FontWeights.SemiBold)
                    return (paragraph.FontSize > baseSize * 1.3 ? "# " : "### ") + value;
                if (value.Length == 0 && paragraph.BorderThickness.Top > 0) return "---";
                return value;
            case Section section:
                var body = Blocks(section.Blocks, markdown);
                return section.BorderThickness.Left > 0 && markdown
                    ? string.Join("\n", body.Split('\n').Select(line => "> " + line)) : body;
            case System.Windows.Documents.List list:
                var index = list.StartIndex;
                return string.Join("\n", list.ListItems.Cast<ListItem>().Select(item =>
                {
                    var marker = list.MarkerStyle is TextMarkerStyle.Decimal or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman
                        ? (index++) + ". " : "- ";
                    return marker + Blocks(item.Blocks, markdown).Replace("\n", "\n  ");
                }));
            case Table table:
                var rows = table.RowGroups.Cast<TableRowGroup>().SelectMany(group => group.Rows.Cast<TableRow>())
                    .Select(row => row.Cells.Cast<TableCell>().Select(cell => Blocks(cell.Blocks, markdown).Replace("\n", markdown ? "<br>" : " ")).ToArray()).ToArray();
                if (!markdown) return string.Join("\n", rows.Select(row => string.Join("\t", row)));
                if (rows.Length == 0) return "";
                var columns = rows.Max(row => row.Length);
                var lines = rows.Select(row => "| " + string.Join(" | ", row.Concat(Enumerable.Repeat("", columns - row.Length))) + " |").ToList();
                lines.Insert(1, "| " + string.Join(" | ", Enumerable.Repeat("---", columns)) + " |");
                return string.Join("\n", lines);
            default:
                return new TextRange(block.ContentStart, block.ContentEnd).Text.TrimEnd('\r', '\n');
        }
    }

    private static string Inlines(InlineCollection inlines, bool markdown) =>
        string.Concat(inlines.Cast<Inline>().Select(inline => InlineText(inline, markdown)));

    private static string InlineText(Inline inline, bool markdown)
    {
        if (inline is LineBreak) return markdown ? "  \n" : "\n";
        if (inline is Hyperlink link)
        {
            var label = new TextRange(link.ContentStart, link.ContentEnd).Text;
            if (link.NavigateUri is not Uri uri) return markdown ? Escape(label) : label;
            return markdown ? MarkdownLink(label, uri) : NoteLinks.PlainText(label, uri);
        }
        var text = inline switch
        {
            Run run => markdown ? Escape(run.Text) : run.Text,
            Span span => Inlines(span.Inlines, markdown),
            _ => new TextRange(inline.ContentStart, inline.ContentEnd).Text
        };
        if (!markdown || text.Length == 0) return text;
        if (IsCode(inline) && !text.Contains('\n') && (inline.Parent is not TextElement fontParent || !IsCode(fontParent)))
        {
            var tick = new string((char)96, 1);
            return tick + text + tick;
        }
        if (inline.FontWeight >= FontWeights.SemiBold && (inline.Parent is not TextElement weightParent || weightParent.FontWeight < FontWeights.SemiBold)) text = "**" + text + "**";
        if (inline.FontStyle == FontStyles.Italic && (inline.Parent is not TextElement styleParent || styleParent.FontStyle != FontStyles.Italic)) text = "*" + text + "*";
        if (inline.TextDecorations.Any(decoration => decoration.Location == TextDecorationLocation.Strikethrough)) text = "~~" + text + "~~";
        return text;
    }
}