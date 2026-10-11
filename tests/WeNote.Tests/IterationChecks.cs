using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using WeNote.App;
using WeNote.Core;

namespace WeNote.Tests;

internal static partial class Program
{
    private static async Task IterationChecks()
    {
        var registryPath = @"Software\WeNote.Tests\" + Guid.NewGuid().ToString("N");
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
            var note = controller.Notes.Values.Single();
            note.Left = 120; note.Top = 120; note.Topmost = true;
            note.Editor.TextContent = "透明与拖动检查";
            note.SaveNow();
            await Task.Delay(100);
            var point = note.PointToScreen(new Point(note.ActualWidth - 30, 120));
            var surface = NoteTheme.For(note.Data.Appearance).Surface.Color;
            foreach (var opacity in new[] { 0.0, 0.5, 1.0 })
            {
                note.ApplyAppearance(note.Data.Appearance with { Opacity = opacity });
                note.MarkDirty(documentChanged: false);
                note.SaveNow();
                await Task.Delay(100);
                var pixel = ScreenPixel((int)point.X, (int)point.Y);
                Check(note.AllowsTransparency && note.Opacity == opacity, $"窗口透明度 {opacity:0%}");
                Check(opacity == 0 ? CloseColor(pixel, behind) : opacity == 1 ? CloseColor(pixel, surface) :
                    pixel.G > behind.G + 20 && pixel.G < surface.G - 20,
                    opacity == 0 ? "全透明实际透出后方窗口，未变黑" : opacity == 1 ? "不透明正常显示便签" : "半透明实际混合后方窗口与便签");
            }
            var before = store.LoadAll().Notes.Single();
            var savedLabel = note.SaveStatus.Content;
            var handle = new WindowInteropHelper(note).Handle;
            SendMessage(handle, 0x0231, 0, 0);
            note.Left += 40; note.Top += 30; note.Width += 12;
            await Task.Delay(550);
            Check(store.LoadAll().Notes.Single().Left == before.Left && Equals(note.SaveStatus.Content, savedLabel),
                "拖动期间不写位置，不闪保存提示");
            SendMessage(handle, 0x0232, 0, 0);
            await Task.Delay(550);
            var after = store.LoadAll().Notes.Single();
            Check(after.Left == note.Left && after.Top == note.Top && after.Width == note.Width && after.RichText == before.RichText,
                "松开后保存位置尺寸，富文本原样保留");
            note.Editor.TextContent = "修改后的正文";
            Check((string)note.SaveStatus.Content == "保存中", "正文修改仍显示保存状态");
            await Task.Delay(550);
            Check(store.LoadAll().Notes.Single().Text == "修改后的正文" && (string)note.SaveStatus.Content == "已保存",
                "正文仍自动保存并恢复已保存提示");
            Check(controller.SetDesktopMode(note, true, showErrors: false) && note.IsDesktopAttached, "透明窗口可切换到桌面");
            note.ApplyAppearance(note.Data.Appearance with { Opacity = 0.5 });
            Check(note.AllowsTransparency && note.Opacity == 0.5, "桌面模式保持窗口透明设置");
            controller.SetDesktopMode(note, false, showErrors: false);
            note.ApplyAppearance(note.Data.Appearance with { Opacity = 1 });
            note.MarkDirty(); note.SaveNow();
            note.Topmost = false;
            backdrop.Close();
            Check(((SolidColorBrush)note.HeaderBand.Background).Color != NoteTheme.For(note.Data.Appearance).Surface.Color,
                "顶部底色与正文区分");
            Capture(note, Path.Combine(root, "note-header.png"));
            var settings = controller.ShowSettings(note);
            await Task.Delay(80);
            Capture(settings, Path.Combine(root, "settings-short.png"));
            using var tray = new TrayMenu(controller);
            Check(tray.StartupItem.Text == "开机自启" && !tray.StartupItem.Checked, "托盘有实际自启状态的勾选项");
            tray.StartupItem.PerformClick();
            await Task.Delay(80);
            Check(startup.IsEnabled && tray.StartupItem.Checked && settings.StartupSwitch.IsChecked == true,
                "托盘开启自启，设置开关同步");
            tray.StartupItem.PerformClick();
            await Task.Delay(80);
            Check(!startup.IsEnabled && !tray.StartupItem.Checked && settings.StartupSwitch.IsChecked == false,
                "托盘关闭自启，设置开关同步");
            settings.StartupSwitch.IsChecked = true;
            tray.Rebuild();
            Check(tray.StartupItem.Checked, "设置开启后再次打开托盘菜单显示勾选");
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