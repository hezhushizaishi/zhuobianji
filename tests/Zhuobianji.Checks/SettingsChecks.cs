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
            // 0.8.x 的数据：每张便签各存一份 Dark。迁移本身（定模式、存设置、不再写 Dark）由 state 组测；这里看启动后界面是深色。
            using (var legacyStore = new NoteStore(Path.Combine(root, "settings-legacy", "Data")))
            {
                var legacyMain = new NoteData { Text = "0.8.x 的深色主便签", IsCollectionMain = true,
                    Appearance = new NoteAppearance { Palette = "green", LegacyDark = true } };
                legacyStore.Save(legacyMain);
                using var legacyController = new NoteController(legacyStore, () => { }, enableHotkeys: false);
                legacyController.Start();
                var legacyNote = legacyController.Windows[legacyMain.Id];
                Check(legacyController.ThemeMode == NoteThemeMode.Dark && NoteTheme.DarkMode &&
                    legacyNote.Resources["Surface"] == NoteTheme.For("green", true).Surface,
                    "0.8.x 数据第一次启动按主便签显示成深色");
            }
            using var store = new NoteStore(Path.Combine(root, "settings", "Data"));
            using var controller = new NoteController(store, () => { }, startup, enableHotkeys: false);
            controller.Start();
            var note = controller.Windows.Values.Single();
            note.Editor.TextContent = "设置调整前的文字与格式";
            note.SaveNow();
            var settings = controller.ShowSettings(note);
            await Task.Delay(100);
            settings.UpdateLayout();
            CaptureDialog(settings, Path.Combine(root, "settings-light.png"));
            var noteCard = note.Snapshot();
            Check(settings.Left + NoteWindow.ShadowMargin >= noteCard.Left + noteCard.Width || settings.Left + settings.Width - NoteWindow.ShadowMargin <= noteCard.Left,
                "设置窗口在所属便签旁边打开，不盖住便签");
            settings.PaletteItems[2].IsChecked = true;
            settings.SizeSlider.Value = 20;
            settings.OpacitySlider.Value = 85;
            settings.DarkMode.IsChecked = true;
            Check(note.Data.Appearance.Palette == "yellow" && note.Data.Appearance.FontSize == 20 && note.Data.Appearance.Opacity == 0.85 && NoteTheme.DarkMode &&
                note.Resources["Surface"] == NoteTheme.For("yellow", true).Surface &&
                note.Editor.TextContent == "设置调整前的文字与格式", "设置控件即时应用配色、字号、透明度和深色，不改便签文字");
            Check(controller.ThemeMode == NoteThemeMode.Dark && store.LoadPreferences().ThemeMode == NoteThemeMode.Dark &&
                NoteTheme.For(controller.Preferences.Appearance) == NoteTheme.For(controller.Preferences.Appearance.Palette, true),
                "深浅色是整个程序的模式，存进设置，之后新建的便签也是深色");
            settings.SystemMode.IsChecked = true;
            using (var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                Check(controller.ThemeMode == NoteThemeMode.System && NoteTheme.DarkMode == (personalize?.GetValue("AppsUseLightTheme") is int light && light == 0),
                    "「跟随系统」按 Windows 的应用深浅色设置");
            settings.DarkMode.IsChecked = true;
            var font = settings.FontMenu.Items.OfType<System.Windows.Controls.MenuItem>().Single(item => (string)item.Tag == NoteTheme.BundledFont);
            font.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            var typefaces = NoteTheme.Font(NoteTheme.BundledFont).GetTypefaces();
            Check(note.Data.Appearance.FontFamily == NoteTheme.BundledFont && note.Editor.Rich.Document.FontFamily == NoteTheme.Font(NoteTheme.BundledFont) &&
                typefaces.Any(t => t.Weight == FontWeights.Normal && t.TryGetGlyphTypeface(out _)) && typefaces.Any(t => t.Weight == FontWeights.Bold && t.TryGetGlyphTypeface(out _)),
                "字体可以选随程序打包的思源黑体，常规和粗体都能用，不用另装");
            Check(settings.Resources["Surface"] == NoteTheme.For(note.Data.Appearance).Surface && settings.PaletteLabel.Text == "淡黄",
                "设置窗口跟着换成便签的新配色，色块旁显示配色名称");
            settings.StartupSwitch.IsChecked = true;
            Check(startup.IsEnabled, "设置窗口开关实际开启自启");
            settings.StartupSwitch.IsChecked = false;
            Check(!startup.IsEnabled, "设置窗口开关实际关闭自启");
            settings.DefaultsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(650);
            Check(!note.IsDirty && store.LoadAll().Notes.Single().Appearance == note.Data.Appearance && store.LoadPreferences().Appearance == note.Data.Appearance,
                "外观自动保存，默认样式按钮保存设置");
            settings.UpdateLayout();
            CaptureDialog(settings, Path.Combine(root, "settings-dark.png"));
            Check(ReferenceEquals(settings, controller.ShowSettings(note)), "重复打开复用同一设置窗口");
            settings.ProgramTab.IsChecked = true;
            settings.UpdateLayout();
            Check(settings.DataPath.IsVisible && settings.DataPath.Text == store.DirectoryPath,
                "程序与数据页面完整显示实际路径");
            CaptureDialog(settings, Path.Combine(root, "settings-data-dark.png"));
            settings.Close();
            var reopened = controller.ShowSettings(note);
            Check(reopened.SizeSlider.Value == 20 && reopened.DarkMode.IsChecked == true && reopened.FontLabel.Text == NoteTheme.BundledFont && reopened.StartupSwitch.IsChecked == false,
                "重新打开设置恢复外观与实际自启状态");
            Check(controller.RequestExit() && !reopened.IsVisible, "退出程序同时关闭设置窗口并保存便签");
        }
        finally { Registry.CurrentUser.DeleteSubKey(registryPath, throwOnMissingSubKey: false); }
    }
}
