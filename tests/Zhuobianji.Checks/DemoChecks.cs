using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zhuobianji;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

// README 用的动图和仓库的社交预览图，和其他截图一样从真实窗口画出来；背景和鼠标箭头只用来摆场景、指出点了哪里。
internal static partial class Program
{
    // 动图：一张便签从 ⋯ 菜单收进主便签，变成左边的标签；再点另一个标签抽出来，点一下收回。约 7 秒一轮。
    private static async Task PreviewDemo()
    {
        using var store = new NoteStore(Path.Combine(root, "demo", "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var main = controller.Windows.Values.Single();
        PreviewSize(main, 460, 457, "mist");
        main.Editor.Rich.Document = FormattedDocuments.FromHtml(
            "<h2>这周</h2><p>手边的事放在这里，其他的收在左边。</p><ul><li>周三前交报告</li><li>约牙医</li><li>给花换盆</li></ul><p><b>周五</b>，早点下班。</p>", main.Data.Appearance);
        controller.SetMainNote(main);
        var children = new[] { ("周末计划", "cream"), ("读书摘记", "green"), ("常用入口", "pink") }.Select(item =>
        {
            var child = controller.CreateNote();
            PreviewSize(child, 350, 342, item.Item2);
            child.Editor.TextContent = item.Item1 + "\n\n先写下来，暂时不用就收起。\n\n需要时点一下标签，再点一下收回。";
            controller.CollectNote(child);
            return child;
        }).ToArray();
        var loose = controller.CreateNote();
        PreviewSize(loose, 330, 290, "yellow");
        loose.Editor.TextContent = "临时想法\n\n周末去看展，记得提前订票。\n\n用完了，收进主便签。";
        // 主便签的标签区左边是 L + 12，子便签从标签区左边 8 抽出（卡片 350 宽）；场景比照 collection.png。
        main.Left = 900;
        main.Top = 170;
        var sceneLeft = main.Left + NoteWindow.ShadowMargin - 8 - 350 - 24;
        var scene = new Rect(sceneLeft, main.Top + NoteWindow.ShadowMargin - 40, 981, 560);
        loose.Left = sceneLeft + 24 + 6 - NoteWindow.ShadowMargin;
        loose.Top = main.Top + 70;
        await PreviewReady(controller);
        // 动图里不要闪的光标：每次操作之后把键盘焦点拿掉（操作本身照常）。
        System.Windows.Input.Keyboard.ClearFocus();

        var recorder = new DemoRecorder(scene, 2, Center(loose.Editor), loose, main, children[1]);
        await recorder.Hold(700);
        await recorder.Move(Center(loose.MoreButton), 600);
        recorder.Click();
        loose.MoreButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var menu = loose.MoreButton.ContextMenu;
        recorder.Menu = menu;
        await recorder.Hold(400, live: true);
        loose.CollectItem.Focus();
        await recorder.Hold(60, live: true);
        await recorder.Move(Center(loose.CollectItem), 450);
        await recorder.Hold(250);
        recorder.Click();
        menu.IsOpen = false;
        recorder.Menu = null;
        loose.CollectItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        System.Windows.Input.Keyboard.ClearFocus();
        Check(loose.Data.CollectedBy == main.Data.Id && !loose.IsVisible, "动图：⋯ 菜单里「收纳到主便签」把便签收成左边的标签");
        await recorder.Hold(700, live: true);
        await recorder.Hold(400);
        var tab = main.CollectionTabButtons[children[1].Data.Id];
        await recorder.Move(Center(tab), 650);
        recorder.Click();
        tab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        System.Windows.Input.Keyboard.ClearFocus();
        Check(children[1].IsVisible, "动图：点标签，子便签从左边抽出来");
        await recorder.Hold(700, live: true);
        await recorder.Hold(1100);
        recorder.Click();
        tab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await recorder.Hold(500, live: true);
        await recorder.Move(Center(loose.Editor, scene.Left + 160, scene.Top + 260), 700);
        await recorder.Hold(900);
        var file = Path.Combine(root, "demo.png");
        recorder.Save(file);
        Console.WriteLine($"  动图 {recorder.Frames} 帧拍下、写入 {recorder.Written} 帧，{new FileInfo(file).Length / 1024} KB；" +
            $"重画窗口平均 {recorder.AverageWindows:0} ms，每帧合成和写入平均 {recorder.AverageCapture:0} ms");
        Check(recorder.Written > 20 && new FileInfo(file).Length < 4 * 1024 * 1024, "动图写成 APNG，只记变化的区域，不到 4 MB");
        controller.RequestExit();
    }

    // 元素中心的屏幕坐标（DIP）；元素不可见时用给定的点。
    private static Point Center(FrameworkElement element, double fallbackX = 0, double fallbackY = 0)
    {
        if (!element.IsVisible) return new Point(fallbackX, fallbackY);
        var scale = VisualTreeHelper.GetDpi(element).DpiScaleX;
        var point = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        return new Point(point.X / scale, point.Y / scale);
    }

    private sealed class DemoRecorder(Rect scene, double scale, Point cursor, params Window[] windows)
    {
        private readonly ApngWriter writer = new((int)(scene.Width * scale), (int)(scene.Height * scale));
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<(Point At, double Time)> clicks = [];
        private readonly int pixelWidth = (int)(scene.Width * scale), pixelHeight = (int)(scene.Height * scale);
        private int[]? still;
        private double captureTime, windowsTime;
        private int windowFrames;
        private Point cursor = cursor;
        internal ContextMenu? Menu { get; set; }
        internal int Frames { get; private set; }
        internal int Written => writer.FrameCount;
        internal double AverageCapture => captureTime / Math.Max(1, Frames);
        internal double AverageWindows => windowsTime / Math.Max(1, windowFrames);
        private double Now => clock.Elapsed.TotalMilliseconds;

        // live：窗口在变（动画、刚点过），每帧都重画窗口；否则只重画鼠标，窗口用上一次的画面。
        internal async Task Hold(double milliseconds, bool live = false)
        {
            // 刚点过：等系统画了两帧再拍（程序里再显示的窗口也是这时才露出来，见 NoteWindow.Reveal），不拍动画开始前的那一刻。
            if (live) { await Rendered(); await Rendered(); }
            var end = Now + milliseconds;
            do { Capture(live); await Task.Delay(15); } while (Now < end);
        }

        internal async Task Move(Point to, double milliseconds)
        {
            var from = cursor;
            var start = Now;
            while (true)
            {
                var t = Math.Min(1, (Now - start) / milliseconds);
                var eased = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
                cursor = new Point(from.X + (to.X - from.X) * eased, from.Y + (to.Y - from.Y) * eased);
                Capture(false);
                if (t >= 1) return;
                await Task.Delay(15);
            }
        }

        internal void Click() => clicks.Add((cursor, Now));

        private static Task Rendered()
        {
            var done = new TaskCompletionSource();
            void OnRendering(object? sender, EventArgs e) { CompositionTarget.Rendering -= OnRendering; done.TrySetResult(); }
            CompositionTarget.Rendering += OnRendering;
            return done.Task;
        }

        internal void Save(string path) => writer.Save(path, Now);

        private void Capture(bool live)
        {
            var started = Now;
            if (live || still is null)
            {
                still = Windows();
                windowsTime += Now - started;
                windowFrames++;
            }
            var composed = Now;
            var pixels = (int[])still.Clone();
            // 鼠标和点击泛开的圈只占一小块：单独画这一块，叠到窗口画面上，不用每帧重画整张。
            var rings = clicks.Select(click => (At: Local(click.At), T: (Now - click.Time) / 350)).Where(ring => ring.T is >= 0 and <= 1).ToArray();
            var tip = Local(cursor);
            var area = new Rect(tip.X - 1, tip.Y - 1, 16, 24);
            foreach (var (at, t) in rings) area.Union(new Rect(at.X - 21, at.Y - 21, 42, 42));
            var left = Math.Max(0, (int)Math.Floor(area.Left * scale));
            var top = Math.Max(0, (int)Math.Floor(area.Top * scale));
            var right = Math.Min(pixelWidth, (int)Math.Ceiling(area.Right * scale));
            var bottom = Math.Min(pixelHeight, (int)Math.Ceiling(area.Bottom * scale));
            if (right > left && bottom > top)
            {
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    drawing.PushTransform(new TranslateTransform(-left / scale, -top / scale));
                    // 点下去的地方泛开一圈，0.35 秒淡掉。
                    foreach (var (at, t) in rings)
                        drawing.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(150 * (1 - t)), 0x30, 0x3C, 0x49)), 2), at, 6 + 12 * t, 6 + 12 * t);
                    DrawCursor(drawing, tip);
                    drawing.Pop();
                }
                var marks = new RenderTargetBitmap(right - left, bottom - top, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                marks.Render(visual);
                var overlay = new int[marks.PixelWidth * marks.PixelHeight];
                marks.CopyPixels(overlay, marks.PixelWidth * 4, 0);
                for (var y = 0; y < marks.PixelHeight; y++)
                    for (var x = 0; x < marks.PixelWidth; x++)
                    {
                        var source = overlay[y * marks.PixelWidth + x];
                        var alpha = (source >> 24) & 0xFF;
                        if (alpha == 0) continue;
                        var index = (top + y) * pixelWidth + left + x;
                        var target = pixels[index];
                        int Channel(int shift) => ((source >> shift) & 0xFF) + ((target >> shift) & 0xFF) * (255 - alpha) / 255;
                        pixels[index] = (0xFF << 24) | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
                    }
            }
            writer.Add(pixels, started);
            Frames++;
            captureTime += Now - composed;
        }

        private Point Local(Point screen) => new(screen.X - scene.Left, screen.Y - scene.Top);

        // 背景和这一刻每个窗口（含打开的菜单）的画面，按窗口实际位置摆。
        private int[] Windows()
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(NoteTheme.B("#E7ECEE"), null, new Rect(0, 0, scene.Width, scene.Height));
                foreach (var window in windows.Where(window => window.IsVisible))
                    Draw(drawing, (FrameworkElement)window.Content, new Point(window.Left, window.Top));
                if (Menu is { IsOpen: true } menu)
                {
                    var dpi = VisualTreeHelper.GetDpi(menu).DpiScaleX;
                    var at = menu.PointToScreen(new Point());
                    Draw(drawing, menu, new Point(at.X / dpi, at.Y / dpi));
                }
            }
            var image = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            image.Render(visual);
            var pixels = new int[pixelWidth * pixelHeight];
            image.CopyPixels(pixels, pixelWidth * 4, 0);
            return pixels;
        }

        private void Draw(DrawingContext drawing, FrameworkElement element, Point screen)
        {
            var captured = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            captured.Render(element);
            var at = Local(screen);
            drawing.DrawImage(captured, new Rect(at.X, at.Y, element.ActualWidth, element.ActualHeight));
        }

        // 常见的箭头光标：白底黑边，底下一点阴影。
        private static readonly Geometry Arrow = Geometry.Parse("M0,0 L0,17 L4.2,13.2 L7,19.6 L9.8,18.4 L7,12.2 L12.6,12.2 Z");

        private static void DrawCursor(DrawingContext drawing, Point at)
        {
            drawing.PushTransform(new TranslateTransform(at.X + 0.8, at.Y + 1.2));
            drawing.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), null, Arrow);
            drawing.Pop();
            drawing.PushTransform(new TranslateTransform(at.X, at.Y));
            drawing.DrawGeometry(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28)), 1) { LineJoin = PenLineJoin.Round }, Arrow);
            drawing.Pop();
        }
    }

    // 仓库的社交预览图（1280×640，GitHub 设置里上传）：左边图标、名字和一句话，右边是真实的主便签和展开的子便签。
    private static async Task SocialPreview()
    {
        using var store = new NoteStore(Path.Combine(root, "social", "Data"));
        using var controller = new NoteController(store, () => { }, enableHotkeys: false);
        controller.Start();
        var main = controller.Windows.Values.Single();
        PreviewSize(main, 400, 420, "mist");
        main.Editor.Rich.Document = FormattedDocuments.FromHtml(
            "<h2>今天先做这些</h2><ul><li>回一封邮件</li><li>整理桌面文件</li><li>给周末留点空闲</li></ul><p><b>下午三点</b>，出门走一走。</p>", main.Data.Appearance);
        controller.SetMainNote(main);
        var children = new[] { ("临时想法", "cream"), ("周末计划", "yellow"), ("常用入口", "green"), ("读书摘记", "pink") }.Select(item =>
        {
            var child = controller.CreateNote();
            PreviewSize(child, 300, 300, item.Item2);
            child.Editor.TextContent = item.Item1 + "\n\n先写下来，暂时不用就收起。";
            controller.CollectNote(child);
            return child;
        }).ToArray();
        controller.ExpandCollectedNote(children[0].Entry);
        await PreviewReady(controller);
        System.Windows.Input.Keyboard.ClearFocus();
        await Task.Delay(100);

        const int width = 1280, height = 640;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(NoteTheme.B("#E7ECEE"), null, new Rect(0, 0, width, height));
            var ink = NoteTheme.For("mist", false).Text;
            var muted = NoteTheme.For("mist", false).Muted;
            var icon = BitmapDecoder.Create(new Uri("pack://application:,,,/Zhuobianji;component/Assets/zhuobianji.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
                .Frames.OrderByDescending(frame => frame.PixelWidth).First();
            drawing.DrawImage(icon, new Rect(84, 150, 112, 112));
            var font = NoteTheme.Font(NoteTheme.BundledFont);
            void Text(string text, double size, FontWeight weight, Brush brush, double y)
            {
                var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                    new Typeface(font, FontStyles.Normal, weight, FontStretches.Normal), size, brush, 1);
                drawing.DrawText(formatted, new Point(84, y));
            }
            Text("桌边记", 76, FontWeights.Bold, ink, 284);
            Text("随手记，收整齐", 34, FontWeights.Normal, ink, 392);
            Text("Windows 桌面便签 · 富文本 · Markdown · 纯文本", 20, FontWeights.Normal, muted, 456);
            // 右边：展开的子便签和主便签，按窗口的相对位置摆，整体缩到放得下。
            var notes = new[] { (Window)children[0], main };
            var left = notes.Min(window => window.Left);
            var top = notes.Min(window => window.Top);
            var right = notes.Max(window => window.Left + window.ActualWidth);
            var bottom = notes.Max(window => window.Top + window.ActualHeight);
            var fit = Math.Min(660 / (right - left), 560 / (bottom - top));
            var originX = width - 48 - (right - left) * fit;
            var originY = (height - (bottom - top) * fit) / 2;
            foreach (var window in notes)
            {
                var content = (FrameworkElement)window.Content;
                var captured = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * 2), (int)Math.Ceiling(content.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                captured.Render(content);
                drawing.DrawImage(captured, new Rect(originX + (window.Left - left) * fit, originY + (window.Top - top) * fit, content.ActualWidth * fit, content.ActualHeight * fit));
            }
        }
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(Path.Combine(root, "social-preview.png"))) encoder.Save(file);
        controller.RequestExit();
    }
}
