using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Zhuobianji.Core;

namespace Zhuobianji;

// 便签的状态在 NoteBook（不碰界面）；这里按状态建窗口、显示隐藏，管托盘、设置、快捷键和桌面模式。
// 操作分两类：用户在某个窗口上做的（关闭、贴桌面、收纳、设为主便签）收 NoteWindow；可能针对没开窗口的便签的（显示、展开、取出、删除）收 NoteEntry。
internal sealed partial class NoteController : IDisposable
{
    private readonly NoteStore store;
    private readonly NoteBook book;
    private readonly Action exitApplication;
    private TrayMenu? tray;
    private SettingsWindow? settings;
    private readonly WindowsStartup startup;
    private bool disposed;
    private readonly bool enableHotkeys;
    private GlobalHotkeys? hotkeys;
    private readonly HashSet<Guid> confirmingDelete = [];
    private readonly System.Windows.Threading.Dispatcher dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
    internal NoteBook Book => book;
    // 已经建好的便签窗口。
    internal Dictionary<Guid, NoteWindow> Windows { get; } = [];
    internal TrayMenu? Tray => tray;
    internal NotePreferences Preferences => book.Preferences;
    internal string DataDirectory => store.DirectoryPath;

    internal NoteController(NoteStore store, Action exitApplication, WindowsStartup? startup = null, bool enableHotkeys = true)
    {
        this.store = store;
        book = new NoteBook(store);
        book.Changed += OnNoteChanged;
        this.enableHotkeys = enableHotkeys;
        this.exitApplication = exitApplication;
        this.startup = startup ?? new WindowsStartup(Path.Combine(AppContext.BaseDirectory, "桌边记.exe"));
    }

    internal NoteWindow? WindowOf(NoteEntry? note) => note is not null && Windows.TryGetValue(note.Id, out var window) ? window : null;
    internal bool IsShown(NoteEntry note) => WindowOf(note)?.IsVisible == true;
    private NoteWindow WindowFor(NoteEntry note) => Windows[note.Id];

    internal void Start()
    {
        var readErrors = book.Load();
        NoteTheme.DarkMode = ResolveDark();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        var modeErrors = new List<string>();
        tray = new TrayMenu(this);
        if (enableHotkeys)
        {
            hotkeys = new GlobalHotkeys(() => CreateNote(), ToggleAll);
            if (!hotkeys.TryApply(Preferences.Hotkeys, out var error))
                Dialogs.Show(error + "\n请到设置的快捷键页面更换。", "快捷键");
        }
        foreach (var note in book.Notes.ToArray())
        {
            try { AddWindow(note); }
            catch (InvalidDataException ex) { readErrors.Add($"{note.Id:N}：{ex.Message}"); book.Forget(note); }
        }
        // 先恢复关系，再显示窗口，避免已收纳的便签在启动时闪现。
        book.ReleaseOrphans();
        foreach (var note in book.Roots.ToArray())
        {
            var window = WindowFor(note);
            if (note.Data.IsOnDesktop && !window.TrySetDesktopMode(true, out var error)) modeErrors.Add(error!);
            if (!note.Data.IsHidden) window.Reveal(activate: false);
        }
        RefreshCollections();
        if (book.Notes.Count == 0) CreateNote();
        else
        {
            var visible = book.Roots.Where(note => !note.Data.IsHidden).ToArray();
            if (visible.Length == 0) ShowSaved(book.Roots.MaxBy(note => note.Data.UpdatedAt)!, activate: true);
            else
            {
                // 普通浮窗在启动时明确前置；桌面便签仍留在桌面层。
                var front = visible.Where(note => !WindowFor(note).IsDesktopAttached).MaxBy(note => note.Data.UpdatedAt)
                    ?? visible.MaxBy(note => note.Data.UpdatedAt)!;
                WindowFor(front).Reveal(activate: true);
            }
        }
        if (readErrors.Count > 0)
            Dialogs.Show("部分数据无法读取，原文件已保留，其他便签可以继续使用。\n\n" + string.Join("\n", readErrors), "数据提示");
        if (modeErrors.Count > 0)
            Dialogs.Show("当前无法贴到桌面，便签先以浮窗显示。稍后可从更多操作重试。\n\n" + string.Join("\n", modeErrors.Distinct()), "桌面模式");
    }

    private NoteWindow AddWindow(NoteEntry note)
    {
        var window = new NoteWindow(note, this);
        note.View = window;
        Windows.Add(note.Id, window);
        return window;
    }

    // 状态变了：窗口跟着调整；收在主便签里的便签变了，主便签的标签跟着刷新。
    private void OnNoteChanged(NoteEntry note, NoteData before)
    {
        WindowOf(note)?.OnDataChanged(before);
        if (book.Main is { } main && (note.Data.CollectedBy == main.Id || before.CollectedBy == main.Id)) RefreshMainTabs();
    }

    private void RefreshMainTabs() => WindowOf(book.Main)?.RefreshCollectionState();

    internal NoteWindow CreateNote(NoteWindow? previous = null, NoteFormat? format = null, Guid? collectedBy = null)
    {
        var offset = book.Notes.Count % 8 * 24;
        var workArea = SystemParameters.WorkArea;
        var previousBounds = previous?.Snapshot();
        var data = new NoteData
        {
            Left = previousBounds is null ? workArea.Right - 470 - offset : previousBounds.Left + 24,
            Top = previousBounds is null ? workArea.Top + 90 + offset : previousBounds.Top + 24,
            Width = 420, Height = 440, Format = format ?? Preferences.DefaultFormat, Appearance = Preferences.Appearance, CollectedBy = collectedBy,
            IsOnDesktop = previous?.Data.IsOnDesktop ?? (book.Notes.Count > 0 && book.Notes.All(note => note.Data.IsOnDesktop))
        };
        var window = AddWindow(book.Add(data));
        if (collectedBy is null && data.IsOnDesktop && !window.TrySetDesktopMode(true, out var error))
            Dialogs.Show(error, "桌面模式");
        window.MarkDirty();
        window.SaveNow();
        if (collectedBy is null) window.Reveal(activate: true);
        RefreshCollections();
        return window;
    }

    internal void HideNote(NoteWindow window)
    {
        if (window.Data.IsCollectionMain)
            foreach (var child in book.Collected(window.Entry).Where(IsShown).ToArray())
                if (!SaveAndHide(child)) return;
        SaveAndHide(window.Entry);
    }

    // 待写正文和隐藏状态一次提交，成功才隐藏；失败时窗口和错误提示留在原处。
    private bool SaveAndHide(NoteEntry note)
    {
        if (!book.TryCommit(note, data => data with { IsHidden = true })) return false;
        WindowOf(note)?.Hide();
        RefreshMainTabs();
        return true;
    }

    // 显示不因保存失败而中止：状态留在内存，窗口上的保存提示负责重试。
    private void ShowSaved(NoteEntry note, bool activate, double slide = 0)
    {
        book.Update(note, data => data with { IsHidden = false });
        book.Save(note);
        WindowFor(note).Reveal(activate, slide);
    }

    internal void ShowNote(NoteEntry note)
    {
        if (note.Data.CollectedBy is not null) { ExpandCollectedNote(note); return; }
        ShowSaved(note, activate: true);
    }

    internal void ShowAll()
    {
        foreach (var note in book.Roots.ToArray()) ShowSaved(note, activate: false);
        if ((book.Main ?? book.Roots.FirstOrDefault()) is { } front) WindowFor(front).Activate();
    }

    internal void HideAll()
    {
        foreach (var note in book.Roots.ToArray())
            if (WindowOf(note) is { } window) HideNote(window);
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

    // 没开窗口的便签只记下设置，显示时再挂到桌面。
    internal bool SetAllDesktopModes(bool enabled, bool showErrors = true)
    {
        var errors = new List<string>();
        var succeeded = true;
        foreach (var note in book.Roots.ToArray())
        {
            if (WindowOf(note) is not { } window)
            {
                if (!book.TryCommit(note, data => data with { IsOnDesktop = enabled })) succeeded = false;
            }
            else if (!window.TrySetDesktopMode(enabled, out var error)) { errors.Add(error!); succeeded = false; }
            else if (!window.SaveNow()) succeeded = false;
        }
        if (showErrors && errors.Count > 0) Dialogs.Show(string.Join("\n", errors.Distinct()), "桌面模式");
        return succeeded;
    }

    // 删除前确认；同一张便签的确认框只开一个（重复点击垃圾桶不会叠出好几个）。
    internal async Task ConfirmDelete(NoteEntry note, Window owner)
    {
        if (!confirmingDelete.Add(note.Id)) return;
        try
        {
            var main = note.Data.IsCollectionMain;
            if (await Dialogs.Confirm(owner, note.Data.Appearance, main ? "删除主便签" : "删除便签",
                main ? "主便签会永久删除，无法撤销。\n收纳的便签会释放到桌面。" : "这张便签会永久删除，无法撤销。",
                "删除", danger: true))
                DeleteNote(note);
        }
        finally { confirmingDelete.Remove(note.Id); }
    }

    internal bool DeleteNote(NoteEntry note)
    {
        if (book.Find(note.Id) is null) return true;
        if (note.Data.IsCollectionMain)
        {
            foreach (var child in book.Collected(note).ToArray()) if (!ReleaseNote(child)) return false;
        }
        var window = WindowOf(note);
        try { book.Delete(note); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (window is not null) window.ReportSaveError("删除未完成：" + ex.Message);
            else Dialogs.Show("删除未完成：" + ex.Message, "删除");
            return false;
        }
        if (window is not null)
        {
            if (settings?.TargetNote == window) settings.Close();
            Windows.Remove(note.Id);
            window.ClosePermanently();
        }
        RefreshCollections();
        return true;
    }

    internal void ToggleAll()
    {
        if (book.Notes.Count == 0) CreateNote();
        else if (book.Roots.Any(IsShown)) HideAll();
        else ShowAll();
    }

    internal bool FlushAll() => book.SaveAll();

    internal bool RequestExit()
    {
        if (!FlushAll())
        {
            foreach (var note in book.Notes.Where(note => note.IsDirty).ToArray()) ShowNote(note);
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
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        hotkeys?.Dispose();
        hotkeys = null;
        tray?.Dispose();
        settings?.Close();
        foreach (var window in Windows.Values.ToArray()) window.ClosePermanently();
        Windows.Clear();
        store.Dispose();
    }
}
