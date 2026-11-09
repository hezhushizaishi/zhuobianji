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

    // 启动时先攒着，读完再一起提示；平时直接提示。
    private List<string>? startupReadErrors, startupModeErrors;
    private const string ReadErrorNotice = "部分数据无法读取，原文件已保留，其他便签可以继续使用。";
    private const string ModeErrorNotice = "当前无法贴到桌面，便签先以浮窗显示。稍后可从更多操作重试。";

    // 便签的窗口：隐藏、收纳的便签启动时不建，第一次要用时才建（建好先隐藏着），之后一直留着。
    // 记着贴桌面的根便签建好就挂到桌面。正文读不了时提示，并把这张从内存里拿掉（原文件保留），返回 null；
    // 它是主便签的话，收着的便签放回桌面并显示出来。
    internal NoteWindow? WindowFor(NoteEntry note)
    {
        if (WindowOf(note) is { } existing) return existing;
        NoteWindow window;
        try { window = new NoteWindow(note, this); }
        catch (InvalidDataException ex)
        {
            var collected = book.Collected(note).ToArray();
            book.Forget(note);
            book.ReleaseOrphans();
            foreach (var orphan in collected) RevealReleased(orphan, activate: false);
            RefreshCollections();
            if (startupReadErrors is not null) startupReadErrors.Add($"{note.Id:N}：{ex.Message}");
            else Dialogs.Show(ReadErrorNotice + "\n\n" + ex.Message, "数据提示");
            return null;
        }
        note.View = window;
        Windows.Add(note.Id, window);
        window.RefreshCollectionState();
        if (note.Data.CollectedBy is null && note.Data.IsOnDesktop && !window.TrySetDesktopMode(true, out var error))
        {
            if (startupModeErrors is not null) startupModeErrors.Add(error!);
            else Dialogs.Show(ModeErrorNotice + "\n\n" + error, "桌面模式");
        }
        return window;
    }

    internal void Start()
    {
        startupReadErrors = book.Load();
        startupModeErrors = [];
        NoteTheme.DarkMode = ResolveDark();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        tray = new TrayMenu(this);
        if (enableHotkeys)
        {
            hotkeys = new GlobalHotkeys(() => CreateNote(), ToggleAll);
            if (!hotkeys.TryApply(Preferences.Hotkeys, out var error))
                Dialogs.Show(error + "\n请到设置的快捷键页面更换。", "快捷键");
        }
        // 先恢复收纳关系，再给要显示的根便签建窗口，避免已收纳的便签在启动时闪现。隐藏和收纳的便签不建窗口。
        // 读不了的便签会被拿掉，它是主便签时收着的便签放回桌面，所以每次重新找还没显示的。
        book.ReleaseOrphans();
        while (book.Roots.FirstOrDefault(note => !note.Data.IsHidden && WindowOf(note) is null) is { } next)
            WindowFor(next)?.Reveal(activate: false);
        RefreshCollections();
        if (book.Notes.Count == 0) CreateNote();
        else
        {
            var visible = book.Roots.Where(note => !note.Data.IsHidden).ToArray();
            if (visible.Length == 0) ShowSaved(book.Roots.MaxBy(note => note.Data.UpdatedAt)!, activate: true);
            else
            {
                // 普通浮窗在启动时明确前置；桌面便签仍留在桌面层。
                var front = visible.Where(note => WindowOf(note)?.IsDesktopAttached == false).MaxBy(note => note.Data.UpdatedAt)
                    ?? visible.MaxBy(note => note.Data.UpdatedAt)!;
                WindowOf(front)?.Reveal(activate: true);
            }
        }
        var readErrors = startupReadErrors;
        var modeErrors = startupModeErrors;
        startupReadErrors = startupModeErrors = null;
        if (readErrors.Count > 0) Dialogs.Show(ReadErrorNotice + "\n\n" + string.Join("\n", readErrors), "数据提示");
        if (modeErrors.Count > 0) Dialogs.Show(ModeErrorNotice + "\n\n" + string.Join("\n", modeErrors.Distinct()), "桌面模式");
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
        // 新便签的正文是空的，建窗口不会读失败。
        var window = WindowFor(book.Add(data))!;
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
        if (WindowFor(note) is not { } window) return;
        book.Update(note, data => data with { IsHidden = false });
        book.Save(note);
        window.Reveal(activate, slide);
    }

    internal void ShowNote(NoteEntry note)
    {
        if (note.Data.CollectedBy is not null) { ExpandCollectedNote(note); return; }
        ShowSaved(note, activate: true);
    }

    internal void ShowAll()
    {
        foreach (var note in book.Roots.ToArray()) ShowSaved(note, activate: false);
        WindowOf(book.Main ?? book.Roots.FirstOrDefault())?.Activate();
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

    // 没开窗口的便签只记下设置，显示时再挂到桌面。挂不上的和存不上的一起提示一次（多数便签没开着窗口，看不到窗口底部的保存提示）。
    internal bool SetAllDesktopModes(bool enabled, bool showErrors = true)
    {
        var errors = new List<string>();
        foreach (var note in book.Roots.ToArray())
        {
            if (WindowOf(note) is not { } window)
            {
                if (!book.TryCommit(note, data => data with { IsOnDesktop = enabled })) errors.Add(NotSaved(note));
            }
            else if (!window.TrySetDesktopMode(enabled, out var error)) errors.Add(error!);
            else if (!window.SaveNow()) errors.Add(NotSaved(note));
        }
        if (showErrors && errors.Count > 0) Dialogs.Show(string.Join("\n", errors.Distinct()), "桌面模式");
        return errors.Count == 0;
    }

    private static string NotSaved(NoteEntry note) => $"“{note.Data.Preview}”的设置没存上：{note.SaveError}";

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

    // 删除主便签先逐张取出收纳的便签；某张取不出就停下、主便签不删，提示里说明已经取出了几张。
    internal bool DeleteNote(NoteEntry note, bool showErrors = true)
    {
        if (book.Find(note.Id) is null) return true;
        if (note.Data.IsCollectionMain)
        {
            var released = 0;
            foreach (var child in book.Collected(note).ToArray())
            {
                if (ReleaseNote(child, showErrors: false)) { released++; continue; }
                if (showErrors)
                    Dialogs.Show("主便签没有删除：" + (released > 0 ? $"已取出 {released} 张便签到桌面，" : "收纳的") +
                        $"“{child.Data.Preview}”没能取出。\n\n{child.SaveError}", "删除");
                return false;
            }
        }
        var window = WindowOf(note);
        try { book.Delete(note); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 窗口开着就显示在窗口底部；没建或隐藏着（比如收起的子便签）就弹提示。
            if (window?.IsVisible == true) window.ReportSaveError("删除未完成：" + ex.Message);
            else if (showErrors) Dialogs.Show("删除未完成：" + ex.Message, "删除");
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

    // 关机、注销（会话结束）：和托盘退出一样，先全部存上，再把窗口彻底关掉。不先关的话，系统接着关窗口时会走 × 的路，
    // 把每张便签存成隐藏，下次启动只剩最近的一张。有没存上的就返回 false，拦下这次会话结束。
    internal bool EndSession()
    {
        if (!FlushAll()) return false;
        Dispose();
        return true;
    }

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
        // 设置窗口开着就退出：没点「完成」的改动照样退回去（便签窗口这时还没关，存得上），失败也不弹框挡住退出。
        if (settings is not null) { settings.QuietRevert = true; settings.Close(); }
        foreach (var window in Windows.Values.ToArray()) window.ClosePermanently();
        Windows.Clear();
        store.Dispose();
    }
}
