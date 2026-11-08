$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$checkPath = Join-Path $projectRoot ('.local\checks\install-current-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
$source = Join-Path $checkPath '构建 文件'
$target = Join-Path $checkPath '正式 桌边记'
$backupRoot = $target + '.rollback'
$registryPath = 'Software\Zhuobianji.Checks\' + [guid]::NewGuid().ToString('N')
$installer = Join-Path $PSScriptRoot 'install-app.ps1'
New-Item -ItemType Directory -Path (Join-Path $source 'docs') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $source '桌边记.exe'), 'fixture-program')
[IO.File]::WriteAllText((Join-Path $source 'docs\使用说明.md'), 'fixture-doc')
$data = Join-Path $target 'Data'
New-Item -ItemType Directory -Path $data -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $target '桌边记.exe'), 'previous-fixture-program')
[IO.File]::WriteAllText((Join-Path $target '用户文件.txt'), 'keep-user-file')
$dataFile = Join-Path $data 'sample.json'
[IO.File]::WriteAllText($dataFile, '{"Text":"现有内容"}')
$originalText = [IO.File]::ReadAllText($dataFile)
try {
    & $installer -SourceDirectory $source -TargetDirectory $target -StartupRegistrySubKey $registryPath
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath)
    try { if ($key.GetValue('WeNote') -ne ('"' + (Join-Path $target '桌边记.exe') + '"')) { throw '正式入口自启路径不正确。' } } finally { $key.Dispose() }
    if ([IO.File]::ReadAllText($dataFile) -ne $originalText) { throw '安装改变现有数据。' }
    if (-not (Test-Path -LiteralPath (Join-Path $target 'docs\使用说明.md'))) { throw '说明没有归入 docs。' }
    Write-Output 'PASS：安装保留 Data 和用户文件，首次安装自启指向正式入口。'
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath,$true)
    try { $key.DeleteValue('WeNote') } finally { $key.Dispose() }
    [IO.File]::WriteAllText((Join-Path $source '桌边记.exe'), 'updated-fixture-program')
    & $installer -SourceDirectory $source -TargetDirectory $target -StartupRegistrySubKey $registryPath
    $backups = @(Get-ChildItem -LiteralPath $backupRoot -Directory)
    if ($backups.Count -ne 1 -or [IO.File]::ReadAllText((Join-Path $backups[0].FullName '桌边记.exe')) -ne 'fixture-program') { throw '没有只保留最近的更新前程序。' }
    Write-Output 'PASS：连续升级只留上一版程序。'

    # 输入文件不可读：复制尚未完成，正式目录和已有回退都不能被替换。
    $docLock = [IO.File]::Open((Join-Path $source 'docs\使用说明.md'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $failed = $false
    try { & $installer -SourceDirectory $source -TargetDirectory $target -StartupRegistrySubKey $registryPath }
    catch { $failed = $true }
    finally { $docLock.Dispose() }
    if (-not $failed -or [IO.File]::ReadAllText((Join-Path $target '桌边记.exe')) -ne 'updated-fixture-program' -or -not (Test-Path -LiteralPath $backups[0].FullName)) { throw '输入失败损坏了当前程序或回退。' }
    Write-Output 'PASS：输入复制失败保留当前程序及已有回退。'

    # 模拟 Windows 占用 EXE：允许读取备份，但拒绝替换。
    $exeLock = [IO.File]::Open((Join-Path $target '桌边记.exe'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    [IO.File]::WriteAllText((Join-Path $source '桌边记.exe'), 'third-fixture-program')
    $failed = $false
    try { & $installer -SourceDirectory $source -TargetDirectory $target -StartupRegistrySubKey $registryPath }
    catch { $failed = $true }
    finally { $exeLock.Dispose() }
    if (-not $failed -or [IO.File]::ReadAllText((Join-Path $target '桌边记.exe')) -ne 'updated-fixture-program' -or -not (Test-Path -LiteralPath $backups[0].FullName)) { throw '替换失败损坏了当前程序或提前淘汰回退。' }
    & $installer -SourceDirectory $source -TargetDirectory $target -StartupRegistrySubKey $registryPath
    $backups = @(Get-ChildItem -LiteralPath $backupRoot -Directory)
    if ($backups.Count -ne 1 -or [IO.File]::ReadAllText((Join-Path $backups[0].FullName '桌边记.exe')) -ne 'updated-fixture-program') { throw '重试后回退保留不正确。' }
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath)
    try { if ($null -ne $key.GetValue('WeNote')) { throw '升级重新开启了自启。' } } finally { $key.Dispose() }
    if ([IO.File]::ReadAllText($dataFile) -ne $originalText -or [IO.File]::ReadAllText((Join-Path $target '用户文件.txt')) -ne 'keep-user-file') { throw '升级改变了数据或用户文件。' }
    Write-Output 'PASS：替换失败可重试；重试成功清理更早副本，数据、用户文件及关闭自启的选择保留。'
} finally { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($registryPath,$false) }
Write-Output ('检查文件：' + $checkPath)
