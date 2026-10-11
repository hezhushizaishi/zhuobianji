param([string[]] $Group = @())

$ErrorActionPreference = 'Stop'
$groups = [ordered]@{
    storage = @('storage')
    basic = @('seed', 'reload')
    desktop = @('desktop-seed', 'desktop-reload')
    editing = @('modern-seed', 'modern-reload')
    settings = @('settings')
    startup = @('closure')
    window = @('iteration')
    lifecycle = @('lifecycle')
    close = @('close-seed', 'close-reload')
    deletion = @('delete-confirm')
    collection = @('collection-seed', 'collection-reload')
    sorting = @('order-seed', 'order-reload')
    rail = @('rail-seed', 'rail-reload')
    paste = @('paste-links')
    layout = @('paste-layout')
    links = @('inline-links')
    formatting = @('polish', 'polish-reload')
    preview = @('preview')
}
if ($Group.Count -eq 0) {
    Write-Output ('可用功能组：' + ($groups.Keys -join ', '))
    Write-Output '示例：.\scripts\test-app.ps1 -Group storage,close'
    Write-Output '安装检查：.\scripts\test-install-app.ps1'
    return
}
$selected = @($Group | Select-Object -Unique)
foreach ($name in $selected) {
    if (-not $groups.Contains($name)) { throw ('未知功能组：' + $name) }
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'tests\WeNote.Tests\WeNote.Tests.csproj'
$dotnetEntry = Join-Path $PSScriptRoot 'dotnet.cmd'
$checkPath = Join-Path $projectRoot ('.local\checks\app-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $checkPath | Out-Null
Write-Output ('检查目录：' + $checkPath)
& $dotnetEntry build $projectPath -c Release -p:NuGetAudit=false --nologo --verbosity minimal |
    Tee-Object -FilePath (Join-Path $checkPath 'build.log')
if ($LASTEXITCODE -ne 0) { throw '检查程序构建失败。' }

$testDll = Join-Path $projectRoot 'tests\WeNote.Tests\bin\Release\net10.0-windows\WeNote.Tests.dll'
foreach ($name in $selected) {
    $groupPath = Join-Path $checkPath $name
    New-Item -ItemType Directory -Path $groupPath | Out-Null
    foreach ($phase in $groups[$name]) {
        & $dotnetEntry $testDll $phase $groupPath |
            Tee-Object -FilePath (Join-Path $groupPath ($phase + '.log'))
        if ($LASTEXITCODE -ne 0) { throw ('检查未通过：' + $name + ' / ' + $phase) }
    }
}
Write-Output ('已通过：' + ($selected -join ', '))
Write-Output ('检查文件：' + $checkPath)
