using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Zhuobianji.Core;

namespace Zhuobianji;

internal partial class NoteEditor
{
    private readonly HashSet<Hyperlink> configuredLinks = [];
    private Hyperlink? pressedLink;
    private Point linkPressPoint;
    private void RefreshLinks()
    {
        if (Format != NoteFormat.RichText) return;
        var links = DocumentText.Elements(Rich.Document).OfType<Hyperlink>().ToHashSet();
        configuredLinks.IntersectWith(links);
        foreach (var link in links)
            if (configuredLinks.Add(link)) ConfigureLink(link);
    }

    private void RefreshPreview()
    {
        Preview.Document = FormattedDocuments.FromMarkdown(Source.Text, Appearance);
        var owner = Window.GetWindow(this);
        if (owner is null) return;
        var matches = PastedLinks.Find(Source.Text).ToList();
        foreach (var link in DocumentText.Elements(Preview.Document).OfType<Hyperlink>())
        {
            var index = matches.FindIndex(match => match.Target == link.NavigateUri);
            if (index < 0) continue;
            var match = matches[index];
            matches.RemoveAt(index);
            void Replace(string replacement)
            {
                Source.BeginChange();
                try { Source.Select(match.Start, match.Length); Source.SelectedText = replacement; }
                finally { Source.EndChange(); }
            }
            ConfigureLink(link,
                (name, target) => Replace(DocumentText.MarkdownLink(name, target)), name => Replace(name));
        }
    }
    internal void InsertLink(Uri uri, string? name = null)
    {
        name = string.IsNullOrWhiteSpace(name) ? NoteLinks.ShortName(uri) : name;
        if (Format != NoteFormat.RichText)
        { InsertPlain(Format == NoteFormat.Markdown ? DocumentText.MarkdownLink(name, uri) : NoteLinks.Address(uri)); return; }
        Rich.BeginChange();
        try
        {
            Rich.Selection.Text = name;
            var link = new Hyperlink(Rich.Selection.Start, Rich.Selection.End) { NavigateUri = uri, ToolTip = NoteLinks.Tooltip(uri), Foreground = NoteTheme.For(Appearance).Link };
            Rich.CaretPosition = link.ElementEnd;
        }
        finally { Rich.EndChange(); }
        RefreshLinks();
        FocusEditor();
    }

    private Hyperlink? LinkAt(MouseButtonEventArgs e)
    {
        Hyperlink? Find(DependencyObject? element)
        {
            while (element is not null)
            {
                if (element is Hyperlink link) return link;
                element = element is FrameworkContentElement content ? content.Parent
                    : element is System.Windows.Media.Visual ? VisualTreeHelper.GetParent(element) : null;
            }
            return null;
        }
        return Find(e.OriginalSource as DependencyObject) ?? Find(Rich.GetPositionFromPoint(e.GetPosition(Rich), true)?.Parent);
    }

    private void OnLinkMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        pressedLink = LinkAt(e);
        if (pressedLink is null) return;
        linkPressPoint = e.GetPosition(Rich);
        e.Handled = true;
    }

    private void OnLinkMouseUp(object sender, MouseButtonEventArgs e)
    {
        var link = pressedLink;
        pressedLink = null;
        if (link is null) return;
        var movement = e.GetPosition(Rich) - linkPressPoint;
        if (LinkAt(e) == link && Math.Abs(movement.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(movement.Y) < SystemParameters.MinimumVerticalDragDistance && link.NavigateUri is Uri uri)
            LinkOpener(uri);
        e.Handled = true;
    }

    private void ConfigureLink(Hyperlink link,
        Action<string, Uri>? replace = null, Action<string>? remove = null)
    {
        if (link.NavigateUri is not Uri uri) return;
        var owner = Window.GetWindow(this);
        var appearance = Appearance;
        link.ToolTip = NoteLinks.Tooltip(uri);
        link.Cursor = System.Windows.Input.Cursors.Hand;
        var menu = new ContextMenu();
        var open = new MenuItem { Header = uri.IsFile ? "打开文件夹" : "打开" };
        open.Click += (_, _) => { if (link.NavigateUri is Uri target) LinkOpener(target); };
        menu.Items.Add(open);
        if (owner is not null)
        {
            var edit = new MenuItem { Header = "修改链接" };
            edit.Click += (_, _) =>
            {
                var dialog = new LinkWindow(appearance, NoteLinks.Address(link.NavigateUri!), new TextRange(link.ContentStart, link.ContentEnd).Text) { Owner = owner };
                if (dialog.ShowDialog() != true) return;
                if (replace is not null) { replace(dialog.LinkName, dialog.TargetUri); return; }
                Rich.BeginChange();
                try
                {
                    link.NavigateUri = dialog.TargetUri;
                    link.ToolTip = NoteLinks.Tooltip(dialog.TargetUri);
                    open.Header = dialog.TargetUri.IsFile ? "打开文件夹" : "打开";
                    link.Inlines.Clear();
                    link.Inlines.Add(new Run(dialog.LinkName));
                }
                finally { Rich.EndChange(); }
            };
            menu.Items.Add(edit);
            var cancel = new MenuItem { Header = "取消链接" };
            cancel.Click += (_, _) =>
            {
                var name = new TextRange(link.ContentStart, link.ContentEnd).Text;
                if (remove is not null) { remove(name); return; }
                var inlines = link.Parent switch { Paragraph p => p.Inlines, Span s => s.Inlines, _ => null };
                if (inlines is null) return;
                Rich.BeginChange();
                try
                {
                    foreach (var child in link.Inlines.ToArray())
                    { link.Inlines.Remove(child); inlines.InsertBefore(link, child); }
                    inlines.Remove(link);
                }
                finally { Rich.EndChange(); }
            };
            menu.Items.Add(cancel);
        }
        var copy = new MenuItem { Header = "复制地址" };
        copy.Click += (_, _) =>
        {
            try { if (link.NavigateUri is Uri target) Clipboard.SetText(NoteLinks.Address(target)); }
            catch (System.Runtime.InteropServices.COMException) { MessageBox.Show("剪贴板正忙，请稍后再试。", "桌边记"); }
        };
        menu.Items.Add(copy);
        link.ContextMenu = menu;
    }
}
