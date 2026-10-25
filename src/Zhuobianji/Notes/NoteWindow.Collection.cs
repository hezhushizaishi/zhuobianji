using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Zhuobianji.Core;

namespace Zhuobianji;

internal partial class NoteWindow
{
    internal NoteEditor Editor => OwnEditor;
    // 主便签左侧留给标签的透明区域宽度；Snapshot 保存的位置宽度不含这一块。
    internal const double RailWidth = 92;
    private double collectionRailWidth;
    private int railAnimationVersion;
    internal double CollectionRailWidth => collectionRailWidth;
    internal Dictionary<Guid, ToggleButton> CollectionTabButtons { get; } = [];

    private void SetRailWidth(double width)
    {
        var difference = width - collectionRailWidth;
        if (difference == 0) return;
        var previousWidth = Width;
        var previousLeft = Left;
        collectionRailWidth = width;
        MinWidth = MinCardWidth + width + 2 * ShadowMargin;
        Width = previousWidth + difference;
        Left = previousLeft - difference;
        RailColumn.Width = new GridLength(width);
    }

    private void StopRailAnimation()
    {
        railAnimationVersion++;
        CollectionRail.BeginAnimation(OpacityProperty, null);
        RailTranslation.BeginAnimation(TranslateTransform.XProperty, null);
    }

    // 成为或不再是主便签时，窗口向左扩出或收回标签区，正文卡片位置不动。
    private void ApplyCollectionMain()
    {
        StopRailAnimation();
        SetRailWidth(Data.IsCollectionMain ? RailWidth : 0);
    }

    private void OnToggleRail(object sender, RoutedEventArgs e)
    {
        if (!Data.IsCollectionMain) return;
        var show = !Data.ShowCollectionRail;
        var fromOpacity = CollectionRail.Opacity;
        var fromOffset = RailTranslation.X;
        StopRailAnimation();

        // 主窗口边界不随开关改变；只移动透明区域内的标签，避免正文重排。
        if (IsVisible && SystemParameters.ClientAreaAnimation)
        {
            CollectionRail.Opacity = fromOpacity;
            RailTranslation.X = fromOffset;
            CollectionRail.Visibility = Visibility.Visible;
            var version = railAnimationVersion;
            var remaining = Math.Abs((show ? 0 : 84) - fromOffset) / 84;
            var duration = TimeSpan.FromMilliseconds(Math.Max(70, (show ? 220 : 170) * remaining));
            var easing = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseInOut };
            var fade = new DoubleAnimation(fromOpacity, show ? 1 : 0, duration) { EasingFunction = easing };
            var slide = new DoubleAnimation(fromOffset, show ? 0 : 84, duration) { EasingFunction = easing };
            fade.Completed += (_, _) =>
            {
                if (version != railAnimationVersion) return;
                StopRailAnimation();
                RefreshCollectionState();
            };
            CollectionRail.BeginAnimation(OpacityProperty, fade);
            RailTranslation.BeginAnimation(TranslateTransform.XProperty, slide);
        }
        Update(data => data with { ShowCollectionRail = show });
        RefreshCollectionState();
    }
    internal void RefreshCollectionState()
    {
        var main = controller.MainNote;
        SetMainItem.Visibility = !Data.IsCollectionMain && Data.CollectedBy is null && main is null
            ? Visibility.Visible : Visibility.Collapsed;
        CollectItem.Visibility = !Data.IsCollectionMain && Data.CollectedBy is null && main is not null
            ? Visibility.Visible : Visibility.Collapsed;
        MainTag.Visibility = Data.IsCollectionMain ? Visibility.Visible : Visibility.Collapsed;
        CloseButton.ToolTip = Data.IsCollectionMain ? "关闭主便签（Ctrl+W）"
            : Data.CollectedBy is not null ? "收起便签（Ctrl+W）" : "关闭便签（Ctrl+W）";
        DeleteButton.ToolTip = Data.IsCollectionMain ? "删除主便签，收纳的便签会释放到桌面" : "删除便签";
        RailColumn.Width = new GridLength(CollectionRailWidth);
        var railVisible = Data.IsCollectionMain && Data.ShowCollectionRail;
        if (!CollectionRail.HasAnimatedProperties)
        {
            CollectionRail.Opacity = railVisible ? 1 : 0;
            RailTranslation.X = railVisible ? 0 : 84;
        }
        CollectionRail.Visibility = Data.IsCollectionMain && (railVisible || CollectionRail.HasAnimatedProperties)
            ? Visibility.Visible : Visibility.Collapsed;
        CollectionRail.IsHitTestVisible = railVisible;
        CollectionRailToggle.Visibility = Data.IsCollectionMain ? Visibility.Visible : Visibility.Collapsed;
        CollectionRailToggle.IsChecked = Data.ShowCollectionRail;
        CollectionRailToggle.ToolTip = Data.ShowCollectionRail ? "隐藏左侧标签" : "显示左侧标签";
        if (!Data.IsCollectionMain) return;

        var children = controller.CollectedNotes(this).ToArray();
        // 文字和显示状态更新只修改现有标签；移动窗口不重建整排控件。
        foreach (var id in CollectionTabButtons.Keys.Where(id => children.All(child => child.Data.Id != id)).ToArray())
        {
            CollectionTabs.Children.Remove(CollectionTabButtons[id]);
            CollectionTabButtons.Remove(id);
        }
        foreach (var child in children)
        {
            if (!CollectionTabButtons.TryGetValue(child.Data.Id, out var tab))
            {
                tab = new ToggleButton { Style = (Style)FindResource("CollectionTab"), Tag = child.Data.Id };
                tab.Click += (_, _) => { controller.ToggleCollectedNote(child); RefreshCollectionState(); };
                tab.PreviewMouseLeftButtonDown += OnCollectionDragStart;
                tab.PreviewMouseMove += OnCollectionDragMove;
                tab.PreviewMouseLeftButtonUp += (_, _) => collectionDragId = null;
                var menu = (ContextMenu)FindResource("CollectionTabMenu");
                menu.DataContext = child;
                tab.ContextMenu = menu;
                CollectionTabButtons.Add(child.Data.Id, tab);
                CollectionTabs.Children.Add(tab);
            }
            tab.Content = child.Data.Preview;
            // 点击会先翻转 IsChecked；这里按子便签的实际显示状态改回来。
            tab.IsChecked = child.IsVisible;
            tab.ToolTip = child.Data.Preview + (child.IsVisible ? "\n收起" : "\n展开") + "\n拖动调整顺序";
            tab.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, child.Data.Preview);
        }
        for (var index = 0; index < children.Length; index++)
        {
            var tab = CollectionTabButtons[children[index].Data.Id];
            if (CollectionTabs.Children[index] == tab) continue;
            CollectionTabs.Children.Remove(tab);
            CollectionTabs.Children.Insert(index, tab);
        }
    }

    // 标签右键菜单（NoteWindow.xaml 的 CollectionTabMenu）；DataContext 是对应的子便签。
    private static NoteWindow TabNote(object sender) => (NoteWindow)((FrameworkElement)sender).DataContext;
    private void OnTabExpand(object sender, RoutedEventArgs e) => controller.ExpandCollectedNote(TabNote(sender));
    private void OnTabCollapse(object sender, RoutedEventArgs e) => controller.CollapseCollectedNote(TabNote(sender));
    private void OnTabTakeOut(object sender, RoutedEventArgs e) => controller.ReleaseNote(TabNote(sender));
    private async void OnTabDelete(object sender, RoutedEventArgs e) => await TabNote(sender).ConfirmDelete(this);

    private void OnSetMain(object sender, RoutedEventArgs e) => controller.SetMainNote(this);
    private void OnCollect(object sender, RoutedEventArgs e) => controller.CollectNote(this);
    private void OnNewCollected(object sender, RoutedEventArgs e) => controller.CreateCollectedNote(this);
}