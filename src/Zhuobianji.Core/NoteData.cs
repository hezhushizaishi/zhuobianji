namespace Zhuobianji.Core;

public sealed record NoteData
{
    public int Version { get; init; } = 2;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Text { get; init; } = "";
    public NoteFormat Format { get; init; } = NoteFormat.RichText;
    public string? RichText { get; init; }
    public bool PreviewMarkdown { get; init; }
    public NoteAppearance Appearance { get; init; } = new();
    public double Left { get; init; } = 100;
    public double Top { get; init; } = 100;
    public double Width { get; init; } = 340;
    public double Height { get; init; } = 360;
    public bool IsPinned { get; init; }
    public bool IsOnDesktop { get; init; }
    public bool IsHidden { get; init; }
    public bool IsCollectionMain { get; init; }
    public bool ShowCollectionRail { get; init; } = true;
    public Guid? CollectedBy { get; init; }
    public Guid[] CollectionOrder { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string Preview
    {
        get
        {
            var line = Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            return line.Length == 0 ? "空白便签" : line.Length > 24 ? line[..24] + "…" : line;
        }
    }
}
