using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

internal static partial class Program
{
    private static async Task BasicChecks()
    {
        var store = new NoteStore(Path.Combine(root, "lifecycle", "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var first = controller.Notes.Values.Single();
        var second = controller.CreateNote(first);
        first.Left = 120;
        first.Top = 130;
        first.Width = 380;
        first.Height = 280;
        first.Editor.TextContent = "第一张便签：中文、多行与 emoji 📝。\n窗口位置和置顶都应该保留。";
        first.TogglePinned();
        second.Left = 530;
        second.Top = 155;
        second.Width = 360;
        second.Height = 310;
        second.Editor.TextContent = "第二张便签：独立保存。\n不会覆盖第一张。";
        second.Editor.TextContent += "\n这是最后一次输入。";
        await Task.Delay(700);
        var loaded = store.LoadAll();
        Check(loaded.Notes.Count == 2 && !first.IsDirty && !second.IsDirty &&
            loaded.Notes.Single(n => n.Id == second.Data.Id).Text == second.Editor.TextContent, "防抖后保存最后一次输入");
        Check(first.Topmost && first.SaveError is null && second.SaveError is null, "置顶生效且两个窗口保存成功");
        var savedBeforeFailure = File.ReadAllBytes(store.GetPath(first.Data.Id));
        using (var locked = new FileStream(store.GetPath(first.Data.Id), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            first.Editor.TextContent += "\n保存失败测试。";
            Check(!first.SaveNow() && first.IsDirty && first.SaveError is not null, "写入失败保留未保存状态并显示错误");
            controller.HideNote(first);
            Check(first.IsVisible && !first.Data.IsHidden && savedBeforeFailure.SequenceEqual(File.ReadAllBytes(store.GetPath(first.Data.Id))),
                "保存失败时不隐藏窗口、不改写旧文件");
        }
        Check(first.SaveNow() && !first.IsDirty && first.SaveError is null, "解除失败原因后可以重试保存");
        controller.HideNote(first);
        Check(!first.IsVisible && first.Data.IsHidden && controller.Notes.Count == 2, "隐藏保留便签及数据");
        controller.ShowAll();
        Check(first.IsVisible && second.IsVisible && !first.Data.IsHidden, "托盘找回使用的显示链路恢复全部便签");
        first.Close();
        Check(!first.IsVisible && controller.Notes.Count == 2, "关闭窗口等同隐藏，未删除便签");
        controller.ShowAll();
        var deleted = controller.CreateNote(second);
        deleted.Editor.TextContent = "待删除的内容";
        Check(controller.DeleteNote(deleted) && controller.Notes.Count == 2 &&
            !File.Exists(store.GetPath(deleted.Data.Id)) && !Directory.Exists(Path.Combine(store.DirectoryPath, "Deleted")), "删除活动窗口不留副本");
        Capture(first, Path.Combine(root, "note-preview.png"));
        controller.HideNote(second);
        first.Editor.TextContent += "\n退出前刚输入的内容也必须保存。";
        var expected = controller.Notes.Values.Select(w => w.Snapshot()).ToArray();
        File.WriteAllText(Path.Combine(root, "expected.json"), JsonSerializer.Serialize(expected));
        Check(controller.RequestExit(), "退出同步保存尚在防抖等待中的内容");
        Check(controller.Notes.Count == 0 && store.LoadAll().Notes.Count == 2, "退出清理窗口而活动数据保留");
    }

    private static void BasicReload()
    {
        var expected = JsonSerializer.Deserialize<NoteData[]>(File.ReadAllText(Path.Combine(root, "expected.json")))!;
        var store = new NoteStore(Path.Combine(root, "lifecycle", "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        Check(controller.Notes.Count == expected.Length, "新进程恢复同样数量的便签");
        foreach (var data in expected)
        {
            var window = controller.Notes[data.Id];
            Check(window.Editor.TextContent == data.Text && SameBounds(window.Snapshot(), data) && window.Topmost == data.IsPinned,
                "新进程恢复文本、位置尺寸和置顶");
            Check(window.Data.IsHidden == data.IsHidden && window.IsVisible == !data.IsHidden, "新进程保持隐藏状态");
        }
        controller.ShowAll();
        Check(controller.Notes.Values.All(w => w.IsVisible), "重启后仍能找回隐藏便签");
        Check(controller.RequestExit(), "重启后的正常退出成功");
    }
}
