using System.Text.Json.Serialization;

namespace Zhuobianji.Core;

[JsonConverter(typeof(JsonStringEnumConverter<NoteFormat>))]
public enum NoteFormat { RichText, Markdown, PlainText }

[JsonConverter(typeof(JsonStringEnumConverter<NoteThemeMode>))]
public enum NoteThemeMode { Light, Dark, System }

public sealed record NoteAppearance
{
    public string Palette { get; init; } = "mist";
    // 深浅色是整个程序的模式（NotePreferences.ThemeMode），由程序统一写入每张便签，不单独设置。
    public bool Dark { get; init; }
    public string FontFamily { get; init; } = "Microsoft YaHei UI";
    public double FontSize { get; init; } = 17;
    public double Opacity { get; init; } = 1;
}

public sealed record NotePreferences
{
    public NoteFormat DefaultFormat { get; init; } = NoteFormat.RichText;
    public NoteAppearance Appearance { get; init; } = new();
    public NoteHotkeys Hotkeys { get; init; } = new();
    public bool ShowFormatWarnings { get; init; } = true;
    // 0.8.x 的设置里没有这一项（null），启动时按主便签的深浅定下来。
    public NoteThemeMode? ThemeMode { get; init; }
}

public sealed record HotkeyBinding(uint Modifiers = 0, uint Key = 0)
{
    [JsonIgnore] public bool IsEnabled => Key != 0;
}

public sealed record NoteHotkeys
{
    public HotkeyBinding NewNote { get; init; } = new(3, 0x4E);
    public HotkeyBinding ToggleNotes { get; init; } = new(3, 0x20);
}