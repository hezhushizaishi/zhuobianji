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

    // 标题栏、标签、托盘用的第一行，由正文算出来，不存（0.8.x 的文件里有这个字段，读的时候忽略）。
    [System.Text.Json.Serialization.JsonIgnore]
    public string Preview
    {
        // 跳过开头的空行，取到下一个换行为止（只有空格的一行也算第一行，显示「空白便签」）；不拆整篇正文。
        get
        {
            var rest = Text.AsSpan().TrimStart("\r\n");
            var end = rest.IndexOfAny('\r', '\n');
            var line = (end < 0 ? rest : rest[..end]).Trim();
            return line.Length == 0 ? "空白便签" : line.Length > 24 ? string.Concat(line[..24], "…") : line.ToString();
        }
    }
}
