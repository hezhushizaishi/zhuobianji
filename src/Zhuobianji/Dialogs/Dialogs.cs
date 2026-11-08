using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Zhuobianji.Core;

namespace Zhuobianji;

// 程序里所有提示框和确认框的入口，外壳见 NoteDialog。
internal static class Dialogs
{
    // 普通提示：标题为「桌边记」或「桌边记 · 主题」。跟随当前便签的配色、在它上面居中；没有便签时用默认外观，屏幕居中。
    internal static void Show(string? message, string? topic = null)
    {
        var notes = Application.Current?.Windows.OfType<NoteWindow>().ToArray() ?? [];
        var owner = notes.FirstOrDefault(note => note.IsActive) ?? notes.FirstOrDefault(note => note.IsVisible);
        var dialog = new MessageDialog(owner?.Data.Appearance ?? new NoteAppearance(), topic is null ? "桌边记" : "桌边记 · " + topic,
            message ?? "", "知道了", confirm: false, danger: false, width: 400);
        Open(dialog, owner);
        dialog.ShowDialog();
    }

    // 带「取消」的确认框，配色跟随便签；点主按钮返回 true。危险操作（danger）主按钮标红，默认焦点放在「取消」上。
    internal static Task<bool> Confirm(Window owner, NoteAppearance appearance, string title, object content, string primaryText,
        bool danger = false, double width = 400)
    {
        var dialog = new MessageDialog(appearance, title, content, primaryText, confirm: true, danger, width);
        Open(dialog, owner);
        return Task.FromResult(dialog.ShowDialog() == true);
    }

    private static void Open(Window dialog, Window? owner)
    {
        if (owner is null)
        {
            dialog.ShowInTaskbar = true;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }
        dialog.Owner = owner;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}
