using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

// 含原生桌面挂载，需要在当前登录的真实 Windows 桌面运行。
internal static partial class Program
{
    private static void SaveDesktopReport(NoteController controller, string name) => File.WriteAllText(Path.Combine(root, name),
        JsonSerializer.Serialize(controller.Notes.Values.Select(w => new
        {
            NoteId = w.Data.Id, DesktopMode = w.Data.IsOnDesktop, PinnedPreference = w.Data.IsPinned,
            RuntimeTopmost = w.Topmost, Hidden = w.Data.IsHidden, Native = w.DesktopDiagnostics
        }), new JsonSerializerOptions { WriteIndented = true }));

    private static async Task DesktopChecks()
    {
        var store = new NoteStore(Path.Combine(root, "desktop", "Data"));
        var original = new NoteData { Version = 1, Text = "早期版本的记录内容完整保留。", Left = 200, Top = 160, Width = 360, Height = 300, IsPinned = true };
        store.Save(original);
        var oldJson = JsonNode.Parse(File.ReadAllText(store.GetPath(original.Id)))!.AsObject();
        oldJson.Remove(nameof(NoteData.IsOnDesktop));
        File.WriteAllText(store.GetPath(original.Id), oldJson.ToJsonString());
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var first = controller.Notes.Values.Single();
        Check(!first.Data.IsOnDesktop && first.Editor.TextContent == original.Text && first.Topmost, "没有桌面字段的早期记录无需导入即可按原设置加载");
        var before = first.Snapshot();
        Check(controller.SetDesktopMode(first, true, showErrors: false), "正式便签成功切到桌面模式");
        await Task.Delay(100);
        Check(first.IsDesktopAttached && NativeChild(first) && !first.Topmost && first.Data.IsPinned,
            "实际 Progman 子窗口挂载正确并保留浮窗置顶偏好");
        Check(SameBounds(before, first.Snapshot()), "桌面切换保持位置和尺寸");
        first.Left += 20;
        first.Top += 15;
        first.Width += 40;
        first.Height += 20;
        first.Editor.TextContent += "\n桌面模式下的新内容。";
        await Task.Delay(650);
        var moved = first.Snapshot();
        Check(!first.IsDirty && first.SaveError is null && moved.Left == before.Left + 20 && moved.Top == before.Top + 15 &&
            moved.Width == before.Width + 40 && moved.Height == before.Height + 20 && NativeChild(first), "桌面模式下移动、缩放和编辑照常保存");
        var second = controller.CreateNote(first, NoteFormat.Markdown);
        second.Editor.TextContent = "# 桌面 Markdown\n\n**新建便签继承桌面模式。**";
        second.SetMarkdownPreview(true);
        Check(second.Data.IsOnDesktop && second.IsDesktopAttached && NativeChild(second), "从桌面便签新建时继承桌面模式");
        controller.HideNote(first);
        Check(!first.IsVisible && !JsonSerializer.SerializeToElement(first.DesktopDiagnostics).GetProperty("Visible").GetBoolean(), "桌面便签隐藏后原生窗口也不可见");
        controller.ShowAll();
        Check(first.IsVisible && first.IsDesktopAttached && second.IsDesktopAttached, "托盘找回保留桌面挂载");
        Check(controller.SetDesktopMode(first, false, showErrors: false) && !first.IsDesktopAttached && first.Topmost &&
            SameBounds(moved, first.Snapshot()), "恢复浮窗时恢复置顶并保持编辑后的几何信息");
        Check(controller.SetDesktopMode(first, true, showErrors: false), "恢复浮窗后可再次贴到桌面");
        controller.HideNote(second);
        first.Editor.TextContent += "\n退出前尚未等待防抖的内容。";
        SaveDesktopReport(controller, "desktop-seed-native.json");
        File.WriteAllText(Path.Combine(root, "desktop-expected.json"), JsonSerializer.Serialize(controller.Notes.Values.Select(w => w.Snapshot()).ToArray()));
        Check(controller.RequestExit(), "桌面模式正常退出并保存待写入内容");
    }

    private static void DesktopReload()
    {
        var expected = JsonSerializer.Deserialize<NoteData[]>(File.ReadAllText(Path.Combine(root, "desktop-expected.json")))!;
        using var store = new NoteStore(Path.Combine(root, "desktop", "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        Check(controller.Notes.Count == 2 && controller.Notes.Values.All(w => w.IsDesktopAttached && NativeChild(w)), "新进程自动恢复每张便签的桌面模式");
        foreach (var data in expected)
        {
            var window = controller.Notes[data.Id];
            Check(window.Editor.TextContent == data.Text && SameBounds(data, window.Snapshot()) && window.Data.IsPinned == data.IsPinned,
                "桌面模式重开后文本、位置尺寸及置顶偏好一致");
            Check(window.Editor.Format == data.Format && window.Editor.IsPreview == data.PreviewMarkdown, "桌面模式恢复编辑格式和 Markdown 预览状态");
            Check(window.IsVisible == !data.IsHidden && JsonSerializer.SerializeToElement(window.DesktopDiagnostics).GetProperty("Visible").GetBoolean() == !data.IsHidden,
                "桌面模式重开后隐藏状态一致");
        }
        SaveDesktopReport(controller, "desktop-reload-native.json");
        Check(controller.SetAllDesktopModes(false, showErrors: false) && controller.Notes.Values.All(w => !w.IsDesktopAttached), "托盘批量恢复浮窗链路成功");
        controller.ShowAll();
        Check(controller.SetAllDesktopModes(true, showErrors: false) && controller.Notes.Values.All(w => NativeChild(w)), "托盘批量桌面切换链路成功");
        Check(controller.RequestExit(), "批量切换后正常退出");
    }
}
