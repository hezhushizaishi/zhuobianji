using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Zhuobianji;

// 窗口所在显示器的工作区（去掉任务栏），WPF 坐标。SystemParameters.WorkArea 只是主显示器的：
// 主便签放在副屏时按它算位置，展开的便签和设置窗口会被拉回主屏。窗口跨两块屏时取占得多的那块。
internal static class ScreenArea
{
    internal static Rect Of(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = handle == IntPtr.Zero ? IntPtr.Zero : MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;
        var work = new Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        // 系统给的是像素；按窗口的缩放换成 WPF 坐标（程序按系统 DPI 缩放，各屏用同一个比例）。
        var toWpf = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        work.Transform(toWpf);
        return work;
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Bounds { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Bounds Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
