using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Zhuobianji;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

internal static partial class Program
{
    private static async Task WindowChecks()
    {
        var registryPath = @"Software\Zhuobianji.Checks\" + Guid.NewGuid().ToString("N");
        var startup = new WindowsStartup(Path.Combine(root, "桌边记.exe"), registryPath);
        var behind = Color.FromRgb(220, 45, 30);
        var backdrop = new Window { Left = 80, Top = 80, Width = 620, Height = 560, Topmost = true,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, Background = new SolidColorBrush(behind) };
        backdrop.Show();
        try
        {
            using var store = new NoteStore(Path.Combine(root, "Data"));
            using var controller = new NoteController(store, () => { }, startup, enableHotkeys: false);
            controller.Start();
            var note = controller.Windows.Values.Single();
            note.Left = 120; note.Top = 120; note.Topmost = true;
            note.Editor.TextContent = "透明与拖动检查";
            note.SaveNow();
            await Task.Delay(100);
            var point = note.PointToScreen(new Point(note.ActualWidth - 30, 120));
            var surface = NoteTheme.For(note.Data.Appearance).Surface.Color;
            // 0% 是 0.8.x 能存下的旧值：现在最低按 70% 显示，便签不会整张看不见。
            foreach (var opacity in new[] { 0.0, 0.7, 1.0 })
            {
                note.ApplyAppearance(note.Data.Appearance with { Opacity = opacity });
                note.MarkDirty(documentChanged: false);
                note.SaveNow();
                await Task.Delay(100);
                var pixel = ScreenPixel((int)point.X, (int)point.Y);
                var shown = Math.Max(opacity, NoteTheme.MinOpacity);
                Check(note.AllowsTransparency && note.Opacity == shown && store.LoadAll().Notes.Single().Appearance.Opacity == opacity,
                    opacity == 0 ? "旧数据里 0% 的便签按最低 70% 显示，存着的值不动" : $"窗口透明度 {opacity:0%}");
                Check(opacity == 1 ? CloseColor(pixel, surface) : pixel.G > behind.G + 20 && pixel.G < surface.G - 20,
                    opacity == 1 ? "不透明正常显示便签" : "半透明实际混合后方窗口与便签，没有变黑或看不见");
            }
            var before = store.LoadAll().Notes.Single();
            var savedLabel = note.SaveState;
            var handle = new WindowInteropHelper(note).Handle;
            SendMessage(handle, 0x0231, 0, 0);
            note.Left += 40; note.Top += 30; note.Width += 12;
            await Task.Delay(550);
            Check(store.LoadAll().Notes.Single().Left == before.Left && note.SaveState == savedLabel,
                "拖动期间不写位置，不闪保存提示");
            SendMessage(handle, 0x0232, 0, 0);
            await Task.Delay(550);
            var after = store.LoadAll().Notes.Single();
            Check(SameBounds(after, note.Snapshot()) && after.RichText == before.RichText,
                "松开后保存位置尺寸，富文本原样保留");
            note.Editor.TextContent = "修改后的正文";
            Check(note.SaveState == "保存中", "正文修改仍显示保存状态");
            await Task.Delay(550);
            Check(store.LoadAll().Notes.Single().Text == "修改后的正文" && note.SaveState == "已保存",
                "正文仍自动保存并恢复已保存提示");
            // 展开的便签、设置窗口按所属便签那块屏的工作区摆（主便签在副屏时不被拉回主屏）。这台机器只有一块屏：
            // 只能看出来的和主屏工作区一样，浮窗和贴桌面的（桌面的子窗口）都一样。双屏没有实测。
            static bool SameArea(Rect a, Rect b) => Math.Abs(a.Left - b.Left) < 1 && Math.Abs(a.Top - b.Top) < 1 && Math.Abs(a.Right - b.Right) < 1 && Math.Abs(a.Bottom - b.Bottom) < 1;
            var floatingArea = ScreenArea.Of(note);
            Check(controller.SetDesktopMode(note, true, showErrors: false) && note.IsDesktopAttached, "透明窗口可切换到桌面");
            Check(SameArea(floatingArea, SystemParameters.WorkArea) && SameArea(ScreenArea.Of(note), SystemParameters.WorkArea),
                "便签所在屏的工作区（单屏时就是主屏的），浮窗和贴在桌面的都找得到");
            note.ApplyAppearance(note.Data.Appearance with { Opacity = 0.8 });
            Check(note.AllowsTransparency && note.Opacity == 0.8, "桌面模式保持窗口透明设置");
            controller.SetDesktopMode(note, false, showErrors: false);
            note.ApplyAppearance(note.Data.Appearance with { Opacity = 1 });
            note.MarkDirty(); note.SaveNow();
            note.Topmost = false;
            backdrop.Close();
            Capture(note, Path.Combine(root, "note-header.png"));
            var settings = controller.ShowSettings(note);
            await Task.Delay(80);
            CaptureDialog(settings, Path.Combine(root, "settings-short.png"));
            var tray = controller.Tray!;
            Check(tray.IsCreated && tray.Menu.Background == NoteTheme.For(controller.Preferences.Appearance).Surface &&
                tray.Menu.Items.OfType<System.Windows.Controls.MenuItem>().Count(item => item.Icon is not null) >= 10, "托盘图标已创建，菜单和便签菜单同一样式并带图标");
            Check(tray.ToolTip.StartsWith($"桌边记 {ShortVersion} · "), "托盘悬停文字带版本号");
            Check(Equals(tray.StartupItem.Header, "开机自启") && !tray.StartupItem.IsChecked, "托盘有实际自启状态的勾选项");
            tray.StartupItem.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            await Task.Delay(80);
            Check(startup.IsEnabled && tray.StartupItem.IsChecked && settings.StartupSwitch.IsChecked == true,
                "托盘开启自启，设置开关同步");
            tray.StartupItem.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            await Task.Delay(80);
            Check(!startup.IsEnabled && !tray.StartupItem.IsChecked && settings.StartupSwitch.IsChecked == false,
                "托盘关闭自启，设置开关同步");
            settings.StartupSwitch.IsChecked = true;
            tray.Rebuild();
            Check(tray.StartupItem.IsChecked, "设置开启后再次打开托盘菜单显示勾选");
            controller.RequestExit();
        }
        finally
        {
            backdrop.Close();
            Registry.CurrentUser.DeleteSubKey(registryPath, throwOnMissingSubKey: false);
        }
    }

    private static Color ScreenPixel(int x, int y)
    {
        var dc = GetDC(0);
        try { var rgb = GetPixel(dc, x, y); return Color.FromRgb((byte)rgb, (byte)(rgb >> 8), (byte)(rgb >> 16)); }
        finally { ReleaseDC(0, dc); }
    }
    private static bool CloseColor(Color a, Color b) => Math.Abs(a.R - b.R) < 4 && Math.Abs(a.G - b.G) < 4 && Math.Abs(a.B - b.B) < 4;
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
}