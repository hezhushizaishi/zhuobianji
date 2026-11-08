using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Zhuobianji;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

internal static partial class Program
{
    private static async Task StartupChecks()
    {
        var directory = Path.Combine(root, "restart", "Data");
        var older = new NoteData { Text = "较早的隐藏便签", IsHidden = true, UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        var latest = new NoteData { Text = "最近的隐藏便签", IsHidden = true, IsPinned = false, Left = 240, Top = 150, UpdatedAt = DateTimeOffset.UtcNow };
        using (var seed = new NoteStore(directory)) { seed.Save(older); seed.Save(latest); }
        using (var store = new NoteStore(directory))
        using (var controller = new NoteController(store, () => { }, enableHotkeys: false))
        {
            controller.Start();
            await Task.Delay(150);
            var note = controller.Windows[latest.Id];
            Check(note.IsVisible && !note.Data.IsHidden && controller.WindowOf(controller.Book.Find(older.Id)) is null,
                "全部隐藏后重启至少显示最近一张，其他隐藏便签不被全部展开");
            Check(ForegroundWindow() == new WindowInteropHelper(note).Handle && !note.Topmost && !note.Data.IsPinned,
                "普通便签启动到前台，原非置顶偏好保持");
            Check(note.Editor.TextContent == latest.Text && note.Snapshot().Left == latest.Left && note.Snapshot().Top == latest.Top,
                "启动显示修正保留原内容和窗口位置");
            var settings = controller.ShowSettings(note);
            Check(settings.DataPath.Text == Path.GetFullPath(directory), "设置直接显示实际数据目录");
            foreach (var value in new[] { 70.0, 85.0, 100.0 })
            {
                settings.OpacitySlider.Value = value;
                Check(note.Opacity == value / 100 && note.SaveNow() && store.LoadAll().Errors.Count == 0 &&
                    store.LoadAll().Notes.Single(n => n.Id == latest.Id).Appearance.Opacity == value / 100,
                    $"不透明度 {value:0}% 可应用、保存并重新读取");
            }
            settings.OpacitySlider.Value = 0;
            Check(settings.OpacitySlider.Value == 70 && note.Opacity == 0.7 && settings.Opacity == 1,
                "透明度最低调到 70%，便签不会透明到看不见；设置窗口自己不跟着透明");
            // 0.8.x 能存下 0%：重开设置时滑块停在 70%，和便签实际显示的一样。
            note.ApplyAppearance(note.Data.Appearance with { Opacity = 0 });
            settings.Close();
            settings = controller.ShowSettings(note);
            Check(note.Opacity == 0.7 && settings.OpacitySlider.Value == 70 && settings.OpacityLabel.Text == "70%",
                "旧数据里 0% 的便签按 70% 显示，设置里也显示 70%");
            settings.OpacitySlider.Value = 100;
            settings.ProgramTab.IsChecked = true;
            CaptureDialog(settings, Path.Combine(root, "settings-data.png"));
            Check(controller.RequestExit(), "正常退出保存显示状态");
        }
        using (var store = new NoteStore(directory))
        using (var controller = new NoteController(store, () => { }, enableHotkeys: false))
        {
            controller.Start();
            await Task.Delay(100);
            Check(controller.Windows[latest.Id].IsVisible && controller.WindowOf(controller.Book.Find(older.Id)) is null,
                "再次启动恢复可见和隐藏状态，内容未重复创建");
            Check(controller.Book.Notes.Count == 2 && controller.Windows[latest.Id].Editor.TextContent == latest.Text,
                "独立控制器重开后便签数量及内容保持");
        }
        using (var store = new NoteStore(Path.Combine(root, "new-computer", "Data")))
        {
            var offScreen = new NoteData { Text = "换电脑后的便签", Left = 999999, Top = 999999 };
            store.Save(offScreen);
            using var controller = new NoteController(store, () => { }, enableHotkeys: false);
            controller.Start();
            var note = controller.Windows[offScreen.Id];
            Check(note.Left < 999999 && note.Top < 999999 && note.IsVisible,
                "换屏幕后原来在屏幕外的便签回到可见位置");
        }

        // 主便签读不了：收着的便签放回桌面并显示出来，即使还有别的便签正常显示着。
        // 一种是文件就读不了（启动时放回）；一种是文件读得了、正文读不了（主便签隐藏着，第一次打开时才发现）。
        using (var store = new NoteStore(Path.Combine(root, "orphans-file", "Data")))
        {
            var shown = new NoteData { Text = "正常显示的便签" };
            var main = new NoteData { Text = "文件坏了的主便签", IsCollectionMain = true };
            var child = new NoteData { Text = "收着的记录", CollectedBy = main.Id, IsHidden = true };
            store.Save(shown); store.Save(main); store.Save(child);
            File.WriteAllText(store.GetPath(main.Id), "主便签读不了");
            using var controller = new NoteController(store, () => { }, enableHotkeys: false);
            var notice = AnswerNotice(controller.Start);
            var released = controller.WindowOf(controller.Book.Find(child.Id));
            Check(notice?.Contains("部分数据无法读取") == true && released?.IsVisible == true &&
                released.Data is { CollectedBy: null, IsHidden: false } && controller.Windows[shown.Id].IsVisible,
                "主便签文件读不了：收着的便签放回桌面并显示，正常的便签照常显示");
        }
        using (var store = new NoteStore(Path.Combine(root, "orphans-content", "Data")))
        {
            var shown = new NoteData { Text = "正常显示的便签" };
            var main = new NoteData { Text = "正文坏了的主便签", IsCollectionMain = true, IsHidden = true, Format = NoteFormat.RichText, RichText = "<Section 坏了" };
            var child = new NoteData { Text = "收着的记录", CollectedBy = main.Id, IsHidden = true };
            store.Save(shown); store.Save(main); store.Save(child);
            using var controller = new NoteController(store, () => { }, enableHotkeys: false);
            controller.Start();
            Check(controller.Windows.Count == 1 && controller.Book.Main is not null, "主便签隐藏着，启动时还没发现正文读不了");
            var notice = AnswerNotice(() => controller.ShowNote(controller.Book.Main!));
            var released = controller.WindowOf(controller.Book.Find(child.Id));
            Check(notice?.Contains("富文本内容无法读取") == true && controller.Book.Main is null && File.Exists(store.GetPath(main.Id)) &&
                released?.IsVisible == true && released.Data is { CollectedBy: null, IsHidden: false } &&
                store.LoadAll().Notes.Single(data => data.Id == child.Id).CollectedBy is null,
                "打开时才发现主便签正文读不了：提示，原文件留着，收着的便签放回桌面并显示");
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint ForegroundWindow();
}