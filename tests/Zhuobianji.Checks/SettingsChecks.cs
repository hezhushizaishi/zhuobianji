using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Zhuobianji;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

internal static partial class Program
{
    private static async Task SettingsChecks()
    {
        var registryPath = @"Software\Zhuobianji.Checks\" + Guid.NewGuid().ToString("N");
        var exe = Path.Combine(root, "安装目录 有空格", "桌边记.exe");
        var startup = new WindowsStartup(exe, registryPath);
        try
        {
            Check(!startup.IsEnabled, "没有启动项时开关关闭");
            startup.SetEnabled(true);
            using (var key = Registry.CurrentUser.OpenSubKey(registryPath))
                Check(startup.IsEnabled && (string?)key?.GetValue("WeNote") == "\"" + exe + "\"", "自启指向带引号的正确 EXE，支持空格与中文路径");
            Check(new WindowsStartup(exe, registryPath).IsEnabled, "重新读取启动设置仍开启");
            startup.SetEnabled(false);
            Check(!startup.IsEnabled, "关闭自启移除自己的启动项");
            using (var key = Registry.CurrentUser.CreateSubKey(registryPath)) key.SetValue("WeNote", "其他程序路径");
            startup.SetEnabled(false);
            using (var key = Registry.CurrentUser.OpenSubKey(registryPath))
                Check((string?)key?.GetValue("WeNote") == "其他程序路径", "关闭只移除当前程序的启动项");
            using var store = new NoteStore(Path.Combine(root, "settings", "Data"));
            using var controller = new NoteController(store, () => { }, startup, enableHotkeys: false);
            controller.Start();
            var note = controller.Notes.Values.Single();
            note.Editor.TextContent = "设置调整前的文字与格式";
            note.SaveNow();
            var settings = controller.ShowSettings(note);
            await Task.Delay(100);
            settings.UpdateLayout();
            Capture(settings, Path.Combine(root, "settings-light.png"));
            settings.PaletteBox.SelectedIndex = 2;
            settings.SizeSlider.Value = 20;
            settings.OpacitySlider.Value = 85;
            settings.DarkSwitch.IsChecked = true;
            Check(note.Data.Appearance.Palette == "yellow" && note.Data.Appearance.FontSize == 20 && note.Data.Appearance.Opacity == 0.85 && note.Data.Appearance.Dark &&
                note.Editor.TextContent == "设置调整前的文字与格式", "设置控件即时应用配色、字号、透明度和深色，不改便签文字");
            settings.StartupSwitch.IsChecked = true;
            Check(startup.IsEnabled, "设置窗口开关实际开启自启");
            settings.StartupSwitch.IsChecked = false;
            Check(!startup.IsEnabled, "设置窗口开关实际关闭自启");
            settings.DefaultsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(650);
            Check(!note.IsDirty && store.LoadAll().Notes.Single().Appearance == note.Data.Appearance && store.LoadPreferences().Appearance == note.Data.Appearance,
                "外观自动保存，默认样式按钮保存设置");
            settings.UpdateLayout();
            Capture(settings, Path.Combine(root, "settings-dark.png"));
            Check(ReferenceEquals(settings, controller.ShowSettings(note)), "重复打开复用同一设置窗口");
            settings.SettingsTabs.SelectedIndex = 1;
            settings.UpdateLayout();
            Check(settings.DataPath.IsVisible && settings.DataPath.Text == store.DirectoryPath,
                "程序与数据页面完整显示实际路径");
            Capture(settings, Path.Combine(root, "settings-data-dark.png"));
            settings.Close();
            var reopened = controller.ShowSettings(note);
            Check(reopened.SizeSlider.Value == 20 && reopened.DarkSwitch.IsChecked == true && reopened.StartupSwitch.IsChecked == false,
                "重新打开设置恢复外观与实际自启状态");
            Check(controller.RequestExit() && !reopened.IsVisible, "退出程序同时关闭设置窗口并保存便签");
        }
        finally { Registry.CurrentUser.DeleteSubKey(registryPath, throwOnMissingSubKey: false); }
    }
}
