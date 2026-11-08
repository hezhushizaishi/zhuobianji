using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Zhuobianji;

// 用到的图标和它们在 Fluent UI System Icons 字体里的码位（取自 WPF UI 4.3.0 的 SymbolRegular，字体 1.1.242）。
// 加图标：在这里加一项（实心的再加进 NoteIcon.FilledGlyphs），然后跑 scripts/subset-icons.ps1 重新生成 Assets/Icons 下的字体。
public enum NoteIconSymbol
{
    Add20 = 0xF109, Archive20 = 0xF139, ArrowDownload20 = 0xF150, ArrowExport20 = 0xF0280, ArrowMinimize20 = 0xF165,
    BookOpen20 = 0xE178, Certificate20 = 0xF27E, Checkmark20 = 0xF294, ChevronDown20 = 0xF2A3, ChevronRight20 = 0xF2B0,
    ClipboardPaste20 = 0xF2D5, Code20 = 0xF2EF, Copy20 = 0xF32B, Cut20 = 0xF33A, Delete20 = 0xF34C,
    Desktop20 = 0xF359, Dismiss20 = 0xF369, Document20 = 0xF378, DocumentText20 = 0xE557, Eye20 = 0xE5F2,
    EyeOff20 = 0xE5F5, Folder20 = 0xF418, FolderOpen20 = 0xF42E, Link20 = 0xF4E4, LinkDismiss20 = 0xE773,
    LinkEdit20 = 0xF4E9, Markdown20 = 0xE7DB, MoreHorizontal20 = 0xE824, Notepad20 = 0xF576, Open20 = 0xF582,
    PanelLeft20 = 0xE8B0, Pin20 = 0xF601, Power20 = 0xF60E, Settings20 = 0xF6A9, TextBold20 = 0xF7A4,
    TextBulletListLtr20 = 0xECD9, TextColor20 = 0xF7BF, TextEditStyle20 = 0xF7D9, TextItalic20 = 0xF7F4, TextNumberListLtr20 = 0xF7F9,
    TextStrikethrough20 = 0xED5F, TextT20 = 0xED63, TextUnderline20 = 0xF80A, Window20 = 0xF8B5, WindowMultiple20 = 0xF8BC
}

// 图标按文字画：透明 Grid 里一个居中的 TextBlock，字体是只留了用到的字形的 Fluent 图标字体（Assets/Icons，MIT）。
// 布局照搬原来用的 WPF UI SymbolIcon，画出来一样：Foreground、FontSize 像文字一样往下继承（样式 Icon 定为 16），
// 字重、字形固定为常规；Filled 换成实心字体和实心字形。
public sealed class NoteIcon : FrameworkElement
{
    private static readonly Uri Assets = new("pack://application:,,,/Zhuobianji;component/");
    internal static readonly FontFamily Regular = new(Assets, "./Assets/Icons/#FluentSystemIcons-Regular");
    internal static readonly FontFamily Solid = new(Assets, "./Assets/Icons/#FluentSystemIcons-Filled");

    // 实心字形的码位和空心的不同；只列用到的。
    internal static readonly Dictionary<NoteIconSymbol, int> FilledGlyphs = new()
    {
        [NoteIconSymbol.Pin20] = 0xF60B
    };

    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(nameof(Symbol), typeof(NoteIconSymbol),
        typeof(NoteIcon), new FrameworkPropertyMetadata(default(NoteIconSymbol), (d, _) => ((NoteIcon)d).UpdateGlyph()));
    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(nameof(Filled), typeof(bool),
        typeof(NoteIcon), new FrameworkPropertyMetadata(false, (d, _) => ((NoteIcon)d).UpdateGlyph()));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(NoteIcon));
    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(NoteIcon));

    private readonly Grid root = new() { Background = Brushes.Transparent, SnapsToDevicePixels = true };
    private readonly TextBlock glyph = new()
    {
        Style = null, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center, FontFamily = Regular, FontStyle = FontStyles.Normal, FontWeight = FontWeights.Normal,
        Focusable = false
    };

    static NoteIcon()
    {
        FocusableProperty.OverrideMetadata(typeof(NoteIcon), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(NoteIcon), new FrameworkPropertyMetadata(false));
    }

    public NoteIcon()
    {
        root.Children.Add(glyph);
        AddVisualChild(root);
    }

    public NoteIconSymbol Symbol
    {
        get => (NoteIconSymbol)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public bool Filled
    {
        get => (bool)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    private void UpdateGlyph()
    {
        var filled = Filled && FilledGlyphs.ContainsKey(Symbol);
        glyph.FontFamily = filled ? Solid : Regular;
        glyph.Text = Symbol == default ? "" : char.ConvertFromUtf32(filled ? FilledGlyphs[Symbol] : (int)Symbol);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => index == 0 ? root : throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size MeasureOverride(Size availableSize)
    {
        root.Measure(availableSize);
        return root.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        root.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
