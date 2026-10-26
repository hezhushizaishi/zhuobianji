using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Zhuobianji.Core;

namespace Zhuobianji;

internal partial class SettingsWindow : NoteDialog
{
    private static readonly string[] Fonts = ["Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "SimSun", "Consolas"];
    private readonly NoteController controller;
    private bool initializing = true;
    private bool hotkeysDirty;
    private string font;
    private readonly Dictionary<TextBox, HotkeyBinding> hotkeys = [];
    internal NoteWindow? TargetNote { get; }

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
        AppearancePage.IsEnabled = note is not null;
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
            var item = new MenuItem { Header = new TextBlock { Text = family, FontFamily = new FontFamily(family) }, Tag = family };
            item.Click += OnFontChosen;
            FontMenu.Items.Add(item);
        }
        DarkSwitch.IsChecked = appearance.Dark;
        SizeSlider.Value = appearance.FontSize;
        OpacitySlider.Value = appearance.Opacity * 100;
        if (controller.TryReadStartup(out var enabled, out var error)) StartupSwitch.IsChecked = enabled;
        else { StartupSwitch.IsEnabled = false; Status.Text = error; }
        AppearanceTab.IsChecked = true;
        UpdateLabels();
        initializing = false;
        Closed += (_, _) => controller.ResumeHotkeys(out _);
    }

    internal RadioButton[] PaletteItems => PalettePanel.Children.OfType<RadioButton>().ToArray();

    private void OnPage(object sender, RoutedEventArgs e)
    {
        if (AppearancePage is null) return;
        AppearancePage.Visibility = AppearanceTab.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
        ProgramPage.Visibility = ProgramTab.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
        HotkeyPage.Visibility = HotkeyTab.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
    }

    private void UpdateLabels()
    {
        var palette = PaletteItems.FirstOrDefault(item => item.IsChecked == true);
        PaletteLabel.Text = palette?.ToolTip as string ?? "";
        FontLabel.Text = font;
        FontLabel.FontFamily = new FontFamily(font);
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
        var appearance = new NoteAppearance { Palette = (string)palette.Tag, Dark = DarkSwitch.IsChecked == true,
            FontFamily = font, FontSize = SizeSlider.Value, Opacity = OpacitySlider.Value / 100 };
        TargetNote.ApplyAppearance(appearance);
        TargetNote.MarkDirty();
        SetAppearance(appearance);
        UpdateLabels();
        Status.Text = "";
    }

    private void OnSaveDefaults(object sender, RoutedEventArgs e)
    {
        if (TargetNote is not null && controller.SaveDefaults(TargetNote.Data.Appearance)) Status.Text = "已设为默认";
    }

    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        if (initializing) return;
        if (controller.TrySetStartup(StartupSwitch.IsChecked == true, out var error))
            Status.Text = StartupSwitch.IsChecked == true ? "自启已开" : "自启已关";
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
        if (controller.TrySetFormatWarnings(FormatWarningsSwitch.IsChecked == true, out var error)) Status.Text = "";
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
        Close();
    }
}
