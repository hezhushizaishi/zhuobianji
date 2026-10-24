using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Zhuobianji;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

internal static partial class Program
{
    private static async Task AcknowledgeConversion(NoteWindow main, bool accept)
    {
        var reached = false;
        _ = main.Dispatcher.BeginInvoke(() =>
        {
            var dialog = Application.Current.Windows.OfType<Wpf.Ui.Controls.MessageBox>().Single();
            var content = (StackPanel)dialog.Content;
            content.Children.OfType<CheckBox>().Single().IsChecked = true;
            dialog.UpdateLayout();
            CaptureDialog(dialog, Path.Combine(root, accept ? "format-acknowledge.png" : "format-cancel.png"));
            reached = true;
            if (accept)
                dialog.TemplateButtonCommand.Execute(Wpf.Ui.Controls.MessageBoxButton.Primary);
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
        var main = controller.Notes.Values.Single();
        main.Left = 520; main.Top = 120; main.Width = 470; main.Height = 440;
        main.Editor.TextContent = "主便签自己的正文\n左边点击标签展开，正文一直保留。";
        main.SaveNow();
        var bodyBounds = main.Snapshot();
        main.SetMainItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        main.UpdateLayout();
        Check(controller.MainNote == main && main.CollectionRail.IsVisible && main.Editor.IsVisible &&
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
            main.CollectionTabButtons.Count == 3 && new[] { rich, markdown, plain }.All(child => !child.IsVisible) &&
            store.LoadAll().Notes.Count(note => note.CollectedBy == main.Data.Id && note.IsHidden) == 3,
            "三种格式收纳到第一层标签，关系与折叠状态保存");
        var richTab = main.CollectionTabButtons[rich.Data.Id];
        var mainText = main.Editor.TextContent;
        richTab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(50);
        Check(rich.IsVisible && rich.Editor.IsVisible && rich.CloseButton.IsVisible && Equals(rich.CloseButton.ToolTip, "收起便签（Ctrl+W）") &&
            main.Editor.TextContent == mainText && main.Editor.IsVisible && ReferenceEquals(rich.EditorHost.Content, rich.Editor) &&
            rich.Left >= main.Left + main.Width,
            "标签单击在主便签旁展开独立编辑窗口，主正文保持可见");
        rich.Editor.InsertPlain("\n展开时补充的内容");
        richTab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(!rich.IsVisible && main.CollectionTabButtons.ContainsKey(rich.Data.Id) &&
            store.LoadAll().Notes.Single(note => note.Id == rich.Data.Id).Text.Contains("展开时补充的内容"),
            "再次点标签先保存再收起，内容及收纳关系保留");

        richTab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        main.CollectionTabButtons[markdown.Data.Id].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(40);
        Check(rich.IsVisible && markdown.IsVisible && main.Editor.IsVisible, "可同时展开多张，主便签正文不切换");
        markdown.PreviewButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(markdown.Editor.IsPreview && !main.Editor.IsPreview &&
            Equals(markdown.ModeButton.Content, "Markdown ▾"), "展开的 Markdown 使用自己的工具和预览");
        markdown.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(!markdown.IsVisible && rich.IsVisible && markdown.Data.CollectedBy == main.Data.Id,
            "子便签顶部 × 只收起自己");
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
        Check(main.IsVisible && controller.CollectedNotes(main).All(child => !child.IsVisible) &&
            store.LoadAll().Notes.Single(note => note.Id == rich.Data.Id).Text.Contains("隐藏前未等待自动保存"),
            "隐藏全部先保存并收起子便签，显示全部不铺开收纳窗口");

        controller.ShowNote(plain);
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
        var added = controller.CollectedNotes(main).Last();
        added.Editor.TextContent = "新建的收纳便签";
        Check(added.IsVisible && main.CollectionTabButtons.ContainsKey(added.Data.Id) &&
            main.Editor.TextContent == mainText, "标签区新建直接展开一张收纳便签");
        await ReplyToDeletion(() => main.CollectionTabButtons[added.Data.Id].ContextMenu.Items.OfType<MenuItem>()
            .Single(item => (string)item.Header == "删除这张").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)), "delete");
        await Task.Delay(480);
        Check(!controller.Notes.ContainsKey(added.Data.Id) && !File.Exists(store.GetPath(added.Data.Id)) &&
            !File.Exists(store.GetPath(added.Data.Id) + ".bak") && !main.CollectionTabButtons.ContainsKey(added.Data.Id) &&
            controller.CollectedNotes(main).Count() == 3, "标签菜单删除指定子便签，清理副本且计时器不复活");

        controller.ExpandCollectedNote(rich);
        rich.Editor.InsertPlain("\n保存失败时仍留在窗口");
        using (var locked = new FileStream(store.GetPath(rich.Data.Id), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            rich.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(rich.IsVisible && rich.IsDirty && rich.SaveError is not null &&
                rich.Data.CollectedBy == main.Data.Id, "收起时保存失败保持窗口和内容，提供重试");
            Check(!controller.DeleteNote(main) && File.Exists(store.GetPath(main.Data.Id)) &&
                controller.CollectedNotes(main).Count() == 3, "子便签保存失败阻止删除主便签");
        }
        Check(rich.SaveNow() && controller.CollapseCollectedNote(rich), "恢复写入后可以保存并收起");
        var beforeDesktop = main.Snapshot();
        Check(controller.SetDesktopMode(main, true, showErrors: false) && main.IsDesktopAttached &&
            Math.Abs(main.Snapshot().Left - beforeDesktop.Left) < 1 && Math.Abs(main.Snapshot().Width - beforeDesktop.Width) < 1 &&
            main.CollectionRail.IsVisible, "桌面挂载保留主正文坐标宽度，左侧标签随主窗口显示");
        Check(controller.SetDesktopMode(main, false, showErrors: false), "标签版主便签恢复普通浮窗");

        main.UpdateLayout();
        Capture(main, Path.Combine(root, "collection-tabs.png"));
        controller.ShowNote(rich);
        await Task.Delay(50);
        Capture(rich, Path.Combine(root, "expanded-note.png"));
        rich.Editor.InsertPlain("\n退出前最后一次输入");
        File.WriteAllText(Path.Combine(root, "collection-expected.json"),
            JsonSerializer.Serialize(controller.Notes.Values.Select(window => window.Snapshot()).ToArray()));
        Check(controller.RequestExit(), "展开便签待写内容与主便签正常保存退出");
    }

    private static async Task CollectionReload()
    {
        var expected = JsonSerializer.Deserialize<NoteData[]>(File.ReadAllText(Path.Combine(root, "collection-expected.json")))!;
        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var main = controller.MainNote!;
        main.UpdateLayout();
        Check(main.IsVisible && main.Editor.IsVisible && main.CollectionTabButtons.Count == 3 &&
            controller.CollectedNotes(main).All(child => !child.IsVisible),
            "新进程恢复左侧标签和主正文，收纳窗口默认收起");
        foreach (var data in expected)
        {
            var window = controller.Notes[data.Id];
            Check(window.Editor.TextContent == data.Text && window.Data.CollectedBy == data.CollectedBy &&
                window.Editor.Format == data.Format, "重开恢复每张独立正文、格式及收纳关系");
        }
        var mainBefore = expected.Single(note => note.IsCollectionMain);
        Check(Math.Abs(main.Snapshot().Left - mainBefore.Left) < 1 && Math.Abs(main.Snapshot().Width - mainBefore.Width) < 1 &&
            Math.Abs(main.NoteCard.ActualWidth - mainBefore.Width) < 1, "重开后主正文宽度不累计增加，位置保持一致");
        Check(!controller.Preferences.ShowFormatWarnings, "重开恢复不再提示的选择");
        var markdown = controller.CollectedNotes(main).Single(child => child.Data.Format == NoteFormat.Markdown);
        controller.ShowNote(markdown);
        Check(markdown.IsVisible && main.Editor.IsVisible && markdown.CloseButton.IsVisible && Equals(markdown.CloseButton.ToolTip, "收起便签（Ctrl+W）"),
            "托盘访问收纳项直接展开独立便签");
        markdown.Editor.Source.CaretIndex = markdown.Editor.Source.Text.Length;
        markdown.Editor.InsertPlain("\n删除主便签前新增");
        await ReplyToDeletion(() => main.DeleteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)), "delete");
        await Task.Delay(50);
        Check(controller.MainNote is null && !File.Exists(store.GetPath(main.Data.Id)) &&
            !File.Exists(store.GetPath(main.Data.Id) + ".bak") && controller.Notes.Count == 3 &&
            controller.Notes.Values.All(child => child.IsVisible && child.Data.CollectedBy is null &&
                Equals(child.CloseButton.ToolTip, "关闭便签（Ctrl+W）")),
            "主便签垃圾桶只删除自己，所有子便签释放为独立窗口");
        var released = store.LoadAll().Notes;
        Check(released.Single(note => note.Id == markdown.Data.Id).Text.Contains("删除主便签前新增") &&
            released.All(note => note.CollectedBy is null) &&
            Elements(controller.Notes[expected.Single(note => note.Format == NoteFormat.RichText && !note.IsCollectionMain).Id].Editor.Rich.Document)
                .OfType<Hyperlink>().Single().NavigateUri!.Host == "example.com",
            "删除主便签前保存待写内容，释放后的文字及富文本链接保留");
        Check(controller.RequestExit(), "释放后正常保存退出");
    }
}
