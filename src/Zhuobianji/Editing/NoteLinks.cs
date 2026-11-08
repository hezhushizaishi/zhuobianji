using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace Zhuobianji;

internal static class NoteLinks
{
    internal static bool TryResolve(string? address, out Uri uri)
    {
        uri = null!;
        var value = address?.Trim().Trim('"') ?? "";
        if (value.Length == 0 || value.Contains('\r') || value.Contains('\n')) return false;
        if (Path.IsPathFullyQualified(value))
        {
            try { uri = new Uri(Path.GetFullPath(value)); return uri.IsFile; }
            catch (Exception ex) when (ex is ArgumentException or UriFormatException or NotSupportedException) { return false; }
        }
        if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) value = "https://" + value;
        return Uri.TryCreate(value, UriKind.Absolute, out uri!) && uri.Scheme is "http" or "https" or "mailto" or "file";
    }

    internal static string Address(Uri uri) => uri.IsFile ? uri.LocalPath : uri.AbsoluteUri;
    // 给人看的地址：中文等字符不显示成 %E4%B8%AD；复制、打开仍用 Address。
    internal static string Readable(Uri uri) => uri.IsFile ? uri.LocalPath : uri.ToString();
    internal static string Tooltip(Uri uri) => uri.IsFile ? "打开文件夹\n" + Readable(uri) : Readable(uri);

    private const int NameLimit = 40;

    // 没有名称的链接自动起名：网址显示「域名/路径」，过长时省略中间；本地显示文件名或文件夹名，邮箱显示地址。
    internal static string ShortName(Uri uri)
    {
        if (uri.IsFile)
        {
            var path = uri.LocalPath.TrimEnd('\\', '/');
            var name = Path.GetFileName(path);
            return name.Length > 0 ? name : path;
        }
        if (uri.Scheme == "mailto") return uri.AbsoluteUri[7..];
        var host = uri.IdnHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.IdnHost[4..] : uri.IdnHost;
        if (!uri.IsDefaultPort) host += ":" + uri.Port;
        var rest = Uri.UnescapeDataString(uri.PathAndQuery);
        if (rest.Any(char.IsControl)) rest = uri.PathAndQuery;
        var label = host + rest.TrimEnd('/');
        return label.Length <= NameLimit ? label : label[..(NameLimit - 13)] + "…" + label[^12..];
    }

    // 0.8.x 给网址自动起的名字只有域名（IdnHost），也算自动名称。
    internal static bool IsAutoName(string label, Uri uri) =>
        label == ShortName(uri) || label == Address(uri) || label == uri.AbsoluteUri || !uri.IsFile && label == uri.IdnHost;

    // 链接写成纯文字（转纯文本、复制）：自动名称直接换成地址，自己起的名称后面括上地址。
    internal static string PlainText(string label, Uri uri) => IsAutoName(label, uri) ? Address(uri) : label + " (" + Address(uri) + ")";

    internal static ProcessStartInfo LaunchInfo(Uri uri) => new(Address(uri)) { UseShellExecute = true };

    internal static void Open(Uri uri) => Open(uri, info => { Process.Start(info); }, OpenFileLocation,
        message => Dialogs.Show(message, "链接"));

    internal static void Open(Uri uri, Action<ProcessStartInfo> launch, Action<string> selectFile, Action<string> showError)
    {
        try
        {
            if (!TryResolve(uri.AbsoluteUri, out _)) throw new ArgumentException("地址格式不正确。");
            if (uri.IsFile)
            {
                if (File.Exists(uri.LocalPath)) { selectFile(uri.LocalPath); return; }
                if (!Directory.Exists(uri.LocalPath)) throw new FileNotFoundException("文件或文件夹不存在，可能已移动或删除。");
            }
            launch(LaunchInfo(uri));
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or COMException)
        {
            showError(ex is FileNotFoundException or ArgumentException ? ex.Message : "无法打开：" + ex.Message);
        }
    }

    // Windows Shell 官方接口：打开父文件夹并选中这个文件，不执行文件。
    // https://learn.microsoft.com/windows/win32/api/shlobj_core/nf-shlobj_core-shopenfolderandselectitems
    internal static void OpenFileLocation(string path)
    {
        var initialized = CoInitializeEx(IntPtr.Zero, 2);
        Marshal.ThrowExceptionForHR(initialized);
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _));
            try { Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0)); }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        finally { CoUninitialize(); }
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bind, out IntPtr pidl, uint attributes, out uint resultingAttributes);
    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint count, IntPtr children, uint flags);

}
