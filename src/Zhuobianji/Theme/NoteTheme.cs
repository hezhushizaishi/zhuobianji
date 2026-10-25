using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

    // 按钮的悬停底和选中底由正文色加透明度得到，任何配色、深浅色下都只是略深（略亮）一层。
    internal SolidColorBrush Subtle { get; } = Tint(Text, 0x12);
    internal SolidColorBrush Selected { get; } = Tint(Text, 0x20);

    // 界面动画：快（悬停、淡入）、标准（展开、滑出），一律减速收尾，只动透明度和位移。
    // 系统关闭「窗口内的动画」时时长为 0，动画直接到终点。样式里用 x:Static 引用，所以是 public。
    public static readonly bool Animate = SystemParameters.ClientAreaAnimation;
    public static readonly Duration Fast = new(TimeSpan.FromMilliseconds(Animate ? 120 : 0));
    public static readonly Duration Standard = new(TimeSpan.FromMilliseconds(Animate ? 180 : 0));
    public static readonly IEasingFunction Ease = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    // 正文排版：行高按字号倍数，段落与列表只留很小的段后距。
    internal const double LineHeightFactor = 1.45;
    internal const double ParagraphSpacing = 3;
    internal const double ListSpacing = 6;
    internal const double ListIndent = 20;
    internal const double BlockSpacing = 12;

    // 富文本里显式写着这些颜色时视为「跟随主题」：存储时去掉，读取和粘贴时换成当前主题的颜色。
    // 0.8.x 及以前会把当时的主题正文色、链接色写进存储，旧值固定列在这里；以后改主题色时这两行保持不动。
    private static readonly Color[] LegacyText = [C("#303C49"), C("#E4ECF5")];
    private static readonly Color[] LegacyLink = [C("#3D75B5"), C("#83B7F0")];

    internal static bool IsThemeText(Color color) => LegacyText.Contains(color) ||
        color == For(new NoteAppearance()).Text.Color || color == For(new NoteAppearance { Dark = true }).Text.Color;
    internal static bool IsThemeLink(Color color) => LegacyLink.Contains(color) ||
        color == For(new NoteAppearance()).Link.Color || color == For(new NoteAppearance { Dark = true }).Link.Color;

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
        var color = C(surface);
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
        resources["Subtle"] = Subtle;
        resources["Selected"] = Selected;
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
        var brush = new SolidColorBrush(C(value));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Tint(SolidColorBrush brush, byte alpha)
    {
        var color = brush.Color;
        return Frozen(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)));
    }

    private static T Frozen<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }

    private static Color C(string value) => (Color)ColorConverter.ConvertFromString(value);
}
