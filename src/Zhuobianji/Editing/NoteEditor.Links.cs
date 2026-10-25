using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Zhuobianji.Core;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace Zhuobianji;

internal partial class NoteEditor
{
    private readonly HashSet<Hyperlink> configuredLinks = [];
    private Hyperlink? cardLink;
    private bool cardQueued;
    private Window? cardOwner;

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
            var link = new Hyperlink(Rich.Selection.Start, Rich.Selection.End) { NavigateUri = uri, Foreground = NoteTheme.For(Appearance).Link };
            Rich.CaretPosition = link.ElementEnd;
        }
        finally { Rich.EndChange(); }
        RefreshLinks();
        FocusEditor();
    }

    // 富文本链接（replace 为空）：单击放光标、Ctrl+单击打开；Markdown 预览只读，单击直接打开，修改和取消改写源码。
    private void ConfigureLink(Hyperlink link,
        Action<string, Uri>? replace = null, Action<string>? remove = null)
    {
        if (link.NavigateUri is not Uri uri) return;
        var preview = replace is not null;
        link.ToolTip = preview ? NoteLinks.Tooltip(uri) : NoteLinks.Readable(uri) + "\nCtrl+单击" + OpenText(uri);
        link.Cursor = preview ? Cursors.Hand : Cursors.IBeam;
        var menu = new ContextMenu();
        var open = new MenuItem { Header = OpenText(uri), Icon = MenuIcon(uri.IsFile ? SymbolRegular.FolderOpen20 : SymbolRegular.Open20) };
        open.Click += (_, _) => { if (link.NavigateUri is Uri target) LinkOpener(target); };
        menu.Items.Add(open);
        if (Window.GetWindow(this) is not null)
        {
            var edit = new MenuItem { Header = "修改链接", Icon = MenuIcon(SymbolRegular.LinkEdit20) };
            edit.Click += (_, _) => EditLink(link, replace);
            menu.Items.Add(edit);
            var cancel = new MenuItem { Header = "取消链接", Icon = MenuIcon(SymbolRegular.LinkDismiss20) };
            cancel.Click += (_, _) => Unlink(link, remove);
            menu.Items.Add(cancel);
        }
        var copy = new MenuItem { Header = "复制地址", Icon = MenuIcon(SymbolRegular.Copy20) };
        copy.Click += (_, _) => { if (link.NavigateUri is Uri target) CopyAddress(target); };
        menu.Items.Add(copy);
        link.ContextMenu = menu;
    }

    private static string OpenText(Uri uri) => uri.IsFile ? "打开文件夹" : "打开";

    private void EditLink(Hyperlink link, Action<string, Uri>? replace = null)
    {
        if (link.NavigateUri is not Uri uri || Window.GetWindow(this) is not Window owner) return;
        var label = new TextRange(link.ContentStart, link.ContentEnd).Text;
        var dialog = new LinkWindow(Appearance, NoteLinks.Address(uri), label) { Owner = owner };
        if (dialog.ShowDialog() != true) return;
        // 只改了地址时，自动起的名称跟着新地址重新起。
        var name = dialog.LinkName == label && NoteLinks.IsAutoName(label, uri) ? NoteLinks.ShortName(dialog.TargetUri) : dialog.LinkName;
        if (replace is not null) { replace(name, dialog.TargetUri); return; }
        Rich.BeginChange();
        try
        {
            link.NavigateUri = dialog.TargetUri;
            link.Inlines.Clear();
            link.Inlines.Add(new Run(name));
        }
        finally { Rich.EndChange(); }
        ConfigureLink(link);
    }

    private void Unlink(Hyperlink link, Action<string>? remove = null)
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
    }

    private static bool CopyAddress(Uri uri)
    {
        try { Clipboard.SetText(NoteLinks.Address(uri)); return true; }
        catch (System.Runtime.InteropServices.COMException) { Dialogs.Show("剪贴板正忙，请稍后再试。"); return false; }
    }

    // 复制出的纯文字里，完整选中的链接写成地址（写法同转纯文本）；RTF 和 XAML 本来就带链接。
    private void OnCopying(object sender, DataObjectCopyingEventArgs e)
    {
        var (selection, document) = ReferenceEquals(sender, Preview) ? (Preview.Selection, Preview.Document) : (Rich.Selection, Rich.Document);
        if (selection is null || document is null || selection.IsEmpty) return;
        var text = CopiedText(selection, document);
        if (text == selection.Text) return;
        e.DataObject.SetData(DataFormats.UnicodeText, text);
        e.DataObject.SetData(DataFormats.Text, text);
    }

    private static string CopiedText(TextRange selection, FlowDocument document)
    {
        var text = selection.Text;
        var links = DocumentText.Elements(document).OfType<Hyperlink>().Where(link => link.NavigateUri is not null &&
            link.ContentStart.CompareTo(selection.Start) >= 0 && link.ContentEnd.CompareTo(selection.End) <= 0).ToArray();
        foreach (var link in links.Reverse())
        {
            var start = new TextRange(selection.Start, link.ContentStart).Text.Length;
            var label = new TextRange(link.ContentStart, link.ContentEnd).Text;
            if (start + label.Length > text.Length || string.CompareOrdinal(text, start, label, 0, label.Length) != 0) continue;
            text = text[..start] + NoteLinks.PlainText(label, link.NavigateUri!) + text[(start + label.Length)..];
        }
        return text;
    }

    // 链接卡片跟着富文本的光标：光标进入链接时出现在这一行下方，离开链接、失去焦点、窗口移动或失活时收起。
    private void QueueLinkCard()
    {
        if (cardQueued) return;
        cardQueued = true;
        Dispatcher.InvokeAsync(() => { cardQueued = false; UpdateLinkCard(); }, DispatcherPriority.Loaded);
    }

    private void UpdateLinkCard()
    {
        var link = Format == NoteFormat.RichText && Rich.IsKeyboardFocused ? LinkAt(Rich.Selection.Start) : null;
        if (link is null || LinkAt(Rich.Selection.End) != link || link.NavigateUri is not Uri uri) { HideLinkCard(); return; }
        var caret = Rich.Selection.End.GetCharacterRect(LogicalDirection.Backward);
        if (caret.IsEmpty || caret.Bottom < 0 || caret.Top > Rich.ActualHeight) { HideLinkCard(); return; }
        var start = link.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        if (link != cardLink) LinkCardCopy.Content = "复制地址";
        cardLink = link;
        LinkCardAddress.Text = NoteLinks.Readable(uri);
        LinkCardOpen.Content = OpenText(uri);
        var left = !start.IsEmpty && Math.Abs(start.Bottom - caret.Bottom) < 1 ? start.Left : caret.Left;
        LinkCard.PlacementRectangle = new Rect(left, caret.Top, 1, caret.Height);
        LinkCard.IsOpen = true;
    }

    private void HideLinkCard()
    {
        LinkCard.IsOpen = false;
        cardLink = null;
    }

    private void WatchCardOwner()
    {
        if (Window.GetWindow(this) is not Window window || window == cardOwner) return;
        cardOwner = window;
        window.Deactivated += (_, _) => HideLinkCard();
        window.LocationChanged += (_, _) => HideLinkCard();
    }

    private static Hyperlink? LinkAt(TextPointer position)
    {
        for (DependencyObject? current = position.Parent; current is TextElement element; current = element.Parent)
            if (element is Hyperlink link) return link;
        return null;
    }

    private void OnCardOpen(object sender, RoutedEventArgs e)
    {
        if (cardLink?.NavigateUri is not Uri uri) return;
        HideLinkCard();
        LinkOpener(uri);
    }

    private void OnCardCopy(object sender, RoutedEventArgs e)
    {
        if (cardLink?.NavigateUri is Uri uri && CopyAddress(uri)) LinkCardCopy.Content = "已复制";
    }

    private void OnCardEdit(object sender, RoutedEventArgs e)
    {
        if (cardLink is not { } link) return;
        HideLinkCard();
        EditLink(link);
    }

    private void OnCardUnlink(object sender, RoutedEventArgs e)
    {
        if (cardLink is not { } link) return;
        HideLinkCard();
        Unlink(link);
    }
}
