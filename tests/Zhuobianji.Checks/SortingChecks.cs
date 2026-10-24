using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Zhuobianji;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

internal static partial class Program
{
    private static Guid[] VisibleTabOrder(NoteWindow main) => main.CollectionTabs.Children.OfType<Button>()
        .Select(button => (Guid)button.Tag).ToArray();

    // WPF 的构造函数不公开；测试向本程序的实际拖放处理器送入同样的参数。
    private static DragEventArgs CollectionDragEvent(NoteWindow main, Guid id, double y, RoutedEvent routedEvent)
    {
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            [new DataObject("Zhuobianji.CollectionTab", id.ToString()), DragDropKeyStates.LeftMouseButton,
                DragDropEffects.Move, main.CollectionTabs, new Point(20, y)], null)!;
        args.RoutedEvent = routedEvent;
        return args;
    }

    private static async Task SortingChecks()
    {
        using var store = new NoteStore(Path.Combine(root, "Data"));
        var initialMain = new NoteData { IsCollectionMain = true, Text = "主便签正文",
            Format = NoteFormat.PlainText, Left = 520, Top = 120, Width = 460, Height = 400 };
        store.Save(initialMain);
        var children = new[] { "记录 A", "记录 B", "记录 C" }.Select((text, index) =>
            new NoteData { Text = text, Format = NoteFormat.PlainText, CollectedBy = initialMain.Id,
                IsHidden = true, CreatedAt = initialMain.CreatedAt.AddSeconds(index) }).ToArray();
        foreach (var child in children) store.Save(child);
        var oldRecord = JsonNode.Parse(File.ReadAllText(store.GetPath(initialMain.Id)))!.AsObject();
        oldRecord.Remove(nameof(NoteData.CollectionOrder));
        File.WriteAllText(store.GetPath(initialMain.Id), oldRecord.ToJsonString());
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var main = controller.MainNote!;
        main.UpdateLayout();
        Check(main.Data.CollectionOrder.Length == 0 && VisibleTabOrder(main).SequenceEqual(children.Select(child => child.Id)),
            "旧记录没有排序字段时保持原创建顺序");
        var a = controller.Notes[children[0].Id];
        var b = controller.Notes[children[1].Id];
        var c = controller.Notes[children[2].Id];
        var originalButton = main.CollectionTabButtons[c.Data.Id];
        main.CollectionTabButtons[b.Data.Id].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        main.CollectionTabButtons[b.Data.Id].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(!b.IsVisible && VisibleTabOrder(main).SequenceEqual(children.Select(child => child.Id)),
            "普通点击仍可展开／收起，不改变标签顺序");

        main.Editor.InsertPlain("\n排序时刚写的文字");
        var dragOver = CollectionDragEvent(main, c.Data.Id, 1, DragDrop.DragOverEvent);
        main.CollectionScroll.RaiseEvent(dragOver);
        Check(dragOver.Effects == DragDropEffects.Move && main.CollectionDropMarker.Visibility == Visibility.Visible,
            "拖到最上方时显示插入线");
        var drop = CollectionDragEvent(main, c.Data.Id, 1, DragDrop.DropEvent);
        main.CollectionScroll.RaiseEvent(drop);
        main.UpdateLayout();
        Check(drop.Effects == DragDropEffects.Move &&
            VisibleTabOrder(main).SequenceEqual(new[] { c.Data.Id, a.Data.Id, b.Data.Id }) &&
            main.CollectionDropMarker.Visibility == Visibility.Collapsed && !c.IsVisible,
            "松手将标签移到最前，收起的便签不被误展开");
        var savedMain = store.LoadAll().Notes.Single(note => note.Id == main.Data.Id);
        Check(savedMain.CollectionOrder.SequenceEqual(VisibleTabOrder(main)) &&
            savedMain.Text.Contains("排序时刚写的文字") && SameCard(savedMain, initialMain),
            "顺序与待保存正文一起保存，主正文位置和宽度不变");

        var lastButton = (Button)main.CollectionTabs.Children[main.CollectionTabs.Children.Count - 1];
        var bottomY = lastButton.TranslatePoint(new Point(), main.CollectionTabs).Y + lastButton.ActualHeight + 6;
        main.CollectionScroll.RaiseEvent(CollectionDragEvent(main, a.Data.Id, bottomY, DragDrop.DropEvent));
        main.UpdateLayout();
        Check(VisibleTabOrder(main).SequenceEqual(new[] { c.Data.Id, b.Data.Id, a.Data.Id }) &&
            ReferenceEquals(main.CollectionTabButtons[c.Data.Id], originalButton),
            "可以从前面移到末尾，复用原标签控件");
        var unchangedAt = main.Data.UpdatedAt;
        controller.MoveCollectedNote(main, a.Data.Id, 3);
        Check(main.Data.UpdatedAt == unchangedAt, "放回原位置不重复写入");
        var foreignDrop = CollectionDragEvent(main, Guid.NewGuid(), 1, DragDrop.DropEvent);
        main.CollectionScroll.RaiseEvent(foreignDrop);
        Check(foreignDrop.Effects == DragDropEffects.None && VisibleTabOrder(main).SequenceEqual(main.Data.CollectionOrder),
            "其他内容不能混入便签排序");

        var added = controller.CreateNote(main);
        added.Editor.TextContent = "新增记录 D";
        added.SaveNow();
        controller.CollectNote(added);
        main.UpdateLayout();
        Check(VisibleTabOrder(main).SequenceEqual(new[] { c.Data.Id, b.Data.Id, a.Data.Id, added.Data.Id }),
            "新收纳项追加在现有顺序后面");
        controller.MoveCollectedNote(main, added.Data.Id, 0);
        controller.ReleaseNote(b);
        controller.DeleteNote(a);
        main.UpdateLayout();
        Check(VisibleTabOrder(main).SequenceEqual(new[] { added.Data.Id, c.Data.Id }) &&
            store.LoadAll().Notes.Single(note => note.Id == c.Data.Id).Text == children[2].Text,
            "取出和删除后剩余标签顺序与各自正文保持");
        Capture(main, Path.Combine(root, "collection-order.png"));
        File.WriteAllText(Path.Combine(root, "order-expected.json"), JsonSerializer.Serialize(VisibleTabOrder(main)));
        File.WriteAllText(Path.Combine(root, "main-expected.json"), JsonSerializer.Serialize(main.Snapshot()));
        Check(controller.RequestExit(), "排序后正常保存退出");
        await Task.CompletedTask;
    }

    private static async Task SortingReload()
    {
        var expected = JsonSerializer.Deserialize<Guid[]>(File.ReadAllText(Path.Combine(root, "order-expected.json")))!;
        var saved = JsonSerializer.Deserialize<NoteData>(File.ReadAllText(Path.Combine(root, "main-expected.json")))!;
        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var main = controller.MainNote!;
        main.UpdateLayout();
        Check(VisibleTabOrder(main).SequenceEqual(expected) && main.Editor.TextContent == saved.Text &&
            SameCard(main.Snapshot(), saved), "独立进程恢复排序、正文和主窗口位置宽度");
        main.CollectionTabButtons[expected[0]].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(controller.Notes[expected[0]].IsVisible && controller.Notes[expected[0]].Data.Preview == "新增记录 D",
            "重开后点击正确的收纳便签");
        Check(controller.RequestExit(), "重开后的排序正常保留");
        await Task.CompletedTask;
    }
}