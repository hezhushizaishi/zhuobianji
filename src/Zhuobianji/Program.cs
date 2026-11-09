using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Zhuobianji.Core;

namespace Zhuobianji;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        UseSoftwareRendering();
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        NoteController? controller = null;
        application.Startup += (_, _) =>
        {
            try
            {
                var dataOption = Array.IndexOf(args, "--data-dir");
                var dataDirectory = dataOption >= 0 && dataOption + 1 < args.Length
                    ? args[dataOption + 1] : Path.Combine(AppContext.BaseDirectory, "Data");
                controller = new NoteController(new NoteStore(dataDirectory), () => application.Shutdown(),
                    new WindowsStartup(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "桌边记.exe")));
                controller.Start();
            }
            catch (IOException ex)
            {
                Dialogs.Show($"无法打开便签数据。程序可能已经在运行，请先从右下角托盘找回便签。\n\n{ex.Message}");
                application.Shutdown(1);
            }
            catch (Exception ex)
            {
                Dialogs.Show($"启动失败：{ex.Message}");
                application.Shutdown(1);
            }
        };
        application.SessionEnding += (_, e) => { if (controller is not null && !controller.EndSession()) e.Cancel = true; };
        application.Exit += (_, _) => controller?.Dispose();
        application.Run();
    }

    // 便签窗口用软件渲染，不建显卡设备：常驻内存少 30–100 MB（窗口越大省得越多），启动也更快；
    // 代价是滚动、缩放大窗口时 CPU 高一些（数字见开发说明的取舍）。检查程序也调用它，测的就是发布的样子。
    internal static void UseSoftwareRendering() => RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
}
