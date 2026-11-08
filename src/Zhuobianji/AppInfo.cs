using System;
using System.IO;
using System.Reflection;

namespace Zhuobianji;

// 桌边记自己的信息：名称、版本、开源地址和随程序附带的许可，「关于」页和托盘从这里取。
// 版本号只写在 Zhuobianji.csproj 的 <Version>，这里读程序集上的版本，exe 属性里的版本也是它。
internal static class AppInfo
{
    internal const string Name = "桌边记";
    internal const string Tagline = "随手记，收整齐";
    internal static readonly string Version = ReadVersion();

    internal static readonly Uri Repository = new("https://github.com/hezhushizaishi/zhuobianji");
    // HEAD 指向默认分支，改分支名不影响这个地址。
    internal static readonly Uri Guide = new(Repository + "/blob/HEAD/docs/使用说明.md");
    internal static readonly Uri Releases = new(Repository + "/releases/latest");

    // 嵌在程序里的许可（csproj 的 EmbeddedResource）：只发一个 exe 时，用户也能在「关于」里看到。
    internal static readonly (string Title, string File)[] Notices =
        [("桌边记", "LICENSE"), ("第三方组件", "THIRD-PARTY-NOTICES.txt"), (".NET 运行库", "DOTNET-THIRD-PARTY-NOTICES.txt")];

    internal static string Notice(string file)
    {
        using var stream = typeof(AppInfo).Assembly.GetManifestResourceStream("Zhuobianji.Notices." + file)
            ?? throw new FileNotFoundException("程序里没有这份许可。", file);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // csproj 关掉了版本后面自动加的「+提交号」，读到的就是 <Version>。
    private static string ReadVersion() =>
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
}
