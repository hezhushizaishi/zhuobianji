using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using WeNote.App;
using WeNote.Core;

namespace WeNote.Tests;

internal static partial class Program
{
    private static async Task ClosureChecks()
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
            var note = controller.Notes[latest.Id];
            Check(note.IsVisible && !note.Data.IsHidden && !controller.Notes[older.Id].IsVisible,
                "全部隐藏后重启至少显示最近一张，其他隐藏便签不被全部展开");
            Check(ForegroundWindow() == new WindowInteropHelper(note).Handle && !note.Topmost && !note.Data.IsPinned,
                "普通便签启动到前台，原非置顶偏好保持");
            Check(note.Editor.TextContent == latest.Text && note.Left == latest.Left && note.Top == latest.Top,
                "启动显示修正保留原内容和窗口位置");
            var settings = controller.ShowSettings(note);
            Check(settings.DataPath.Text == Path.GetFullPath(directory), "设置直接显示实际数据目录");
            foreach (var value in new[] { 0.0, 10.0, 100.0 })
            {
                settings.OpacitySlider.Value = value;
                Check(note.Opacity == value / 100 && note.SaveNow() && store.LoadAll().Errors.Count == 0 &&
                    store.LoadAll().Notes.Single(n => n.Id == latest.Id).Appearance.Opacity == value / 100,
                    $"不透明度 {value:0}% 可应用、保存并重新读取");
            }
            Check(settings.Opacity == 1, "便签全透明不影响独立设置窗口，可从设置恢复");
            settings.SettingsTabs.SelectedIndex = 1;
            settings.UpdateLayout();
            Capture(settings, Path.Combine(root, "settings-data.png"));
            Check(controller.RequestExit(), "正常退出保存显示状态");
        }
        using (var store = new NoteStore(directory))
        using (var controller = new NoteController(store, () => { }, enableHotkeys: false))
        {
            controller.Start();
            await Task.Delay(100);
            Check(controller.Notes[latest.Id].IsVisible && !controller.Notes[older.Id].IsVisible,
                "再次启动恢复可见和隐藏状态，内容未重复创建");
            Check(controller.Notes.Count == 2 && controller.Notes[latest.Id].Editor.TextContent == latest.Text,
                "独立控制器重开后便签数量及内容保持");
        }
        using (var store = new NoteStore(Path.Combine(root, "new-computer", "Data")))
        {
            var offScreen = new NoteData { Text = "换电脑后的便签", Left = 999999, Top = 999999 };
            store.Save(offScreen);
            using var controller = new NoteController(store, () => { }, enableHotkeys: false);
            controller.Start();
            var note = controller.Notes[offScreen.Id];
            Check(note.Left < 999999 && note.Top < 999999 && note.IsVisible,
                "换屏幕后原来在屏幕外的便签回到可见位置");
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint ForegroundWindow();
}