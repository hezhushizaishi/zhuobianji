param([string[]] $Group = @())
# 按功能组运行检查。组名由检查程序自己列出（--list）；有 "<组>-reload" 阶段的组会再开一个进程验证重开结果。
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'tests\Zhuobianji.Checks\Zhuobianji.Checks.csproj'
$checksDll = Join-Path $projectRoot 'tests\Zhuobianji.Checks\bin\Release\net10.0-windows\Zhuobianji.Checks.dll'

dotnet build $projectPath -c Release -p:NuGetAudit=false --nologo --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw '检查程序构建失败。' }
$phases = @(dotnet $checksDll --list)
$groups = @($phases | Where-Object { -not $_.EndsWith('-reload') })
if ($Group.Count -eq 0) {
    Write-Output ('可用功能组：' + ($groups -join ', '))
    Write-Output '示例：.\scripts\test-app.ps1 -Group storage,close；全部运行：-Group all'
    return
}
$selected = @(if ($Group -contains 'all') { $groups } else { $Group | Select-Object -Unique })
foreach ($name in $selected) {
    if ($groups -notcontains $name) { throw ('未知功能组：' + $name) }
}

$checkPath = Join-Path $projectRoot ('.local\checks\app-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $checkPath | Out-Null
Write-Output ('检查目录：' + $checkPath)
foreach ($name in $selected) {
    $groupPath = Join-Path $checkPath $name
    New-Item -ItemType Directory -Path $groupPath | Out-Null
    foreach ($phase in @($name, ($name + '-reload')) | Where-Object { $phases -contains $_ }) {
        dotnet $checksDll $phase $groupPath | Tee-Object -FilePath (Join-Path $groupPath ($phase + '.log'))
        if ($LASTEXITCODE -ne 0) { throw ('检查未通过：' + $phase) }
    }
}
Write-Output ('已通过：' + ($selected -join ', '))
Write-Output ('检查文件：' + $checkPath)
