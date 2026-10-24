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
