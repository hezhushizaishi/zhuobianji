using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace WeNote.App;

// 使用 M0 已验证的 Progman 方式，只改变本程序窗口的父级。
internal sealed class DesktopHost
{
    private readonly Window window;
    private nint handle;
    private nint desktopParent;
    internal bool IsAttached => handle != 0 && desktopParent != 0 && GetParent(handle) == desktopParent;

    internal DesktopHost(Window window) => this.window = window;

    internal void SetAttached(bool enabled)
    {
        if (!enabled && !IsAttached) return;
        if (enabled && IsAttached) return;
        var target = enabled ? FindWindow("Progman", null) : 0;
        if (enabled && target == 0) throw new Win32Exception("当前无法连接桌面，请稍后重试。");
        handle = new WindowInteropHelper(window).EnsureHandle();
        if (!GetWindowRect(handle, out var rect)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var oldStyle = GetWindowLongPtr(handle, -16).ToInt64();
        var oldParent = IsAttached ? desktopParent : 0;
        var newStyle = enabled ? (oldStyle & ~WsPopup) | WsChild : (oldStyle & ~WsChild) | WsPopup;
        var reparented = false;
        try
        {
            if (enabled) SetStyle(newStyle);
            Reparent(target);
            reparented = true;
            if (!enabled) SetStyle(newStyle);
            Position(target, rect);
            desktopParent = target;
        }
        catch
        {
            if (reparented) Reparent(oldParent);
            SetStyle(oldStyle);
            Position(oldParent, rect);
            throw;
        }
    }

    internal Rect ScreenBounds()
    {
        if (!GetWindowRect(handle, out var rect)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var transform = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        return new Rect(transform.Transform(new Point(rect.Left, rect.Top)), transform.Transform(new Point(rect.Right, rect.Bottom)));
    }

    internal object Diagnostics()
    {
        var currentHandle = new WindowInteropHelper(window).Handle;
        GetWindowRect(currentHandle, out var rect);
        var parent = GetParent(currentHandle);
        var parentClass = new StringBuilder(128);
        if (parent != 0) GetClassName(parent, parentClass, parentClass.Capacity);
        return new
        {
            Handle = currentHandle.ToInt64(),
            Parent = parent.ToInt64(),
            ParentClass = parentClass.ToString(),
            Visible = IsWindowVisible(currentHandle),
            Attached = IsAttached,
            Style = GetWindowLongPtr(currentHandle, -16).ToInt64().ToString("X"),
            Dpi = GetDpiForWindow(currentHandle),
            Rectangle = new { rect.Left, rect.Top, rect.Right, rect.Bottom }
        };
    }

    private void SetStyle(long style)
    {
        SetLastError(0);
        var previous = SetWindowLongPtr(handle, -16, new nint(style));
        var error = Marshal.GetLastPInvokeError();
        if (previous == 0 && error != 0) throw new Win32Exception(error);
    }

    private void Reparent(nint parent)
    {
        SetLastError(0);
        var previous = SetParent(handle, parent);
        var error = Marshal.GetLastPInvokeError();
        if (previous == 0 && error != 0) throw new Win32Exception(error);
    }

    private void Position(nint parent, NativeRect rect)
    {
        var point = new NativePoint { X = rect.Left, Y = rect.Top };
        if (parent != 0 && !ScreenToClient(parent, ref point)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (!SetWindowPos(handle, 0, point.X, point.Y, rect.Right - rect.Left, rect.Bottom - rect.Top, 0x4 | 0x10 | 0x20))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    private const long WsChild = 0x40000000, WsPopup = 0x80000000;
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string className, string? name);
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetParent(nint window, nint parent);
    [DllImport("kernel32.dll")] private static extern void SetLastError(uint error);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ScreenToClient(nint window, ref NativePoint point);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder text, int capacity);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
}
