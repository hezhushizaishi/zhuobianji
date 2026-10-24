using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Zhuobianji.Core;

namespace Zhuobianji;

internal partial class NoteWindow : Window
{
    private readonly NoteController controller;
    private readonly DesktopHost desktop;
    private bool permanentlyClosing;
    private bool deleteConfirmationOpen;
    internal NoteData Data { get; private set; }
    internal bool IsDesktopAttached => desktop.IsAttached;
    internal object DesktopDiagnostics => desktop.Diagnostics();

    internal NoteWindow(NoteData data, NoteController controller)
    {
        InitializeComponent();
        this.controller = controller;
        desktop = new DesktopHost(this);
        Data = data;
        collectionRailWidth = data.IsCollectionMain ? RailWidth : 0;
        MinWidth = 320 + CollectionRailWidth;
        Width = Math.Max(MinWidth, data.Width + CollectionRailWidth);
        Height = Math.Max(MinHeight, data.Height);
        Left = data.Left - CollectionRailWidth;
        Top = data.Top;
        KeepReachable();
        Topmost = data.IsPinned;
        Editor.Initialize(data);
        ApplyAppearance(data.Appearance);
        UpdateEditorButtons();
        UpdateTitle();
        UpdatePinnedButton();
        InitializeSaving();
        PreviewKeyDown += OnKeyboard;
    }

    private void KeepReachable()
    {
        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var titleBar = new Rect(Left, Top, Width, 40);
        if (!desktop.IntersectsWith(titleBar))
        {
            var workArea = SystemParameters.WorkArea;
            Left = Math.Max(workArea.Left + 16, workArea.Right - Width - 36);
            Top = workArea.Top + 80;
        }
    }

    internal void Reveal(bool activate)
    {
        WindowState = WindowState.Normal;
        ShowActivated = activate;
        Show();
        // 从隐藏的启动器运行时，仍应展示用户要编辑的窗口。
        ShowWindow(new WindowInteropHelper(this).Handle, 1);
        if (activate)
        {
            if (!desktop.IsAttached)
            {
                // 恢复窗口到前台，不改变用户持久化的置顶偏好。
                var pinned = Topmost;
                Topmost = true;
                Topmost = pinned;
            }
            Activate();
            Dispatcher.BeginInvoke(() => Editor.FocusEditor(), DispatcherPriority.Input);
        }
    }

    internal void TogglePinned()
    {
        if (desktop.IsAttached) return;
        Topmost = !Topmost;
        Update(data => data with { IsPinned = Topmost });
        UpdatePinnedButton();
    }

    private void UpdateTitle()
    {
        Title = "桌边记 · " + Data.Preview;
    }

    private void UpdatePinnedButton()
    {
        PinButton.Content = desktop.IsAttached ? "桌面" : Topmost ? "已置顶" : "置顶";
        PinButton.IsEnabled = !desktop.IsAttached;
        PinButton.ToolTip = desktop.IsAttached ? "桌面模式下不置顶；从更多操作切回浮窗后可置顶。" : "切换置顶（Ctrl+Shift+T）";
        PinButton.IsChecked = desktop.IsAttached || Topmost;
        DesktopMenuItem.IsEnabled = !desktop.IsAttached;
        FloatingMenuItem.IsEnabled = desktop.IsAttached || Data.IsOnDesktop;
    }

    internal bool TrySetDesktopMode(bool enabled, out string? error)
    {
        var wasTopmost = Topmost;
        try
        {
            if (enabled)
            {
                WindowState = WindowState.Normal;
                Topmost = false;
            }
            desktop.SetAttached(enabled);
            Update(data => data with { IsOnDesktop = enabled });
            if (!enabled) Topmost = Data.IsPinned;
            UpdatePinnedButton();
            error = null;
            return true;
        }
        catch (Win32Exception ex)
        {
            Topmost = wasTopmost;
            UpdatePinnedButton();
            error = ex.Message;
            return false;
        }
    }

    private void OnKeyboard(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.N: controller.CreateNote(this); break;
                case Key.S: SaveNow(); break;
                case Key.W: controller.HideNote(this); break;
                default: return;
            }
        }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (e.Key == Key.T) TogglePinned();
            else if (e.Key == Key.Q) controller.RequestExit();
            else return;
        }
        else return;
        e.Handled = true;
    }

    private void OnNewNote(object sender, RoutedEventArgs e) => controller.CreateNote(this);
    private void OnNewRich(object sender, RoutedEventArgs e) => controller.CreateNote(this, NoteFormat.RichText);
    private void OnNewPlain(object sender, RoutedEventArgs e) => controller.CreateNote(this, NoteFormat.PlainText);
    private void OnNewMarkdown(object sender, RoutedEventArgs e) => controller.CreateNote(this, NoteFormat.Markdown);
    private void OnTogglePinned(object sender, RoutedEventArgs e) => TogglePinned();
    private void OnHide(object sender, RoutedEventArgs e) => controller.HideNote(this);
    private void OnShowAll(object sender, RoutedEventArgs e) => controller.ShowAll();
    private void OnDesktop(object sender, RoutedEventArgs e) => controller.SetDesktopMode(this, true);
    private void OnFloating(object sender, RoutedEventArgs e) => controller.SetDesktopMode(this, false);
    private async void OnDelete(object sender, RoutedEventArgs e) => await ConfirmDelete();

    internal async Task ConfirmDelete(NoteWindow? owner = null)
    {
        if (deleteConfirmationOpen) return;
        deleteConfirmationOpen = true;
        try
        {
            if (await Dialogs.Confirm(owner ?? this, Data.Appearance.Dark, Data.IsCollectionMain ? "删除主便签" : "删除便签",
                Data.IsCollectionMain ? "主便签会永久删除，无法撤销。\n收纳的便签会释放到桌面。" : "这张便签会永久删除，无法撤销。",
                "删除", danger: true, width: 400))
                controller.DeleteNote(this);
        }
        finally { deleteConfirmationOpen = false; }
    }
    private void OnSave(object sender, RoutedEventArgs e) => SaveNow();
    private void OnExit(object sender, RoutedEventArgs e) => controller.RequestExit();
    private void OnFormatBar(object sender, RoutedEventArgs e) => FormatBar.Visibility = FormatBar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    private void OnStrike(object sender, RoutedEventArgs e) => Editor.ToggleStrike();
    private void OnSource(object sender, RoutedEventArgs e) => SetMarkdownPreview(false);
    private void OnPreview(object sender, RoutedEventArgs e) => SetMarkdownPreview(true);

    internal void SetMarkdownPreview(bool enabled)
    {
        Editor.SetPreview(enabled);
        Update(data => data with { PreviewMarkdown = enabled });
        UpdateEditorButtons();
    }

    private void UpdateEditorButtons()
    {
        var editor = Editor;
        var markdown = editor.Format == NoteFormat.Markdown;
        var rich = editor.Format == NoteFormat.RichText;
        ModeButton.Content = editor.Format switch { NoteFormat.RichText => "富文本 ▾", NoteFormat.Markdown => "Markdown ▾", _ => "纯文本 ▾" };
        RichFormatItem.IsChecked = editor.Format == NoteFormat.RichText;
        MarkdownFormatItem.IsChecked = editor.Format == NoteFormat.Markdown;
        PlainFormatItem.IsChecked = editor.Format == NoteFormat.PlainText;
        FormatButton.Visibility = rich ? Visibility.Visible : Visibility.Collapsed;
        LinkButton.Visibility = rich || markdown ? Visibility.Visible : Visibility.Collapsed;
        if (!rich) FormatBar.Visibility = Visibility.Collapsed;
        MarkdownButtons.Visibility = markdown ? Visibility.Visible : Visibility.Collapsed;
        SourceButton.IsChecked = !editor.IsPreview;
        PreviewButton.IsChecked = editor.IsPreview;
    }
    internal void ApplyAppearance(NoteAppearance appearance)
    {
        Data = Data with { Appearance = appearance };
        NoteTheme.For(appearance).ApplyTo(Resources);
        Opacity = appearance.Opacity;
        Editor.ApplyAppearance(appearance);
    }

    private void OnAppearance(object sender, RoutedEventArgs e) => controller.ShowSettings(this);

    private void OnTextColor(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ColorButton, Placement = PlacementMode.Top };
        foreach (var (name, color) in NoteTheme.TextColors.Prepend(("默认文字", NoteTheme.For(Data.Appearance).Text)))
        {
            var item = new MenuItem { Header = name, Foreground = color };
            item.Click += (_, _) => Editor.SetTextColor(color);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void OnLink(object sender, RoutedEventArgs e)
    {
        if (Editor.Format == NoteFormat.PlainText) return;
        if (Editor.IsPreview) SetMarkdownPreview(false);
        var selected = Editor.Format == NoteFormat.RichText ? Editor.Rich.Selection.Text : Editor.Source.SelectedText;
        var isAddress = NoteLinks.TryResolve(selected, out _);
        var dialog = new LinkWindow(Data.Appearance, isAddress ? selected : "", isAddress ? "" : selected) { Owner = this };
        if (dialog.ShowDialog() == true) Editor.InsertLink(dialog.TargetUri, dialog.LinkName);
        Editor.FocusEditor();
    }

    private void OnFormatMenu(object sender, RoutedEventArgs e)
    {
        ModeButton.ContextMenu.PlacementTarget = ModeButton;
        ModeButton.ContextMenu.Placement = PlacementMode.Top;
        ModeButton.ContextMenu.IsOpen = true;
    }

    internal string? FormatConversionNotice(NoteFormat target)
    {
        if (Editor.TextContent.Length == 0 || target == Editor.Format) return null;
        if (target == NoteFormat.PlainText && Editor.Format != NoteFormat.PlainText)
            return "会去掉文字排版，并保留正文和链接地址。";
        if (target == NoteFormat.Markdown && Editor.Format == NoteFormat.RichText)
            return "会保留常见排版和链接，字体、颜色等样式会去掉。";
        return null;
    }

    private async void OnChangeFormat(object sender, RoutedEventArgs e)
    {
        var target = Enum.Parse<NoteFormat>((string)((MenuItem)sender).Tag);
        var notice = FormatConversionNotice(target);
        if (notice is not null && controller.Preferences.ShowFormatWarnings)
        {
            var acknowledge = new CheckBox { Content = "已知晓，不再提示", Margin = new Thickness(0, 16, 0, 0),
                Foreground = NoteTheme.For(Data.Appearance).Text };
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = notice, TextWrapping = TextWrapping.Wrap, Foreground = NoteTheme.For(Data.Appearance).Text });
            content.Children.Add(acknowledge);
            if (!await Dialogs.Confirm(this, Data.Appearance.Dark, "切换格式", content, "转换"))
            { UpdateEditorButtons(); return; }
            if (acknowledge.IsChecked == true && !controller.TrySetFormatWarnings(false, out var error))
                Dialogs.Show(error, "设置");
        }
        ChangeNoteFormat(target);
    }

    internal void ChangeNoteFormat(NoteFormat target)
    {
        Editor.ChangeFormat(target);
        UpdateEditorButtons();
    }
    private void OnMore(object sender, RoutedEventArgs e)
    {
        RefreshCollectionState();
        MoreButton.ContextMenu.PlacementTarget = MoreButton;
        MoreButton.ContextMenu.Placement = PlacementMode.Bottom;
        MoreButton.ContextMenu.IsOpen = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!permanentlyClosing)
        {
            e.Cancel = true;
            controller.HideNote(this);
        }
        base.OnClosing(e);
    }

    internal void ClosePermanently()
    {
        permanentlyClosing = true;
        StopRailAnimation();
        saveTimer.Stop();
        Close();
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);
}
