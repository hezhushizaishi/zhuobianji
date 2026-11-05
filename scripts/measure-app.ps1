param(
    [string]$Exe,
    [int[]]$Notes = @(10, 50),
    [int]$FirstRuns = 3,
    [int]$Runs = 5,
    [int]$SettleSeconds = 5,
    [string]$Label = ''
)
# 测量启动时间、内存和体积，结果追加到 .local\measure\history.csv，方便每次改动后对比。
# 用生成的假便签和独立 Data，不碰正式便签；会反复打开、关闭桌边记窗口并抢前台，跑的时候别操作。
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $projectRoot '.local\build\current\桌边记.exe' }
$Exe = (Resolve-Path -LiteralPath $Exe).ProviderPath
if (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe }) { throw ('要测量的程序正在运行，请先退出：' + $Exe) }
$measureRoot = Join-Path $projectRoot '.local\measure'
$runRoot = Join-Path $measureRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Path $runRoot | Out-Null
$utf8 = New-Object Text.UTF8Encoding $false

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class ZhuobianjiMeasure
{
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);

    // wpfOnly：只算程序自己的 WPF 窗口（输入法等系统组件也会在进程里建窗口）。
    private static List<IntPtr> Windows(int processId, bool wpfOnly, bool visibleOnly)
    {
        var found = new List<IntPtr>();
        var name = new StringBuilder(256);
        EnumWindows(delegate(IntPtr window, IntPtr parameter)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != processId || (visibleOnly && !IsWindowVisible(window))) return true;
            name.Length = 0;
            GetClassName(window, name, name.Capacity);
            if (!wpfOnly || name.ToString().StartsWith("HwndWrapper[")) found.Add(window);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // 启动，返回出现第一个可见窗口的毫秒数；超时或程序退出返回 -1。
    public static double Start(Process process, int timeout)
    {
        var clock = Stopwatch.StartNew();
        process.Start();
        while (clock.ElapsedMilliseconds < timeout)
        {
            if (process.HasExited) return -1;
            if (Windows(process.Id, true, true).Count > 0) return clock.Elapsed.TotalMilliseconds;
            Thread.Sleep(5);
        }
        return -1;
    }

    // { 工作集, 专用内存, 句柄, 线程, GDI 对象, USER 对象, WPF 窗口（含隐藏）, 可见 WPF 窗口, CPU 毫秒 }
    public static double[] Sample(Process process)
    {
        process.Refresh();
        return new double[] { process.WorkingSet64, process.PrivateMemorySize64, process.HandleCount, process.Threads.Count,
            GetGuiResources(process.Handle, 0), GetGuiResources(process.Handle, 1),
            Windows(process.Id, true, false).Count, Windows(process.Id, true, true).Count, process.TotalProcessorTime.TotalMilliseconds };
    }

    // 像注销时一样发会话结束消息，程序走正常退出（保存、清掉托盘图标）；没退出再结束进程。返回是否正常退出。
    public static bool Stop(Process process, int timeout)
    {
        IntPtr result;
        foreach (var window in Windows(process.Id, false, false))
            SendMessageTimeout(window, 0x0011, IntPtr.Zero, new IntPtr(1), 0x0002, 2000, out result);
        foreach (var window in Windows(process.Id, false, false))
            SendMessageTimeout(window, 0x0016, new IntPtr(1), new IntPtr(1), 0x0002, 2000, out result);
        if (process.WaitForExit(timeout)) return true;
        process.Kill();
        process.WaitForExit(5000);
        return false;
    }
}
'@

# 假便签照站长的实际数据：大多富文本，少量 Markdown 和纯文本；1 张主便签显示，七成收纳，其余隐藏。固定种子，每次一样。
$sentences = @(
    '今天下午三点和设计组过一遍首页改版的稿子。', '记得把上周的报销单交给财务，发票在左边抽屉里。',
    '接口文档已经更新，新的分页参数从第 0 页开始。', '周末去超市：牛奶、鸡蛋、面包、洗衣液。',
    '这个问题先复现，再看日志里有没有超时。', 'Release notes need a short summary for each fix.',
    '会议纪要：下个版本只做性能，不加新功能。', '读书笔记：好的结构比好的句子更重要。',
    '服务器证书下个月到期，提前一周续上。', '把常用的命令整理成一页，贴在显示器旁边。',
    'The build takes about two minutes on a clean checkout.', '给客户的回复先写要点，再补细节和时间。')
$links = @('https://github.com/hezhushizaishi/zhuobianji/issues/{0}', 'https://learn.microsoft.com/dotnet/desktop/wpf/page-{0}',
    'https://example.com/docs/section/{0}')
$palettes = @('mist', 'cream', 'yellow', 'green', 'pink', 'neutral')

function New-NoteText([Random]$random, [string]$format) {
    $paragraphs = 1 + [int]([Math]::Pow($random.NextDouble(), 2) * 40)
    $plain = New-Object Collections.Generic.List[string]
    $rich = New-Object Text.StringBuilder
    [void]$rich.Append('<Section xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xml:space="preserve">')
    if ($format -eq 'Markdown') { $plain.Add('# 笔记 ' + $random.Next(100)) }
    for ($p = 0; $p -lt $paragraphs; $p++) {
        $parts = @(for ($s = 0; $s -le $random.Next(3); $s++) { $sentences[$random.Next($sentences.Count)] })
        $text = -join $parts
        $link = $null
        if ($random.NextDouble() -lt 0.15) { $link = $links[$random.Next($links.Count)] -f $random.Next(1, 500) }
        $bold = $random.NextDouble() -lt 0.2
        $code = $random.NextDouble() -lt 0.05
        if ($format -eq 'Markdown') {
            if ($code) { $plain.Add('```' + "`r`n" + 'dotnet publish -c Release -r win-x64' + "`r`n" + '```') }
            elseif ($p % 5 -eq 4) { $plain.Add(($parts | ForEach-Object { '- ' + $_ }) -join "`r`n") }
            else {
                $line = if ($bold) { '**' + $parts[0] + '**' + (-join @($parts | Select-Object -Skip 1)) } else { $text }
                if ($link) { $line += ' [链接](' + $link + ')' }
                $plain.Add($line)
            }
            continue
        }
        $plain.Add($(if ($link) { $text + ' ' + $link } else { $text }))
        if ($code) {
            [void]$rich.Append('<Paragraph FontFamily="Consolas"><Run>dotnet publish -c Release -r win-x64</Run></Paragraph>')
            continue
        }
        [void]$rich.Append('<Paragraph>')
        if ($bold) {
            [void]$rich.Append('<Span FontWeight="Bold"><Run>' + [Security.SecurityElement]::Escape($parts[0]) + '</Run></Span>')
            $parts = @($parts | Select-Object -Skip 1)
        }
        if ($parts.Count -gt 0) { [void]$rich.Append('<Run>' + [Security.SecurityElement]::Escape((-join $parts)) + '</Run>') }
        if ($link) {
            $uri = [Uri]$link
            [void]$rich.Append('<Run> </Run><Hyperlink NavigateUri="' + $link + '"><Run>' + $uri.Host + $uri.AbsolutePath.TrimEnd('/') + '</Run></Hyperlink>')
        }
        [void]$rich.Append('</Paragraph>')
    }
    [void]$rich.Append('</Section>')
    $separator = if ($format -eq 'PlainText') { "`r`n" } else { "`r`n`r`n" }
    # PowerShell 5.1 会把空的 $(if …) 写成 {}，所以 RichText 显式给 $null。
    $richText = $null
    if ($format -eq 'RichText') { $richText = $rich.ToString() }
    @{ Text = ($plain -join $separator); RichText = $richText }
}

function New-NoteSet([string]$path, [int]$count) {
    New-Item -ItemType Directory -Path $path | Out-Null
    $random = New-Object Random 20261105
    $collected = [int][Math]::Round($count * 0.7)
    $mainId = [guid]::NewGuid()
    $created = [DateTimeOffset]::new(2026, 10, 1, 8, 0, 0, [TimeSpan]::Zero)
    for ($i = 0; $i -lt $count; $i++) {
        $id = $mainId
        if ($i -gt 0) { $id = [guid]::NewGuid() }
        $format = 'RichText'
        if ($i -gt 0 -and $i % 10 -eq 3) { $format = 'Markdown' }
        if ($i % 10 -eq 7) { $format = 'PlainText' }
        $collectedBy = $null
        if ($i -ge 1 -and $i -le $collected) { $collectedBy = $mainId.ToString() }
        $content = New-NoteText $random $format
        $note = [ordered]@{
            Version = 2; Id = $id.ToString(); Text = $content.Text; Format = $format; RichText = $content.RichText
            PreviewMarkdown = ($i % 20 -eq 3)
            Appearance = [ordered]@{ Palette = $palettes[$i % $palettes.Count]; Dark = $false; FontFamily = 'Microsoft YaHei UI'; FontSize = 17; Opacity = 0.95 }
            Left = 300 + ($i % 8) * 40; Top = 120 + ($i % 6) * 30; Width = 420; Height = 440
            IsPinned = $false; IsOnDesktop = $false
            IsHidden = ($i -ne 0); IsCollectionMain = ($i -eq 0); ShowCollectionRail = $true
            CollectedBy = $collectedBy; CollectionOrder = @()
            CreatedAt = $created.AddHours($i).ToString('o'); UpdatedAt = $created.AddDays(30).AddHours($i).ToString('o')
        }
        if ($i -eq 0) { $note.Width = 483; $note.Height = 498 }
        [IO.File]::WriteAllText((Join-Path $path ($id.ToString('N') + '.json')), (ConvertTo-Json $note -Depth 5), $utf8)
    }
    # 关掉快捷键：正在用的桌边记已经占着默认快捷键，冲突会弹框挡住启动。
    $settings = [ordered]@{
        DefaultFormat = 'RichText'
        Appearance = [ordered]@{ Palette = 'mist'; Dark = $false; FontFamily = 'Microsoft YaHei UI'; FontSize = 17; Opacity = 1 }
        Hotkeys = [ordered]@{ NewNote = [ordered]@{ Modifiers = 0; Key = 0 }; ToggleNotes = [ordered]@{ Modifiers = 0; Key = 0 } }
        ShowFormatWarnings = $true
    }
    [IO.File]::WriteAllText((Join-Path $path 'settings.json'), (ConvertTo-Json $settings -Depth 5), $utf8)
}

function Get-Median([double[]]$values) {
    $sorted = @($values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    [Math]::Round($(if ($sorted.Count % 2) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }), 1)
}

function Measure-Launch([string]$template, [string]$extract, [string]$kind, [int]$count, [int]$index) {
    if (-not (Test-Path -LiteralPath $extract)) { New-Item -ItemType Directory -Path $extract | Out-Null }
    $data = Join-Path $runRoot 'run-data'
    if (Test-Path -LiteralPath $data) { Remove-Item -LiteralPath $data -Recurse -Force }
    Copy-Item -LiteralPath $template -Destination $data -Recurse
    $info = New-Object Diagnostics.ProcessStartInfo $Exe
    $info.Arguments = '--data-dir "' + $data + '"'
    $info.UseShellExecute = $false
    $info.WorkingDirectory = Split-Path -Parent $Exe
    $info.EnvironmentVariables['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = $extract
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $info
    $shown = [ZhuobianjiMeasure]::Start($process, 60000)
    if ($shown -lt 0) {
        if (-not $process.HasExited) { [void][ZhuobianjiMeasure]::Stop($process, 5000) }
        throw ("第 $index 次${kind}启动（$count 张）60 秒内没有出现窗口。")
    }
    Start-Sleep -Seconds $SettleSeconds
    $sample = [ZhuobianjiMeasure]::Sample($process)
    $graceful = [ZhuobianjiMeasure]::Stop($process, 8000)
    $process.Dispose()
    $row = [pscustomobject]@{
        便签 = $count; 类型 = $kind; 次 = $index; 出窗口ms = [Math]::Round($shown); CPUms = [Math]::Round($sample[8])
        工作集MB = [Math]::Round($sample[0] / 1MB, 1); 专用MB = [Math]::Round($sample[1] / 1MB, 1)
        句柄 = $sample[2]; 线程 = $sample[3]; GDI = $sample[4]; USER = $sample[5]; WPF窗口 = $sample[6]; 可见窗口 = $sample[7]; 正常退出 = $graceful
    }
    Write-Host ("  {0}启动 {1}：出窗口 {2} ms，CPU {3} ms，工作集 {4} MB，专用 {5} MB，可见窗口 {6}" -f $kind, $index, $row.出窗口ms, $row.CPUms, $row.工作集MB, $row.专用MB, $row.可见窗口)
    $row
}

Write-Output '测量会反复打开、关闭桌边记窗口并抢前台，约 3 分钟，期间请别操作。'
Write-Output ('程序：' + $Exe)
$rows = New-Object Collections.Generic.List[object]
$extractSize = 0
try {
    foreach ($count in $Notes) {
        $template = Join-Path $runRoot "data-$count"
        New-NoteSet $template $count
        Write-Output "$count 张便签："
        $extract = $null
        for ($i = 1; $i -le $FirstRuns; $i++) {
            # 首次启动：解压目录是空的，相当于升级后第一次打开。
            $extract = Join-Path $runRoot "extract-$count-$i"
            $rows.Add((Measure-Launch $template $extract '首次' $count $i))
        }
        if (-not $extractSize) { $extractSize = (Get-ChildItem -LiteralPath $extract -Recurse -File | Measure-Object -Property Length -Sum).Sum }
        for ($i = 1; $i -le $Runs; $i++) { $rows.Add((Measure-Launch $template $extract '再次' $count $i)) }
    }
} finally {
    foreach ($leftover in @(Get-ChildItem -LiteralPath $runRoot -Directory | Where-Object { $_.Name -like 'extract-*' -or $_.Name -eq 'run-data' })) {
        Remove-Item -LiteralPath $leftover.FullName -Recurse -Force
    }
}

$exeItem = Get-Item -LiteralPath $Exe
$exeMB = [Math]::Round($exeItem.Length / 1MB, 1)
$extractMB = [Math]::Round($extractSize / 1MB, 1)
$commit = (& git -C $projectRoot rev-parse --short HEAD) + $(if (& git -C $projectRoot status --porcelain) { '+改动' } else { '' })
$hash = (Get-FileHash -LiteralPath $Exe).Hash.Substring(0, 12)
$summary = foreach ($count in $Notes) {
    $first = @($rows | Where-Object { $_.便签 -eq $count -and $_.类型 -eq '首次' })
    $again = @($rows | Where-Object { $_.便签 -eq $count -and $_.类型 -eq '再次' })
    [pscustomobject]@{
        便签 = $count
        首次出窗口ms = Get-Median ($first | ForEach-Object { $_.出窗口ms })
        再次出窗口ms = Get-Median ($again | ForEach-Object { $_.出窗口ms })
        CPUms = Get-Median ($again | ForEach-Object { $_.CPUms })
        工作集MB = Get-Median ($again | ForEach-Object { $_.工作集MB })
        专用MB = Get-Median ($again | ForEach-Object { $_.专用MB })
        句柄 = Get-Median ($again | ForEach-Object { $_.句柄 })
        线程 = Get-Median ($again | ForEach-Object { $_.线程 })
        GDI = Get-Median ($again | ForEach-Object { $_.GDI })
        USER = Get-Median ($again | ForEach-Object { $_.USER })
        WPF窗口 = Get-Median ($again | ForEach-Object { $_.WPF窗口 })
    }
}

$result = [ordered]@{
    时间 = (Get-Date).ToString('yyyy-MM-dd HH:mm'); 说明 = $Label; 提交 = $commit; 版本 = $exeItem.VersionInfo.ProductVersion
    程序 = $Exe; 哈希 = $hash; 程序MB = $exeMB; 解压MB = $extractMB; 汇总 = @($summary); 每次 = $rows.ToArray()
}
[IO.File]::WriteAllText((Join-Path $runRoot 'result.json'), (ConvertTo-Json $result -Depth 5), $utf8)
$history = Join-Path $measureRoot 'history.csv'
if (-not (Test-Path -LiteralPath $history)) {
    [IO.File]::WriteAllText($history, "时间,说明,提交,版本,哈希,程序MB,解压MB,便签,首次出窗口ms,再次出窗口ms,CPUms,工作集MB,专用MB,句柄,线程,GDI,USER,WPF窗口`r`n", (New-Object Text.UTF8Encoding $true))
}
foreach ($line in $summary) {
    $fields = @($result.时间, ('"' + $Label.Replace('"', '""') + '"'), $commit, $result.版本, $hash, $exeMB, $extractMB,
        $line.便签, $line.首次出窗口ms, $line.再次出窗口ms, $line.CPUms, $line.工作集MB, $line.专用MB, $line.句柄, $line.线程, $line.GDI, $line.USER, $line.WPF窗口)
    [IO.File]::AppendAllText($history, ($fields -join ',') + "`r`n", $utf8)
}

Write-Output ''
Write-Output ("程序 {0} MB，首次启动解压本机库 {1} MB；提交 {2}，版本 {3}。下面是中位数，内存取「再次启动」{4} 秒后：" -f $exeMB, $extractMB, $commit, $result.版本, $SettleSeconds)
foreach ($line in $summary) {
    Write-Output ("  {0} 张：出窗口 首次 {1} ms / 再次 {2} ms，CPU {3} ms；工作集 {4} MB，专用 {5} MB；句柄 {6}，线程 {7}，GDI {8}，USER {9}，WPF 窗口 {10}" -f
        $line.便签, $line.首次出窗口ms, $line.再次出窗口ms, $line.CPUms, $line.工作集MB, $line.专用MB, $line.句柄, $line.线程, $line.GDI, $line.USER, $line.WPF窗口)
}
$forced = @($rows | Where-Object { -not $_.正常退出 }).Count
if ($forced) { Write-Output "有 $forced 次没能正常退出，已结束进程；托盘可能残留图标，鼠标移过去就会消失。" }
$odd = @($rows | Where-Object { $_.可见窗口 -ne 1 }).Count
if ($odd) { Write-Output "有 $odd 次启动后可见窗口不是 1 个（可能弹了提示框），见明细。" }
Write-Output ('明细：' + (Join-Path $runRoot 'result.json'))
Write-Output ('历史：' + $history)
