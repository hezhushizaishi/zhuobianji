using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Navigation;
using Zhuobianji.Core;

namespace Zhuobianji;

internal partial class NoteEditor : UserControl
{
    private bool loading;
    private string text = "";
    internal NoteFormat Format { get; private set; }
    internal bool IsPreview { get; private set; }
    internal NoteAppearance Appearance { get; private set; } = new();
    public RichTextBox RichEditor => Rich;
    internal Action<Uri> LinkOpener { get; set; } = NoteLinks.Open;
    internal event Action? ContentChanged;

    public NoteEditor()
    {
        InitializeComponent();
        Rich.TextChanged += (_, _) => Changed();
        Loaded += (_, _) => { configuredLinks.Clear(); RefreshLinks(); if (IsPreview) RefreshPreview(); };
        Source.TextChanged += (_, _) => Changed();
        DataObject.AddPastingHandler(Rich, OnPasting);
        DataObject.AddPastingHandler(Source, OnPasting);
        RequestNavigateEventHandler navigate = (_, e) => { LinkOpener(e.Uri); e.Handled = true; };
        Rich.AddHandler(Hyperlink.RequestNavigateEvent, navigate);
        Preview.AddHandler(Hyperlink.RequestNavigateEvent, navigate);
        Rich.PreviewMouseLeftButtonDown += OnLinkMouseDown;
        Rich.PreviewMouseLeftButtonUp += OnLinkMouseUp;
        Rich.AllowDrop = true;
        Rich.PreviewDragOver += (_, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; } };
        Rich.PreviewDrop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
            foreach (var path in paths)
                if (NoteLinks.TryResolve(path, out var uri)) { InsertLink(uri); InsertPlain("\n"); }
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.V && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            { PastePlain(); e.Handled = true; }
        };
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "剪切", Command = ApplicationCommands.Cut });
        menu.Items.Add(new MenuItem { Header = "复制", Command = ApplicationCommands.Copy });
        menu.Items.Add(new MenuItem { Header = "粘贴（自动识别）", Command = ApplicationCommands.Paste });
        var markdown = new MenuItem { Header = "按 Markdown 粘贴" };
        markdown.Click += (_, _) => PasteMarkdown();
        menu.Items.Add(markdown);
        var plain = new MenuItem { Header = "按原文粘贴", InputGestureText = "Ctrl+Shift+V" };
        plain.Click += (_, _) => PastePlain();
        menu.Items.Add(plain);
        Rich.ContextMenu = menu;
    }

    internal void Initialize(NoteData data)
    {
        loading = true;
        Format = data.Format;
        Appearance = data.Appearance;
        text = data.Text;
        Rich.Document = FormattedDocuments.Create(Appearance);
        if (data.RichText is not null && Format == NoteFormat.RichText)
        {
            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data.RichText));
                new TextRange(Rich.Document.ContentStart, Rich.Document.ContentEnd).Load(stream, DataFormats.Xaml);
            }
            catch (Exception ex) when (ex is XamlParseException or System.Xml.XmlException or ArgumentException)
            { throw new InvalidDataException("富文本内容无法读取，原文件已保留。", ex); }
        }
        else Rich.Document = FormattedDocuments.FromText(data.Text, Appearance);
        Source.Text = data.Text;
        ApplyAppearance(Appearance);
        SetPreview(data.PreviewMarkdown);
        loading = false;
        RefreshLinks();
        UpdateHint();
    }

    internal string TextContent
    {
        get => text;
        set
        {
            if (Format != NoteFormat.RichText) Source.Text = value;
            else Rich.Document = FormattedDocuments.FromText(value, Appearance);
        }
    }

    private void Changed()
    {
        if (loading) return;
        text = Format != NoteFormat.RichText ? Source.Text : FormattedDocuments.PlainText(Rich.Document);
        RefreshLinks();
        if (IsPreview) RefreshPreview();
        UpdateHint();
        ContentChanged?.Invoke();
    }

    private void UpdateHint()
    {
        Hint.Visibility = text.Length == 0 && !IsPreview ? Visibility.Visible : Visibility.Collapsed;
        Hint.Foreground = NoteTheme.For(Appearance).Muted;
        Hint.FontFamily = new FontFamily(Appearance.FontFamily);
        Hint.FontSize = Appearance.FontSize;
    }

    internal NoteData Snapshot(NoteData data) => data with
    {
        Version = 2, Text = text, Format = Format,
        RichText = Format == NoteFormat.RichText ? FormattedDocuments.Save(Rich.Document, DataFormats.Xaml) : null,
        PreviewMarkdown = IsPreview, Appearance = Appearance
    };

    internal void ChangeFormat(NoteFormat target)
    {
        if (target == Format) return;
        string result;
        FlowDocument? richResult = null;
        if (target == NoteFormat.RichText)
        {
            richResult = Format == NoteFormat.Markdown ? FormattedDocuments.FromMarkdown(text, Appearance) : FormattedDocuments.FromText(text, Appearance, detectLinks: true);
            result = FormattedDocuments.PlainText(richResult);
        }
        else if (Format == NoteFormat.RichText)
            result = target == NoteFormat.Markdown ? DocumentText.Markdown(Rich.Document) : DocumentText.Plain(Rich.Document);
        else if (Format == NoteFormat.Markdown && target == NoteFormat.PlainText)
            result = DocumentText.Plain(FormattedDocuments.FromMarkdown(text, Appearance));
        else result = text;
        loading = true;
        Format = target;
        text = result;
        configuredLinks.Clear();
        if (richResult is not null) Rich.Document = richResult;
        Source.Text = result;
        SetPreview(false);
        ApplyAppearance(Appearance);
        loading = false;
        Changed();
        FocusEditor();
    }

    internal void SetPreview(bool enabled)
    {
        IsPreview = Format == NoteFormat.Markdown && enabled;
        Rich.Visibility = Format == NoteFormat.RichText ? Visibility.Visible : Visibility.Collapsed;
        Source.Visibility = Format != NoteFormat.RichText && !IsPreview ? Visibility.Visible : Visibility.Collapsed;
        Preview.Visibility = IsPreview ? Visibility.Visible : Visibility.Collapsed;
        Source.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, Format == NoteFormat.PlainText ? "纯文本内容" : "Markdown 源码");
        if (IsPreview) RefreshPreview();
        UpdateHint();
    }

    internal void FocusEditor()
    {
        if (Format == NoteFormat.RichText) Rich.Focus();
        else if (!IsPreview) Source.Focus();
        else Preview.Focus();
    }

    internal void ApplyAppearance(NoteAppearance value)
    {
        var wasLoading = loading;
        loading = true;
        var before = Appearance;
        var oldTheme = NoteTheme.For(before);
        var theme = NoteTheme.For(value);
        foreach (var element in DocumentText.Elements(Rich.Document))
        {
            if (element.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush brush && brush.Color == oldTheme.Text.Color)
                element.Foreground = theme.Text;
            else if (element is Hyperlink && element.Foreground is SolidColorBrush linkBrush && linkBrush.Color == oldTheme.Link.Color)
                element.Foreground = theme.Link;
            if (element.ReadLocalValue(TextElement.FontSizeProperty) is double size) element.FontSize = size * value.FontSize / before.FontSize;
            if (element.ReadLocalValue(TextElement.FontFamilyProperty) is FontFamily family && family.Source == before.FontFamily)
                element.FontFamily = new FontFamily(value.FontFamily);
            if (element is Paragraph paragraph && paragraph.Margin == new Thickness(0, 0, 0, 10))
                paragraph.Margin = new Thickness(0, 0, 0, 3);
        }
        Appearance = value;
        Rich.Document.FontFamily = new FontFamily(value.FontFamily);
        Rich.Document.FontSize = value.FontSize;
        Rich.Document.Foreground = theme.Text;
        FormattedDocuments.ApplyReadingLayout(Rich.Document, value);
        Rich.CaretBrush = theme.Text;
        Source.Foreground = theme.Text;
        Source.CaretBrush = theme.Text;
        Source.FontFamily = new FontFamily(Format == NoteFormat.PlainText ? value.FontFamily : "Consolas, Microsoft YaHei UI");
        Source.FontSize = value.FontSize;
        if (IsPreview) RefreshPreview();
        UpdateHint();
        loading = wasLoading;
    }

    internal IDataObject PreparePaste(IDataObject original)
    {
        if (Format == NoteFormat.PlainText) return original;
        FlowDocument? document = null;
        if (original.GetData(DataFormats.FileDrop) is string[] paths)
            document = FormattedDocuments.FromText(string.Join("\n", paths), Appearance, detectLinks: true);
        else if (original.GetData(DataFormats.Rtf) is string rtf)
        {
            document = FormattedDocuments.Create(Appearance);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rtf));
            new TextRange(document.ContentStart, document.ContentEnd).Load(stream, DataFormats.Rtf);
            PastedLinks.Apply(document, Appearance);
            // 有些来源的 RTF 丢掉 href，HTML 仍保留；此时使用带目标的版本。
            if (!DocumentText.Elements(document).OfType<Hyperlink>().Any() && original.GetData(DataFormats.Html) is string linkedHtml)
            {
                var htmlDocument = FormattedDocuments.FromHtml(linkedHtml, Appearance);
                if (DocumentText.Elements(htmlDocument).OfType<Hyperlink>().Any()) document = htmlDocument;
            }
            if (Format == NoteFormat.Markdown && !DocumentText.Elements(document).OfType<Hyperlink>().Any()) return original;
        }
        else if (original.GetData(DataFormats.Html) is string html)
            document = FormattedDocuments.FromHtml(html, Appearance);
        else if (Format == NoteFormat.RichText && original.GetData(DataFormats.UnicodeText) is string plain)
        {
            document = FormattedDocuments.LooksLikeMarkdown(plain)
                ? FormattedDocuments.FromMarkdown(plain, Appearance, pasted: true)
                : FormattedDocuments.FromText(plain, Appearance, detectLinks: true);
        }
        if (document is null) return original;
        FormattedDocuments.ApplyReadingLayout(document, Appearance, pasted: true);
        return Format == NoteFormat.Markdown
            ? new DataObject(DataFormats.UnicodeText, DocumentText.Markdown(document))
            : PasteData(document);
    }

    private static IDataObject PasteData(FlowDocument document) => new DataObject(DataFormats.Xaml, FormattedDocuments.Save(document, DataFormats.Xaml));

    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (Format == NoteFormat.PlainText) return;
        var prepared = PreparePaste(e.DataObject);
        if (ReferenceEquals(prepared, e.DataObject)) return;
        e.DataObject = prepared;
        e.FormatToApply = Format == NoteFormat.Markdown ? DataFormats.UnicodeText : DataFormats.Xaml;
    }
    internal void InsertPlain(string value)
    {
        if (Format != NoteFormat.RichText)
        {
            if (IsPreview) SetPreview(false);
            var start = Source.SelectionStart;
            Source.SelectedText = value;
            Source.CaretIndex = start + value.Length;
            Source.SelectionLength = 0;
        }
        else { Rich.Selection.Text = value; Rich.CaretPosition = Rich.Selection.End; }
        FocusEditor();
    }

    internal void InsertMarkdown(string value)
    {
        if (Format != NoteFormat.RichText) { InsertPlain(value); return; }
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(FormattedDocuments.Save(FormattedDocuments.FromMarkdown(value, Appearance, pasted: true), DataFormats.Xaml)));
        Rich.Selection.Load(stream, DataFormats.Xaml);
        Rich.CaretPosition = Rich.Selection.End;
        FocusEditor();
    }

    private void PastePlain()
    {
        try { if (Clipboard.ContainsText()) InsertPlain(Clipboard.GetText()); }
        catch (System.Runtime.InteropServices.COMException) { MessageBox.Show("剪贴板正忙，请稍后再试。", "桌边记 · 粘贴"); }
    }

    private void PasteMarkdown()
    {
        try { if (Clipboard.ContainsText()) InsertMarkdown(Clipboard.GetText()); }
        catch (System.Runtime.InteropServices.COMException) { MessageBox.Show("剪贴板正忙，请稍后再试。", "桌边记 · 粘贴"); }
    }

    internal void ToggleStrike()
    {
        var current = Rich.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        Rich.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
            current is TextDecorationCollection decorations && decorations.Any(d => d.Location == TextDecorationLocation.Strikethrough)
                ? new TextDecorationCollection() : TextDecorations.Strikethrough);
        Rich.Focus();
    }

    internal void SetTextColor(Brush brush) { Rich.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush); Rich.Focus(); }
}