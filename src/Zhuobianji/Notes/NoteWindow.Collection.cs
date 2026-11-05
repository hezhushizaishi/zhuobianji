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
    // 主便签左侧留给标签的透明区域；Snapshot 保存的位置宽度不含这一块。
    // 标签跟主便签卡片等比缩放：卡片短边约 305 时是基准尺寸（区宽 92、标签高 30），457 时 1.5 倍；按 0.1 一档，最多 1.6 倍。
    // 标签区宽度只放大一半（1.5 倍时 115），标签不至于伸得太长。标签不压矮，放不下就用滚轮滚动（滚动条隐藏，上下边缘渐隐）。
    internal const double RailWidth = 92;
    private double railScale;
    private double collectionRailWidth;
    // 标签高、间距，展开时整张抽出的距离和悬停时抽出的距离；随档位换算（ScaleTabs）。
    private double tabHeight, tabGap, tabPulled, tabPeek;
    private int railAnimationVersion;
    internal double CollectionRailWidth => collectionRailWidth;
    internal Dictionary<Guid, ToggleButton> CollectionTabButtons { get; } = [];
    private double TabPitch => tabHeight + tabGap;
    // 关闭标签时整排滑到卡片下面的距离。
    private double RailHiddenOffset => collectionRailWidth - 8;

    internal static double RailScale(double cardWidth, double cardHeight) =>
        Math.Clamp(Math.Round(Math.Min(cardWidth, cardHeight) / 305, 1), 1, 1.6);

    private static double RailWidthAt(double scale) => Math.Round(RailWidth * (1 + scale) / 2);

    internal static double RailWidthFor(double cardWidth, double cardHeight) => RailWidthAt(RailScale(cardWidth, cardHeight));

    // 卡片跨过一档时换算标签尺寸：高、间距、缩进、压在卡片下的一截和抽出距离一起放大，字号最大 16（和正文一样）。
    // 样式里的尺寸引用标签区上的资源，之后新建的标签自动用当前档位。
    private void ScaleTabs(double scale)
    {
        if (scale == railScale) return;
        railScale = scale;
        double S(double value) => Math.Round(value * scale);
        tabHeight = S(30);
        tabGap = S(16 / 3.0);
        tabPulled = -S(10);
        tabPeek = -S(4);
        var tuck = S(10);
        RailViewport.Margin = new Thickness(0, 48, -tuck, 6);
        var resources = CollectionRail.Resources;
        resources["TabHeight"] = tabHeight;
        resources["TabMargin"] = new Thickness(S(12), 0, 0, tabGap);
        resources["TabPadding"] = new Thickness(S(10), 0, tuck + S(4), 0);
        resources["TabFontSize"] = Math.Min(16, S(12));
        resources["NewTabHeight"] = S(24);
        resources["NewTabWidth"] = S(38);
        resources["NewTabMargin"] = new Thickness(S(12), 0, 0, 0);
        resources["NewTabIconSize"] = Math.Min(20, S(14));
        foreach (ToggleButton tab in CollectionTabs.Children) PullTab(tab);
    }

    // 标签区宽度跟着档位走。改宽度要同时移动窗口左边，所以拖动缩放时等松手再改（NoteWindow.Saving 的 WM_EXITSIZEMOVE）。
    private void FitRailWidth() => SetRailWidth(Data.IsCollectionMain ? RailWidthAt(railScale) : 0);

    private void OnCardResized(object sender, SizeChangedEventArgs e)
    {
        ScaleTabs(RailScale(e.NewSize.Width, e.NewSize.Height));
        if (!moving) FitRailWidth();
    }

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
        FitRailWidth();
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
            var remaining = Math.Min(1, Math.Abs((show ? 0 : RailHiddenOffset) - fromOffset) / RailHiddenOffset);
            var duration = TimeSpan.FromMilliseconds(Math.Max(70, (show ? 220 : 170) * remaining));
            var easing = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseInOut };
            var fade = new DoubleAnimation(fromOpacity, show ? 1 : 0, duration) { EasingFunction = easing };
            var slide = new DoubleAnimation(fromOffset, show ? 0 : RailHiddenOffset, duration) { EasingFunction = easing };
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
        MainTray.Visibility = Data.IsCollectionMain ? Visibility.Visible : Visibility.Collapsed;
        CloseButton.ToolTip = Data.IsCollectionMain ? "关闭主便签（Ctrl+W）"
            : Data.CollectedBy is not null ? "收起便签（Ctrl+W）" : "关闭便签（Ctrl+W）";
        DeleteButton.ToolTip = Data.IsCollectionMain ? "删除主便签，收纳的便签会释放到桌面" : "删除便签";
        RailColumn.Width = new GridLength(CollectionRailWidth);
        var railVisible = Data.IsCollectionMain && Data.ShowCollectionRail;
        if (!CollectionRail.HasAnimatedProperties)
        {
            CollectionRail.Opacity = railVisible ? 1 : 0;
            RailTranslation.X = railVisible ? 0 : RailHiddenOffset;
        }
        CollectionRail.Visibility = Data.IsCollectionMain && (railVisible || CollectionRail.HasAnimatedProperties)
            ? Visibility.Visible : Visibility.Collapsed;
        CollectionRail.IsHitTestVisible = railVisible;
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
                tab = new ToggleButton { Style = (Style)FindResource("CollectionTab"), Tag = child.Data.Id,
                    RenderTransform = new TranslateTransform(), ToolTip = "" };
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
    }

    // 展开的标签整张抽出，悬停时抽出一点。
    private void PullTab(ToggleButton tab) => SlideTab(tab, TranslateTransform.XProperty,
        tab.IsChecked == true ? tabPulled : tab.IsMouseOver ? tabPeek : 0, NoteTheme.Standard);

    // 标签的位移动画：X 是往外抽，Y 是拖动排序时让位。目标没变就不重新开始，连续的 DragOver 不会让动画一直拖着。
    private static void SlideTab(UIElement tab, DependencyProperty axis, double target, Duration duration)
    {
        var shift = (TranslateTransform)tab.RenderTransform;
        if ((double)shift.GetAnimationBaseValue(axis) == target) return;
        var from = (double)shift.GetValue(axis);
        shift.SetValue(axis, target);
        shift.BeginAnimation(axis, new DoubleAnimation(from, target, duration) { EasingFunction = NoteTheme.Ease });
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
        var viewport = CollectionScroll.TransformToAncestor(this).Transform(new Point()).Y;
        return Top + Math.Clamp(y, viewport, Math.Max(viewport, viewport + CollectionScroll.ActualHeight - tab.ActualHeight));
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
