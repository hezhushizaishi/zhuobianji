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
            // 应用到全部便签：开着的、隐藏的、收着的都换成主便签的字体、字号、透明度，配色各自不变；没开窗口的不为它建窗口。
            // 怎么改存着的富文本由 documents 组测（RestyleStored），这里看按钮链路、建不建窗口、存盘和存不上的提示。
            using (var allStore = new NoteStore(Path.Combine(root, "settings-all", "Data")))
            {
                var main = new NoteData { Text = "主便签", IsCollectionMain = true, Left = 300, Top = 200, Appearance = new NoteAppearance { Palette = "green" } };
                var enlarged = new System.Windows.Documents.FlowDocument(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("放大的字") { FontSize = 34 }));
                var hidden = new NoteData { Text = "放大的字", Format = NoteFormat.RichText, RichText = FormattedDocuments.SaveRich(enlarged), IsHidden = true,
                    Appearance = new NoteAppearance { Palette = "pink", Opacity = 0.9 } };
                var collected = new NoteData { Text = "收着的便签", IsHidden = true, CollectedBy = main.Id, Appearance = new NoteAppearance { Palette = "yellow" } };
                var loose = new NoteData { Text = "另一张开着的", Left = 820, Top = 200, Appearance = new NoteAppearance { Palette = "mist" } };
                foreach (var data in new[] { main, hidden, collected, loose }) allStore.Save(data);
                using var allController = new NoteController(allStore, () => { }, enableHotkeys: false);
                allController.Start();
                var mainWindow = allController.Windows[main.Id];
                var allSettings = allController.ShowSettings(mainWindow);
                allSettings.FontMenu.Items.OfType<System.Windows.Controls.MenuItem>().Single(item => (string)item.Tag == NoteTheme.BundledFont)
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
                allSettings.SizeSlider.Value = 20;
                allSettings.OpacitySlider.Value = 95;
                Check(allController.Windows[loose.Id].Data.Appearance.FontSize == 17, "平时在设置里改外观只改这一张");
                allSettings.ApplyAllButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                var saved = allStore.LoadAll().Notes.ToDictionary(n => n.Id);
                Check(saved.Values.All(n => n.Appearance.FontFamily == NoteTheme.BundledFont && n.Appearance.FontSize == 20 && n.Appearance.Opacity == 0.95) &&
                    saved[main.Id].Appearance.Palette == "green" && saved[hidden.Id].Appearance.Palette == "pink" &&
                    saved[collected.Id].Appearance.Palette == "yellow" && saved[loose.Id].Appearance.Palette == "mist" && allSettings.Status.Text == "已应用到全部便签",
                    "应用到全部便签：开着的、隐藏的、收着的都换成这张的字体、字号、透明度并存上，配色各自不变");
                var looseWindow = allController.Windows[loose.Id];
                Check(looseWindow.Opacity == 0.95 && looseWindow.Editor.Rich.Document.FontSize == 20 &&
                    looseWindow.Editor.Rich.Document.FontFamily == NoteTheme.Font(NoteTheme.BundledFont) &&
                    allController.WindowOf(allController.Book.Find(hidden.Id)) is null && allController.WindowOf(allController.Book.Find(collected.Id)) is null,
                    "开着的便签马上变，没开窗口的便签不为它建窗口");
                var reopenedHidden = new NoteEditor();
                reopenedHidden.Initialize(saved[hidden.Id]);
                Check(Elements(reopenedHidden.Rich.Document).OfType<System.Windows.Documents.Run>().Single(run => run.Text == "放大的字").FontSize == 40,
                    "没开窗口的便签里单独设了字号的字也按比例变（17 → 20 号，34 → 40）");
                allSettings.SizeSlider.Value = 22;
                var hiddenEntry = allController.Book.Find(hidden.Id)!;
                var applied = true;
                string? notice;
                using (Lock(allStore.GetPath(hidden.Id))) notice = AnswerNotice(() => applied = allController.ApplyAppearanceToAll(mainWindow));
                Check(!applied && notice?.Contains("放大的字") == true && notice.Contains("没存上") && hiddenEntry.Data.Appearance.FontSize == 20 &&
                    allStore.LoadAll().Notes.Single(n => n.Id == loose.Id).Appearance.FontSize == 22,
                    "有便签存不上时提示写明是哪张，它的外观不变，其他照常改好");
                allSettings.Close();
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
            // 关于：版本号来自 csproj（和程序集版本一致），三个键打开对应的地址，许可三页都有全文。
            settings.AboutTab.IsChecked = true;
            settings.UpdateLayout();
            var opened = new System.Collections.Generic.List<Uri>();
            settings.LinkOpener = opened.Add;
            foreach (var button in new[] { settings.GuideButton, settings.RepositoryButton, settings.ReleasesButton })
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(settings.AboutPage.IsVisible && settings.AppName.Text == "桌边记" && settings.AppVersion.Text == ShortVersion && settings.AppIcon.Source is { Width: > 0 },
                "关于页显示图标、桌边记和版本号（和程序集版本一致）");
            Check(opened.Select(uri => uri.ToString()).SequenceEqual(["https://github.com/hezhushizaishi/zhuobianji/blob/HEAD/docs/使用说明.md",
                "https://github.com/hezhushizaishi/zhuobianji", "https://github.com/hezhushizaishi/zhuobianji/releases/latest"]),
                "关于页三个键分别打开使用说明、开源地址和发布页");
            CaptureDialog(settings, Path.Combine(root, "settings-about-dark.png"));
            settings.LicenseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(100);
            var licenses = settings.Licenses!;
            var notices = licenses.TabItems.Select(tab => { tab.IsChecked = true; return licenses.NoticeText.Text; }).ToArray();
            Check(licenses.IsVisible && licenses.Owner == settings && notices.Length == 3 && notices[0].Contains("MIT License") &&
                notices[1].Contains("Markdig") && notices[1].Contains("SIL Open Font License") && notices[2].Contains(".NET Runtime"),
                "「查看许可」打开三份许可：桌边记的 MIT、第三方组件（含思源黑体）、.NET 运行库");
            licenses.TabItems[1].IsChecked = true;
            licenses.UpdateLayout();
            CaptureDialog(licenses, Path.Combine(root, "licenses-dark.png"));
            Check(ReferenceEquals(controller.ShowSettings(note), settings) && ReferenceEquals(settings.Licenses, licenses), "许可窗口跟着设置窗口，重复打开设置不新开");
            licenses.Close();
            Check(settings.Licenses is null, "关掉许可窗口后设置窗口照常");
            settings.Close();
            var reopened = controller.ShowSettings(note);
            Check(reopened.SizeSlider.Value == 20 && reopened.DarkMode.IsChecked == true && reopened.FontLabel.Text == NoteTheme.BundledFont && reopened.StartupSwitch.IsChecked == false,
                "重新打开设置恢复外观与实际自启状态");
            // 只改字号：正文里单独设了字号的字按比例跟着变，而且存进正文（换个编辑器读出来还是新字号）。
            note.Editor.Rich.Document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("单独放大的字") { FontSize = 40 }));
            note.MarkDirty();
            note.SaveNow();
            reopened.SizeSlider.Value = 24;
            note.SaveNow();
            var restored = new NoteEditor();
            restored.Initialize(store.LoadAll().Notes.Single());
            Check(Elements(restored.Rich.Document).OfType<System.Windows.Documents.Run>().Single(run => run.Text == "单独放大的字").FontSize == 48,
                "只改字号：单独设了字号的字按比例变大（20 → 24 号，40 → 48），并存进正文");
            Check(controller.RequestExit() && !reopened.IsVisible, "退出程序同时关闭设置窗口并保存便签");
        }
        finally { Registry.CurrentUser.DeleteSubKey(registryPath, throwOnMissingSubKey: false); }
    }
}
