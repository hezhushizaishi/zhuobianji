using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Zhuobianji.Core;

namespace Zhuobianji;

// 弹窗外壳：提示、确认、链接和设置共用。配色跟随所属便签（NoteTheme.ApplyTo），外观见 NoteStyles.xaml 的 DialogWindow；
// 弹窗里的输入框、滑块和勾选框默认用 Field、DialogSlider、DialogCheck。出现时淡入并略微放大，Esc 关闭。
internal class NoteDialog : Window
{
    internal NoteDialog(NoteAppearance appearance)
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Zhuobianji;component/Theme/NoteStyles.xaml", UriKind.Relative) });
        Resources[typeof(TextBox)] = FindResource("Field");
        Resources[typeof(Slider)] = FindResource("DialogSlider");
        Resources[typeof(CheckBox)] = FindResource("DialogCheck");
        Style = (Style)FindResource("DialogWindow");
        SetAppearance(appearance);
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => Close()));
        Loaded += (_, _) => { if (GetTemplateChild("Card") is UIElement card) NoteTheme.Pop(card); };
    }

    internal void SetAppearance(NoteAppearance appearance) => NoteTheme.For(appearance).ApplyTo(Resources);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || e.Handled) return;
        e.Handled = true;
        Close();
    }

    // 放在便签旁边：卡片之间留 8，优先右侧，放不下放左侧，都放不下就盖在便签中间；顶部和便签对齐，不出工作区。
    // 两边的窗口都带 12 的阴影边距（NoteWindow.ShadowMargin）。
    internal void PlaceBeside(Window note)
    {
        const double margin = NoteWindow.ShadowMargin, gap = 8;
        Measure(new Size(Width, double.PositiveInfinity));
        var area = SystemParameters.WorkArea;
        var left = note.Left + note.Width - 2 * margin + gap;
        if (left + Width - margin > area.Right) left = note.Left + 2 * margin - gap - Width;
        if (left + margin < area.Left) left = note.Left + (note.Width - Width) / 2;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = Math.Max(area.Left - margin, Math.Min(left, area.Right - Width + margin));
        Top = Math.Max(area.Top - margin, Math.Min(note.Top, area.Bottom - DesiredSize.Height + margin));
    }
}

// 提示框和确认框：一段说明加一排按键。确认框的危险操作主按钮标红，默认焦点放在「取消」上。
internal sealed class MessageDialog : NoteDialog
{
    internal UIElement Body { get; }
    internal Button PrimaryButton { get; }
    internal Button? CancelButton { get; }

    internal MessageDialog(NoteAppearance appearance, string title, object content, string primaryText, bool confirm, bool danger, double width)
        : base(appearance)
    {
        Title = title;
        Width = width;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        if (confirm)
        {
            CancelButton = new Button { Content = "取消", Style = (Style)FindResource("KeyButton"), Margin = new Thickness(0, 0, 8, 0) };
            CancelButton.Click += (_, _) => DialogResult = false;
            buttons.Children.Add(CancelButton);
        }
        PrimaryButton = new Button { Content = primaryText, Style = (Style)FindResource(danger ? "DangerButton" : "PrimaryButton"), IsDefault = !danger };
        PrimaryButton.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(PrimaryButton);
        Body = content as UIElement ?? new TextBlock { Text = content.ToString(), TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        var panel = new StackPanel { Margin = new Thickness(16, 16, 16, 16) };
        panel.Children.Add(Body);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => (danger && CancelButton is not null ? CancelButton : PrimaryButton).Focus();
    }
}
