using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Zhuobianji;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

internal static partial class Program
{
    private static async Task ReplyToDeletion(Action request, string response,
        Action<Wpf.Ui.Controls.MessageBox>? inspect = null)
    {
        var reached = false;
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var dialog = Application.Current.Windows.OfType<Wpf.Ui.Controls.MessageBox>().Single();
            reached = true;
            inspect?.Invoke(dialog);
            if (response == "close") ((Window)dialog).Close();
            else dialog.TemplateButtonCommand.Execute(response == "delete"
                ? Wpf.Ui.Controls.MessageBoxButton.Primary : Wpf.Ui.Controls.MessageBoxButton.Close);
        });
        request();
        await Task.Delay(80);
        if (!reached) throw new InvalidOperationException("删除确认框未显示");
    }

    private static async Task DeletionChecks()
    {
        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var note = controller.Notes.Values.Single();
        note.Editor.TextContent = "需要保留的记录";
        note.SaveNow();
        var other = controller.CreateNote(note);
        other.Editor.TextContent = "其他便签";
        other.SaveNow();

        Action clickDelete = () => note.DeleteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await ReplyToDeletion(clickDelete, "cancel", dialog =>
        {
            Check(controller.Notes.ContainsKey(note.Data.Id) && File.Exists(store.GetPath(note.Data.Id)) &&
                dialog.CloseButtonText == "取消" && dialog.PrimaryButtonText == "删除",
                "点击垃圾桶先显示取消／删除确认，确认前窗口和数据仍在");
            clickDelete();
            Check(Application.Current.Windows.OfType<Wpf.Ui.Controls.MessageBox>().Count() == 1,
                "同一张便签连续点删除只显示一个确认框");
            CaptureDialog(dialog, Path.Combine(root, "delete-confirmation.png"));
        });
        Check(note.IsVisible && File.Exists(store.GetPath(note.Data.Id)) && note.Editor.TextContent.Contains("需要保留"),
            "取消保留原窗口和正文");
        await ReplyToDeletion(clickDelete, "close");
        Check(controller.Notes.ContainsKey(note.Data.Id) && note.IsVisible,
            "关闭确认框不删除便签");
        var menuDelete = note.MoreButton.ContextMenu.Items.OfType<MenuItem>().Single(item =>
            Equals(item.Header, "删除这张便签"));
        await ReplyToDeletion(() => menuDelete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)), "cancel");
        Check(controller.Notes.ContainsKey(note.Data.Id), "更多菜单删除同样先确认，取消不删除");

        await ReplyToDeletion(clickDelete, "delete");
        await Task.Delay(500);
        Check(!controller.Notes.ContainsKey(note.Data.Id) &&
            !Directory.EnumerateFiles(store.DirectoryPath, note.Data.Id.ToString("N") + "*").Any() &&
            controller.Notes.ContainsKey(other.Data.Id), "确认后只永久删除目标和副本，计时器不会重新生成文件");

        controller.SetMainNote(other);
        var child = controller.CreateNote(other);
        child.Editor.TextContent = "收纳的记录";
        child.SaveNow();
        controller.CollectNote(child);
        var railDelete = other.CollectionTabButtons[child.Data.Id].ContextMenu.Items.OfType<MenuItem>()
            .Single(item => Equals(item.Header, "删除这张"));
        Action deleteChild = () => railDelete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await ReplyToDeletion(deleteChild, "cancel", dialog =>
            Check(dialog.Owner == other && File.Exists(store.GetPath(child.Data.Id)),
                "隐藏的收纳便签从主便签弹出确认，确认前数据不动"));
        Check(child.Data.CollectedBy == other.Data.Id && !child.IsVisible &&
            other.CollectionTabButtons.ContainsKey(child.Data.Id), "收纳项取消删除后仍保持收纳与收起状态");
        await ReplyToDeletion(deleteChild, "delete");
        Check(!controller.Notes.ContainsKey(child.Data.Id) && !other.CollectionTabButtons.ContainsKey(child.Data.Id) &&
            !File.Exists(store.GetPath(child.Data.Id)), "收纳项确认删除后清理该便签和标签");

        var retained = controller.CreateNote(other);
        retained.Editor.TextContent = "主便签删掉后仍需保留";
        retained.SaveNow();
        controller.CollectNote(retained);
        Action deleteMain = () => other.DeleteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await ReplyToDeletion(deleteMain, "cancel", dialog =>
        {
            Check(((string)dialog.Content).Contains("收纳的便签会释放") &&
                retained.Data.CollectedBy == other.Data.Id && !retained.IsVisible,
                "主便签确认说明释放收纳项，确认前不提前释放");
            CaptureDialog(dialog, Path.Combine(root, "delete-main-confirmation.png"));
        });
        Check(controller.MainNote == other && retained.Data.CollectedBy == other.Data.Id,
            "取消删除主便签保留收纳关系");
        await ReplyToDeletion(deleteMain, "delete");
        Check(controller.MainNote is null && retained.IsVisible && retained.Data.CollectedBy is null &&
            File.Exists(store.GetPath(retained.Data.Id)) && retained.Editor.TextContent.Contains("仍需保留"),
            "确认删除主便签只删自己，收纳便签和文字释放后保留");
        Check(controller.RequestExit(), "确认删除后剩余便签正常保存退出");
    }
}