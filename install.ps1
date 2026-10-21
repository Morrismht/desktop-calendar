# 桌面日历安装脚本：把 exe 装到用户目录，创建桌面/开始菜单快捷方式，并清理桌面上的旧 exe 副本。
# 用法： powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1 [-Source <exe>] [-InstallDir <dir>]
param(
    [string]$Source = '',
    [string]$InstallDir = '',
    [switch]$NoShortcut,
    [switch]$KeepOldCopy
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($Source)) { $Source = Join-Path $root 'build\desktop-calendar.exe' }
if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\DesktopCalendar'
}

Write-Host '=== 桌面日历 安装 ===' -ForegroundColor Cyan
Write-Host ('源 exe：{0}' -f $Source)
Write-Host ('安装到：{0}' -f $InstallDir)

if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
    Write-Host ('找不到源 exe：{0}，请先运行 build.ps1' -f $Source) -ForegroundColor Red
    exit 1
}

# 1) 结束正在运行的实例（同名进程）
$running = @(Get-Process -Name 'desktop-calendar' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host ('结束正在运行的实例：{0}' -f (($running | ForEach-Object { $_.Id }) -join ', '))
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 600
}

# 2) 复制 exe
if (-not (Test-Path -LiteralPath $InstallDir)) { New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null }
$target = Join-Path $InstallDir 'desktop-calendar.exe'
Copy-Item -LiteralPath $Source -Destination $target -Force
$hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
$size = (Get-Item -LiteralPath $target).Length
Write-Host ('已安装：{0}（{1} 字节，SHA256 {2}）' -f $target, $size, $hash.Substring(0, 16))

# 3) 快捷方式
$ws = New-Object -ComObject WScript.Shell
$created = @()
if (-not $NoShortcut) {
    $desktopLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) '桌面日历.lnk'
    $s1 = $ws.CreateShortcut($desktopLnk)
    $s1.TargetPath = $target
    $s1.WorkingDirectory = $InstallDir
    $s1.IconLocation = $target + ',0'
    $s1.Description = '桌面日历 · 日程与提醒'
    $s1.Save()
    $created += $desktopLnk

    $startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) '桌面日历.lnk'
    $s2 = $ws.CreateShortcut($startMenu)
    $s2.TargetPath = $target
    $s2.WorkingDirectory = $InstallDir
    $s2.IconLocation = $target + ',0'
    $s2.Description = '桌面日历 · 日程与提醒'
    $s2.Save()
    $created += $startMenu

    # 开机启动目录里的旧 cmd 若指向别处，这里不碰，交给程序内置的开机自启开关管理
    foreach ($c in $created) { Write-Host ('快捷方式：{0}' -f $c) }
}

# 4) 清理桌面上的旧 exe 副本（移动到安装目录下的 backup，可回滚）
$oldCopies = @(Get-ChildItem -LiteralPath ([Environment]::GetFolderPath('Desktop')) -Filter 'desktop-calendar*.exe' -ErrorAction SilentlyContinue)
foreach ($old in $oldCopies) {
    if ($old.FullName -ieq $target) { continue }
    $backupDir = Join-Path $InstallDir 'backup'
    if (-not (Test-Path -LiteralPath $backupDir)) { New-Item -ItemType Directory -Path $backupDir -Force | Out-Null }
    $dest = Join-Path $backupDir ($old.Name + '.' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.bak')
    if ($KeepOldCopy) {
        Write-Host ('保留旧副本（未移动）：{0}' -f $old.FullName) -ForegroundColor Yellow
    }
    else {
        Move-Item -LiteralPath $old.FullName -Destination $dest -Force
        Write-Host ('旧副本已移到备份：{0} -> {1}' -f $old.FullName, $dest) -ForegroundColor Yellow
    }
}

# 5) 安装清单
$manifest = [ordered]@{
    installedAt = (Get-Date).ToString('s')
    target = $target
    source = $Source
    size = $size
    sha256 = $hash
    shortcuts = $created
}
$manifestPath = Join-Path $InstallDir 'install.json'
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host ('安装清单：{0}' -f $manifestPath)

Write-Host '=== 完成 ===' -ForegroundColor Green
Write-Host '下一步：双击桌面「桌面日历」启动；托盘图标若看不到，程序会写日志到 %APPDATA%\DesktopCalendar\diagnostics.log'
