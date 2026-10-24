using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using Zhuobianji.Core;

namespace Zhuobianji;

// 外观的唯一来源：便签配色、语义色和正文排版间距都在这里定义。
// 便签窗口通过 ApplyTo 写入资源键，样式（Theme/NoteStyles.xaml）只引用这些键。
internal sealed record NoteTheme(SolidColorBrush Surface, SolidColorBrush Header, SolidColorBrush Text, SolidColorBrush Muted,
    SolidColorBrush Border, SolidColorBrush Accent, SolidColorBrush Code, SolidColorBrush Link)
{
    internal static readonly (string Key, string Name)[] Palettes =
        [("mist", "雾蓝"), ("cream", "奶油白"), ("yellow", "淡黄"), ("green", "浅绿"), ("pink", "浅粉"), ("neutral", "中性灰")];

    // 富文本「文字颜色」菜单；第一项「默认文字」使用当前主题的正文色。
    internal static readonly (string Name, SolidColorBrush Brush)[] TextColors =
        [("蓝色", B("#447EC2")), ("红色", B("#C95555")), ("绿色", B("#39906F")), ("紫色", B("#9963B6"))];

    internal static readonly SolidColorBrush Danger = B("#D64B45");
    internal static readonly SolidColorBrush Error = B("#B22222");
    internal static readonly SolidColorBrush Highlight = B("#F1DC83");

    // 正文排版：行高按字号倍数，段落与列表只留很小的段后距。
    internal const double LineHeightFactor = 1.45;
    internal const double ParagraphSpacing = 3;
    internal const double ListSpacing = 6;
    internal const double ListIndent = 20;
    internal const double BlockSpacing = 12;

    private static readonly Dictionary<(string Palette, bool Dark), NoteTheme> Cache = [];

    internal static NoteTheme For(NoteAppearance appearance)
    {
        var key = (appearance.Palette, appearance.Dark);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var surface = (appearance.Palette, appearance.Dark) switch
        {
            ("cream", false) => "#FFF9E7", ("yellow", false) => "#FFF3C9",
            ("green", false) => "#EFF6EB", ("pink", false) => "#FBEFF1", ("neutral", false) => "#FAFAFA",
            ("cream", true) => "#302D25", ("yellow", true) => "#363022",
            ("green", true) => "#283127", ("pink", true) => "#35292E", ("neutral", true) => "#26282B",
            (_, true) => "#242D37", _ => "#EEF4FA"
        };
        var theme = appearance.Dark
            ? new NoteTheme(B(surface), HeaderFor(surface, dark: true), B("#E4ECF5"), B("#ADBCCA"), B("#465160"), B("#394C63"), B("#1C252E"), B("#83B7F0"))
            : new NoteTheme(B(surface), HeaderFor(surface, dark: false), B("#303C49"), B("#607183"), B("#CFD9E3"), B("#D6E4F1"), B("#E2EAF2"), B("#3D75B5"));
        Cache[key] = theme;
        return theme;
    }

    // 顶栏比正文底色略深（浅色）或略亮（深色），与正文区分。
    private static SolidColorBrush HeaderFor(string surface, bool dark)
    {
        var color = (Color)ColorConverter.ConvertFromString(surface);
        byte Shade(byte value) => dark ? (byte)(value + (255 - value) * 0.07) : (byte)(value * 0.96);
        var brush = new SolidColorBrush(Color.FromRgb(Shade(color.R), Shade(color.G), Shade(color.B)));
        brush.Freeze();
        return brush;
    }

    internal void ApplyTo(ResourceDictionary resources)
    {
        resources["Surface"] = Surface;
        resources["Header"] = Header;
        resources["Text"] = Text;
        resources["Muted"] = Muted;
        resources["Edge"] = Border;
        resources["Accent"] = Accent;
        resources["Danger"] = Danger;
        resources["Error"] = Error;
    }

    // 设置、链接和确认框使用 WPF UI 控件，深浅色跟随便签。
    internal static void ApplyFluent(ResourceDictionary resources, bool dark)
    {
        var theme = new ThemesDictionary { Theme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light };
        var index = resources.MergedDictionaries.ToList().FindIndex(dictionary => dictionary is ThemesDictionary);
        if (index >= 0) resources.MergedDictionaries[index] = theme;
        else resources.MergedDictionaries.Insert(0, theme);
        if (!resources.MergedDictionaries.OfType<ControlsDictionary>().Any()) resources.MergedDictionaries.Add(new ControlsDictionary());
    }

    internal static SolidColorBrush B(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        brush.Freeze();
        return brush;
    }
}
