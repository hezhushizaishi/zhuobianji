using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zhuobianji;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

internal static partial class Program
{
    // 使用实际窗口控件和独立演示数据；背景只用于排列截图，不绘制仿制界面。
    private static async Task PreviewChecks()
    {
        using (var store = new NoteStore(Path.Combine(root, "collection", "Data")))
        using (var controller = new NoteController(store, () => { }, enableHotkeys: false))
        {
            controller.Start();
            var main = controller.Notes.Values.Single();
            PreviewSize(main, 380, 410, "mist");
            main.Editor.Rich.Document = FormattedDocuments.FromHtml(
                "<h2>今天先做这些</h2><p>慢慢来，一件一件完成。</p><ul><li>回一封邮件</li><li>整理桌面文件</li><li>给周末留点空闲</li></ul><p><b>下午三点</b>，出门走一走。</p>", main.Data.Appearance);
            controller.SetMainNote(main);
            var titles = new[] { "临时想法", "周末计划", "常用入口", "读书摘记" };
            var children = titles.Select(title =>
            {
                var child = controller.CreateNote();
                PreviewSize(child, 350, 342, "cream");
                child.Editor.TextContent = title + "\n\n先写下来，暂时不用就收起。\n\n留点时间，做自己喜欢的事。";
                controller.CollectNote(child);
                return child;
            }).ToArray();
            controller.ExpandCollectedNote(children[0]);
            await PreviewReady(controller);
            // 子便签从标签一侧抽出：卡片在主便签标签区左边 8，顶部对齐第一个标签（卡片顶 + 48）。
            PreviewScene("collection.png", 878, 490, (children[0], 24, 88), (main, 382, 40));
            controller.RequestExit();
        }

        // 标签很多时：压矮、不出滚动条，底部渐隐；标签颜色取自各自的便签。
        using (var store = new NoteStore(Path.Combine(root, "rail", "Data")))
        using (var controller = new NoteController(store, () => { }, enableHotkeys: false))
        {
            controller.Start();
            var main = controller.Notes.Values.Single();
            PreviewSize(main, 360, 330, "green");
            main.Editor.TextContent = "主便签\n左边是收起来的便签。";
            controller.SetMainNote(main);
            var palettes = new[] { "green", "cream", "mist", "yellow", "pink", "neutral" };
            var titles = new[] { "模型使用记录", "节点连接", "服务器", "重构原则", "设计提纲", "Solve", "代理设置", "周末计划", "", "读书摘记", "", "常用入口" };
            for (var index = 0; index < titles.Length; index++)
            {
                var child = controller.CreateNote();
                child.ApplyAppearance(new NoteAppearance { Palette = palettes[index % palettes.Length] });
                child.Editor.TextContent = titles[index];
                controller.CollectNote(child);
            }
            controller.ExpandCollectedNote(controller.CollectedNotes(main).ElementAt(2));
            await PreviewReady(controller);
            PreviewScene("rail.png", 500, 394, (main, 24, 32));
            controller.RequestExit();
        }

        using (var store = new NoteStore(Path.Combine(root, "formats", "Data")))
        using (var controller = new NoteController(store, () => { }, enableHotkeys: false))
        {
            controller.Start();
            var rich = controller.Notes.Values.Single();
            PreviewSize(rich, 340, 400, "mist");
            rich.Editor.Rich.Document = FormattedDocuments.FromHtml(
                "<h2>随手写，也能排整齐</h2><p>一句想法，一份清单。</p><p><b>重要的事</b>加粗，<span style='color:#C65E59'>喜欢的句子</span>换个颜色。</p><ul><li>带上相机</li><li>去公园看落叶</li></ul>", rich.Data.Appearance);
            var markdown = controller.CreateNote(format: NoteFormat.Markdown);
            PreviewSize(markdown, 340, 400, "cream");
            markdown.Editor.TextContent = "# 周末的小计划\n\n写完，切到**预览**读一读。\n\n- 找一家安静的咖啡店\n- 读几页书\n- 给朋友回个消息\n\n> 留一点空白给自己。";
            markdown.SetMarkdownPreview(true);
            var plain = controller.CreateNote(format: NoteFormat.PlainText);
            PreviewSize(plain, 340, 400, "green");
            plain.Editor.TextContent = "只记几句话\n\n牛奶、面包、苹果\n记得带伞\n\n灵感来了就先写下，\n不必每次都整理得很正式。";
            await PreviewReady(controller);
            PreviewScene("formats.png", 1124, 464, (rich, 32, 32), (markdown, 392, 32), (plain, 752, 32));
            controller.RequestExit();
        }

        using (var store = new NoteStore(Path.Combine(root, "links", "Data")))
        using (var controller = new NoteController(store, () => { }, enableHotkeys: false))
        {
            var material = Path.Combine(root, "演示资料");
            Directory.CreateDirectory(material);
            var document = Path.Combine(material, "项目说明.txt");
            File.WriteAllText(document, "桌边记截图使用的演示文件。");
            var source = "# 常用入口\n\n资料和链接，放在手边。\n\n- [项目说明](" + new Uri(document).AbsoluteUri + ")\n- [资料文件夹](" + new Uri(material).AbsoluteUri + ")\n- [GitHub](https://github.com/)\n\n下次要用，点文字就能打开。";
            controller.Start();
            var rich = controller.Notes.Values.Single();
            PreviewSize(rich, 420, 380, "mist");
            rich.Editor.Rich.Document = FormattedDocuments.FromMarkdown(source, rich.Data.Appearance);
            var markdown = controller.CreateNote(format: NoteFormat.Markdown);
            PreviewSize(markdown, 420, 380, "cream");
            markdown.Editor.TextContent = source;
            markdown.SetMarkdownPreview(true);
            await PreviewReady(controller);
            PreviewScene("links.png", 956, 444, (rich, 40, 32), (markdown, 496, 32));
            controller.RequestExit();
        }
        // 细节图：排版栏、文字颜色、更多菜单、深色便签，以及一张屏幕实拍（真实阴影和文字渲染）。
        using (var store = new NoteStore(Path.Combine(root, "details", "Data")))
        using (var controller = new NoteController(store, () => { }, enableHotkeys: false))
        {
            controller.Start();
            var rich = controller.Notes.Values.Single();
            PreviewSize(rich, 360, 330, "cream");
            rich.Editor.Rich.Document = FormattedDocuments.FromHtml(
                "<h2>细节</h2><p>选中文字，再点下面的图标。</p><ul><li><b>粗体</b>、<i>斜体</i>、<u>下划线</u></li><li><span style='color:#C95555'>换个颜色</span></li></ul>", rich.Data.Appearance);
            rich.FormatButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var dark = controller.CreateNote(format: NoteFormat.Markdown);
            dark.Width = 360 + 2 * NoteWindow.ShadowMargin;
            dark.Height = 330 + 2 * NoteWindow.ShadowMargin;
            dark.ApplyAppearance(new NoteAppearance { Palette = "mist", Dark = true });
            dark.Editor.TextContent = "# 深色便签\n\n晚上写字不刺眼。\n\n- 顶栏和底栏都是图标\n- 悬停才变实";
            dark.SetMarkdownPreview(true);
            await PreviewReady(controller);
            PreviewScene("details.png", 820, 394, (rich, 32, 32), (dark, 428, 32));

            rich.Editor.Rich.Selection.Select(rich.Editor.Rich.Document.ContentStart, rich.Editor.Rich.Document.ContentStart);
            rich.ColorButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Task.Delay(300);
            PreviewPart("colors.png", (FrameworkElement)rich.ColorPopup.Child);
            rich.ColorPopup.IsOpen = false;
            var menu = rich.MoreButton.ContextMenu;
            menu.PlacementTarget = rich.MoreButton;
            menu.IsOpen = true;
            await Task.Delay(300);
            PreviewPart("menu.png", menu);
            menu.IsOpen = false;

            var backdrop = new Window { Left = rich.Left - 40, Top = rich.Top - 40, Width = rich.Width + 80, Height = rich.Height + 80,
                Topmost = true, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Background = NoteTheme.B("#E7ECEE") };
            backdrop.Show();
            rich.Topmost = true;
            rich.Activate();
            await Task.Delay(300);
            ScreenShot(rich, Path.Combine(root, "screen-note.png"));

            backdrop.Close();
            controller.RequestExit();
        }
        Console.WriteLine("已生成 collection.png、formats.png、links.png、rail.png 和细节图；未使用正式 Data。");
    }

    // 便签窗口四周有阴影边距：这里的宽高和场景坐标都按正文卡片算。
    private static void PreviewSize(NoteWindow window, double width, double height, string palette)
    {
        window.Width = width + 2 * NoteWindow.ShadowMargin;
        window.Height = height + 2 * NoteWindow.ShadowMargin;
        window.ApplyAppearance(new NoteAppearance { Palette = palette });
    }

    private static void PreviewPart(string name, FrameworkElement part)
    {
        part.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(part.ActualWidth * 2), (int)Math.Ceiling(part.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        var scene = new DrawingVisual();
        using (var drawing = scene.RenderOpen())
        {
            drawing.DrawRectangle(NoteTheme.B("#E7ECEE"), null, new Rect(0, 0, part.ActualWidth, part.ActualHeight));
            var captured = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 192, 192, PixelFormats.Pbgra32);
            captured.Render(part);
            drawing.DrawImage(captured, new Rect(0, 0, part.ActualWidth, part.ActualHeight));
        }
        image.Render(scene);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(root, name));
        encoder.Save(file);
    }

    private static async Task PreviewReady(NoteController controller)
    {
        foreach (var window in controller.Notes.Values) window.SaveNow();
        Keyboard.ClearFocus();
        // 等标签抽出、窗口淡入等动画播完；窗口多时动画起步会晚一些。
        await Task.Delay(600);
        foreach (var window in controller.Notes.Values.Where(window => window.IsVisible)) window.UpdateLayout();
    }

    private static void PreviewScene(string name, int width, int height, params (Window Window, double X, double Y)[] notes)
    {
        var scene = new DrawingVisual();
        using (var drawing = scene.RenderOpen())
        {
            drawing.DrawRectangle(NoteTheme.B("#E7ECEE"), null, new Rect(0, 0, width, height));
            foreach (var (window, x, y) in notes)
            {
                var content = (FrameworkElement)window.Content;
                var captured = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * 2),
                    (int)Math.Ceiling(content.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                captured.Render(content);
                drawing.DrawImage(captured, new Rect(x - NoteWindow.ShadowMargin, y - NoteWindow.ShadowMargin, content.ActualWidth, content.ActualHeight));
            }
        }
        var image = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        image.Render(scene);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(root, name));
        encoder.Save(file);
    }
}
