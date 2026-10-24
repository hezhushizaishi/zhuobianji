using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;
using Zhuobianji.Core;

namespace Zhuobianji;

internal partial class SettingsWindow : FluentWindow
{
    private readonly NoteController controller;
    private bool initializing = true;
    private bool? darkTheme;
    private bool hotkeysDirty;
    internal NoteWindow? TargetNote { get; }

    internal SettingsWindow(NoteController controller, NoteWindow? note)
    {
        InitializeComponent();
        this.controller = controller;
        TargetNote = note;
        DataPath.Text = controller.DataDirectory;
        FormatWarningsSwitch.IsChecked = controller.Preferences.ShowFormatWarnings;
        SetHotkey(NewNoteHotkey, controller.Preferences.Hotkeys.NewNote);
        SetHotkey(ToggleNotesHotkey, controller.Preferences.Hotkeys.ToggleNotes);
        var appearance = note?.Data.Appearance ?? controller.Preferences.Appearance;
        NoteLabel.Text = note?.Data.Preview ?? "没有便签";
        AppearancePanel.IsEnabled = note is not null;
        foreach (var (key, name) in NoteTheme.Palettes)
            PaletteBox.Items.Add(new ComboBoxItem { Content = name, Tag = key });
        foreach (ComboBoxItem item in PaletteBox.Items)
            if ((string)item.Tag == appearance.Palette) PaletteBox.SelectedItem = item;
        if (PaletteBox.SelectedIndex < 0) PaletteBox.SelectedIndex = 0;
        foreach (var font in new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "SimSun", "Consolas" }) FontBox.Items.Add(font);
        if (!FontBox.Items.Contains(appearance.FontFamily)) FontBox.Items.Add(appearance.FontFamily);
        FontBox.SelectedItem = appearance.FontFamily;
        DarkSwitch.IsChecked = appearance.Dark;
        SizeSlider.Value = appearance.FontSize;
        OpacitySlider.Value = appearance.Opacity * 100;
        if (controller.TryReadStartup(out var enabled, out var error)) StartupSwitch.IsChecked = enabled;
        else { StartupSwitch.IsEnabled = false; Status.Text = error; }
        SetTheme(appearance.Dark);
        UpdateLabels();
        initializing = false;
        Closed += (_, _) => controller.ResumeHotkeys(out _);
    }

    private void SetTheme(bool dark)
    {
        if (darkTheme == dark) return;
        darkTheme = dark;
        NoteTheme.ApplyFluent(Resources, dark);
    }

    private void UpdateLabels()
    {
        FontSizeLabel.Text = $"{SizeSlider.Value:0}";
        OpacityLabel.Text = $"{OpacitySlider.Value:0}%";
    }

    private void OnAppearanceChanged(object sender, RoutedEventArgs e)
    {
        if (initializing || TargetNote is null || PaletteBox.SelectedItem is not ComboBoxItem palette || FontBox.SelectedItem is not string font) return;
        var appearance = new NoteAppearance { Palette = (string)palette.Tag, Dark = DarkSwitch.IsChecked == true,
            FontFamily = font, FontSize = SizeSlider.Value, Opacity = OpacitySlider.Value / 100 };
        TargetNote.ApplyAppearance(appearance);
        TargetNote.MarkDirty();
        SetTheme(appearance.Dark);
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

    private static void SetHotkey(System.Windows.Controls.TextBox box, HotkeyBinding binding)
    {
        box.Tag = binding;
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
        CaptureHotkey((System.Windows.Controls.TextBox)sender, key, Keyboard.Modifiers);
    }

    internal void CaptureHotkey(System.Windows.Controls.TextBox box, Key key, ModifierKeys modifiers)
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
        var box = (System.Windows.Controls.TextBox)FindName((string)((FrameworkElement)sender).Tag);
        SetHotkey(box, new());
        hotkeysDirty = true;
        Status.Text = "";
    }

    private bool SaveHotkeys()
    {
        var bindings = new NoteHotkeys { NewNote = (HotkeyBinding)NewNoteHotkey.Tag, ToggleNotes = (HotkeyBinding)ToggleNotesHotkey.Tag };
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
