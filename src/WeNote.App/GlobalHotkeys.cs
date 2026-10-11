using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using WeNote.Core;

namespace WeNote.App;

internal sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource source;
    private readonly Action newNote;
    private readonly Action toggleNotes;
    private NoteHotkeys active = Empty;
    private static NoteHotkeys Empty => new() { NewNote = new(), ToggleNotes = new() };
    internal nint Handle => source.Handle;

    internal GlobalHotkeys(Action newNote, Action toggleNotes)
    {
        this.newNote = newNote;
        this.toggleNotes = toggleNotes;
        source = new HwndSource(new HwndSourceParameters("WeNote.Hotkeys")
        {
            ParentWindow = new nint(-3), WindowStyle = 0, Width = 0, Height = 0
        });
        source.AddHook(OnMessage);
    }

    internal bool TryApply(NoteHotkeys bindings, out string? error)
    {
        if (!Validate(bindings, out error)) return false;
        var previous = active;
        Unregister();
        if (Register(bindings, out error)) { active = bindings; return true; }
        Unregister();
        Register(previous, out _);
        active = previous;
        return false;
    }

    internal static bool Validate(NoteHotkeys bindings, out string? error)
    {
        if (bindings is null || !Valid(bindings.NewNote) || !Valid(bindings.ToggleNotes))
        { error = "请使用 Ctrl 或 Alt 加一个按键，F12 和 Win 组合暂不支持。"; return false; }
        if (bindings.NewNote.IsEnabled && bindings.NewNote == bindings.ToggleNotes)
        { error = "两项快捷键不能相同。"; return false; }
        error = null;
        return true;
    }

    private static bool Valid(HotkeyBinding binding) => binding is not null &&
        (!binding.IsEnabled || (binding.Key <= 255 && binding.Key != 0x7B &&
        binding.Key is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C) &&
        (binding.Modifiers & ~7u) == 0 && (binding.Modifiers & 3) != 0));

    private bool Register(NoteHotkeys bindings, out string? error)
    {
        foreach (var (id, label, binding) in new[] { (1, "新建便签", bindings.NewNote), (2, "显示／隐藏全部", bindings.ToggleNotes) })
        {
            if (binding.IsEnabled && !RegisterHotKey(source.Handle, id, binding.Modifiers | 0x4000, binding.Key))
            { error = $"{label}的 {Display(binding)} 无法使用，可能已被其他程序占用。"; return false; }
        }
        error = null;
        return true;
    }

    internal void SuspendCapture() => Unregister();

    private void Unregister()
    {
        UnregisterHotKey(source.Handle, 1);
        UnregisterHotKey(source.Handle, 2);
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x0312) return 0;
        if (wParam == 1) { handled = true; newNote(); }
        else if (wParam == 2) { handled = true; toggleNotes(); }
        return 0;
    }

    internal static string Display(HotkeyBinding binding)
    {
        if (!binding.IsEnabled) return "";
        var parts = new List<string>();
        if ((binding.Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((binding.Modifiers & 1) != 0) parts.Add("Alt");
        if ((binding.Modifiers & 4) != 0) parts.Add("Shift");
        var key = KeyInterop.KeyFromVirtualKey((int)binding.Key);
        parts.Add(key == Key.Space ? "空格" : key is >= Key.D0 and <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString());
        return string.Join("+", parts);
    }

    public void Dispose() { Unregister(); source.Dispose(); }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}