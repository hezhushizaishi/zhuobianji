using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using Zhuobianji.Core;

namespace Zhuobianji;

// 托盘图标和右键菜单。菜单是 WPF 菜单，和便签菜单同一套样式（NoteStyles.xaml），配色用默认外观；每次打开前按当前状态重建。
internal sealed class TrayMenu : IDisposable
{
    private readonly NoteController controller;
    private readonly TaskbarIcon icon;
    private readonly ContextMenu menu = new();
    internal MenuItem StartupItem { get; private set; } = null!;
    internal ContextMenu Menu => menu;
    internal bool IsCreated => icon.IsCreated;
    internal string ToolTip => icon.ToolTipText;

    internal TrayMenu(NoteController controller)
    {
        this.controller = controller;
        menu.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Zhuobianji;component/Theme/NoteStyles.xaml", UriKind.Relative) });
        menu.Style = (Style)menu.FindResource(typeof(ContextMenu));
        icon = new TaskbarIcon
        {
            IconSource = BitmapFrame.Create(new Uri("pack://application:,,,/Zhuobianji;component/Assets/zhuobianji.ico")),
            ToolTipText = $"{AppInfo.Name} {AppInfo.Version} · 双击找回所有便签", ContextMenu = menu
        };
        icon.TrayLeftMouseDoubleClick += (_, _) => controller.ShowAll();
        icon.PreviewTrayContextMenuOpen += (_, _) => Rebuild();
        Rebuild();
        // 不放进窗口，要手动创建；不开「效率模式」，否则整个程序会被系统降速。
        icon.ForceCreate(enablesEfficiencyMode: false);
    }

    internal void Rebuild()
    {
        NoteTheme.For(controller.Preferences.Appearance).ApplyTo(menu.Resources);
        menu.Items.Clear();
        Add(menu.Items, "新建便签", NoteIconSymbol.Add20, () => controller.CreateNote());
        Add(menu.Items, "新建 Markdown 便签", NoteIconSymbol.Markdown20, () => controller.CreateNote(format: NoteFormat.Markdown));
        Add(menu.Items, "新建纯文本便签", NoteIconSymbol.TextT20, () => controller.CreateNote(format: NoteFormat.PlainText));
        menu.Items.Add(new Separator());
        Add(menu.Items, "设置…", NoteIconSymbol.Settings20, () => controller.ShowSettings());
        StartupItem = new MenuItem { Header = "开机自启", IsEnabled = controller.TryReadStartup(out var startupEnabled, out _), IsChecked = startupEnabled };
        StartupItem.Click += (_, _) => Dispatch(() =>
        {
            if (controller.TryReadStartup(out var current, out var error) && controller.TrySetStartup(!current, out error))
                StartupItem.IsChecked = !current;
            else Dialogs.Show(error);
        });
        menu.Items.Add(StartupItem);
        menu.Items.Add(new Separator());
        if (controller.Book.Main is { } main) Add(menu.Items, "打开收纳", NoteIconSymbol.PanelLeft20, () => controller.ShowNote(main));
        Add(menu.Items, "显示所有便签", NoteIconSymbol.WindowMultiple20, controller.ShowAll);
        Add(menu.Items, "隐藏所有便签", NoteIconSymbol.EyeOff20, controller.HideAll);
        menu.Items.Add(new Separator());
        Add(menu.Items, "全部贴到桌面", NoteIconSymbol.Desktop20, () => controller.SetAllDesktopModes(true));
        Add(menu.Items, "全部恢复普通浮窗", NoteIconSymbol.Window20, () => controller.SetAllDesktopModes(false));
        menu.Items.Add(new Separator());
        var all = controller.Book.Notes;
        var notes = new MenuItem { Header = $"便签（{all.Count}）", Icon = new NoteIcon { Symbol = NoteIconSymbol.Notepad20 } };
        // 预览文字放进 TextBlock，下划线不会被当成快捷键标记。
        foreach (var note in all.ToArray())
            Add(notes.Items, new TextBlock { Text = (note.Data.CollectedBy is not null ? "收纳 · " : note.Data.IsHidden ? "隐藏 · " : "") + note.Data.Preview,
                MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis },
                null, () => controller.ShowNote(note));
        notes.IsEnabled = all.Count > 0;
        menu.Items.Add(notes);
        menu.Items.Add(new Separator());
        Add(menu.Items, "退出程序", NoteIconSymbol.Power20, () => controller.RequestExit());
    }

    private static void Add(ItemCollection items, object header, NoteIconSymbol? symbol, Action action)
    {
        var item = new MenuItem { Header = header, Icon = symbol is { } value ? new NoteIcon { Symbol = value } : null };
        item.Click += (_, _) => Dispatch(action);
        items.Add(item);
    }

    private static void Dispatch(Action action) => Application.Current.Dispatcher.BeginInvoke(action);

    public void Dispose() => icon.Dispose();
}
