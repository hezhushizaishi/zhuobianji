using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Zhuobianji;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

internal static partial class Program
{
    // 系统这时画不画这个窗口（DWMWA_CLOAKED）。
    private static bool Cloaked(Window window)
    {
        DwmGetWindowAttribute(new WindowInteropHelper(window).Handle, 14, out var cloaked, sizeof(int));
        return cloaked != 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    private static async Task AcknowledgeConversion(NoteWindow main, bool accept)
    {
        var reached = false;
        _ = main.Dispatcher.BeginInvoke(() =>
        {
            var dialog = Application.Current.Windows.OfType<MessageDialog>().Single();
            var content = (StackPanel)dialog.Body;
            content.Children.OfType<CheckBox>().Single().IsChecked = true;
            dialog.UpdateLayout();
            CaptureDialog(dialog, Path.Combine(root, accept ? "format-acknowledge.png" : "format-cancel.png"));
            reached = true;
            if (accept)
                dialog.PrimaryButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            else ((Window)dialog).Close();
        });
        main.PlainFormatItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Task.Delay(80);
        Check(reached, "格式提示实际显示“已知晓，不再提示”选项");
    }

    private static async Task CollectionChecks()
    {
        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var main = controller.Windows.Values.Single();
        // 左边留够一张子便签加放大后的标签区，展开时放在主便签左边。
        main.Left = 640; main.Top = 120; main.Width = 470; main.Height = 440;
        main.Editor.TextContent = "主便签自己的正文\n左边点击标签展开，正文一直保留。";
        main.SaveNow();
        var bodyBounds = main.Snapshot();
        main.SetMainItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        main.UpdateLayout();
        Check(controller.Book.Main == main.Entry && main.CollectionRail.IsVisible && main.Editor.IsVisible &&
            main.Snapshot().Left == bodyBounds.Left && main.Snapshot().Width == bodyBounds.Width &&
            Math.Abs(main.NoteCard.ActualWidth - bodyBounds.Width) < 1,
            "设为主便签显示左侧标签区，原正文位置与宽度不变");

        var rich = controller.CreateNote(format: NoteFormat.RichText);
        rich.Editor.InsertPlain("资料记录\n");
        rich.Editor.InsertLink(new Uri("https://example.com/reference"), "参考网站");
        rich.SaveNow();
        var markdown = controller.CreateNote(format: NoteFormat.Markdown);
        markdown.Editor.TextContent = "# 工作计划\n\n- 记录并保留";
        markdown.SaveNow();
        var plain = controller.CreateNote(format: NoteFormat.PlainText);
        plain.Editor.TextContent = "长期记录\n第二行";
        plain.SaveNow();
        Check(controller.CollectNote(rich) && controller.CollectNote(markdown) && controller.CollectNote(plain) &&
            main.CollectionTabButtons.Count == 3 && new[] { rich, markdown, plain }.All(child => !child.IsVisible),
            "三种格式收纳后各出一个标签，窗口收起");
        var richTab = main.CollectionTabButtons[rich.Data.Id];
        var mainText = main.Editor.TextContent;
        richTab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(50);
        var tabTop = richTab.TransformToAncestor(main).Transform(new Point()).Y + main.Top;
        Check(rich.IsVisible && rich.Editor.IsVisible && rich.CloseButton.IsVisible && Equals(rich.CloseButton.ToolTip, "收起便签（Ctrl+W）") &&
            main.Editor.TextContent == mainText && main.Editor.IsVisible &&
            rich.Left + rich.Width - NoteWindow.ShadowMargin <= main.Left + NoteWindow.ShadowMargin &&
            Math.Abs(rich.Snapshot().Top - tabTop) < 1 && richTab.IsChecked == true,
            "标签单击从标签一侧展开独立编辑窗口，顶部对齐标签，主正文保持可见");
        rich.Editor.InsertPlain("\n展开时补充的内容");
        richTab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(!rich.IsVisible && main.CollectionTabButtons.ContainsKey(rich.Data.Id) &&
            store.LoadAll().Notes.Single(note => note.Id == rich.Data.Id).Text.Contains("展开时补充的内容"),
            "再次点标签先保存再收起，内容及收纳关系保留");

        richTab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var cloakedOnReveal = Cloaked(rich);
        main.CollectionTabButtons[markdown.Data.Id].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(40);
        Check(rich.IsVisible && markdown.IsVisible && main.Editor.IsVisible, "可同时展开多张，主便签正文不切换");
        await Task.Delay(120);
        Check(cloakedOnReveal == NoteTheme.Animate && !Cloaked(rich) && !Cloaked(markdown),
            "再展开显示过的子便签时先不让系统画，画出淡入的头几帧再露出来，不会先贴出隐藏前的旧画面（闪一下）");
        markdown.PreviewButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(markdown.Editor.IsPreview && !main.Editor.IsPreview &&
            markdown.ModeLabel.Text == "Markdown" && markdown.ModeIcon.Symbol == NoteIconSymbol.Markdown20, "展开的 Markdown 使用自己的工具和预览");
        markdown.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(!markdown.IsVisible && rich.IsVisible && markdown.Data.CollectedBy == main.Data.Id,
            "子便签顶部 × 只收起自己");
        // 深浅色是整个程序的模式：切到深色时，收着的标签、展开的子便签和主便签一起变深，配色各自保留。
        Check(controller.TrySetThemeMode(NoteThemeMode.Dark, out _) && NoteTheme.DarkMode &&
            new[] { rich, markdown, plain }.All(child =>
                main.CollectionTabButtons[child.Data.Id].Resources["HeaderFill"] == NoteTheme.For(child.Data.Appearance.Palette, true).HeaderFill) &&
            main.Resources["Surface"] == NoteTheme.For(main.Data.Appearance.Palette, true).Surface,
            "切到深色模式，左侧标签和子便签跟着主便签一起变深");
        Check(controller.TrySetThemeMode(NoteThemeMode.Light, out _) && !NoteTheme.DarkMode &&
            main.CollectionTabButtons[markdown.Data.Id].Resources["HeaderFill"] == NoteTheme.For(markdown.Data.Appearance.Palette, false).HeaderFill,
            "切回浅色，标签一起变回来");
        var originalTab = main.CollectionTabButtons[rich.Data.Id];
        main.Left += 5;
        Check(ReferenceEquals(originalTab, main.CollectionTabButtons[rich.Data.Id]), "移动主便签不重建标签控件");
        main.SaveNow();
        Check(ReferenceEquals(originalTab, main.CollectionTabButtons[rich.Data.Id]), "保存位置后也复用已有标签");

        var link = Elements(rich.Editor.Rich.Document).OfType<Hyperlink>().Single();
        var correctOwner = false;
        _ = rich.Dispatcher.BeginInvoke(() =>
        {
            var dialog = Application.Current.Windows.OfType<LinkWindow>().Single();
            correctOwner = dialog.Owner == rich;
            dialog.NameBox.Text = "项目参考";
            dialog.SaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        });
        link.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "修改链接")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(correctOwner && rich.Editor.TextContent.Contains("项目参考") && main.Editor.TextContent == mainText,
            "展开后的链接修改归属独立便签，不影响主正文");
        rich.Editor.InsertPlain("\n隐藏前未等待自动保存");
        controller.HideAll();
        controller.ShowAll();
        Check(main.IsVisible && controller.Book.Collected(main.Entry).All(child => !controller.IsShown(child)) &&
            store.LoadAll().Notes.Single(note => note.Id == rich.Data.Id).Text.Contains("隐藏前未等待自动保存"),
            "隐藏全部先保存并收起子便签，显示全部不铺开收纳窗口");

        controller.ShowNote(plain.Entry);
        plain.RichFormatItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Task.Delay(30);
        await AcknowledgeConversion(plain, accept: false);
        Check(controller.Preferences.ShowFormatWarnings && plain.Editor.Format == NoteFormat.RichText,
            "独立窗口取消转换保持格式与提示偏好");
        await AcknowledgeConversion(plain, accept: true);
        Check(!controller.Preferences.ShowFormatWarnings && plain.Editor.Format == NoteFormat.PlainText &&
            store.LoadAll().Notes.Single(note => note.IsCollectionMain).Text == mainText,
            "确认转换保存提示选择，主正文不变");
        main.CollectionTabButtons[plain.Data.Id].ContextMenu.Items.OfType<MenuItem>()
            .Single(item => (string)item.Header == "取出").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(plain.IsVisible && plain.Data.CollectedBy is null && !main.CollectionTabButtons.ContainsKey(plain.Data.Id) &&
            Equals(plain.CloseButton.ToolTip, "关闭便签（Ctrl+W）"), "标签菜单取出解除收纳，保留独立窗口和内容");
        controller.CollectNote(plain);

        main.CollectionNewButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var added = controller.WindowOf(controller.Book.Collected(main.Entry).Last())!;
        Check(!Cloaked(added), "第一次显示的窗口没有旧画面，直接显示，不用等");
        added.Editor.TextContent = "新建的收纳便签";
        Check(added.IsVisible && main.CollectionTabButtons.ContainsKey(added.Data.Id) &&
            main.Editor.TextContent == mainText, "标签区新建直接展开一张收纳便签");
        await ReplyToDeletion(() => main.CollectionTabButtons[added.Data.Id].ContextMenu.Items.OfType<MenuItem>()
            .Single(item => (string)item.Header == "删除这张").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)), "delete");
        await Task.Delay(480);
        Check(!controller.Windows.ContainsKey(added.Data.Id) && !File.Exists(store.GetPath(added.Data.Id)) &&
            !File.Exists(store.GetPath(added.Data.Id) + ".bak") && !main.CollectionTabButtons.ContainsKey(added.Data.Id) &&
            controller.Book.Collected(main.Entry).Count() == 3, "标签菜单删除指定子便签，清理副本且计时器不复活");

        controller.ExpandCollectedNote(rich.Entry);
        rich.Editor.InsertPlain("\n保存失败时仍留在窗口");
        using (var locked = new FileStream(store.GetPath(rich.Data.Id), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            rich.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(rich.IsVisible && rich.IsDirty && rich.SaveError is not null &&
                rich.Data.CollectedBy == main.Data.Id, "收起时保存失败保持窗口和内容，提供重试");
            var deleted = true;
            var blocked = AnswerNotice(() => deleted = controller.DeleteNote(main.Entry));
            Check(!deleted && File.Exists(store.GetPath(main.Data.Id)) && controller.Book.Collected(main.Entry).Count() == 3 &&
                blocked?.Contains("主便签没有删除") == true && blocked.Contains("没能取出"),
                "子便签保存失败阻止删除主便签，并说明哪张没取出");
        }
        Check(rich.SaveNow() && controller.CollapseCollectedNote(rich.Entry), "恢复写入后可以保存并收起");
        var beforeDesktop = main.Snapshot();
        Check(controller.SetDesktopMode(main, true, showErrors: false) && main.IsDesktopAttached &&
            Math.Abs(main.Snapshot().Left - beforeDesktop.Left) < 1 && Math.Abs(main.Snapshot().Width - beforeDesktop.Width) < 1 &&
            main.CollectionRail.IsVisible, "桌面挂载保留主正文坐标宽度，左侧标签随主窗口显示");
        Check(controller.SetDesktopMode(main, false, showErrors: false), "标签版主便签恢复普通浮窗");

        main.UpdateLayout();
        Capture(main, Path.Combine(root, "collection-tabs.png"));
        controller.ShowNote(rich.Entry);
        await Task.Delay(50);
        Capture(rich, Path.Combine(root, "expanded-note.png"));
        rich.Editor.InsertPlain("\n退出前最后一次输入");
        File.WriteAllText(Path.Combine(root, "collection-expected.json"),
            JsonSerializer.Serialize(controller.Windows.Values.Select(window => window.Snapshot()).ToArray()));
        Check(controller.RequestExit(), "展开便签待写内容与主便签正常保存退出");
    }

    private static async Task CollectionReload()
    {
        var expected = JsonSerializer.Deserialize<NoteData[]>(File.ReadAllText(Path.Combine(root, "collection-expected.json")))!;
        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var main = controller.WindowOf(controller.Book.Main)!;
        main.UpdateLayout();
        Check(main.IsVisible && main.Editor.IsVisible && main.CollectionTabButtons.Count == 3 &&
            controller.Book.Collected(main.Entry).All(child => !controller.IsShown(child)),
            "新进程恢复左侧标签和主正文，收纳窗口默认收起");
        Check(controller.Windows.Count == 1, "收纳的便签启动时不建窗口");
        // 还没建窗口的收纳便签从标签菜单取出，文件写不进：弹提示说原因，关系不变；解除占用后再取出，然后收回去。
        var unopened = controller.Book.Collected(main.Entry).Last();
        Action takeOut = () => main.CollectionTabButtons[unopened.Id].ContextMenu.Items.OfType<MenuItem>()
            .Single(item => (string)item.Header == "取出").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        string? notice;
        using (Lock(store.GetPath(unopened.Id))) notice = AnswerNotice(takeOut);
        Check(notice?.Contains("没能取出") == true && notice.Contains(unopened.Data.Preview) &&
            unopened.Data.CollectedBy == main.Data.Id && controller.WindowOf(unopened) is null &&
            main.CollectionTabButtons.ContainsKey(unopened.Id), "没开窗口的收纳便签取出失败时弹提示，收纳关系不变");
        Check(AnswerNotice(takeOut) is null && controller.WindowOf(unopened)?.IsVisible == true && unopened.Data.CollectedBy is null,
            "解除占用后再取出成功");
        controller.CollectNote(controller.WindowOf(unopened)!);
        foreach (var data in expected)
        {
            var window = WindowOf(controller, data.Id);
            Check(window.Editor.TextContent == data.Text && window.Data.CollectedBy == data.CollectedBy &&
                window.Editor.Format == data.Format, "重开恢复每张独立正文、格式及收纳关系");
        }
        var mainBefore = expected.Single(note => note.IsCollectionMain);
        Check(Math.Abs(main.Snapshot().Left - mainBefore.Left) < 1 && Math.Abs(main.Snapshot().Width - mainBefore.Width) < 1 &&
            Math.Abs(main.NoteCard.ActualWidth - mainBefore.Width) < 1, "重开后主正文宽度不累计增加，位置保持一致");
        Check(!controller.Preferences.ShowFormatWarnings, "重开恢复不再提示的选择");
        var markdown = WindowOf(controller, controller.Book.Collected(main.Entry).Single(child => child.Data.Format == NoteFormat.Markdown).Id);
        controller.ShowNote(markdown.Entry);
        Check(markdown.IsVisible && main.Editor.IsVisible && markdown.CloseButton.IsVisible && Equals(markdown.CloseButton.ToolTip, "收起便签（Ctrl+W）"),
            "托盘访问收纳项直接展开独立便签");
        markdown.Editor.Source.CaretIndex = markdown.Editor.Source.Text.Length;
        markdown.Editor.InsertPlain("\n删除主便签前新增");
        // 删除主便签时逐张取出：前两张取出了、最后一张写不进 → 停下，主便签不删，提示里说已取出几张。
        Action deleteMain = () => main.DeleteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var last = controller.Book.Collected(main.Entry).Last();
        string? partial = null;
        using (Lock(store.GetPath(last.Id)))
            await ReplyToDeletion(() => partial = AnswerNotice(deleteMain), "delete");
        Check(partial?.Contains("已取出 2 张") == true && partial.Contains(last.Data.Preview) && controller.Book.Main == main.Entry &&
            File.Exists(store.GetPath(main.Data.Id)) && last.Data.CollectedBy == main.Data.Id && controller.Book.Collected(main.Entry).Count() == 1,
            "删除主便签中途取出失败：停下不删主便签，提示已取出的张数和没取出的那张");
        await ReplyToDeletion(deleteMain, "delete");
        await Task.Delay(50);
        Check(controller.Book.Main is null && !File.Exists(store.GetPath(main.Data.Id)) &&
            !File.Exists(store.GetPath(main.Data.Id) + ".bak") && controller.Windows.Count == 3 &&
            controller.Windows.Values.All(child => child.IsVisible && child.Data.CollectedBy is null &&
                Equals(child.CloseButton.ToolTip, "关闭便签（Ctrl+W）")),
            "主便签垃圾桶只删除自己，所有子便签释放为独立窗口");
        var released = store.LoadAll().Notes;
        Check(released.Single(note => note.Id == markdown.Data.Id).Text.Contains("删除主便签前新增") &&
            released.All(note => note.CollectedBy is null) &&
            Elements(controller.Windows[expected.Single(note => note.Format == NoteFormat.RichText && !note.IsCollectionMain).Id].Editor.Rich.Document)
                .OfType<Hyperlink>().Single().NavigateUri!.Host == "example.com",
            "删除主便签前保存待写内容，释放后的文字及富文本链接保留");
        Check(controller.RequestExit(), "释放后正常保存退出");
    }
}
