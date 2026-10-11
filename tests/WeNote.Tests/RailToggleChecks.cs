using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WeNote.App;
using WeNote.Core;

namespace WeNote.Tests;

internal static partial class Program
{
    private static bool SameCard(NoteData left, NoteData right) =>
        Math.Abs(left.Left - right.Left) < 1 && Math.Abs(left.Width - right.Width) < 1;

    [StructLayout(LayoutKind.Sequential)]
    private record struct RailRect(int Left, int Top, int Right, int Bottom);
    [StructLayout(LayoutKind.Sequential)]
    private record struct RailPoint(int X, int Y);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetRailWindowRect(nint window, out RailRect rect);
    [DllImport("user32.dll", EntryPoint = "WindowFromPoint")]
    private static extern nint RailWindowFromPoint(RailPoint point);

    private static async Task RailToggleSeed()
    {
        using var store = new NoteStore(Path.Combine(root, "Data"));
        var original = new NoteData { IsCollectionMain = true, Text = "主便签正文",
            Left = 520, Top = 120, Width = 460, Height = 400 };
        var childData = new NoteData { Text = "常用记录", Format = NoteFormat.PlainText, CollectedBy = original.Id, IsHidden = true };
        store.Save(original);
        store.Save(childData);
        var oldRecord = JsonNode.Parse(File.ReadAllText(store.GetPath(original.Id)))!.AsObject();
        oldRecord.Remove(nameof(NoteData.ShowCollectionRail));
        File.WriteAllText(store.GetPath(original.Id), oldRecord.ToJsonString());

        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var main = controller.MainNote!;
        var child = controller.Notes[childData.Id];
        main.UpdateLayout();
        Check(main.Data.ShowCollectionRail && main.CollectionRail.IsVisible && main.CollectionRailToggle.IsVisible,
            "旧记录默认显示标签，主便签顶部有开关");
        controller.ShowNote(child);
        await Task.Delay(80);
        main.Activate();
        Keyboard.Focus(main.CollectionRailToggle);
        main.CollectionRailToggle.ApplyTemplate();
        var toggleBorder = (Border)main.CollectionRailToggle.Template.FindName("ToggleBorder", main.CollectionRailToggle);
        Check(main.CollectionRailToggle.IsKeyboardFocused && ((SolidColorBrush)toggleBorder.Background).Color.A > 0,
            "标签开启且按钮有焦点时显示开启底色");
        var handle = new WindowInteropHelper(main).Handle;
        GetRailWindowRect(handle, out var originalBounds);
        var geometryChanges = 0;
        main.LocationChanged += (_, _) => geometryChanges++;
        main.SizeChanged += (_, _) => geometryChanges++;
        main.CollectionRailToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(!main.Data.ShowCollectionRail &&
            (!SystemParameters.ClientAreaAnimation || main.CollectionRail.HasAnimatedProperties),
            "点击开关切换目标状态，系统允许动画时启动短过渡");
        main.UpdateLayout();
        Check(main.CollectionRailToggle.IsKeyboardFocused && ((SolidColorBrush)toggleBorder.Background).Color.A == 0,
            "关闭后按钮仍有焦点，底色立即清除，无需另点正文");
        var stableFrames = true;
        for (var frame = 0; frame < 6; frame++)
        {
            await Task.Delay(20);
            GetRailWindowRect(handle, out var bounds);
            stableFrames &= bounds.Equals(originalBounds) && Math.Abs(main.NoteCard.ActualWidth - original.Width) < 1;
        }
        Check(stableFrames && geometryChanges == 0, "收起逐帧保持原生窗口边界和正文宽度，不触发移动或缩放重排");
        main.Editor.InsertPlain("\n切换期间继续记录");
        main.SaveStatus.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var savedDuringTransition = store.LoadAll().Notes.Single(note => note.Id == main.Data.Id);
        Check(!savedDuringTransition.ShowCollectionRail && SameCard(savedDuringTransition, original) &&
            savedDuringTransition.Text.Contains("切换期间继续记录"), "动画期间立即保存也保留正文、位置和宽度");
        await Task.Delay(240);
        main.UpdateLayout();
        Check(main.CollectionRail.Visibility == Visibility.Collapsed && main.CollectionRailWidth == 92 &&
            Math.Abs(main.Width - original.Width - 92) < 1 && SameCard(main.Snapshot(), original) &&
            child.IsVisible && child.Data.CollectedBy == main.Data.Id && main.CollectionRailToggle.IsVisible,
            "关闭标签完全隐藏，透明区域保留，正文和已展开窗口不变");
        var transparentPoint = main.PointToScreen(new Point(30, 140));
        Check(main.RailViewport.InputHitTest(new Point(30, 100)) is null &&
            RailWindowFromPoint(new RailPoint((int)transparentPoint.X, (int)transparentPoint.Y)) != handle,
            "关闭后的透明标签区域不挡鼠标，可点到后方窗口");
        Capture(main, Path.Combine(root, "rail-hidden.png"));

        main.CollectionRailToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(240);
        main.UpdateLayout();
        Check(main.CollectionRail.IsVisible && SameCard(main.Snapshot(), original) &&
            Math.Abs(main.NoteCard.ActualWidth - original.Width) < 1 && main.CollectionTabButtons.Count == 1,
            "重新开启标签，正文位置宽度不变，原收纳项仍可使用");
        main.CollectionTabButtons[child.Data.Id].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(!child.IsVisible && File.Exists(store.GetPath(child.Data.Id)), "重新开启后标签仍可收起子便签");
        main.CollectionRailToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(65);
        var offsetBeforeReverse = main.RailTranslation.X;
        main.CollectionRailToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(Math.Abs(main.RailTranslation.X - offsetBeforeReverse) < 2,
            "动画中途反向从当前位移继续，没有跳回起点");
        await Task.Delay(65);
        main.CollectionRailToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(240);
        Check(!main.Data.ShowCollectionRail && main.CollectionRail.Visibility == Visibility.Collapsed &&
            SameCard(main.Snapshot(), original), "连续点击最终状态正确，不被旧动画回调覆盖");

        GetRailWindowRect(handle, out var afterSwitches);
        Check(afterSwitches.Equals(originalBounds) && geometryChanges == 0,
            "展开、收起及连续切换都不改变窗口边界");
        Check(controller.SetDesktopMode(main, true, showErrors: false), "关闭标签时仍可贴到桌面");
        main.CollectionRailToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(240);
        Check(main.CollectionRail.IsVisible && SameCard(main.Snapshot(), original), "桌面模式开启标签保持正文位置和宽度");
        main.CollectionRailToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(240);
        Check(!main.Data.ShowCollectionRail && SameCard(main.Snapshot(), original), "桌面模式关闭标签保持正文位置和宽度");
        controller.SetDesktopMode(main, false, showErrors: false);
        main.CollectionRailToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(240);
        main.UpdateLayout();
        Capture(main, Path.Combine(root, "rail-visible.png"));

        main.CollectionRailToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(controller.RequestExit(), "动画期间退出仍完成保存");
        await Task.Delay(240);
        Check(!main.IsDirty && !main.CollectionRail.HasAnimatedProperties &&
            !main.RailTranslation.HasAnimatedProperties, "退出取消待完成动画，不在关闭后重新改动窗口");
        var saved = store.LoadAll().Notes.Single(note => note.Id == main.Data.Id);
        Check(!saved.ShowCollectionRail && SameCard(saved, original) && saved.Text.Contains("切换期间继续记录"),
            "退出保存关闭状态及原正文几何，没有中间动画值");
        File.WriteAllText(Path.Combine(root, "rail-expected.json"), JsonSerializer.Serialize(saved));
    }

    private static async Task RailToggleReload()
    {
        var expected = JsonSerializer.Deserialize<NoteData>(File.ReadAllText(Path.Combine(root, "rail-expected.json")))!;
        using var store = new NoteStore(Path.Combine(root, "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var main = controller.MainNote!;
        main.UpdateLayout();
        Check(!main.Data.ShowCollectionRail && main.CollectionRail.Visibility == Visibility.Collapsed &&
            main.CollectionRailToggle.IsVisible && SameCard(main.Snapshot(), expected) &&
            main.Editor.TextContent == expected.Text, "独立进程恢复关闭状态，正文位置宽度和文字不变");
        main.CollectionRailToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(240);
        var child = controller.CollectedNotes(main).Single();
        main.CollectionTabButtons[child.Data.Id].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(main.CollectionRail.IsVisible && child.IsVisible && child.Data.CollectedBy == main.Data.Id,
            "重开后可以开启标签并展开原有收纳项");
        Check(controller.RequestExit(), "开启状态正常保存退出");
    }
}