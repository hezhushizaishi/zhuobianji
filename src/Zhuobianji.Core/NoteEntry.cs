namespace Zhuobianji.Core;

// 一张便签的状态：内存里的最新数据、有没有没存的改动、上次保存的错误。只有 NoteBook 能改。
public sealed class NoteEntry
{
    internal NoteEntry(NoteData data) => Data = data;

    public Guid Id => Data.Id;
    public NoteData Data { get; internal set; }
    public bool IsDirty { get; internal set; }
    public string? SaveError { get; internal set; }
    // 便签开着窗口时由窗口提供：正文和位置还在窗口里，保存前要取出来。
    public INoteView? View { get; set; }
}

public interface INoteView
{
    // 保存前：把编辑器里的正文和窗口的位置、尺寸写进 data 交出来。
    NoteData Capture(NoteData data);

    // 保存后：error 为 null 表示存上了。
    void Saved(string? error);
}
