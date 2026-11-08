using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;
using Zhuobianji.Core;

namespace Zhuobianji;

// 便签保存：正文或状态变化后 400ms 防抖写入；拖动、缩放期间暂停，松手后续写。
// 状态字段（隐藏、收纳、排序、置顶等）一律通过 Update 或 TryCommit 修改，实际在 NoteBook 里改和存；
// 保存时 NoteBook 通过 INoteView 向窗口要正文和位置（Capture），存完告诉窗口结果（Saved）。
internal partial class NoteWindow
{
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    // 编辑器里有还没交给 NoteBook 的正文改动；保存时才把富文本序列化。
    private bool documentDirty;
    private bool errorShown;
    private bool moving;
    internal bool IsDirty => Entry.IsDirty;
    internal string? SaveError => Entry.SaveError;
    internal string SaveState { get; private set; } = "";

    private void InitializeSaving()
    {
        ShowSaveState("已保存");
        saveTimer.Tick += (_, _) => { if (!moving) SaveNow(); };
        Editor.ContentChanged += () =>
        {
            controller.Book.Update(Entry, data => data with { Text = Editor.TextContent, Format = Editor.Format, PreviewMarkdown = Editor.IsPreview });
            UpdateEditorButtons();
            UpdateTitle();
            MarkDirty();
        };
        LocationChanged += (_, _) => MarkDirty(documentChanged: false);
        SizeChanged += (_, _) => MarkDirty(documentChanged: false);
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnMoveMessage);
    }

    // 修改状态字段并排队保存。
    internal void Update(Func<NoteData, NoteData> change)
    {
        var before = Data;
        controller.Book.Update(Entry, change);
        if (!ReferenceEquals(before, Data)) MarkDirty(documentChanged: false);
    }

    // 修改状态字段并立即保存；保存失败时恢复原状态，界面不会停在磁盘上不存在的状态。
    internal bool TryCommit(Func<NoteData, NoteData> change) => controller.Book.TryCommit(Entry, change);

    // 状态被改了（窗口自己改的，或控制器、NoteBook 改的）：界面跟上。
    internal void OnDataChanged(NoteData before)
    {
        if (before.IsCollectionMain != Data.IsCollectionMain) ApplyCollectionMain();
    }

    internal void MarkDirty(bool documentChanged = true)
    {
        if (permanentlyClosing) return;
        controller.Book.MarkDirty(Entry);
        documentDirty |= documentChanged;
        if (documentChanged && !errorShown) ShowSaveState("保存中");
        saveTimer.Stop();
        if (!moving) saveTimer.Start();
    }

    // 当前内容和窗口几何；坐标与尺寸只算正文卡片，不含四周的阴影边距和主便签左侧的标签区。
    NoteData INoteView.Capture(NoteData data)
    {
        var bounds = desktop.IsAttached ? desktop.ScreenBounds()
            : WindowState == WindowState.Normal || RestoreBounds.IsEmpty ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        return (documentDirty ? Editor.Snapshot(data) : data) with
        {
            Left = bounds.Left + CollectionRailWidth + ShadowMargin,
            Top = bounds.Top + ShadowMargin,
            Width = bounds.Width - CollectionRailWidth - 2 * ShadowMargin,
            Height = bounds.Height - 2 * ShadowMargin,
            IsPinned = desktop.IsAttached ? data.IsPinned : Topmost
        };
    }

    void INoteView.Saved(string? error)
    {
        if (error is not null) { ReportSaveError(error); return; }
        var reportSaved = documentDirty || errorShown;
        documentDirty = false;
        errorShown = false;
        if (reportSaved) ShowSaveState("已保存");
    }

    // 现在存的话会写进去的数据（检查和「新建便签」的位置用）。
    internal NoteData Snapshot() => ((INoteView)this).Capture(Data) with { UpdatedAt = DateTimeOffset.UtcNow };

    internal bool SaveNow()
    {
        saveTimer.Stop();
        return controller.Book.Save(Entry);
    }

    internal void ReportSaveError(string message)
    {
        errorShown = true;
        ShowSaveState("保存失败 · 点击重试", message);
    }

    // 平时只有一个很淡的勾，保存中更淡；失败时换成红字，提示里是失败原因。
    private void ShowSaveState(string state, string? error = null)
    {
        SaveState = state;
        SaveStatus.ToolTip = error ?? state + "（Ctrl+S 立即保存）";
        AutomationProperties.SetName(SaveStatus, state);
        SaveIcon.Visibility = error is null ? Visibility.Visible : Visibility.Collapsed;
        SaveIcon.Opacity = state == "保存中" ? 0.3 : 0.6;
        SaveErrorText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // WM_ENTERSIZEMOVE / WM_EXITSIZEMOVE：拖动或缩放期间不写盘；松手后标签区宽度跟上卡片的新大小。
    private nint OnMoveMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0231) { moving = true; saveTimer.Stop(); }
        else if (message == 0x0232)
        {
            moving = false;
            Dispatcher.BeginInvoke(FitRailWidth);
            if (IsDirty) saveTimer.Start();
        }
        return 0;
    }
}
