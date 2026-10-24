using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace Zhuobianji;

// 程序里所有提示框和确认框的入口；以后统一换外观只改这里。
internal static class Dialogs
{
    // 普通提示：标题为「桌边记」或「桌边记 · 主题」。
    internal static void Show(string? message, string? topic = null) =>
        System.Windows.MessageBox.Show(message ?? "", topic is null ? "桌边记" : "桌边记 · " + topic);

    // 带「取消」的确认框，深浅色跟随便签；点主按钮返回 true。
    // 危险操作（danger）主按钮标红，默认焦点放在「取消」上。
    internal static async Task<bool> Confirm(Window owner, bool dark, string title, object content, string primaryText,
        bool danger = false, double width = 440)
    {
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Owner = owner, Title = title, Content = content,
            PrimaryButtonText = primaryText, CloseButtonText = "取消",
            PrimaryButtonAppearance = danger ? ControlAppearance.Danger : ControlAppearance.Secondary,
            Width = width, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        NoteTheme.ApplyFluent(dialog.Resources, dark);
        if (danger) dialog.Loaded += (_, _) => dialog.MoveFocus(new TraversalRequest(FocusNavigationDirection.Last));
        return await dialog.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }
}
