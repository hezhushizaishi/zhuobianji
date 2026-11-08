param([string]$Package = (Join-Path $env:USERPROFILE '.nuget\packages\wpf-ui\4.3.0'))
# 重新生成图标字体：从 Fluent UI System Icons 的完整字体里只留 NoteIcon.cs 里列出的图标，写到 src/Zhuobianji/Assets/Icons。
# 完整字体取自 WPF UI 4.3.0 包里的 Wpf.Ui.dll（Fluent UI System Icons 1.1.242，MIT），码位和它的 SymbolRegular 一致。
# 没有这个包时，从 https://www.nuget.org/packages/WPF-UI/4.3.0 下载 .nupkg，解压后用 -Package 指过去。
# 需要 Python 和 fontTools（pip install fonttools）。只在加图标时用，构建不需要。
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$source = Join-Path $projectRoot 'src\Zhuobianji\Theme\NoteIcon.cs'
$output = Join-Path $projectRoot 'src\Zhuobianji\Assets\Icons'
$dll = Join-Path $Package 'lib\net462\Wpf.Ui.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw "找不到 $dll，见脚本开头的说明。" }

# 码位：枚举里的「名字 = 0x码位」是空心字形，FilledGlyphs 里的「[NoteIconSymbol.名字] = 0x码位」是实心字形。
$code = [IO.File]::ReadAllText($source)
$enum = [regex]::Match($code, 'enum NoteIconSymbol\s*\{(?<body>[^}]*)\}').Groups['body'].Value
$regular = @([regex]::Matches($enum, '0x([0-9A-Fa-f]+)') | ForEach-Object { 'U+' + $_.Groups[1].Value })
$filledBlock = [regex]::Match($code, 'int> FilledGlyphs = new\(\)\s*\{(?<body>[^}]*)\}').Groups['body'].Value
$filled = @([regex]::Matches($filledBlock, '0x([0-9A-Fa-f]+)') | ForEach-Object { 'U+' + $_.Groups[1].Value })
if ($regular.Count -eq 0 -or $filled.Count -eq 0) { throw '没从 NoteIcon.cs 读到码位。' }

$work = Join-Path ([IO.Path]::GetTempPath()) ('zhuobianji-icons-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $assembly = [Reflection.Assembly]::LoadFrom($dll)
    $reader = New-Object Resources.ResourceReader ($assembly.GetManifestResourceStream('Wpf.Ui.g.resources'))
    foreach ($entry in $reader) {
        if ($entry.Key -notlike 'resources/fonts/fluentsystemicons-*.ttf') { continue }
        $file = [IO.File]::Create((Join-Path $work (Split-Path $entry.Key -Leaf)))
        ([IO.Stream]$entry.Value).CopyTo($file)
        $file.Close()
    }
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    foreach ($font in @(@{ Name = 'Regular'; Unicodes = $regular }, @{ Name = 'Filled'; Unicodes = $filled })) {
        $from = Join-Path $work ('fluentsystemicons-' + $font.Name.ToLowerInvariant() + '.ttf')
        $to = Join-Path $output ('FluentSystemIcons-' + $font.Name + '.ttf')
        & python -I -m fontTools.subset $from ('--unicodes=' + ($font.Unicodes -join ',')) ('--output-file=' + $to)
        if ($LASTEXITCODE -ne 0) { throw "裁剪 $($font.Name) 失败。" }
        '{0}：{1} 个图标，{2:n1} KB' -f (Split-Path $to -Leaf), $font.Unicodes.Count, ((Get-Item -LiteralPath $to).Length / 1KB)
    }
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
