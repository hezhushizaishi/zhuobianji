using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Zhuobianji.Core;

namespace Zhuobianji;

internal sealed partial class NoteController
{
    internal bool SaveDefaults(NoteAppearance appearance)
    {
        if (TrySavePreferences(Preferences with { Appearance = appearance }, "默认外观未保存", out var error)) return true;
        Dialogs.Show(error, "设置");
        return false;
    }

    // 深浅色是整个程序的模式：所有便签、左侧标签、弹窗和托盘菜单一起变，配色仍是每张便签自己选。
    // 每张便签外观里的 Dark 统一由这里写入；「跟随系统」读 Windows 的应用深浅色设置，系统切换时跟着变。
    internal NoteThemeMode ThemeMode => Preferences.ThemeMode ?? NoteThemeMode.Light;
    internal bool IsDark { get; private set; }

    internal NoteAppearance Themed(NoteAppearance appearance) => appearance.Dark == IsDark ? appearance : appearance with { Dark = IsDark };

    internal bool TrySetThemeMode(NoteThemeMode mode, out string? error)
    {
        if (!TrySavePreferences(Preferences with { ThemeMode = mode }, "深浅色设置未保存", out error)) return false;
        ApplyThemeMode();
        return true;
    }

    private void ApplyThemeMode()
    {
        var dark = ResolveDark();
        if (dark == IsDark) return;
        IsDark = dark;
        // 只换显示，不标记修改：深浅色不算改了便签，下次保存时顺带写进去，启动时也会按模式重新写入。
        foreach (var window in Notes.Values) window.ApplyAppearance(Themed(window.Data.Appearance));
        RefreshCollections();
        settings?.ApplyThemeMode();
    }

    private bool ResolveDark() => ThemeMode switch
    {
        NoteThemeMode.Dark => true,
        NoteThemeMode.System => SystemIsDark(),
        _ => false
    };

    private static bool SystemIsDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    // 系统切换深浅色时会发这个通知（在系统事件的线程上），转回界面线程再处理。
    private void OnSystemPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General && ThemeMode == NoteThemeMode.System)
            dispatcher.BeginInvoke(() => { if (!disposed) ApplyThemeMode(); });
    }

    internal bool TrySetFormatWarnings(bool show, out string? error) =>
        TrySavePreferences(Preferences with { ShowFormatWarnings = show }, "格式提示设置未保存", out error);

    private bool TrySavePreferences(NotePreferences preferences, string failure, out string? error)
    {
        try { store.SavePreferences(preferences); Preferences = preferences; error = null; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { error = failure + "：" + ex.Message; return false; }
    }

    internal bool TryReadStartup(out bool enabled, out string? error)
    {
        try { enabled = startup.IsEnabled; error = null; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { enabled = false; error = "无法读取开机启动设置：" + ex.Message; return false; }
    }

    internal bool TrySetStartup(bool enabled, out string? error)
    {
        try { startup.SetEnabled(enabled); settings?.RefreshStartup(); error = null; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { error = "开机启动设置未完成：" + ex.Message; return false; }
    }

    internal void OpenDataDirectory()
    {
        try { Process.Start(new ProcessStartInfo(DataDirectory) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        { Dialogs.Show("无法打开数据文件夹：" + ex.Message, "数据位置"); }
    }

    internal SettingsWindow ShowSettings(NoteWindow? note = null)
    {
        note ??= Notes.Values.FirstOrDefault(w => w.IsVisible) ?? Notes.Values.FirstOrDefault();
        if (settings is not null && settings.TargetNote == note) { settings.Activate(); return settings; }
        settings?.Close();
        var window = new SettingsWindow(this, note);
        if (note is { IsVisible: true }) window.PlaceBeside(note);
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        settings = window;
        window.Closed += (_, _) => { if (ReferenceEquals(settings, window)) settings = null; };
        window.Show();
        window.Activate();
        return window;
    }

    internal void SuspendHotkeyCapture() => hotkeys?.SuspendCapture();

    internal bool ResumeHotkeys(out string? error)
    {
        error = null;
        return hotkeys is null || hotkeys.TryApply(Preferences.Hotkeys, out error);
    }

    internal bool TrySaveHotkeys(NoteHotkeys bindings, out string? error)
    {
        if (!GlobalHotkeys.Validate(bindings, out error)) return false;
        if (hotkeys is not null && !hotkeys.TryApply(bindings, out error)) return false;
        if (TrySavePreferences(Preferences with { Hotkeys = bindings }, "快捷键未保存", out error)) return true;
        hotkeys?.TryApply(Preferences.Hotkeys, out _);
        return false;
    }
}
