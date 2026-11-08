using System;
using System.IO;
using Microsoft.Win32;

namespace Zhuobianji;

// 启动项名沿用早期的 WeNote：已开启自启的用户升级后开关状态保持一致。
internal sealed class WindowsStartup
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string registryPath;
    private readonly string valueName;
    internal string Command { get; }

    internal WindowsStartup(string executable, string registryPath = RunKey, string valueName = "WeNote")
    {
        Command = "\"" + Path.GetFullPath(executable) + "\"";
        this.registryPath = registryPath;
        this.valueName = valueName;
    }

    internal bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath);
            return string.Equals(key?.GetValue(valueName) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true);
        if (enabled) key.SetValue(valueName, Command, RegistryValueKind.String);
        else if (string.Equals(key.GetValue(valueName) as string, Command, StringComparison.OrdinalIgnoreCase))
            key.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
