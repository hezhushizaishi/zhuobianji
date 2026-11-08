using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Zhuobianji.Core;

namespace Zhuobianji;

// 「关于」页的「查看许可」：嵌在程序里的三份许可，切换着看。
internal partial class LicenseWindow : NoteDialog
{
    internal LicenseWindow(NoteAppearance appearance) : base(appearance)
    {
        InitializeComponent();
        foreach (var (title, file) in AppInfo.Notices)
        {
            var tab = new RadioButton { Style = (Style)FindResource("DialogTab"), Content = title, Tag = file,
                Margin = new Thickness(Tabs.Children.Count == 0 ? 0 : 2, 0, 0, 0) };
            tab.Checked += (_, _) => { NoticeText.Text = AppInfo.Notice(file); NoticeText.ScrollToHome(); };
            Tabs.Children.Add(tab);
        }
        TabItems.First().IsChecked = true;
    }

    internal RadioButton[] TabItems => Tabs.Children.OfType<RadioButton>().ToArray();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
