using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WeNote.App;
using WeNote.Core;

namespace WeNote.Tests;

internal static partial class Program
{
    private static async Task LifecycleChecks()
    {
        using var store = new NoteStore(Path.Combine(root, "Data"));
        var original = new NoteHotkeys { NewNote = new(7, 0x77), ToggleNotes = new(7, 0x78) };
        store.SavePreferences(new() { Hotkeys = original });
        using var controller = new NoteController(store, () => { });
        controller.Start();
        var receiver = (GlobalHotkeys)typeof(NoteController).GetField("hotkeys", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
        SendLifecycleMessage(receiver.Handle, 0x0312, 1, 0);
        Check(controller.Notes.Count == 2, "系统快捷键消息实际新建便签");
        SendLifecycleMessage(receiver.Handle, 0x0312, 2, 0);
        Check(controller.Notes.Values.All(w => !w.IsVisible), "全局快捷键隐藏全部");
        SendLifecycleMessage(receiver.Handle, 0x0312, 2, 0);
        Check(controller.Notes.Values.All(w => w.IsVisible), "再次按快捷键找回全部");

        var note = controller.Notes.Values.First();
        var other = controller.Notes.Values.Last();
        note.Editor.TextContent = "即将删除";
        note.SaveNow();
        note.Editor.TextContent = "生成保存副本";
        note.SaveNow();
        Check(File.Exists(store.GetPath(note.Data.Id) + ".bak"), "删除前确有上一版保存副本");
        var settings = controller.ShowSettings(note);
        settings.SettingsTabs.SelectedIndex = 2;
        await Task.Delay(100);
        settings.UpdateLayout();
        Check(settings.NewNoteHotkey.IsVisible && settings.NewNoteHotkey.Text == "Ctrl+Alt+Shift+F8", "设置的快捷键页显示已保存组合");
        settings.NewNoteHotkey.Focus();
        using (var captureProbe = new GlobalHotkeys(() => { }, () => { }))
            Check(captureProbe.TryApply(new() { NewNote = original.NewNote, ToggleNotes = new() }, out _),
                "录入框获得焦点时暂停自身全局快捷键，避免录入误触");
        settings.ApplyHotkeysButton.Focus();
        using (var resumeProbe = new GlobalHotkeys(() => { }, () => { }))
            Check(!resumeProbe.TryApply(new() { NewNote = original.NewNote, ToggleNotes = new() }, out _),
                "离开录入框后恢复原全局快捷键");
        Capture(settings, Path.Combine(root, "settings-hotkeys.png"));
        Capture(note, Path.Combine(root, "note-delete.png"));

        using (var blocker = new GlobalHotkeys(() => { }, () => { }))
        {
            Check(blocker.TryApply(new() { NewNote = new(7, 0x79), ToggleNotes = new() }, out _), "测试用冲突组合已在系统注册");
            settings.CaptureHotkey(settings.NewNoteHotkey, Key.F10, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
            settings.ApplyHotkeysButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(controller.Preferences.Hotkeys == original && store.LoadPreferences().Hotkeys == original &&
                settings.Status.Text.Contains("占用"), "占用时保留原快捷键和文件，并显示原因");
            SendLifecycleMessage(receiver.Handle, 0x0312, 1, 0);
            Check(controller.Notes.Count == 3, "配置失败后原快捷键仍能工作");
            controller.DeleteNote(controller.Notes.Values.Last());
        }
        settings.ApplyHotkeysButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(controller.Preferences.Hotkeys.NewNote == new HotkeyBinding(7, 0x79) &&
            store.LoadPreferences().Hotkeys == controller.Preferences.Hotkeys, "释放占用后可应用并持久保存新组合");
        var persisted = controller.Preferences;
        using (var lockedSettings = new FileStream(Path.Combine(store.DirectoryPath, "settings.json"),
            FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(!controller.TrySetFormatWarnings(!persisted.ShowFormatWarnings, out var formatError) &&
                formatError is not null && controller.Preferences == persisted && store.LoadPreferences() == persisted,
                "格式提示写入失败保持内存偏好和已保存设置");
            Check(!controller.TrySaveHotkeys(original, out var hotkeyError) &&
                hotkeyError is not null && controller.Preferences == persisted && store.LoadPreferences() == persisted,
                "快捷键存储失败保持文件与偏好，恢复已成功保存的组合");
        }
        using (var rollbackProbe = new GlobalHotkeys(() => { }, () => { }))
        {
            Check(rollbackProbe.TryApply(new() { NewNote = original.NewNote, ToggleNotes = new() }, out _) &&
                !rollbackProbe.TryApply(new() { NewNote = persisted.Hotkeys.NewNote, ToggleNotes = new() }, out _),
                "存储失败释放未保存的新组合，并重新注册原有效组合");
        }
        using (var oldKeyProbe = new GlobalHotkeys(() => { }, () => { }))
            Check(oldKeyProbe.TryApply(new() { NewNote = original.NewNote, ToggleNotes = new() }, out _), "改键后旧组合已释放");

        settings.CaptureHotkey(settings.ToggleNotesHotkey, Key.F10, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
        settings.ApplyHotkeysButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(settings.Status.Text.Contains("不能相同") && controller.Preferences.Hotkeys.ToggleNotes == original.ToggleNotes,
            "两项重复不会覆盖有效设置");
        settings.CaptureHotkey(settings.ToggleNotesHotkey, Key.Back, ModifierKeys.None);
        settings.ApplyHotkeysButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(!controller.Preferences.Hotkeys.ToggleNotes.IsEnabled && settings.ToggleNotesHotkey.Text == "",
            "清空可关闭单项全局快捷键");

        note.Editor.TextContent = "尚在防抖等待中的正文";
        var id = note.Data.Id;
        await ReplyToDeletion(() => note.DeleteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)), "delete");
        await Task.Delay(650);
        Check(!controller.Notes.ContainsKey(id) && !note.IsVisible && !settings.IsVisible &&
            !Directory.EnumerateFiles(store.DirectoryPath, id.ToString("N") + "*").Any() &&
            !Directory.Exists(Path.Combine(store.DirectoryPath, "Deleted")),
            "点垃圾桶确认后删除窗口、JSON、副本，等待保存周期后也不会复活");
        Check(other.IsVisible && controller.Notes.Count == 1, "删除只影响这一张便签，其他窗口继续工作");

        using (var locked = new FileStream(store.GetPath(other.Data.Id), FileMode.Open, FileAccess.Read, FileShare.Read))
            Check(!controller.DeleteNote(other) && other.IsVisible && controller.Notes.Count == 1 &&
                File.Exists(store.GetPath(other.Data.Id)), "文件占用导致删除失败时保留便签并提示");
        Check(controller.DeleteNote(other) && controller.Notes.Count == 0, "解除占用后可删除最后一张");
        controller.ToggleAll();
        Check(controller.Notes.Count == 1 && controller.Notes.Values.Single().IsVisible, "全部删除后仍能通过快捷键新建继续使用");
        Check(controller.RequestExit() && store.LoadAll().Notes.Count == 1, "正常退出保存剩余便签而不删除");

        using var releasedProbe = new GlobalHotkeys(() => { }, () => { });
        Check(releasedProbe.TryApply(new() { NewNote = new(7, 0x79), ToggleNotes = new() }, out _),
            "退出后释放系统快捷键");
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendLifecycleMessage(nint window, uint message, nint wParam, nint lParam);
}