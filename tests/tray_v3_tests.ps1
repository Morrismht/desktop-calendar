# tray_v3_tests.ps1 -- TrayController v3 (own Shell_NotifyIcon + injectable shell channel).
# Compiles every src\*.cs (except Program.cs) together with a probe, so the probe can use
# internal members directly. No assertion depends on the real tray: the shell channel is
# injected through TrayController.Shell (fake implementations), the real channel is only
# constructed to prove it does not throw.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$buildDir = Join-Path $root '_scratch\build'
if (-not (Test-Path -LiteralPath $buildDir)) { New-Item -ItemType Directory -Path $buildDir -Force | Out-Null }
$probeCs = Join-Path $root '_scratch\TrayV3Probe.cs'
$exe = Join-Path $buildDir 'TrayV3Probe.exe'
if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }

$probe = @'
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using DesktopCalendar.Data;
using DesktopCalendar.Services;
using DesktopCalendar.UI;

// Fake shell channel: simulates NIM_ADD / GetRect entirely in-process.
internal sealed class FakeShell : TrayController.ITrayShellChannel
{
    public bool AddOk;
    public int AddError;
    public int AddCalls;
    private bool _ownPresent;

    public bool AddIcon(IntPtr hwnd, int id, Guid guid, IntPtr hicon, string tip, int callbackMessage, out int errorCode)
    {
        AddCalls++;
        if (AddOk) { _ownPresent = true; errorCode = 0; return true; }
        _ownPresent = false;
        errorCode = AddError;
        return false;
    }

    public bool SetVersion(IntPtr hwnd, int id, Guid guid, int version, out int errorCode)
    {
        errorCode = AddOk ? 0 : AddError;
        return AddOk;
    }

    public bool SetTip(IntPtr hwnd, int id, Guid guid, string tip, out int errorCode)
    {
        errorCode = AddOk ? 0 : AddError;
        return AddOk;
    }

    public bool ShowInfo(IntPtr hwnd, int id, Guid guid, string title, string body, int infoFlags, out int errorCode)
    {
        errorCode = AddOk ? 0 : AddError;
        return AddOk;
    }

    public bool DeleteIcon(IntPtr hwnd, int id, Guid guid, out int errorCode)
    {
        _ownPresent = false;
        errorCode = 0;
        return true;
    }

    public bool IsPresent(IntPtr hwnd, int id, Guid guid)
    {
        // The NotifyIcon slot is always reported absent so the self-registration path is exercised.
        if (guid == TrayController.OwnSlotGuid) return _ownPresent;
        return false;
    }
}

internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static void Check(string name, bool ok, string detail)
    {
        if (ok) { _pass++; Console.WriteLine("PASS  " + name); }
        else { _fail++; Console.WriteLine("FAIL  " + name + " :: " + detail); }
    }

    private static void InvokeMinimize(Form f)
    {
        MethodInfo mi = typeof(MainForm).GetMethod("MinimizeToTray", BindingFlags.Instance | BindingFlags.NonPublic);
        if (mi == null) throw new InvalidOperationException("MinimizeToTray not found on MainForm");
        mi.Invoke(f, null);
    }

    private static long FileLen(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch (Exception) { return 0; }
    }

    private static string ReadFrom(string path, long offset)
    {
        try
        {
            if (!File.Exists(path)) return "";
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (offset > fs.Length) offset = 0;
                fs.Seek(offset, SeekOrigin.Begin);
                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8)) return sr.ReadToEnd();
            }
        }
        catch (Exception) { return ""; }
    }

    private static string Escape(string s)
    {
        if (s == null) return "";
        StringBuilder sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < 128) sb.Append(c);
            else sb.Append("\\u").Append(((int)c).ToString("x4"));
        }
        return sb.ToString();
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine("EXCEPTION " + ex.GetType().FullName);
            Console.WriteLine("MSG " + Escape(ex.Message));
            Console.WriteLine("STACK " + Escape(ex.StackTrace));
            return 99;
        }
    }

    private static int Run(string[] args)
    {
        Application.EnableVisualStyles();
        // The store dir is created by the caller: this probe is not allowed to create dirs.
        string dir = args != null && args.Length > 0 ? args[0] : Application.StartupPath;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        Store store = new Store(dir);
        Scheduler sched = new Scheduler(store);
        // A modal MessageBox would hang this non-interactive probe: swallow every notice.
        TrayController.NoticeSink = delegate(string m) { };
        // This probe is denied writes under %APPDATA% by the harness: aim the log at the scratch dir.
        DiagnosticLog.PathOverride = Path.Combine(dir, "diag_selftest.log");
        Console.WriteLine("LOG override: " + DiagnosticLog.LogPath);

        // ---------------- A: RegistrationOk must be the real shell result
        FakeShell okShell = new FakeShell();
        okShell.AddOk = true;
        TrayController.Shell = okShell;
        TrayController good = new TrayController(store);
        bool a1 = good.RegistrationOk && okShell.AddCalls >= 1;
        Check("A1 RegistrationOk reflects the injected NIM_ADD result (success)", a1,
            "reg=" + good.RegistrationOk + " addCalls=" + okShell.AddCalls + " diag=" + good.DiagnosticLine());

        string diag = good.DiagnosticLine();
        bool a2 = diag.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
        Check("A2 DiagnosticLine() contains the exe path", a2, diag);

        FakeShell badShell = new FakeShell();
        badShell.AddOk = false;
        badShell.AddError = 5;
        TrayController.Shell = badShell;
        TrayController broken = new TrayController(store);
        bool a3 = !broken.RegistrationOk && broken.LastWin32Error == 5 && broken.AddFailures >= 1;
        Check("A3 injected NIM_ADD failure(err=5) -> RegistrationOk=false, LastWin32Error=5, AddFailures>=1", a3,
            "reg=" + broken.RegistrationOk + " err=" + broken.LastWin32Error + " fails=" + broken.AddFailures);

        // ---------------- B: EnsureVisible is idempotent
        TrayController.Shell = okShell;
        int failBefore = good.AddFailures;
        int addBefore = okShell.AddCalls;
        bool threw = false;
        try { good.EnsureVisible(); good.EnsureVisible(); }
        catch (Exception) { threw = true; }
        bool b1 = !threw && good.AddFailures == failBefore && okShell.AddCalls == addBefore;
        Check("B  EnsureVisible() x2: no throw, no redundant NIM_ADD", b1,
            "threw=" + threw + " addFailures " + failBefore + "->" + good.AddFailures + " addCalls " + addBefore + "->" + okShell.AddCalls);

        // ---------------- C: balloon never throws
        bool c1 = false;
        try { good.ShowBalloon("T", "B"); c1 = true; }
        catch (Exception) { c1 = false; }
        bool c2 = true;
        try { broken.ShowBalloon("T", "B"); }
        catch (Exception) { c2 = false; }
        Check("C  ShowBalloon(title,body) does not throw (registered + unregistered)", c1 && c2,
            "registeredOk=" + c1 + " unregisteredOk=" + c2);

        // ---------------- D: tooltip truncation
        good.SetTooltip(new string('X', 200));
        string tip = good.Icon.Text == null ? "" : good.Icon.Text;
        bool d1 = tip.Length <= 63 && tip.Length <= 127;
        Check("D  SetTooltip(200 chars) truncated to <=63 (<=127)", d1, "len=" + tip.Length);

        // PreviewParts.cs:257-264 的托盘契约
        int menuItems = good.Icon.ContextMenuStrip == null ? -1 : good.Icon.ContextMenuStrip.Items.Count;
        bool d2 = good.Icon.Icon != null && menuItems >= 4;
        Check("D2 PreviewParts contract: Icon.Icon != null, menu items >= 4", d2,
            "iconNull=" + (good.Icon.Icon == null) + " items=" + menuItems);

        // ---------------- E: MinimizeToTray decision
        bool e1 = MainForm.ShouldHideOnMinimize(true) && !MainForm.ShouldHideOnMinimize(false);
        Check("E1 ShouldHideOnMinimize(true)=true, (false)=false", e1, "");

        FakeShell okShell2 = new FakeShell();
        okShell2.AddOk = true;
        TrayController.Shell = okShell2;
        TrayController good2 = new TrayController(store);
        TrayController.ResetNoticeForTests();
        int tryAddsBefore = TrayController.TryAddCalls;
        MainForm formOk = new MainForm(store, sched);
        formOk.Show();
        InvokeMinimize(formOk);
        int tryAdds = TrayController.TryAddCalls - tryAddsBefore;
        bool e2 = !formOk.Visible && tryAdds == 1;
        Check("E2 registration ok -> window hidden, TryAdd() called exactly once", e2,
            "visible=" + formOk.Visible + " windowState=" + formOk.WindowState + " tryAdds=" + tryAdds);
        formOk.Dispose();

        FakeShell badShell2 = new FakeShell();
        badShell2.AddOk = false;
        badShell2.AddError = 5;
        TrayController.Shell = badShell2;
        TrayController broken2 = new TrayController(store);
        TrayController.ResetNoticeForTests();
        string logPath = DiagnosticLog.LogPath;
        long logStart = FileLen(logPath);
        MainForm formBad = new MainForm(store, sched);
        formBad.Show();
        InvokeMinimize(formBad);
        bool keptOpen = formBad.Visible
            && formBad.WindowState == FormWindowState.Minimized
            && formBad.ShowInTaskbar;
        InvokeMinimize(formBad);
        bool noticeOnce = TrayController.NoticeCount == 1;
        string added = ReadFrom(logPath, logStart);
        bool logged = added.IndexOf("lastErr=5", StringComparison.Ordinal) >= 0;
        bool noticeHasCode = TrayController.LastNoticeText.IndexOf("5", StringComparison.Ordinal) >= 0;
        Check("E3 registration failed -> stays minimized + in taskbar, notice only once, log carries err=5",
            keptOpen && noticeOnce && logged && noticeHasCode,
            "keptOpen=" + keptOpen + " state=" + formBad.WindowState + " visible=" + formBad.Visible
            + " inTaskbar=" + formBad.ShowInTaskbar + " noticeCount=" + TrayController.NoticeCount
            + " logged=" + logged + " noticeHasCode=" + noticeHasCode + " logPath=" + logPath);
        formBad.Dispose();

        // ---------------- F: DiagnosticLog
        string token = "trayv3-" + Guid.NewGuid().ToString("N");
        bool wrote = DiagnosticLog.Write(token);
        string content = File.Exists(DiagnosticLog.LogPath) ? File.ReadAllText(DiagnosticLog.LogPath) : "";
        bool f1 = content.IndexOf(token, StringComparison.Ordinal) >= 0;
        Check("F1 DiagnosticLog.Write appends a line that can be read back", f1,
            "logPath=" + DiagnosticLog.LogPath + " exists=" + File.Exists(DiagnosticLog.LogPath) + " len=" + content.Length
            + " wrote=" + wrote + " err=" + (DiagnosticLog.LastError == null ? "-" : DiagnosticLog.LastError));

        bool f2 = true;
        try { for (int i = 0; i < 300; i++) DiagnosticLog.Write("trayv3 spam " + i); }
        catch (Exception) { f2 = false; }
        Check("F2 300 DiagnosticLog.Write calls do not throw", f2, "");

        // ---------------- G: the real shell channel must at least run without throwing
        TrayController.Shell = null;
        TrayController real = null;
        bool g1 = false;
        string realDiag = "";
        try
        {
            real = new TrayController(store);
            realDiag = real.DiagnosticLine();
            g1 = true;
        }
        catch (Exception ex)
        {
            realDiag = ex.GetType().Name + ": " + ex.Message;
        }
        Console.WriteLine("REAL DiagnosticLine: " + realDiag);
        Check("G  real Win32 shell channel constructs and reports without throwing", g1, realDiag);
        if (real != null) real.Dispose();
        good.Dispose();
        broken.Dispose();
        good2.Dispose();
        broken2.Dispose();

        string overridePath = DiagnosticLog.LogPath;
        DiagnosticLog.PathOverride = null;
        bool realWrite = DiagnosticLog.Write("trayv3 real-path probe");
        Console.WriteLine("REAL log write=" + realWrite + " path=" + DiagnosticLog.LogPath
            + " err=" + (DiagnosticLog.LastError == null ? "-" : DiagnosticLog.LastError));
        DiagnosticLog.PathOverride = overridePath;

        Console.WriteLine("PASS " + _pass + " / FAIL " + _fail);
        return _fail == 0 ? 0 : 1;
    }
}
'@

[System.IO.File]::WriteAllText($probeCs, $probe, (New-Object System.Text.UTF8Encoding($true)))

$src = Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs |
    Where-Object { $_.Name -ne 'Program.cs' } |
    ForEach-Object { $_.FullName }
$cscArgs = @(
    '/nologo', '/target:exe', '/platform:anycpu', '/codepage:65001', '/langversion:5', '/warn:0',
    ('/out:' + $exe),
    '/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Core.dll'
)
& $csc @cscArgs @($src + $probeCs) 2>&1 | Select-Object -First 6 | ForEach-Object { "  csc: $_" }
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host '=== tray v3 BUILD FAILED'
    exit 1
}

& $exe $buildDir
$exit = $LASTEXITCODE
if ($null -eq $exit) { $exit = 0 }
exit $exit
