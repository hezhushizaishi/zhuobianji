using System.Text.Json.Serialization;

namespace Zhuobianji.Core;

[JsonConverter(typeof(JsonStringEnumConverter<NoteFormat>))]
public enum NoteFormat { RichText, Markdown, PlainText }

[JsonConverter(typeof(JsonStringEnumConverter<NoteThemeMode>))]
public enum NoteThemeMode { Light, Dark, System }

public sealed record NoteAppearance
{
    public string Palette { get; init; } = "mist";
    public string FontFamily { get; init; } = "Microsoft YaHei UI";
    public double FontSize { get; init; } = 17;
    public double Opacity { get; init; } = 1;
    // 深浅色是整个程序的模式（NotePreferences.ThemeMode），不在便签里存。0.8.x 每张便签各存一份 Dark：
    // 只在设置里还没有模式时读一次、用来定下模式，读完清掉，之后不再写出。
    [JsonPropertyName("Dark"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyDark { get; init; }
}

public sealed record NotePreferences
{
    public NoteFormat DefaultFormat { get; init; } = NoteFormat.RichText;
    public NoteAppearance Appearance { get; init; } = new();
    public NoteHotkeys Hotkeys { get; init; } = new();
    public bool ShowFormatWarnings { get; init; } = true;
    // 0.8.x 的设置里没有这一项（null），第一次启动时按主便签的深浅定下来并存进设置。
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