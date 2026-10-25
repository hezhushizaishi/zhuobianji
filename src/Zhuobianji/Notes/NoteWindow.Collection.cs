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
    // 标签高度：放得下时 30，标签多了逐步压到 24，再多就滚动（滚动条隐藏，上下边缘渐隐）。
    private const double TabHeightMax = 30, TabHeightMin = 24, TabGap = 3, NewTabRoom = 27;
    // 展开的标签往外抽出 10，悬停抽出 4。
    private const double TabPulled = -10, TabPeek = -4;
    private double collectionRailWidth;
    private double tabHeight = TabHeightMax;
    private int railAnimationVersion;
    internal double CollectionRailWidth => collectionRailWidth;
    internal Dictionary<Guid, ToggleButton> CollectionTabButtons { get; } = [];
    private double TabPitch => tabHeight + TabGap;

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
        var countBefore = CollectionTabButtons.Count;
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
                tab = new ToggleButton { Style = (Style)FindResource("CollectionTab"), Tag = child.Data.Id,
                    Height = tabHeight, RenderTransform = new TranslateTransform(), ToolTip = "" };
                ToolTipService.SetPlacement(tab, PlacementMode.Left);
                ToolTipService.SetInitialShowDelay(tab, 450);
                tab.Click += (_, _) => { controller.ToggleCollectedNote(child); RefreshCollectionState(); };
                tab.PreviewMouseLeftButtonDown += OnCollectionDragStart;
                tab.PreviewMouseMove += OnCollectionDragMove;
                tab.PreviewMouseLeftButtonUp += (_, _) => collectionDragId = null;
                tab.MouseEnter += (_, _) => PullTab(tab);
                tab.MouseLeave += (_, _) => PullTab(tab);
                tab.ToolTipOpening += (_, _) => tab.ToolTip = TabPreview(child);
                var menu = (ContextMenu)FindResource("CollectionTabMenu");
                menu.DataContext = child;
                tab.ContextMenu = menu;
                CollectionTabButtons.Add(child.Data.Id, tab);
                CollectionTabs.Children.Add(tab);
                // 收纳进来的新标签淡入；启动时一次建好的不播。
                if (IsVisible) NoteTheme.Reveal(tab);
            }
            // 标签用子便签自己的配色；同一配色的画刷是同一个实例，没变就不重写资源。
            var theme = NoteTheme.For(child.Data.Appearance);
            if (!ReferenceEquals(tab.Resources["Surface"], theme.Surface)) theme.ApplyTo(tab.Resources);
            var empty = string.IsNullOrWhiteSpace(child.Data.Text);
            tab.Content = empty ? "空白" : child.Data.Preview;
            tab.Opacity = empty && !child.IsVisible ? 0.6 : 1;
            // 点击会先翻转 IsChecked；这里按子便签的实际显示状态改回来。
            tab.IsChecked = child.IsVisible;
            tab.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, child.Data.Preview);
            PullTab(tab);
        }
        for (var index = 0; index < children.Length; index++)
        {
            var tab = CollectionTabButtons[children[index].Data.Id];
            if (CollectionTabs.Children[index] == tab) continue;
            CollectionTabs.Children.Remove(tab);
            CollectionTabs.Children.Insert(index, tab);
        }
        if (CollectionTabButtons.Count != countBefore) FitCollectionTabs();
    }

    // 展开的标签整张抽出，悬停时抽出一点。
    private void PullTab(ToggleButton tab) => SlideTab(tab, TranslateTransform.XProperty,
        tab.IsChecked == true ? TabPulled : tab.IsMouseOver ? TabPeek : 0, NoteTheme.Standard);

    // 标签的位移动画：X 是往外抽，Y 是拖动排序时让位。目标没变就不重新开始，连续的 DragOver 不会让动画一直拖着。
    private static void SlideTab(UIElement tab, DependencyProperty axis, double target, Duration duration)
    {
        var shift = (TranslateTransform)tab.RenderTransform;
        if ((double)shift.GetAnimationBaseValue(axis) == target) return;
        var from = (double)shift.GetValue(axis);
        shift.SetValue(axis, target);
        shift.BeginAnimation(axis, new DoubleAnimation(from, target, duration) { EasingFunction = NoteTheme.Ease });
    }

    private void OnCollectionResized(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged) FitCollectionTabs();
    }

    // 标签多到放不下时先压矮（最矮 24），仍放不下才需要滚动。
    private void FitCollectionTabs()
    {
        var count = CollectionTabs.Children.Count;
        var room = CollectionScroll.ActualHeight - NewTabRoom;
        if (count == 0 || room <= 0) return;
        var height = Math.Clamp(Math.Floor(room / count) - TabGap, TabHeightMin, TabHeightMax);
        if (height == tabHeight && CollectionTabs.Children.OfType<FrameworkElement>().All(tab => tab.Height == height)) return;
        tabHeight = height;
        foreach (FrameworkElement tab in CollectionTabs.Children) tab.Height = height;
    }

    // 能往上或往下滚时，对应的边缘渐隐，提示还有标签。
    private void OnCollectionScrolled(object sender, ScrollChangedEventArgs e) =>
        CollectionScroll.OpacityMask = NoteTheme.FadeMask(CollectionScroll.ActualHeight,
            CollectionScroll.VerticalOffset > 0.5, CollectionScroll.VerticalOffset < CollectionScroll.ScrollableHeight - 0.5);

    // 子便签展开时顶部对齐它的标签（屏幕坐标）；标签区隐藏或标签滚出视野时用最近的可见位置。
    internal double CollectionTabTop(NoteWindow child)
    {
        var cardTop = Top + ShadowMargin;
        if (!CollectionRail.IsVisible || !CollectionTabButtons.TryGetValue(child.Data.Id, out var tab)) return cardTop;
        UpdateLayout();
        if (tab.ActualHeight == 0) return cardTop;
        var y = tab.TransformToAncestor(this).Transform(new Point()).Y - ((TranslateTransform)tab.RenderTransform).Y;
        var viewport = RailViewport.TransformToAncestor(this).Transform(new Point()).Y;
        return Top + Math.Clamp(y, viewport, Math.Max(viewport, viewport + RailViewport.ActualHeight - tab.ActualHeight));
    }

    // 标签悬停预览：标题、正文开头三行、修改时间。
    private static StackPanel TabPreview(NoteWindow child)
    {
        var panel = new StackPanel { MaxWidth = 260 };
        panel.Children.Add(new TextBlock { Text = child.Data.Preview, Style = (Style)child.FindResource("TabPreviewTitle") });
        var lines = child.Editor.TextContent.Split('\n').Select(line => line.Trim().TrimStart('#', '>', '-', '*', ' ').Trim())
            .Where(line => line.Length > 0).Skip(1).Take(3);
        foreach (var line in lines)
            panel.Children.Add(new TextBlock { Text = line, Style = (Style)child.FindResource("TabPreviewLine") });
        var state = child.IsVisible ? "单击收起" : "单击展开";
        panel.Children.Add(new TextBlock { Text = $"{Ago(child.Data.UpdatedAt)}修改 · {state} · 可拖动排序",
            Style = (Style)child.FindResource("TabPreviewNote") });
        return panel;
    }

    private static string Ago(DateTimeOffset time)
    {
        var elapsed = DateTimeOffset.UtcNow - time;
        if (elapsed.TotalMinutes < 1) return "刚刚";
        if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes} 分钟前";
        if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours} 小时前";
        if (elapsed.TotalDays < 2) return "昨天";
        if (elapsed.TotalDays < 30) return $"{(int)elapsed.TotalDays} 天前";
        var local = time.ToLocalTime();
        return local.Year == DateTime.Now.Year ? $"{local.Month} 月 {local.Day} 日" : $"{local.Year} 年 {local.Month} 月 {local.Day} 日";
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
