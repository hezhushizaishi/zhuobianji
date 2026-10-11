using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WeNote.App;
using WeNote.Core;

namespace WeNote.Tests;

internal static partial class Program
{
    // 使用实际窗口控件和独立演示数据；背景只用于排列截图，不绘制仿制界面。
    private static async Task Preview()
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
            PreviewScene("collection.png", 990, 490, (main, 36, 40), (children[0], 544, 76));
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
        Console.WriteLine("已生成 collection.png、formats.png、links.png；未使用正式 Data。");
    }

    private static void PreviewSize(NoteWindow window, double width, double height, string palette)
    {
        window.Width = width;
        window.Height = height;
        window.ApplyAppearance(new NoteAppearance { Palette = palette });
    }

    private static async Task PreviewReady(NoteController controller)
    {
        foreach (var window in controller.Notes.Values) window.SaveNow();
        Keyboard.ClearFocus();
        await Task.Delay(250);
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
                drawing.DrawImage(captured, new Rect(x, y, content.ActualWidth, content.ActualHeight));
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
