using System;
using System.IO;
using System.Linq;
using System.Windows;
using WeNote.Core;

namespace WeNote.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
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
                MessageBox.Show($"无法打开便签数据。程序可能已经在运行，请先从右下角托盘找回便签。\n\n{ex.Message}", "桌边记");
                application.Shutdown(1);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动失败：{ex.Message}", "桌边记");
                application.Shutdown(1);
            }
        };
        application.SessionEnding += (_, e) => { if (controller is not null && !controller.FlushAll()) e.Cancel = true; };
        application.Exit += (_, _) => controller?.Dispose();
        application.Run();
    }
}
