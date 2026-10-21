# 统一自检入口：编译并运行全部 C# 自检与注入式 PowerShell 自检。
# 用法： powershell -NoProfile -ExecutionPolicy Bypass -File tests\run_all_tests.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outDir = Join-Path $root '_scratch\build'
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$srcFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -Filter *.cs -File |
    Where-Object { $_.Name -ne 'Program.cs' } |
    ForEach-Object { $_.FullName })

$results = @()

function Invoke-CSharpTest([string]$name, [string]$testFile) {
    Write-Host ''
    Write-Host ('=== ' + $name + ' ===') -ForegroundColor Cyan
    $exe = Join-Path $outDir ($name + '.exe')
    $args = @('/nologo', '/target:exe', '/platform:anycpu', '/codepage:65001', ('/out:' + $exe),
        '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll') + $srcFiles + @((Join-Path $root (Join-Path 'tests' $testFile)))
    $compile = & $csc @args 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        Write-Host $compile -ForegroundColor Red
        $script:results += ('{0}: COMPILE_FAIL' -f $name)
        return
    }
    $output = & $exe 2>&1 | Out-String
    $code = $LASTEXITCODE
    $lines = ($output -split "`r?`n") | Where-Object { $_ -match 'RESULT|SUMMARY|通过|PASS|FAIL' }
    $lines | ForEach-Object { Write-Host ('  ' + $_) }
    if ($code -eq 0) { $script:results += ('{0}: PASS' -f $name) } else { $script:results += ('{0}: FAIL(exit {1})' -f $name, $code) }
}

function Invoke-PsTest([string]$name, [string]$relative) {
    Write-Host ''
    Write-Host ('=== ' + $name + ' ===') -ForegroundColor Cyan
    $path = Join-Path $root $relative
    $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $path 2>&1 | Out-String
    $code = $LASTEXITCODE
    $lines = ($output -split "`r?`n") | Where-Object { $_ -match 'RESULT|SUMMARY|通过|PASS|FAIL' } | Select-Object -Last 8
    $lines | ForEach-Object { Write-Host ('  ' + $_) }
    if ($code -eq 0) { $script:results += ('{0}: PASS' -f $name) } else { $script:results += ('{0}: FAIL(exit {1})' -f $name, $code) }
}

Invoke-CSharpTest 'LogicTests' 'LogicTests.cs'
Invoke-CSharpTest 'TipTests' 'TipTests.cs'
Invoke-CSharpTest 'TrayFixTests' 'TrayFixTests.cs'
Invoke-CSharpTest 'SoundTests' 'SoundTests.cs'
Invoke-PsTest 'TrayV3' 'tests\tray_v3_tests.ps1'
Invoke-PsTest 'AutoStartV2' 'tests\autostart_v2_tests.ps1'

Write-Host ''
Write-Host '=== 汇总 ===' -ForegroundColor Cyan
$results | ForEach-Object { Write-Host ('  ' + $_) }
$failed = @($results | Where-Object { $_ -notmatch ': PASS' })
if ($failed.Count -eq 0) { Write-Host 'ALL SUITES PASS' -ForegroundColor Green; exit 0 }
Write-Host ('FAILED SUITES: ' + $failed.Count) -ForegroundColor Red
exit 1
