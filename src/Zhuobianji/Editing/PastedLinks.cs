using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using Zhuobianji.Core;

namespace Zhuobianji;

// 粘贴来源只负责保留原排版；这里统一识别正文里的链接，不检查目标是否存在。
internal static class PastedLinks
{
    private const string Code = @"(?m)^[ \t]{0,3}(?<fence>" + "`" + @"{3,}|~{3,})[^\r\n]*\r?\n[\s\S]*?^[ \t]{0,3}\k<fence>[^\r\n]*(?:\r?\n|$)|" + "`" + @"+[^" + "`" + @"\r\n]*" + "`" + @"+";
    private const string Named = @"\\?\[(?<name>[^\]\r\n]*?)\\?\]\\?\(\s*<?(?<address>(?:https?://|mailto:|file:|[A-Za-z]:[\\/]|\\\\)[^\r\n]*?)>?\s*\\?\)";
    private static readonly Regex Tokens = new(Code + "|" + Named +
        @"|(?<bare>https?://[^\s<>""" + "`" + @"]+|www\.[^\s<>""" + "`" + @"]+|""(?:[A-Za-z]:[\\/]|\\\\)[^""\r\n]+""|(?:[A-Za-z]:[\\/]|\\\\)[^\s<>""" + "`" + @"]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal sealed record MatchLink(int Start, int Length, Uri Target, string Name);

    internal static IEnumerable<MatchLink> Find(string text)
    {
        // 单独粘贴的本地路径可含空格；嵌在正文中的路径用引号或 Markdown 边界。
        var trimmed = text.Trim();
        if (!trimmed.Contains((char)96) && NoteLinks.TryResolve(trimmed, out var whole) && whole.IsFile)
        {
            yield return new(text.IndexOf(trimmed, StringComparison.Ordinal), trimmed.Length, whole, NoteLinks.ShortName(whole));
            yield break;
        }
        foreach (Match match in Tokens.Matches(text))
        {
            var named = match.Groups["address"].Success;
            if (!named && !match.Groups["bare"].Success) continue;
            var address = named ? match.Groups["address"].Value.Trim() : match.Groups["bare"].Value;
            if (!named && !address.StartsWith('"'))
                address = address.TrimEnd('.', ',', ';', '!', ')', '。', '，', '；', '！', '）');
            if (!NoteLinks.TryResolve(address, out var target)) continue;
            var name = named ? match.Groups["name"].Value : "";
            if (string.IsNullOrWhiteSpace(name)) name = NoteLinks.ShortName(target);
            yield return new(match.Index, named ? match.Length : address.Length, target, name);
        }
    }

    internal static string NormalizeCopiedSyntax(string text) => Tokens.Replace(text, match =>
    {
        if (!match.Groups["address"].Success || !NoteLinks.TryResolve(match.Groups["address"].Value.Trim(), out var uri))
            return match.Value;
        var name = match.Groups["name"].Value;
        if (name.Length == 0) name = NoteLinks.ShortName(uri);
        return "[" + name + "](<" + uri.AbsoluteUri + ">)";
    });

    internal static bool Apply(FlowDocument document, NoteAppearance appearance)
    {
        var changed = false;
        foreach (var paragraph in DocumentText.Elements(document).OfType<Paragraph>().ToArray())
        {
            var runs = new List<Run>();
            void Flush()
            {
                if (runs.Count == 0) return;
                var text = string.Concat(runs.Select(run => run.Text));
                TextPointer Position(int offset)
                {
                    foreach (var run in runs)
                    {
                        if (offset <= run.Text.Length) return run.ContentStart.GetPositionAtOffset(offset)!;
                        offset -= run.Text.Length;
                    }
                    return runs[^1].ContentEnd;
                }
                foreach (var match in Find(text).Reverse())
                {
                    var range = new TextRange(Position(match.Start), Position(match.Start + match.Length));
                    range.Text = match.Name;
                    _ = new Hyperlink(range.Start, range.End)
                    {
                        NavigateUri = match.Target, Foreground = NoteTheme.For(appearance).Link
                    };
                    changed = true;
                }
                runs.Clear();
            }
            foreach (var run in Runs(paragraph.Inlines).ToArray())
                if (run is null) Flush(); else runs.Add(run);
            Flush();
        }
        return changed;
    }

    private static IEnumerable<Run?> Runs(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Hyperlink || DocumentText.IsCode(inline))
                yield return null;
            else if (inline is Run run) yield return run;
            else if (inline is Span span)
                foreach (var child in Runs(span.Inlines)) yield return child;
            else yield return null;
        }
    }
}