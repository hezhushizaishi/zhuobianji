using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WeNote.App;
using WeNote.Core;

namespace WeNote.Tests;

internal static partial class Program
{
    private static int passed;
    private static string root = "";

    [STAThread]
    private static int Main(string[] args)
    {
        var phases = new Dictionary<string, Func<Task>>(StringComparer.Ordinal)
        {
            ["storage"] = Sync(StorageChecks),
            ["seed"] = Seed,
            ["reload"] = Sync(Reload),
            ["preview"] = Preview,
            ["desktop-seed"] = DesktopSeed,
            ["desktop-reload"] = Sync(DesktopReload),
            ["modern-seed"] = ModernSeed,
            ["modern-reload"] = Sync(ModernReload),
            ["settings"] = SettingsChecks,
            ["closure"] = ClosureChecks,
            ["iteration"] = IterationChecks,
            ["lifecycle"] = LifecycleChecks,
            ["close-seed"] = CloseNoteSeed,
            ["close-reload"] = CloseNoteReload,
            ["order-seed"] = CollectionOrderSeed,
            ["order-reload"] = CollectionOrderReload,
            ["delete-confirm"] = DeleteConfirmationChecks,
            ["rail-seed"] = RailToggleSeed,
            ["rail-reload"] = RailToggleReload,
            ["collection-seed"] = CollectionSeed,
            ["collection-reload"] = CollectionReload,
            ["paste-layout"] = PasteLayoutChecks,
            ["paste-links"] = PasteLinkChecks,
            ["inline-links"] = InlineLinkChecks,
            ["polish"] = PolishChecks,
            ["polish-reload"] = Sync(PolishReload)
        };
        if (args.Length != 2 || !phases.TryGetValue(args[0], out var check))
        {
            Console.Error.WriteLine("需要有效检查阶段与测试目录；按功能运行请使用 scripts/test-app.ps1。");
            return 1;
        }
        root = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(root);
        if (args[0] == "storage")
        {
            try { check().GetAwaiter().GetResult(); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                await check();
                Console.WriteLine($"PASS {args[0]}：{passed} 项检查");
                app.Shutdown(0);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
        };
        return app.Run();
    }

    private static Func<Task> Sync(Action action) => () =>
    {
        action();
        return Task.CompletedTask;
    };
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL：" + name);
        passed++;
        Console.WriteLine("PASS：" + name);
    }

    private static bool SameNoteData(NoteData? actual, NoteData expected) =>
        actual is not null && actual.CollectionOrder.SequenceEqual(expected.CollectionOrder) &&
        (actual with { CollectionOrder = expected.CollectionOrder }) == expected;
    private static void StorageChecks()
    {
        var directory = Path.Combine(root, "storage");
        using (var store = new NoteStore(directory))
        {
            var first = new NoteData { Text = "中文 📝\n第二行\t制表符", Left = -300, IsPinned = true };
            var second = new NoteData { Text = "独立便签", IsHidden = true };
            store.Save(first);
            store.Save(second);
            var loaded = store.LoadAll();
            Check(loaded.Errors.Count == 0 && loaded.Notes.Count == 2 && loaded.Notes.Any(note => SameNoteData(note, first)) && loaded.Notes.Any(note => SameNoteData(note, second)),
                "Unicode、多行、窗口设置完整往返，两便签互不覆盖");
            var updated = first with { Text = "最新内容" };
            store.Save(updated);
            var backup = JsonSerializer.Deserialize<NoteData>(File.ReadAllText(store.GetPath(first.Id) + ".bak"));
            Check(SameNoteData(backup, first), "覆盖保存保留上次成功版本");
            var originalBytes = File.ReadAllBytes(store.GetPath(first.Id));
            using (var locked = new FileStream(store.GetPath(first.Id), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var failed = false;
                try { store.Save(updated with { Text = "不应写入" }); }
                catch (IOException) { failed = true; }
                Check(failed && originalBytes.SequenceEqual(File.ReadAllBytes(store.GetPath(first.Id))), "替换失败时现有内容保持完整");
            }
            var blocked = false;
            try { using var duplicate = new NoteStore(directory); }
            catch (IOException) { blocked = true; }
            Check(blocked, "同一数据目录阻止第二个写入实例");
            var brokenPath = Path.Combine(directory, "broken.json");
            File.WriteAllText(brokenPath, "不是 JSON");
            loaded = store.LoadAll();
            Check(loaded.Notes.Count == 2 && loaded.Errors.Count == 1 && File.ReadAllText(brokenPath) == "不是 JSON", "损坏记录报告错误且原文不被改写");
            store.Save(second);
            File.WriteAllText(store.GetPath(second.Id) + ".tmp", "待写副本");
            store.Delete(second.Id);
            Check(!File.Exists(store.GetPath(second.Id)) && !File.Exists(store.GetPath(second.Id) + ".bak") &&
                !File.Exists(store.GetPath(second.Id) + ".tmp") && store.LoadAll().Notes.Count == 1,
                "永久删除同时清理活动记录、保存副本与临时文件");
        }
        using var reopened = new NoteStore(directory);
        Check(reopened.LoadAll().Notes.Count == 1, "释放实例后可重新打开数据目录");
        Console.WriteLine($"PASS storage：{passed} 项检查");
    }

    private static async Task Seed()
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

    private static void Reload()
    {
        var expected = JsonSerializer.Deserialize<NoteData[]>(File.ReadAllText(Path.Combine(root, "expected.json")))!;
        var store = new NoteStore(Path.Combine(root, "lifecycle", "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        Check(controller.Notes.Count == expected.Length, "新进程恢复同样数量的便签");
        foreach (var data in expected)
        {
            var window = controller.Notes[data.Id];
            Check(window.Editor.TextContent == data.Text && window.Left == data.Left && window.Top == data.Top &&
                window.Width == data.Width && window.Height == data.Height && window.Topmost == data.IsPinned,
                "新进程恢复文本、位置尺寸和置顶");
            Check(window.Data.IsHidden == data.IsHidden && window.IsVisible == !data.IsHidden, "新进程保持隐藏状态");
        }
        controller.ShowAll();
        Check(controller.Notes.Values.All(w => w.IsVisible), "重启后仍能找回隐藏便签");
        Check(controller.RequestExit(), "重启后的正常退出成功");
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var visual = window.Content as FrameworkElement ?? (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        var image = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static bool SameBounds(NoteData a, NoteData b) => Math.Abs(a.Left - b.Left) < 1 && Math.Abs(a.Top - b.Top) < 1 &&
        Math.Abs(a.Width - b.Width) < 1 && Math.Abs(a.Height - b.Height) < 1;

    private static bool NativeChild(NoteWindow window)
    {
        var diagnostic = JsonSerializer.SerializeToElement(window.DesktopDiagnostics);
        var style = long.Parse(diagnostic.GetProperty("Style").GetString()!, NumberStyles.HexNumber);
        return (style & 0x40000000) != 0 && diagnostic.GetProperty("ParentClass").GetString() == "Progman";
    }

    private static void SaveDesktopReport(NoteController controller, string name) => File.WriteAllText(Path.Combine(root, name),
        JsonSerializer.Serialize(controller.Notes.Values.Select(w => new
        {
            NoteId = w.Data.Id, DesktopMode = w.Data.IsOnDesktop, PinnedPreference = w.Data.IsPinned,
            RuntimeTopmost = w.Topmost, Hidden = w.Data.IsHidden, Native = w.DesktopDiagnostics
        }), new JsonSerializerOptions { WriteIndented = true }));

    private static async Task DesktopSeed()
    {
        var store = new NoteStore(Path.Combine(root, "desktop", "Data"));
        var original = new NoteData { Version = 1, Text = "M1 格式内容完整保留。", Left = 200, Top = 160, Width = 360, Height = 300, IsPinned = true };
        store.Save(original);
        var oldJson = JsonNode.Parse(File.ReadAllText(store.GetPath(original.Id)))!.AsObject();
        oldJson.Remove(nameof(NoteData.IsOnDesktop));
        File.WriteAllText(store.GetPath(original.Id), oldJson.ToJsonString());
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var first = controller.Notes.Values.Single();
        Check(!first.Data.IsOnDesktop && first.Editor.TextContent == original.Text && first.Topmost, "M1 文件无需导入即可按原设置加载");
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
