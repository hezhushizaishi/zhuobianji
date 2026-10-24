using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Zhuobianji.Core;

namespace Zhuobianji;

internal sealed partial class NoteController : IDisposable
{
    private readonly NoteStore store;
    private readonly Action exitApplication;
    private TrayMenu? tray;
    private SettingsWindow? settings;
    private readonly WindowsStartup startup;
    private bool disposed;
    private readonly bool enableHotkeys;
    private GlobalHotkeys? hotkeys;
    internal Dictionary<Guid, NoteWindow> Notes { get; } = [];
    internal NotePreferences Preferences { get; private set; } = new();
    internal string DataDirectory => store.DirectoryPath;

    internal NoteController(NoteStore store, Action exitApplication, WindowsStartup? startup = null, bool enableHotkeys = true)
    {
        this.store = store;
        this.enableHotkeys = enableHotkeys;
        this.exitApplication = exitApplication;
        this.startup = startup ?? new WindowsStartup(Path.Combine(AppContext.BaseDirectory, "桌边记.exe"));
    }

    internal void Start()
    {
        var loaded = store.LoadAll();
        var readErrors = loaded.Errors.ToList();
        try { Preferences = store.LoadPreferences(); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        { readErrors.Add("默认外观读取失败，暂用初始外观：" + ex.Message); }
        var modeErrors = new List<string>();
        tray = new TrayMenu(this);
        if (enableHotkeys)
        {
            hotkeys = new GlobalHotkeys(() => CreateNote(), ToggleAll);
            if (!hotkeys.TryApply(Preferences.Hotkeys, out var error))
                Dialogs.Show(error + "\n请到设置的快捷键页面更换。", "快捷键");
        }
        foreach (var data in loaded.Notes)
        {
            try { AddWindow(data); }
            catch (InvalidDataException ex) { readErrors.Add($"{data.Id:N}：{ex.Message}"); continue; }

        }
        // 先恢复关系，再显示窗口，避免已收纳的便签在启动时闪现。
        foreach (var window in Notes.Values)
        {
            if (window.Data.CollectedBy is Guid parent && (MainNote is null || MainNote.Data.Id != parent))
            { window.Update(data => data with { IsCollectionMain = false, CollectedBy = null }); window.SaveNow(); }
            if (window.Data.CollectedBy is not null) continue;
            if (window.Data.IsOnDesktop && !window.TrySetDesktopMode(true, out var error)) modeErrors.Add(error!);
            if (!window.Data.IsHidden) window.Reveal(activate: false);
        }
        RefreshCollections();
        if (Notes.Count == 0) CreateNote();
        else
        {
            var visible = RootNotes.Where(w => !w.Data.IsHidden).ToArray();
            if (visible.Length == 0)
            {
                var latest = RootNotes.MaxBy(w => w.Data.UpdatedAt)!;
                latest.Update(data => data with { IsHidden = false });
                latest.SaveNow();
                latest.Reveal(activate: true);
            }
            else
            {
                // 普通浮窗在启动时明确前置；桌面便签仍留在桌面层。
                (visible.Where(w => !w.IsDesktopAttached).MaxBy(w => w.Data.UpdatedAt)
                    ?? visible.MaxBy(w => w.Data.UpdatedAt))!.Reveal(activate: true);
            }
        }
        if (readErrors.Count > 0)
            Dialogs.Show("部分数据无法读取，原文件已保留，其他便签可以继续使用。\n\n" + string.Join("\n", readErrors), "数据提示");
        if (modeErrors.Count > 0)
            Dialogs.Show("当前无法贴到桌面，便签先以浮窗显示。稍后可从更多操作重试。\n\n" + string.Join("\n", modeErrors.Distinct()), "桌面模式");
    }

    private NoteWindow AddWindow(NoteData data)
    {
        var window = new NoteWindow(data, this);
        Notes.Add(data.Id, window);
        return window;
    }

    internal NoteWindow CreateNote(NoteWindow? previous = null, NoteFormat? format = null, Guid? collectedBy = null)
    {
        var offset = Notes.Count % 8 * 24;
        var workArea = SystemParameters.WorkArea;
        var previousBounds = previous?.Snapshot();
        var data = new NoteData
        {
            Left = previousBounds is null ? workArea.Right - 470 - offset : previousBounds.Left + 24,
            Top = previousBounds is null ? workArea.Top + 90 + offset : previousBounds.Top + 24,
            Width = 420, Height = 440, Format = format ?? Preferences.DefaultFormat, Appearance = Preferences.Appearance, CollectedBy = collectedBy,
            IsOnDesktop = previous?.Data.IsOnDesktop ?? (Notes.Count > 0 && Notes.Values.All(w => w.Data.IsOnDesktop))
        };
        var window = AddWindow(data);
        if (collectedBy is null && data.IsOnDesktop && !window.TrySetDesktopMode(true, out var error))
            Dialogs.Show(error, "桌面模式");
        window.MarkDirty();
        window.SaveNow();
        if (collectedBy is null) window.Reveal(activate: true);
        RefreshCollections();
        return window;
    }

    internal bool Save(NoteData data, out string? error)
    {
        try { store.Save(data); error = null; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { error = ex.Message; return false; }
    }

    internal void HideNote(NoteWindow window)
    {
        if (window.Data.IsCollectionMain)
            foreach (var child in CollectedNotes(window).Where(child => child.IsVisible).ToArray())
                if (!SaveAndHide(child)) return;
        SaveAndHide(window);
    }

    // 待写正文和隐藏状态一次提交，成功才隐藏；失败时窗口和错误提示留在原处。
    private bool SaveAndHide(NoteWindow window)
    {
        if (!window.TryCommit(data => data with { IsHidden = true })) return false;
        window.Hide();
        NoteChanged();
        return true;
    }

    // 显示不因保存失败而中止：状态留在内存，窗口上的保存提示负责重试。
    private static void ShowSaved(NoteWindow window, bool activate)
    {
        window.Update(data => data with { IsHidden = false });
        window.SaveNow();
        window.Reveal(activate);
    }

    internal void ShowNote(NoteWindow window)
    {
        if (window.Data.CollectedBy is not null) { ExpandCollectedNote(window); return; }
        ShowSaved(window, activate: true);
    }
    internal void ShowAll()
    {
        foreach (var window in RootNotes.ToArray()) ShowSaved(window, activate: false);
        (MainNote ?? RootNotes.FirstOrDefault())?.Activate();
    }

    internal void HideAll()
    {
        foreach (var window in RootNotes.ToArray()) HideNote(window);
    }
    internal bool SetDesktopMode(NoteWindow window, bool enabled, bool showErrors = true)
    {
        if (!window.TrySetDesktopMode(enabled, out var error))
        {
            if (showErrors) Dialogs.Show(error, "桌面模式");
            return false;
        }
        return window.SaveNow();
    }

    internal bool SetAllDesktopModes(bool enabled, bool showErrors = true)
    {
        var errors = new List<string>();
        var succeeded = true;
        foreach (var window in RootNotes.ToArray())
        {
            if (!window.TrySetDesktopMode(enabled, out var error)) { errors.Add(error!); succeeded = false; }
            else if (!window.SaveNow()) succeeded = false;
        }
        if (showErrors && errors.Count > 0) Dialogs.Show(string.Join("\n", errors.Distinct()), "桌面模式");
        return succeeded;
    }

    internal bool DeleteNote(NoteWindow window)
    {
        if (!Notes.ContainsKey(window.Data.Id)) return true;
        if (window.Data.IsCollectionMain)
        {
            foreach (var child in CollectedNotes(window).ToArray()) if (!ReleaseNote(child)) return false;
        }
        try
        {
            store.Delete(window.Data.Id);
            if (settings?.TargetNote == window) settings.Close();
            Notes.Remove(window.Data.Id);
            window.ClosePermanently();
            RefreshCollections();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            window.ReportSaveError("删除未完成：" + ex.Message);
            return false;
        }
    }
    internal void ToggleAll()
    {
        if (Notes.Count == 0) CreateNote();
        else if (RootNotes.Any(w => w.IsVisible)) HideAll();
        else ShowAll();
    }
    internal bool FlushAll()
    {
        var succeeded = true;
        foreach (var window in Notes.Values) if (!window.SaveNow()) succeeded = false;
        return succeeded;
    }

    internal bool RequestExit()
    {
        if (!FlushAll())
        {
            foreach (var window in Notes.Values.Where(w => w.IsDirty).ToArray()) ShowNote(window);
            Dialogs.Show("有便签尚未保存，程序保持运行。请在提示保存失败的便签上重试，保存成功后再退出。", "尚未保存");
            return false;
        }
        Dispose();
        exitApplication();
        return true;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        hotkeys?.Dispose();
        hotkeys = null;
        tray?.Dispose();
        settings?.Close();
        foreach (var window in Notes.Values.ToArray()) window.ClosePermanently();
        Notes.Clear();
        store.Dispose();
    }
}
