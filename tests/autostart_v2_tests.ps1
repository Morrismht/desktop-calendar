# AutoStart v2 自检：编译临时探针 exe（引用 build\desktop-calendar.exe），跑 A–H 断言，
# 并负责真机清理（Run 值 / 启动文件夹 cmd / 计划任务 DesktopCalendar）。
# 本会话派生的子进程可能被运行环境限制（写入被拒），所以断言只校验「链路被走全 + 错误串格式 + 状态自洽」，
# 成功路径由 EnableCascade 注入假机制覆盖，不依赖真实权限。
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root = Split-Path -Parent $PSScriptRoot
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$targetExe = Join-Path $root 'build\desktop-calendar.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$valueName = 'DesktopCalendar'
$taskName = 'DesktopCalendar'
$startupDir = [Environment]::GetFolderPath('Startup')
$startupCmd = Join-Path $startupDir 'DesktopCalendar.cmd'
$script:cleanFail = 0

function Say($line) { Write-Host $line }

function Check($id, $ok, $detail) {
    $text = '[{0}] {1}' -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $id
    if ($detail) { $text = $text + ' : ' + $detail }
    Say $text
    if (-not $ok) { $script:cleanFail++ }
}

function Read-RunValue {
    try {
        $item = Get-ItemProperty -LiteralPath $runKey -Name $valueName -ErrorAction Stop
        return $item.$valueName
    } catch {
        return $null
    }
}

function Task-Exists {
    try {
        & schtasks.exe /Query /TN $taskName *> $null
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

Say '=== AutoStart v2 自检 ==='
Say ('项目根：' + $root)
Say ('被测程序集：' + $targetExe)

if (-not (Test-Path -LiteralPath $targetExe)) {
    Say '[FAIL] 前置：build\desktop-calendar.exe 不存在，请先跑 build.ps1'
    Say 'SUMMARY PASS=0 FAIL=1'
    exit 1
}

# ---------- 1) 记录真机原状态 ----------
$runBefore = Read-RunValue
$cmdBeforeExists = Test-Path -LiteralPath $startupCmd
$cmdBeforeContent = $null
if ($cmdBeforeExists) {
    try { $cmdBeforeContent = [System.IO.File]::ReadAllText($startupCmd, [System.Text.Encoding]::Default) } catch { }
}
$taskBefore = Task-Exists

$runBeforeText = if ($runBefore) { $runBefore } else { '（无）' }
Say ('快照：Run\DesktopCalendar=' + $runBeforeText + '；启动文件夹 cmd 存在=' + $cmdBeforeExists + '；计划任务存在=' + $taskBefore)

$taskXmlFile = $null
if ($taskBefore) {
    try {
        $taskXmlFile = Join-Path $env:TEMP ('dc_task_before_' + [guid]::NewGuid().ToString('N') + '.xml')
        & schtasks.exe /Query /TN $taskName /XML | Set-Content -LiteralPath $taskXmlFile -Encoding Unicode
        Say ('快照：已备份计划任务 XML → ' + $taskXmlFile)
    } catch {
        $taskXmlFile = $null
    }
}

# ---------- 2) 编译探针 ----------
$work = Join-Path $root '_scratch\autostart_v2'
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $work -Force | Out-Null
$probeAssembly = Join-Path $work 'desktop-calendar.exe'
Copy-Item -LiteralPath $targetExe -Destination $probeAssembly -Force
$probeSrc = Join-Path $PSScriptRoot 'AutoStartV2Probe.cs'
$probeExe = Join-Path $work 'probe.exe'

Say '=== 编译探针 ==='
& $csc /nologo /target:exe /codepage:65001 /warn:0 ('/out:' + $probeExe) /r:System.dll /r:System.Core.dll ('/r:' + $probeAssembly) $probeSrc | ForEach-Object { Say $_ }
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $probeExe)) {
    Say '[FAIL] 前置：探针编译失败'
    Say 'SUMMARY PASS=0 FAIL=1'
    exit 1
}

# ---------- 3) 跑探针 ----------
$stdout = Join-Path $work 'probe.out.txt'
$stderr = Join-Path $work 'probe.err.txt'
Say '=== 执行探针 ==='
$proc = Start-Process -FilePath $probeExe -WorkingDirectory $work -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$probeExit = $proc.ExitCode
$outLines = @()
if (Test-Path -LiteralPath $stdout) { $outLines = @(Get-Content -LiteralPath $stdout -Encoding UTF8) }
$errLines = @()
if (Test-Path -LiteralPath $stderr) { $errLines = @(Get-Content -LiteralPath $stderr -Encoding UTF8) }
foreach ($line in $outLines) { Say $line }
if ($errLines.Count -gt 0) {
    foreach ($line in $errLines) { Say ('[stderr] ' + $line) }
}

$probePass = 0
$probeFail = 0
foreach ($line in $outLines) {
    if ($line -match '^SUMMARY PASS=(\d+) FAIL=(\d+)') {
        $probePass = [int]$Matches[1]
        $probeFail = [int]$Matches[2]
    }
}
Say ('探针退出码=' + $probeExit + '（探针自计 PASS=' + $probePass + ' FAIL=' + $probeFail + '）')

# ---------- 4) 真机清理与还原 ----------
Say '=== 真机清理与还原 ==='

$runAfter = Read-RunValue
if ($runBefore) {
    if ($runAfter -ne $runBefore) {
        try {
            New-Item -Path $runKey -Force | Out-Null
            Set-ItemProperty -LiteralPath $runKey -Name $valueName -Value $runBefore
            Say '还原：Run\DesktopCalendar 已恢复为测试前的值'
        } catch {
            Say ('还原失败：Run\DesktopCalendar → ' + $_.Exception.GetType().Name + ' ' + $_.Exception.Message)
        }
    } else {
        Say '还原：Run\DesktopCalendar 未被改动'
    }
} else {
    if ($null -ne $runAfter) {
        try {
            Remove-ItemProperty -LiteralPath $runKey -Name $valueName -Force -ErrorAction Stop
            Say '清理：已删除测试残留的 Run\DesktopCalendar'
        } catch {
            Say ('清理失败：Run\DesktopCalendar → ' + $_.Exception.GetType().Name + ' ' + $_.Exception.Message)
        }
    } else {
        Say '清理：Run\DesktopCalendar 本就无残留'
    }
}

$cmdAfterExists = Test-Path -LiteralPath $startupCmd
if ($cmdBeforeExists) {
    if (-not $cmdAfterExists) {
        try {
            [System.IO.File]::WriteAllText($startupCmd, $cmdBeforeContent, [System.Text.Encoding]::Default)
            Say '还原：启动文件夹 DesktopCalendar.cmd 已恢复'
        } catch {
            Say ('还原失败：DesktopCalendar.cmd → ' + $_.Exception.GetType().Name + ' ' + $_.Exception.Message)
        }
    } else {
        Say '还原：启动文件夹 DesktopCalendar.cmd 未被改动'
    }
} else {
    if ($cmdAfterExists) {
        try {
            Remove-Item -LiteralPath $startupCmd -Force -ErrorAction Stop
            Say '清理：已删除测试残留的 DesktopCalendar.cmd'
        } catch {
            Say ('清理失败：DesktopCalendar.cmd → ' + $_.Exception.GetType().Name + ' ' + $_.Exception.Message)
        }
    } else {
        Say '清理：启动文件夹 DesktopCalendar.cmd 本就无残留'
    }
}

$taskAfter = Task-Exists
if ($taskBefore) {
    if (-not $taskAfter) {
        if ($taskXmlFile -and (Test-Path -LiteralPath $taskXmlFile)) {
            try {
                & schtasks.exe /Create /TN $taskName /XML $taskXmlFile /F *> $null
                Say '还原：计划任务 DesktopCalendar 已按测试前 XML 重建'
            } catch {
                Say ('还原失败：计划任务 → ' + $_.Exception.GetType().Name + ' ' + $_.Exception.Message)
            }
        } else {
            Say '还原失败：计划任务 DesktopCalendar 已丢失且无 XML 备份'
        }
    } else {
        Say '还原：计划任务 DesktopCalendar 未被改动'
    }
} else {
    if ($taskAfter) {
        try {
            & schtasks.exe /Delete /TN $taskName /F *> $null
            Say '清理：已删除测试残留的计划任务 DesktopCalendar'
        } catch {
            Say ('清理失败：计划任务 → ' + $_.Exception.GetType().Name + ' ' + $_.Exception.Message)
        }
    } else {
        Say '清理：计划任务 DesktopCalendar 本就无残留'
    }
}

# ---------- 5) 残留验证 ----------
Say '=== 残留验证 ==='
$runFinal = Read-RunValue
$cmdFinal = Test-Path -LiteralPath $startupCmd
$taskFinal = Task-Exists
$runFinalText = if ($null -eq $runFinal) { '当前无值' } else { '当前值=' + $runFinal }
Check 'Z1 真机 Run\DesktopCalendar 已还原（无新增残留）' (($null -eq $runBefore -and $null -eq $runFinal) -or ($runBefore -eq $runFinal)) $runFinalText
Check 'Z2 真机启动文件夹 DesktopCalendar.cmd 已还原' ($cmdBeforeExists -eq $cmdFinal) ('现在存在=' + $cmdFinal)
Check 'Z3 真机计划任务 DesktopCalendar 已还原' ($taskBefore -eq $taskFinal) ('现在存在=' + $taskFinal)

# ---------- 6) 汇总 ----------
$totalPass = $probePass
$totalFail = $probeFail + $script:cleanFail
Say ('探针断言：PASS=' + $probePass + ' FAIL=' + $probeFail + '；真机清理断言：PASS=' + (3 - $script:cleanFail) + ' FAIL=' + $script:cleanFail)
Say ('SUMMARY PASS=' + $totalPass + ' FAIL=' + $totalFail)
if ($totalFail -gt 0) { exit 1 } else { exit 0 }
