using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Markdig;
using MarkdownTable = Markdig.Extensions.Tables.Table;
using TableRow = Markdig.Extensions.Tables.TableRow;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Zhuobianji;

// Markdown 源码里哪些字符是语法：用和预览同样的扩展解析（带精确位置），所以变淡的正好是预览里会消失的符号。
// 标题的 #、强调和删除线的 * _ ~、行内代码的反引号、链接和图片的 [ ](地址) !、尖括号链接的 < >、
// 列表和引用的标记、代码块的围栏行、分隔线、表格的 | 和分隔行、转义用的 \。不成对的星号（2*3）不算。
internal static class MarkdownSyntax
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseEmphasisExtras().UseAutoLinks().UsePreciseSourceLocation().Build();

    // 按位置排好、互不重叠的区间。
    internal static List<(int Start, int Length)> Find(string text)
    {
        var marks = new bool[text.Length];
        void Mark(int start, int end)
        {
            for (var index = Math.Max(0, start); index < Math.Min(end, text.Length); index++)
                if (text[index] is not ('\r' or '\n')) marks[index] = true;
        }
        int LineEnd(int index)
        {
            while (index < text.Length && text[index] is not ('\r' or '\n')) index++;
            return index;
        }
        int Skip(int index, int end)
        {
            while (index < end && text[index] is ' ' or '\t') index++;
            return index;
        }
        int NextLine(int index)
        {
            index = LineEnd(index);
            if (index < text.Length && text[index] == '\r') index++;
            if (index < text.Length && text[index] == '\n') index++;
            return index;
        }
        // 块里每一行的开头；第一行从块的开头算（块可能在列表、引用里，前面还有别的标记）。
        IEnumerable<int> LineStarts(SourceSpan span)
        {
            for (var index = span.Start; index <= span.End && index < text.Length; index = NextLine(index)) yield return index;
        }

        var document = Markdown.Parse(text, Pipeline);
        // 行内代码占着的字（表格里代码中的 | 不是分隔）；有表格时才建，每个字查一次。
        bool[]? inCode = null;
        foreach (var node in document.Descendants())
        {
            var span = node.Span;
            if (span.IsEmpty || span.Start < 0 || span.Start >= text.Length) continue;
            switch (node)
            {
                case HeadingBlock { IsSetext: true }:
                    var underline = LineStarts(span).Last();
                    Mark(underline, LineEnd(underline));
                    break;
                case HeadingBlock heading:
                    var lineEnd = LineEnd(span.Start);
                    var hashes = Skip(span.Start, lineEnd);
                    var afterHashes = hashes;
                    while (afterHashes < lineEnd && text[afterHashes] == '#') afterHashes++;
                    Mark(hashes, Skip(afterHashes, lineEnd));
                    // 行尾可选的收尾 #（「## 标题 ##」）。
                    var tail = lineEnd;
                    while (tail > afterHashes && text[tail - 1] is ' ' or '\t') tail--;
                    var closing = tail;
                    while (closing > afterHashes && text[closing - 1] == '#') closing--;
                    if (closing < tail && (closing == afterHashes || text[closing - 1] is ' ' or '\t')) Mark(closing, tail);
                    break;
                case QuoteBlock:
                    var first = true;
                    foreach (var start in LineStarts(span))
                    {
                        var end = LineEnd(start);
                        var index = Skip(first ? span.Start : start, end);
                        first = false;
                        var from = index;
                        while (index < end && text[index] == '>') index = Skip(index + 1, end);
                        if (index > from) Mark(from, index);
                    }
                    break;
                case ListItemBlock:
                    var bullet = span.Start;
                    while (bullet < text.Length && char.IsAsciiDigit(text[bullet])) bullet++;
                    if (bullet < text.Length && (bullet > span.Start ? text[bullet] is '.' or ')' : text[bullet] is '-' or '*' or '+'))
                        Mark(span.Start, Skip(bullet + 1, LineEnd(bullet)));
                    break;
                case FencedCodeBlock fenced:
                    Mark(span.Start, LineEnd(span.Start));
                    if (fenced.ClosingFencedCharCount > 0)
                    {
                        var closingLine = LineStarts(span).Last();
                        if (closingLine > span.Start) Mark(Skip(closingLine, LineEnd(closingLine)), LineEnd(closingLine));
                    }
                    break;
                case ThematicBreakBlock:
                    Mark(span.Start, span.End + 1);
                    break;
                case MarkdownTable table:
                    // 分隔行按解析出的结构找（表头下面那一行）：数据行的内容恰好像「| --- | - |」也只淡竖线。
                    var separator = table.OfType<TableRow>().LastOrDefault(row => row.IsHeader) is { Span.IsEmpty: false } header
                        ? NextLine(header.Span.Start) : -1;
                    if (inCode is null)
                    {
                        inCode = new bool[text.Length];
                        foreach (var code in document.Descendants<CodeInline>())
                            for (var index = Math.Max(0, code.Span.Start); index <= code.Span.End && index < text.Length; index++) inCode[index] = true;
                    }
                    foreach (var start in LineStarts(span))
                    {
                        var end = LineEnd(start);
                        if (start == separator) { Mark(Skip(start, end), end); continue; }
                        for (var index = start; index < end; index++)
                            if (text[index] == '|' && (index == 0 || text[index - 1] != '\\') && !inCode[index]) Mark(index, index + 1);
                    }
                    break;
                case EmphasisInline emphasis:
                    Mark(span.Start, span.Start + emphasis.DelimiterCount);
                    Mark(span.End + 1 - emphasis.DelimiterCount, span.End + 1);
                    break;
                case CodeInline code:
                    Mark(span.Start, span.Start + code.DelimiterCount);
                    Mark(span.End + 1 - code.DelimiterCount, span.End + 1);
                    break;
                case LinkInline { IsAutoLink: true }:
                    break;
                case LinkInline link:
                    // 链接文字照常，前面的 ! [ 和后面的 ](地址 "标题") 变淡；没有文字的整段变淡。
                    var label = link.FirstChild?.Span ?? SourceSpan.Empty;
                    var labelEnd = link.LastChild?.Span ?? SourceSpan.Empty;
                    if (label.IsEmpty || labelEnd.IsEmpty) { Mark(span.Start, span.End + 1); break; }
                    Mark(span.Start, label.Start);
                    Mark(labelEnd.End + 1, span.End + 1);
                    break;
                case AutolinkInline:
                    Mark(span.Start, span.Start + 1);
                    Mark(span.End, span.End + 1);
                    break;
                case LiteralInline { IsFirstCharacterEscaped: true }:
                case LineBreakInline { IsBackslash: true }:
                    if (text[span.Start] == '\\') Mark(span.Start, span.Start + 1);
                    break;
            }
        }

        var ranges = new List<(int, int)>();
        for (var index = 0; index < marks.Length; index++)
        {
            if (!marks[index]) continue;
            var start = index;
            while (index < marks.Length && marks[index]) index++;
            ranges.Add((start, index - start));
        }
        return ranges;
    }
}

// 把 Source（Markdown 源码的文本框）里的语法符号变淡：在它的文字层上加一层装饰，给看得见的几行里的语法字符
// 盖一层半透明的便签底色（NoteTheme.SyntaxVeil）。正文卡片的底色是纯色，盖一层等于把这些字的颜色往底色调，
// 和给字换颜色一样；文本框本身不动，撤销、粘贴、输入法照旧。选中的字不盖。
// 光标和这层在同一个装饰层里、画在这层下面，所以光标停在一段符号的边上时，这段让开光标那一条（见 Span）。
// 改字、滚动、选中、改大小之后，等这一轮排版完（LayoutUpdated）再按新位置重画，和文字同一帧更新。
internal sealed class SourceSyntax
{
    // 字数超过这个的便签，解析放到空闲时做（5 万字约 20 ms），打字不卡；变淡会晚一两帧跟上。
    private const int SyncLimit = 20000;
    private readonly TextBox box;
    private readonly DrawingGroup drawing = new();
    private Adorner? veil;
    private List<(int Start, int Length)> ranges = [];
    private Brush fill = Brushes.Transparent;
    private bool enabled, dirty, warm, pending;

    internal SourceSyntax(TextBox box)
    {
        this.box = box;
        box.TextChanged += (_, _) => Parse();
        box.SelectionChanged += (_, _) => Invalidate();
        box.IsKeyboardFocusedChanged += (_, _) => Invalidate();
        box.SizeChanged += (_, _) => Invalidate();
        box.IsVisibleChanged += (_, _) => Invalidate();
        box.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => Invalidate()));
        box.LayoutUpdated += (_, _) => Draw();
    }

    // 检查用：现在盖着的矩形（文本框坐标）。
    internal IReadOnlyList<Rect> Veiled { get; private set; } = [];
    internal IReadOnlyList<(int Start, int Length)> Ranges => ranges;

    // 只在 Markdown 格式开着；纯文本保留原文，什么都不盖。
    internal bool Enabled
    {
        get => enabled;
        set { if (enabled == value) return; enabled = value; Parse(); }
    }

    internal Brush Fill
    {
        set { fill = value; Invalidate(); }
    }

    private void Parse()
    {
        if (!enabled) { ranges = []; Invalidate(); return; }
        // 第一次解析放到窗口画出来之后（Markdig 第一次用要编译几十毫秒），不拖慢便签出现。
        if (!warm || box.Text.Length > SyncLimit)
        {
            warm = true;
            if (pending) return;
            pending = true;
            box.Dispatcher.InvokeAsync(() => { pending = false; ParseNow(); }, DispatcherPriority.Background);
            return;
        }
        ParseNow();
    }

    private void ParseNow()
    {
        ranges = enabled ? MarkdownSyntax.Find(box.Text) : [];
        Invalidate();
    }

    private void Invalidate()
    {
        dirty = true;
        // 选中、换颜色这类变化本身不引起排版，这里要一轮排版，好让 LayoutUpdated 来画。
        (veil ?? (UIElement)box).InvalidateArrange();
    }

    private void Draw()
    {
        // 看 Visibility 不看 IsVisible：便签窗口还没显示时也先画好，显示出来不用再等一轮。
        if (!dirty || box.Visibility != Visibility.Visible) return;
        if (veil is null && !Attach()) return;
        dirty = false;
        var rects = new List<Rect>();
        if (ranges.Count > 0) Measure(rects);
        var toView = box.TransformToDescendant(veil!.AdornedElement);
        using (var context = drawing.Open())
            if (toView is not null)
                foreach (var rect in rects) context.DrawRectangle(fill, null, toView.TransformBounds(rect));
        Veiled = rects;
    }

    // 文字层是文本框模板里滚动区的内容；装饰挂在滚动区自己的装饰层上，跟着滚动、被滚动区裁剪，光标在它上面。
    private bool Attach()
    {
        if (box.Template?.FindName("PART_ContentHost", box) is not ScrollViewer { Content: UIElement view } ||
            AdornerLayer.GetAdornerLayer(view) is not { } layer) return false;
        veil = new Veil(view, drawing);
        layer.Add(veil);
        return true;
    }

    private void Measure(List<Rect> rects)
    {
        var first = box.GetFirstVisibleLineIndex();
        var last = box.GetLastVisibleLineIndex();
        if (first < 0 || last < first) return;
        var from = box.GetCharacterIndexFromLineIndex(first);
        var to = box.GetCharacterIndexFromLineIndex(last) + box.GetLineLength(last);
        var selectionStart = box.SelectionStart;
        var selectionEnd = selectionStart + box.SelectionLength;
        foreach (var (start, length) in ranges)
        {
            if (start >= to) break;
            var head = Math.Max(start, from);
            var tail = Math.Min(start + length, to);
            if (head >= tail) continue;
            if (selectionEnd > selectionStart)
            {
                if (head < selectionStart) Span(rects, head, Math.Min(tail, selectionStart));
                if (tail > selectionEnd) Span(rects, Math.Max(head, selectionEnd), tail);
            }
            else Span(rects, head, tail);
        }
    }

    // 一段字在同一行就是一个矩形；折行了就对半分开再看。
    private void Span(List<Rect> rects, int start, int end)
    {
        if (start >= end) return;
        var head = box.GetRectFromCharacterIndex(start);
        var tail = box.GetRectFromCharacterIndex(end - 1, true);
        if (head.IsEmpty || tail.IsEmpty) return;
        if (Math.Abs(head.Top - tail.Top) < 0.5 && tail.X > head.X)
        {
            // 光标就在这段的头或尾：让开光标的宽度加 1（一般是字形旁边的空白），光标不跟着变淡。
            var caret = box.IsKeyboardFocused && box.SelectionLength == 0 ? box.CaretIndex : -1;
            var gap = SystemParameters.CaretWidth + 1;
            var left = caret == start ? head.X + gap : head.X;
            var right = caret == end ? tail.X - gap : tail.X;
            if (right > left) rects.Add(new Rect(left, head.Top, right - left, head.Height));
            return;
        }
        if (end - start == 1) return;
        var middle = (start + end) / 2;
        Span(rects, start, middle);
        Span(rects, middle, end);
    }

    private sealed class Veil : Adorner
    {
        private readonly Drawing drawing;
        internal Veil(UIElement view, Drawing drawing) : base(view) { this.drawing = drawing; IsHitTestVisible = false; }
        protected override void OnRender(DrawingContext context) => context.DrawDrawing(drawing);
    }
}
