using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zhuobianji;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

// 功能检查入口：每个功能组一个阶段；需要独立进程重开的组另有 "<组>-reload" 阶段。
// scripts/test-app.ps1 通过 --list 读取这里的阶段，按组依次运行。
internal static partial class Program
{
    private static int passed;
    private static string root = "";

    private static readonly Dictionary<string, Func<Task>> Phases = new(StringComparer.Ordinal)
    {
        ["storage"] = Sync(StorageChecks),
        ["basic"] = BasicChecks,
        ["basic-reload"] = Sync(BasicReload),
        ["desktop"] = DesktopChecks,
        ["desktop-reload"] = Sync(DesktopReload),
        ["editing"] = EditingChecks,
        ["editing-reload"] = Sync(EditingReload),
        ["settings"] = SettingsChecks,
        ["startup"] = StartupChecks,
        ["window"] = WindowChecks,
        ["lifecycle"] = LifecycleChecks,
        ["close"] = CloseChecks,
        ["close-reload"] = CloseReload,
        ["deletion"] = DeletionChecks,
        ["collection"] = CollectionChecks,
        ["collection-reload"] = CollectionReload,
        ["sorting"] = SortingChecks,
        ["sorting-reload"] = SortingReload,
        ["rail"] = RailChecks,
        ["rail-reload"] = RailReload,
        ["paste"] = PasteChecks,
        ["layout"] = LayoutChecks,
        ["links"] = LinksChecks,
        ["formatting"] = FormattingChecks,
        ["formatting-reload"] = Sync(FormattingReload),
        ["preview"] = PreviewChecks
    };

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--list"])
        {
            foreach (var name in Phases.Keys) Console.WriteLine(name);
            return 0;
        }
        if (args.Length != 2 || !Phases.TryGetValue(args[0], out var check))
        {
            Console.Error.WriteLine("需要有效检查阶段与测试目录；按功能运行请使用 scripts/test-app.ps1。");
            return 1;
        }
        root = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(root);
        if (args[0] == "storage")
        {
            try { check().GetAwaiter().GetResult(); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                await check();
                Console.WriteLine($"PASS {args[0]}：{passed} 项检查");
                app.Shutdown(0);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
        };
        return app.Run();
    }

    private static Func<Task> Sync(Action action) => () =>
    {
        action();
        return Task.CompletedTask;
    };

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL：" + name);
        passed++;
        Console.WriteLine("PASS：" + name);
    }

    private static bool SameNoteData(NoteData? actual, NoteData expected) =>
        actual is not null && actual.CollectionOrder.SequenceEqual(expected.CollectionOrder) &&
        (actual with { CollectionOrder = expected.CollectionOrder }) == expected;

    private static bool SameBounds(NoteData a, NoteData b) => Math.Abs(a.Left - b.Left) < 1 && Math.Abs(a.Top - b.Top) < 1 &&
        Math.Abs(a.Width - b.Width) < 1 && Math.Abs(a.Height - b.Height) < 1;

    // 主便签的正文卡片：左边和宽度不含左侧标签区。
    private static bool SameCard(NoteData left, NoteData right) =>
        Math.Abs(left.Left - right.Left) < 1 && Math.Abs(left.Width - right.Width) < 1;

    private static IEnumerable<TextElement> Elements(FlowDocument document) => DocumentText.Elements(document);

    private static bool HasBold(FlowDocument document) =>
        Elements(document).Any(e => e.FontWeight == FontWeights.Bold || e.FontWeight == FontWeights.SemiBold);

    private static FlowDocument LoadPaste(IDataObject data)
    {
        var document = new FlowDocument();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes((string)data.GetData(DataFormats.Xaml)!));
        new TextRange(document.ContentStart, document.ContentEnd).Load(stream, DataFormats.Xaml);
        return document;
    }

    // 截图只写入本次检查目录，供人工比对外观，不作为断言。
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        SavePng(window.Content as FrameworkElement ?? (FrameworkElement)VisualTreeHelper.GetChild(window, 0), path);
    }

    private static void CaptureDialog(Window dialog, string path)
    {
        dialog.UpdateLayout();
        SavePng((FrameworkElement)VisualTreeHelper.GetChild(dialog, 0), path);
    }

    private static void SavePng(FrameworkElement visual, string path)
    {
        var image = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static bool NativeChild(NoteWindow window)
    {
        var diagnostic = JsonSerializer.SerializeToElement(window.DesktopDiagnostics);
        var style = long.Parse(diagnostic.GetProperty("Style").GetString()!, NumberStyles.HexNumber);
        return (style & 0x40000000) != 0 && diagnostic.GetProperty("ParentClass").GetString() == "Progman";
    }
}
