param(
    [string]$SourceDirectory,
    [string]$TargetDirectory,
    [string]$StartupRegistrySubKey = 'Software\Microsoft\Windows\CurrentVersion\Run'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$projectPrefix = $projectRoot.TrimEnd('\') + '\'
$sourcePath = [IO.Path]::GetFullPath($(if ($SourceDirectory) { $SourceDirectory } else { Join-Path $projectRoot '.local\build\current' }))
$targetPath = [IO.Path]::GetFullPath($(if ($TargetDirectory) { $TargetDirectory } else { Join-Path $projectRoot '桌边记' }))
$targetExe = Join-Path $targetPath '桌边记.exe'
if (-not (Test-Path -LiteralPath (Join-Path $sourcePath '桌边记.exe'))) { throw '请先构建桌边记。' }
if ($sourcePath.TrimEnd('\') -eq $targetPath.TrimEnd('\')) { throw '构建目录和运行目录不能相同。' }
if (-not $targetPath.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '升级目录必须位于项目内。' }
if (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $targetExe }) {
    throw '请从便签或托盘菜单正常退出程序，保存内容后再升级。'
}
$isFormal = $targetPath -eq (Join-Path $projectRoot '桌边记')
# 隔离安装的回退文件留在测试目录，不能淘汰正式回退版本。
$backupRoot = $(if ($isFormal) { Join-Path $projectRoot 'archive\rollback' } else { $targetPath + '.rollback' })
$startupRecord = $(if ($isFormal) { Join-Path $projectRoot 'archive\Startup-before.txt' } else { $targetPath + '.startup-before.txt' })
$backupPath = Join-Path $backupRoot ('Before-update-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
$stagingPath = Join-Path $projectRoot ('.local\install-' + [guid]::NewGuid().ToString('N'))
$replaced = $false
try {
    New-Item -ItemType Directory -Path (Join-Path $stagingPath 'docs') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourcePath '桌边记.exe') -Destination $stagingPath
    # 所有输入先复制成功，再开始更新正式目录。
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $sourcePath 'docs') -File) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $stagingPath 'docs')
    }
    $legacyFiles = @(if (Test-Path -LiteralPath $targetPath) {
        Get-ChildItem -LiteralPath $targetPath -File | Where-Object {
            $_.Extension -in @('.dll', '.pdb') -or $_.Name -in @(
                'WeNote.Rewrite.deps.json', 'WeNote.Rewrite.runtimeconfig.json', 'createdump.exe',
                '使用说明.md', 'THIRD-PARTY-NOTICES.txt', 'WPF-UI-THIRD-PARTY-NOTICES.txt')
        }
    })
    if ((Test-Path -LiteralPath $targetExe) -or $legacyFiles.Count) {
        New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
        if (Test-Path -LiteralPath $targetExe) { Copy-Item -LiteralPath $targetExe -Destination $backupPath }
        if (Test-Path -LiteralPath (Join-Path $targetPath 'docs')) { Copy-Item -LiteralPath (Join-Path $targetPath 'docs') -Destination $backupPath -Recurse }
        foreach ($file in $legacyFiles) { Copy-Item -LiteralPath $file.FullName -Destination $backupPath }
    }
    $dataPath = Join-Path $targetPath 'Data'
    New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
    $docsPath = Join-Path $targetPath 'docs'
    New-Item -ItemType Directory -Path $docsPath -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $stagingPath 'docs') -File) {
        Copy-Item -LiteralPath $file.FullName -Destination $docsPath -Force
    }
    $stagedExe = Join-Path $stagingPath '桌边记.exe'
    if (Test-Path -LiteralPath $targetExe) { [IO.File]::Replace($stagedExe, $targetExe, [NullString]::Value) }
    else { Move-Item -LiteralPath $stagedExe -Destination $targetExe }
    $replaced = $true
    $startupMarker = Join-Path $dataPath '.autostart-initialized'
    $oldStartupMarker = Join-Path $targetPath '.autostart-initialized'
    if (Test-Path -LiteralPath $oldStartupMarker) { Move-Item -LiteralPath $oldStartupMarker -Destination $startupMarker -Force }
    if (-not (Test-Path -LiteralPath $startupMarker)) {
        $startupKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($StartupRegistrySubKey)
        try {
            $previousStartup = $startupKey.GetValue('WeNote')
            if ($null -ne $previousStartup -and -not (Test-Path -LiteralPath $startupRecord)) {
                New-Item -ItemType Directory -Path (Split-Path -Parent $startupRecord) -Force | Out-Null
                [IO.File]::WriteAllText($startupRecord, [string]$previousStartup)
            }
            $startupKey.SetValue('WeNote', ('"' + $targetExe + '"'), [Microsoft.Win32.RegistryValueKind]::String)
        } finally { $startupKey.Dispose() }
        [IO.File]::WriteAllText($startupMarker, 'Startup initialized. Later upgrades preserve the user choice.')
    }
} catch {
    if ($replaced -and (Test-Path -LiteralPath (Join-Path $backupPath '桌边记.exe'))) {
        Copy-Item -LiteralPath (Join-Path $backupPath '桌边记.exe') -Destination $targetExe -Force
    }
    throw
} finally {
    if (Test-Path -LiteralPath $stagingPath) {
        $resolvedStaging = (Resolve-Path -LiteralPath $stagingPath).ProviderPath
        if (-not $resolvedStaging.StartsWith((Join-Path $projectRoot '.local\') , [StringComparison]::OrdinalIgnoreCase)) { throw '升级临时目录清理路径无效。' }
        Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
    }
}
# 更新成功后清理已备份的旧运行库，并只保留最近一份程序回退。
foreach ($file in $legacyFiles) {
    if ($file.DirectoryName -ne $targetPath) { throw '旧文件路径不在升级目录内。' }
    Remove-Item -LiteralPath $file.FullName -Force
}
if (Test-Path -LiteralPath $backupPath) {
    foreach ($oldBackup in Get-ChildItem -LiteralPath $backupRoot -Directory -Filter 'Before-update-*') {
        if ($oldBackup.FullName -eq $backupPath) { continue }
        $resolvedBackup = (Resolve-Path -LiteralPath $oldBackup.FullName).ProviderPath
        if (-not $resolvedBackup.StartsWith($backupRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '回退清理路径无效。' }
        if (Test-Path -LiteralPath (Join-Path $resolvedBackup 'Data')) { throw '回退目录含 Data，不能自动清理。' }
        Remove-Item -LiteralPath $resolvedBackup -Recurse -Force
    }
}
Write-Output ('正式入口：' + $targetExe)
Write-Output ('数据目录：' + $dataPath)