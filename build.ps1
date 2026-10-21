# desktop-calendar 构建脚本
# 用法： powershell -ExecutionPolicy Bypass -File build.ps1 [-Clean]

param(
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) {
    Write-Host "找不到 C# 编译器：$csc" -ForegroundColor Red
    exit 1
}

$buildDir = Join-Path $root 'build'
$exePath = Join-Path $buildDir 'desktop-calendar.exe'

if ($Clean -and (Test-Path -LiteralPath $buildDir)) {
    Remove-Item -LiteralPath $buildDir -Recurse -Force
}

if (-not (Test-Path -LiteralPath $buildDir)) {
    New-Item -ItemType Directory -Path $buildDir | Out-Null
}

# 显式列出全部源文件（不使用通配符，绝不包含 _scratch）
$sources = @(
    (Join-Path $root 'src\Models\Schedule.cs'),
    (Join-Path $root 'src\Models\RepeatRule.cs'),
    (Join-Path $root 'src\Data\Json.cs'),
    (Join-Path $root 'src\Data\Store.cs'),
    (Join-Path $root 'src\Data\TipStore.cs'),
    (Join-Path $root 'src\Services\Scheduler.cs'),
    (Join-Path $root 'src\Services\AutoStart.cs'),
    (Join-Path $root 'src\Services\DiagnosticLog.cs'),
    (Join-Path $root 'src\UI\Theme.cs'),
    (Join-Path $root 'src\UI\Draw.cs'),
    (Join-Path $root 'src\UI\Branding.cs'),
    (Join-Path $root 'src\UI\Chrome.cs'),
    (Join-Path $root 'src\UI\MonthView.cs'),
    (Join-Path $root 'src\UI\EventEditor.cs'),
    (Join-Path $root 'src\UI\TipEditor.cs'),
    (Join-Path $root 'src\UI\ReminderCard.cs'),
    (Join-Path $root 'src\UI\TrayController.cs'),
    (Join-Path $root 'src\UI\MainForm.cs'),
    (Join-Path $root 'src\Program.cs')
)

$missing = @()
foreach ($s in $sources) {
    if (-not (Test-Path -LiteralPath $s -PathType Leaf)) { $missing += $s }
}
if ($missing.Count -gt 0) {
    Write-Host "缺少源文件：" -ForegroundColor Red
    foreach ($m in $missing) { Write-Host "  $m" -ForegroundColor Red }
    exit 1
}

$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/codepage:65001',
    '/warn:4',
    ('/out:' + $exePath),
    '/r:System.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Core.dll'
)

# 应用图标：assets\app.ico 存在才传 /win32icon；缺失只提示一行，绝不让构建失败。
$appIcon = Join-Path $root 'assets\app.ico'
if (Test-Path -LiteralPath $appIcon -PathType Leaf) {
    Write-Host ("图标：{0}" -f $appIcon)
    $cscArgs += ('/win32icon:' + $appIcon)
}
else {
    Write-Host '[warn] assets\app.ico 缺失，跳过图标'
}

$cscArgs += $sources

Write-Host '=== 编译命令 ==='
Write-Host ('"{0}" {1}' -f $csc, ($cscArgs -join ' '))
Write-Host ''

& $csc @cscArgs
$cscExit = $LASTEXITCODE

Write-Host ''
if ($cscExit -ne 0) {
    Write-Host "编译失败，csc 退出码：$cscExit" -ForegroundColor Red
    exit $cscExit
}

$info = Get-Item -LiteralPath $exePath
Write-Host '编译成功'
Write-Host ("输出：{0}" -f $info.FullName)
Write-Host ("字节数：{0}" -f $info.Length)

exit $LASTEXITCODE
