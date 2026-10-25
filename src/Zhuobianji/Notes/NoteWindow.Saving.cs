using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;
using Zhuobianji.Core;

namespace Zhuobianji;

// 便签保存：正文或状态变化后 400ms 防抖写入；拖动、缩放期间暂停，松手后续写。
// 状态字段（隐藏、收纳、排序、置顶等）一律通过 Update 或 TryCommit 修改。
internal partial class NoteWindow
{
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool documentDirty;
    private bool moving;
    internal bool IsDirty { get; private set; }
    internal string? SaveError { get; private set; }
    internal string SaveState { get; private set; } = "";

    private void InitializeSaving()
    {
        ShowSaveState("已保存");
        saveTimer.Tick += (_, _) => { if (!moving) SaveNow(); };
        Editor.ContentChanged += () =>
        {
            Data = Data with { Text = Editor.TextContent, Format = Editor.Format, PreviewMarkdown = Editor.IsPreview };
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
        var next = change(Data);
        if (next == Data) return;
        var mainChanged = next.IsCollectionMain != Data.IsCollectionMain;
        Data = next;
        if (mainChanged) ApplyCollectionMain();
        MarkDirty(documentChanged: false);
    }

    // 修改状态字段并立即保存；保存失败时恢复原状态，界面不会停在磁盘上不存在的状态。
    internal bool TryCommit(Func<NoteData, NoteData> change)
    {
        var before = Data;
        Update(change);
        if (SaveNow()) return true;
        Update(_ => before);
        return false;
    }

    internal void MarkDirty(bool documentChanged = true)
    {
        if (permanentlyClosing) return;
        IsDirty = true;
        documentDirty |= documentChanged;
        if (documentChanged && SaveError is null) ShowSaveState("保存中");
        saveTimer.Stop();
        if (!moving) saveTimer.Start();
        if (documentChanged) controller.NoteChanged();
    }

    // 当前内容和窗口几何；坐标与尺寸只算正文卡片，不含四周的阴影边距和主便签左侧的标签区。
    internal NoteData Snapshot()
    {
        var bounds = desktop.IsAttached ? desktop.ScreenBounds()
            : WindowState == WindowState.Normal || RestoreBounds.IsEmpty ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        return (documentDirty ? Editor.Snapshot(Data) : Data) with
        {
            Left = bounds.Left + CollectionRailWidth + ShadowMargin,
            Top = bounds.Top + ShadowMargin,
            Width = bounds.Width - CollectionRailWidth - 2 * ShadowMargin,
            Height = bounds.Height - 2 * ShadowMargin,
            IsPinned = desktop.IsAttached ? Data.IsPinned : Topmost,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    internal bool SaveNow()
    {
        saveTimer.Stop();
        if (!IsDirty) return true;
        var reportSaved = documentDirty || SaveError is not null;
        var snapshot = Snapshot();
        if (!controller.Save(snapshot, out var error))
        {
            ReportSaveError(error!);
            return false;
        }
        Data = snapshot;
        IsDirty = false;
        documentDirty = false;
        SaveError = null;
        if (reportSaved) ShowSaveState("已保存");
        controller.NoteChanged();
        return true;
    }

    internal void ReportSaveError(string message)
    {
        SaveError = message;
        ShowSaveState("保存失败 · 点击重试", message);
        controller.NoteChanged();
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

    // WM_ENTERSIZEMOVE / WM_EXITSIZEMOVE：拖动或缩放期间不写盘。
    private nint OnMoveMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0231) { moving = true; saveTimer.Stop(); }
        else if (message == 0x0232)
        {
            moving = false;
            if (IsDirty) saveTimer.Start();
        }
        return 0;
    }
}
