# tests/

这里的文件都**不参与** `build.ps1` 的 exe 构建，只用于自检与预览截图生成。

## `LogicTests.cs` —— 纯逻辑自检

覆盖 `Store` / `RepeatRule` / `Json` / `Scheduler` / `AutoStart`，不碰界面，单独编译就能跑完：

```powershell
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:exe /platform:anycpu /codepage:65001 /out:_scratch\LogicTests.exe `
  /r:System.dll /r:System.Core.dll `
  src\Models\Schedule.cs src\Models\RepeatRule.cs `
  src\Data\Json.cs src\Data\Store.cs `
  src\Services\AutoStart.cs src\Services\Scheduler.cs `
  tests\LogicTests.cs
.\_scratch\LogicTests.exe
```

退出码 0 即全部通过；输出末尾会打印 `通过 N 项 / 失败 0 项`。

> 有两条断言会被标 `SKIP`：它们要真正写 `HKCU\...\Run`。在受限/沙箱进程里写注册表会被系统拒绝，此时自检会说明原因并跳过，不算失败。

## `PreviewMonth.cs` / `PreviewParts.cs` —— 界面预览与像素自检

它们编译时需要一个最小 **stub**（`ScheduleItem`、`Store` 等类型的测试替身），存根放在 `_scratch/` 下，而 `_scratch/` 被 `.gitignore` 排除，所以**从仓库直接拉下来是编不过的**。

想复现 `preview/*.png`：在 `_scratch/` 里自备 stub 后，用 csc 单独编译「被测的 `src\UI\*.cs` + stub + 这两个文件」，运行即会把 PNG 重新画出来。
