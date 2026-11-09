using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zhuobianji.Core;

namespace Zhuobianji;

internal partial class SettingsWindow : NoteDialog
{
    private static readonly string[] Fonts = ["Microsoft YaHei UI", NoteTheme.BundledFont, "Microsoft YaHei", "Segoe UI", "SimSun", "Consolas"];
    private readonly NoteController controller;
    private bool initializing = true;
    private bool hotkeysDirty;
    private string font;
    // 窗口实际的不透明度。只在拖滑块时按 0–100 换算，换配色、字号时照原值写回，旧数据里的 0.95 不会被取整成 0.949。
    private double opacity;
    // 这里的改动马上能看到，点「完成」才算数；×、Esc、「取消」，或者别处把窗口关掉（换一张便签开设置、退出程序），
    // 都把这次打开以来的改动退回去。本身就是保存的按钮（设为默认外观、应用到全部便签、保存快捷键）点了就算数，不退。
    // 只退这个窗口自己改的：关窗时那一项已经被别处改了（比如托盘开关自启），以别处的为准。
    private bool committed;
    private NoteAppearance? keptAppearance, setAppearance;
    private NoteThemeMode keptTheme;
    private NoteThemeMode? setTheme;
    private bool keptStartup, keptWarnings;
    private bool? setStartup, setWarnings;
    // 退出程序时由控制器关掉，这时退回失败不弹框。
    internal bool QuietRevert { get; set; }
    private readonly Dictionary<TextBox, HotkeyBinding> hotkeys = [];
    internal NoteWindow? TargetNote { get; }
    // 「关于」页的三个地址用它打开；检查里换成记录地址，不真开浏览器。
    internal Action<Uri> LinkOpener { get; set; } = NoteLinks.Open;
    internal LicenseWindow? Licenses { get; private set; }

    internal SettingsWindow(NoteController controller, NoteWindow? note) : base(note?.Data.Appearance ?? controller.Preferences.Appearance)
    {
        InitializeComponent();
        this.controller = controller;
        TargetNote = note;
        DataPath.Text = controller.DataDirectory;
        FormatWarningsSwitch.IsChecked = controller.Preferences.ShowFormatWarnings;
        SetHotkey(NewNoteHotkey, controller.Preferences.Hotkeys.NewNote);
        SetHotkey(ToggleNotesHotkey, controller.Preferences.Hotkeys.ToggleNotes);
        var appearance = note?.Data.Appearance ?? controller.Preferences.Appearance;
        NoteLabel.Text = note is null ? "没有便签" : "这张便签：" + note.Data.Preview;
        NotePanel.IsEnabled = note is not null;
        ThemeButton(controller.ThemeMode).IsChecked = true;
        foreach (var (key, name, swatch) in NoteTheme.Palettes)
        {
            var item = new RadioButton { Style = (Style)FindResource("PaletteSwatch"), Background = swatch, Tag = key, ToolTip = name,
                IsChecked = key == appearance.Palette };
            System.Windows.Automation.AutomationProperties.SetName(item, name);
            item.Checked += OnAppearanceChanged;
            PalettePanel.Children.Add(item);
        }
        if (PaletteItems.All(item => item.IsChecked != true)) PaletteItems.First().IsChecked = true;
        font = appearance.FontFamily;
        foreach (var family in Fonts.Contains(font) ? Fonts : Fonts.Append(font))
        {
            var item = new MenuItem { Header = new TextBlock { Text = family, FontFamily = NoteTheme.Font(family) }, Tag = family };
            item.Click += OnFontChosen;
            FontMenu.Items.Add(item);
        }
        SizeSlider.Value = appearance.FontSize;
        opacity = appearance.Opacity;
        OpacitySlider.Value = NoteTheme.OpacityLevel(appearance.Opacity);
        if (controller.TryReadStartup(out var enabled, out var error)) StartupSwitch.IsChecked = enabled;
        else { StartupSwitch.IsEnabled = false; Status.Text = error; }
        keptAppearance = note?.Data.Appearance;
        keptTheme = controller.ThemeMode;
        keptStartup = enabled;
        keptWarnings = controller.Preferences.ShowFormatWarnings;
        AppIcon.Source = LargeIcon();
        AppName.Text = AppInfo.Name;
        AppVersion.Text = AppInfo.Version;
        AppTagline.Text = AppInfo.Tagline;
        RepositoryText.Text = AppInfo.Repository.Host + AppInfo.Repository.AbsolutePath;
        AppearanceTab.IsChecked = true;
        UpdateLabels();
        initializing = false;
        Closed += (_, _) =>
        {
            if (!committed) Revert();
            controller.ResumeHotkeys(out _);
        };
    }

    private void Revert()
    {
        // 便签已经删了（删除时会关掉它的设置窗口）就不用退外观。退外观存不上时，便签底部会显示保存失败。
        if (TargetNote is { } note && setAppearance is { } shown && shown != keptAppearance && note.Data.Appearance == shown &&
            controller.Book.Find(note.Data.Id) is not null)
        {
            note.ChangeAppearance(keptAppearance!);
            note.SaveNow();
        }
        var errors = new List<string>();
        if (setTheme is { } mode && mode != keptTheme && controller.ThemeMode == mode && !controller.TrySetThemeMode(keptTheme, out var themeError))
            errors.Add(themeError!);
        if (setStartup is { } on && on != keptStartup && controller.TryReadStartup(out var now, out _) && now == on &&
            !controller.TrySetStartup(keptStartup, out var startupError))
            errors.Add(startupError!);
        if (setWarnings is { } show && show != keptWarnings && controller.Preferences.ShowFormatWarnings == show &&
            !controller.TrySetFormatWarnings(keptWarnings, out var warningsError))
            errors.Add(warningsError!);
        if (errors.Count > 0 && !QuietRevert) Dialogs.Show("没点“完成”，想把设置退回原样，但没成功：\n\n" + string.Join("\n", errors), "设置");
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    internal RadioButton[] PaletteItems => PalettePanel.Children.OfType<RadioButton>().ToArray();

    private void OnPage(object sender, RoutedEventArgs e)
    {
        if (AppearancePage is null) return;
        AppearancePage.Visibility = AppearanceTab.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
        ProgramPage.Visibility = ProgramTab.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
        HotkeyPage.Visibility = HotkeyTab.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
        AboutPage.Visibility = AboutTab.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
    }

    // 图标文件里有 16 到 256 几种尺寸，取 128 的那张缩小，高分屏上也清楚。
    private static BitmapSource LargeIcon() =>
        BitmapDecoder.Create(new Uri("pack://application:,,,/Zhuobianji;component/Assets/zhuobianji.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
            .Frames.OrderBy(frame => Math.Abs(frame.PixelWidth - 128)).First();

    private void OnOpenLink(object sender, RoutedEventArgs e) =>
        LinkOpener(sender == GuideButton ? AppInfo.Guide : sender == ReleasesButton ? AppInfo.Releases : AppInfo.Repository);

    // 许可窗口跟着设置窗口，不挡住便签操作；已经开着就拿到前面。
    private void OnShowLicenses(object sender, RoutedEventArgs e)
    {
        if (Licenses is not null) { Licenses.Activate(); return; }
        Licenses = new LicenseWindow(TargetNote?.Data.Appearance ?? controller.Preferences.Appearance) { Owner = this };
        Licenses.Closed += (_, _) => Licenses = null;
        Licenses.Show();
    }

    private void UpdateLabels()
    {
        var palette = PaletteItems.FirstOrDefault(item => item.IsChecked == true);
        PaletteLabel.Text = palette?.ToolTip as string ?? "";
        FontLabel.Text = font;
        FontLabel.FontFamily = NoteTheme.Font(font);
        foreach (MenuItem item in FontMenu.Items) item.IsChecked = (string)item.Tag == font;
        FontSizeLabel.Text = $"{SizeSlider.Value:0}";
        OpacityLabel.Text = $"{OpacitySlider.Value:0}%";
    }

    private void OnFontMenu(object sender, RoutedEventArgs e)
    {
        FontMenu.PlacementTarget = FontButton;
        FontMenu.Placement = PlacementMode.Bottom;
        FontMenu.IsOpen = true;
    }

    private void OnFontChosen(object sender, RoutedEventArgs e)
    {
        font = (string)((MenuItem)sender).Tag;
        OnAppearanceChanged(sender, e);
    }

    private void OnAppearanceChanged(object sender, RoutedEventArgs e)
    {
        if (initializing || TargetNote is null || PaletteItems.FirstOrDefault(item => item.IsChecked == true) is not { } palette) return;
        if (sender == OpacitySlider) opacity = NoteTheme.OpacityOf(OpacitySlider.Value);
        var appearance = new NoteAppearance { Palette = (string)palette.Tag, FontFamily = font, FontSize = SizeSlider.Value, Opacity = opacity };
        TargetNote.ChangeAppearance(appearance);
        setAppearance = appearance;
        SetAppearance(appearance);
        UpdateLabels();
        Status.Text = "";
    }

    private RadioButton ThemeButton(NoteThemeMode mode) => mode switch
    {
        NoteThemeMode.Dark => DarkMode,
        NoteThemeMode.System => SystemMode,
        _ => LightMode
    };

    private void OnThemeMode(object sender, RoutedEventArgs e)
    {
        if (initializing) return;
        var mode = DarkMode.IsChecked == true ? NoteThemeMode.Dark : SystemMode.IsChecked == true ? NoteThemeMode.System : NoteThemeMode.Light;
        if (controller.TrySetThemeMode(mode, out var error)) { setTheme = mode; Status.Text = ""; return; }
        initializing = true;
        ThemeButton(controller.ThemeMode).IsChecked = true;
        initializing = false;
        Status.Text = error;
    }

    // 程序换了深浅色（在这里选的，或跟随系统变了）：设置窗口跟着所属便签换色。
    internal void ApplyThemeMode()
    {
        var appearance = TargetNote?.Data.Appearance ?? controller.Preferences.Appearance;
        SetAppearance(appearance);
        Licenses?.SetAppearance(appearance);
    }

    // 这两个按钮本身就是保存：点了以后，这张便签现在的外观也算数，之后点 × 只退回点按钮之后的改动。
    private void OnSaveDefaults(object sender, RoutedEventArgs e)
    {
        if (TargetNote is not null && controller.SaveDefaults(TargetNote.Data.Appearance)) { KeepAppearance(); Status.Text = "已设为默认"; }
    }

    private void OnApplyToAll(object sender, RoutedEventArgs e)
    {
        if (TargetNote is not null && controller.ApplyAppearanceToAll(TargetNote)) { KeepAppearance(); Status.Text = "已应用到全部便签"; }
    }

    private void KeepAppearance()
    {
        keptAppearance = TargetNote?.Data.Appearance;
        setAppearance = null;
    }

    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        if (initializing) return;
        if (controller.TrySetStartup(StartupSwitch.IsChecked == true, out var error))
        {
            setStartup = StartupSwitch.IsChecked == true;
            Status.Text = "";
        }
        else
        {
            initializing = true;
            if (controller.TryReadStartup(out var enabled, out _)) StartupSwitch.IsChecked = enabled;
            initializing = false;
            Status.Text = error;
        }
    }

    private void OnFormatWarningsChanged(object sender, RoutedEventArgs e)
    {
        if (initializing) return;
        if (controller.TrySetFormatWarnings(FormatWarningsSwitch.IsChecked == true, out var error)) { setWarnings = FormatWarningsSwitch.IsChecked == true; Status.Text = ""; }
        else
        {
            initializing = true;
            FormatWarningsSwitch.IsChecked = controller.Preferences.ShowFormatWarnings;
            initializing = false;
            Status.Text = error;
        }
    }
    internal void RefreshStartup()
    {
        var wasInitializing = initializing;
        initializing = true;
        if (controller.TryReadStartup(out var enabled, out var error)) StartupSwitch.IsChecked = enabled;
        else Status.Text = error;
        initializing = wasInitializing;
    }

    // 输入框的 Tag 是占位提示，组合键另外记着。
    private void SetHotkey(TextBox box, HotkeyBinding binding)
    {
        hotkeys[box] = binding;
        box.Text = GlobalHotkeys.Display(binding);
    }

    private void OnHotkeyFocus(object sender, KeyboardFocusChangedEventArgs e) => controller.SuspendHotkeyCapture();

    private void OnHotkeyBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!controller.ResumeHotkeys(out var error)) Status.Text = error;
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape) return;
        e.Handled = true;
        CaptureHotkey((TextBox)sender, key, Keyboard.Modifiers);
    }

    internal void CaptureHotkey(TextBox box, Key key, ModifierKeys modifiers)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var binding = modifiers == ModifierKeys.None && key is Key.Back or Key.Delete
            ? new HotkeyBinding() : new HotkeyBinding((uint)modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        var candidate = new NoteHotkeys { NewNote = binding, ToggleNotes = new() };
        if (!GlobalHotkeys.Validate(candidate, out var error)) { Status.Text = error; return; }
        SetHotkey(box, binding);
        hotkeysDirty = true;
        Status.Text = "";
    }

    private void OnClearHotkey(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)FindName((string)((FrameworkElement)sender).Tag);
        SetHotkey(box, new());
        hotkeysDirty = true;
        Status.Text = "";
    }

    private bool SaveHotkeys()
    {
        var bindings = new NoteHotkeys { NewNote = hotkeys[NewNoteHotkey], ToggleNotes = hotkeys[ToggleNotesHotkey] };
        if (!controller.TrySaveHotkeys(bindings, out var error)) { Status.Text = error; return false; }
        hotkeysDirty = false;
        Status.Text = "快捷键已保存";
        return true;
    }

    private void OnSaveHotkeys(object sender, RoutedEventArgs e) => SaveHotkeys();

    private void OnOpenData(object sender, RoutedEventArgs e) => controller.OpenDataDirectory();

    private void OnDone(object sender, RoutedEventArgs e)
    {
        if (hotkeysDirty && !SaveHotkeys()) return;
        if (TargetNote is not null && !TargetNote.SaveNow()) { Status.Text = TargetNote.SaveError; return; }
        committed = true;
        Close();
    }
}
