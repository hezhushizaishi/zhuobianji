using System.Windows.Media;
using Zhuobianji.Core;

namespace Zhuobianji;

internal sealed record NoteTheme(SolidColorBrush Surface, SolidColorBrush Text, SolidColorBrush Muted, SolidColorBrush Border, SolidColorBrush Accent, SolidColorBrush Code, SolidColorBrush Link)
{
    internal static readonly (string Key, string Name)[] Palettes =
        [("mist", "雾蓝"), ("cream", "奶油白"), ("yellow", "淡黄"), ("green", "浅绿"), ("pink", "浅粉"), ("neutral", "中性灰")];

    internal static NoteTheme For(NoteAppearance appearance)
    {
        var surface = (appearance.Palette, appearance.Dark) switch
        {
            ("cream", false) => "#FFF9E7", ("yellow", false) => "#FFF3C9",
            ("green", false) => "#EFF6EB", ("pink", false) => "#FBEFF1", ("neutral", false) => "#FAFAFA",
            ("cream", true) => "#302D25", ("yellow", true) => "#363022",
            ("green", true) => "#283127", ("pink", true) => "#35292E", ("neutral", true) => "#26282B",
            (_, true) => "#242D37", _ => "#EEF4FA"
        };
        return appearance.Dark
            ? new(B(surface), B("#E4ECF5"), B("#ADBCCA"), B("#465160"), B("#394C63"), B("#1C252E"), B("#83B7F0"))
            : new(B(surface), B("#303C49"), B("#607183"), B("#CFD9E3"), B("#D6E4F1"), B("#E2EAF2"), B("#3D75B5"));
    }

    internal static SolidColorBrush B(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        brush.Freeze();
        return brush;
    }
}
