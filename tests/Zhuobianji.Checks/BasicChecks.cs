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
        var first = controller.Windows.Values.Single();
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
        // 保存失败时数据和文件怎么样由 state 组测；这里只看窗口：显示失败提示、关闭时窗口留着。
        using (var locked = new FileStream(store.GetPath(first.Data.Id), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            first.Editor.TextContent += "\n保存失败测试。";
            Check(!first.SaveNow() && first.SaveState == "保存失败 · 点击重试", "写入失败时便签底部显示「保存失败 · 点击重试」");
            controller.HideNote(first);
            Check(first.IsVisible && !first.Data.IsHidden, "保存失败时关闭不隐藏窗口");
        }
        Check(first.SaveNow() && first.SaveState == "已保存", "解除失败原因后重试保存，提示变回「已保存」");
        controller.HideNote(first);
        Check(!first.IsVisible && first.Data.IsHidden && controller.Windows.Count == 2, "隐藏保留便签及数据");
        controller.ShowAll();
        Check(first.IsVisible && second.IsVisible && !first.Data.IsHidden, "托盘找回使用的显示链路恢复全部便签");
        first.Close();
        Check(!first.IsVisible && controller.Windows.Count == 2, "关闭窗口等同隐藏，未删除便签");
        controller.ShowAll();
        var deleted = controller.CreateNote(second);
        deleted.Editor.TextContent = "待删除的内容";
        Check(controller.DeleteNote(deleted.Entry) && controller.Windows.Count == 2 && !deleted.IsVisible &&
            controller.Book.Find(deleted.Data.Id) is null && first.IsVisible && second.IsVisible, "删除关掉这张窗口，其他便签不动");
        Capture(first, Path.Combine(root, "note-preview.png"));
        controller.HideNote(second);
        first.Editor.TextContent += "\n退出前刚输入的内容也必须保存。";
        var expected = controller.Windows.Values.Select(w => w.Snapshot()).ToArray();
        File.WriteAllText(Path.Combine(root, "expected.json"), JsonSerializer.Serialize(expected));
        Check(controller.RequestExit(), "退出同步保存尚在防抖等待中的内容");
        Check(controller.Windows.Count == 0 && store.LoadAll().Notes.Count == 2, "退出清理窗口而活动数据保留");
    }

    private static void BasicReload()
    {
        var expected = JsonSerializer.Deserialize<NoteData[]>(File.ReadAllText(Path.Combine(root, "expected.json")))!;
        var store = new NoteStore(Path.Combine(root, "lifecycle", "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        Check(controller.Book.Notes.Count == expected.Length && controller.Windows.Count == expected.Count(data => !data.IsHidden),
            "新进程恢复同样数量的便签，隐藏的便签不建窗口");
        foreach (var data in expected)
        {
            var window = WindowOf(controller, data.Id);
            Check(window.Editor.TextContent == data.Text && SameBounds(window.Snapshot(), data) && window.Topmost == data.IsPinned,
                "新进程恢复文本、位置尺寸和置顶");
            Check(window.Data.IsHidden == data.IsHidden && window.IsVisible == !data.IsHidden, "新进程保持隐藏状态");
        }
        controller.ShowAll();
        Check(controller.Windows.Values.All(w => w.IsVisible), "重启后仍能找回隐藏便签");
        // 关机、注销（会话结束）：先全部存上、彻底关掉窗口；系统随后再关剩下的窗口，便签也不会被存成隐藏。
        Check(controller.EndSession() && controller.Windows.Count == 0, "会话结束时全部存上并关掉窗口");
        foreach (var window in System.Windows.Application.Current.Windows.OfType<NoteWindow>().ToArray()) window.Close();
        using var reopened = new NoteController(new NoteStore(Path.Combine(root, "lifecycle", "Data")), () => { }, enableHotkeys: false);
        reopened.Start();
        Check(reopened.Book.Notes.Count == 2 && reopened.Windows.Count == 2 && reopened.Windows.Values.All(w => w.IsVisible && !w.Data.IsHidden),
            "关机后重开，便签照样全部显示，没有被存成隐藏");
        Check(reopened.RequestExit(), "重开后正常退出");
    }
}
