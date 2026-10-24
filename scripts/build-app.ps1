param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$buildRoot = Join-Path $projectRoot '.local\build'
$outputPath = [IO.Path]::GetFullPath($(if ($OutputDirectory) { $OutputDirectory } else { Join-Path $buildRoot 'current' }))
$buildPrefix = $buildRoot.TrimEnd('\') + '\'
if (-not $outputPath.StartsWith($buildPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '构建输出必须位于 .local/build 内。' }
if (Test-Path -LiteralPath (Join-Path $outputPath 'Data')) { throw '构建输出包含 Data，不能替换。' }
if (Test-Path -LiteralPath $outputPath) {
    $resolvedOutput = (Resolve-Path -LiteralPath $outputPath).ProviderPath
    if (-not $resolvedOutput.StartsWith($buildPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '构建输出路径无效。' }
}
$stagingPath = $outputPath + '.publishing-' + [guid]::NewGuid().ToString('N')
$retiredPath = $outputPath + '.retired-' + [guid]::NewGuid().ToString('N')
$promoted = $false
try {
    # 在空目录发布；构建失败时原 current 仍在原处。
    & dotnet publish (Join-Path $projectRoot 'src\Zhuobianji\Zhuobianji.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeSourceRevisionInInformationalVersion=false -p:DebugType=embedded -p:NuGetAudit=false --nologo --verbosity minimal -o $stagingPath
    if ($LASTEXITCODE -ne 0) { throw '桌边记构建失败，已有输出保留。' }
    Move-Item -LiteralPath (Join-Path $stagingPath 'Zhuobianji.exe') -Destination (Join-Path $stagingPath '桌边记.exe')
    $docsPath = Join-Path $stagingPath 'docs'
    New-Item -ItemType Directory -Path $docsPath -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\使用说明.md') -Destination (Join-Path $docsPath '使用说明.md')
    if (Test-Path -LiteralPath $outputPath) { Move-Item -LiteralPath $outputPath -Destination $retiredPath }
    try {
        Move-Item -LiteralPath $stagingPath -Destination $outputPath
        $promoted = $true
    } catch {
        if (Test-Path -LiteralPath $retiredPath) { Move-Item -LiteralPath $retiredPath -Destination $outputPath }
        throw
    }
} finally {
    foreach ($discard in @($stagingPath, $(if ($promoted) { $retiredPath }))) {
        if ($discard -and (Test-Path -LiteralPath $discard)) {
            $resolvedDiscard = (Resolve-Path -LiteralPath $discard).ProviderPath
            if (-not $resolvedDiscard.StartsWith($buildPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '临时输出清理路径无效。' }
            Remove-Item -LiteralPath $resolvedDiscard -Recurse -Force
        }
    }
}
Write-Output ('构建完成：' + (Join-Path $outputPath '桌边记.exe'))
