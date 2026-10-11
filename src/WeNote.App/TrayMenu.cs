using System;
using System.Drawing;
using System.Linq;
using System.Windows;
using WeNote.Core;
using Forms = System.Windows.Forms;

namespace WeNote.App;

internal sealed class TrayMenu : IDisposable
{
    private readonly NoteController controller;
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Icon trayIcon;
    internal Forms.ToolStripMenuItem StartupItem { get; private set; } = null!;

    internal TrayMenu(NoteController controller)
    {
        this.controller = controller;
        using var stream = typeof(TrayMenu).Assembly.GetManifestResourceStream("WeNote.App.Icon")!;
        trayIcon = new Icon(stream);
        icon = new Forms.NotifyIcon { Icon = trayIcon, Text = "桌边记 · 双击找回所有便签", ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => Dispatch(controller.ShowAll);
        menu.Opening += (_, _) => Rebuild();
        Rebuild();
    }

    internal void Rebuild()
    {
        foreach (Forms.ToolStripItem item in menu.Items.Cast<Forms.ToolStripItem>().ToArray()) item.Dispose();
        menu.Items.Clear();
        Add(menu.Items, "新建便签", () => controller.CreateNote());
        Add(menu.Items, "新建 Markdown 便签", () => controller.CreateNote(format: NoteFormat.Markdown));
        Add(menu.Items, "新建纯文本便签", () => controller.CreateNote(format: NoteFormat.PlainText));
        Add(menu.Items, "设置…", () => controller.ShowSettings());
        StartupItem = new Forms.ToolStripMenuItem("开机自启");
        StartupItem.Enabled = controller.TryReadStartup(out var startupEnabled, out _);
        StartupItem.Checked = startupEnabled;
        StartupItem.Click += (_, _) => Dispatch(() =>
        {
            if (controller.TryReadStartup(out var current, out var error) && controller.TrySetStartup(!current, out error))
                StartupItem.Checked = !current;
            else MessageBox.Show(error, "桌边记");
        });
        menu.Items.Add(StartupItem);
        if (controller.MainNote is { } main) Add(menu.Items, "打开收纳", () => controller.ShowNote(main));
        Add(menu.Items, "显示所有便签", controller.ShowAll);
        Add(menu.Items, "隐藏所有便签", controller.HideAll);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add(menu.Items, "全部贴到桌面", () => controller.SetAllDesktopModes(true));
        Add(menu.Items, "全部恢复普通浮窗", () => controller.SetAllDesktopModes(false));
        menu.Items.Add(new Forms.ToolStripSeparator());
        var notes = new Forms.ToolStripMenuItem($"便签（{controller.Notes.Count}）");
        foreach (var note in controller.Notes.Values)
            Add(notes.DropDownItems, (note.Data.CollectedBy is not null ? "收纳 · " : note.Data.IsHidden ? "隐藏 · " : "") + note.Data.Preview, () => controller.ShowNote(note));
        notes.Enabled = controller.Notes.Count > 0;
        menu.Items.Add(notes);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add(menu.Items, "退出程序", () => controller.RequestExit());
    }

    private static void Add(Forms.ToolStripItemCollection items, string text, Action action)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += (_, _) => Dispatch(action);
        items.Add(item);
    }

    private static void Dispatch(Action action) => Application.Current.Dispatcher.BeginInvoke(action);

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        menu.Dispose();
        trayIcon.Dispose();
    }

}
