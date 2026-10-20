using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using DesktopCalendar.Data;
using DesktopCalendar.UI;

namespace DesktopCalendar.Tests
{
    /// <summary>
    /// 托盘图标修复的结构性回归测试（不依赖真实 explorer 交互）。
    /// 覆盖：图标句柄非零且不是空白图 / 自绘 HICON 所有权 / EnsureVisible 幂等 /
    /// TaskbarCreated 消息 id 与恢复 / Bind 后隐藏触发 EnsureVisible / 公开契约不变。
    /// </summary>
    internal static class TrayFixTests
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string name);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetIconInfo(IntPtr hIcon, out IconInfo info);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct IconInfo
        {
            public bool IsIcon;
            public int HotspotX;
            public int HotspotY;
            public IntPtr MaskBitmap;
            public IntPtr ColorBitmap;
        }

        private static int _pass;
        private static int _fail;
        private static readonly List<string> _failed = new List<string>();
        private static StringBuilder _log;
        private static string _logPath;
        private static string _tmpRoot;

        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); }
            catch (Exception) { }

            _log = new StringBuilder();
            _logPath = args != null && args.Length > 0 && args[0] != null ? args[0] : "trayfix-tests.log";
            _tmpRoot = Path.Combine(Path.GetTempPath(), "dc-trayfix-" + Guid.NewGuid().ToString("N"));
            try { Directory.CreateDirectory(_tmpRoot); }
            catch (Exception)
            {
                _tmpRoot = Path.Combine(Environment.CurrentDirectory, "_trayfix-tmp-" + Guid.NewGuid().ToString("N"));
                try { Directory.CreateDirectory(_tmpRoot); }
                catch (Exception) { }
            }

            Line("== TrayController 托盘图标修复回归测试 ==");
            Line("exe = " + Application.ExecutablePath);
            Line("tmp = " + _tmpRoot);
            Line("");

            Run("① 托盘图标句柄非零", TestIconHandleNonZero);
            Run("② 图标句柄有效且位图不空白(不透明像素>=50)", TestIconNotBlank);
            Run("③ 构造-销毁-再构造不抛异常", TestConstructDisposeConstruct);
            Run("③b 自绘路径持有 HICON 到 Dispose 才销毁", TestSelfDrawnOwnership);
            Run("④ EnsureVisible 连调 3 次后仍可见(幂等)", TestEnsureVisibleIdempotent);
            Run("⑤ TaskbarCreated 消息 id 与系统一致", TestTaskbarCreatedId);
            Run("⑥ Bind 后窗体 Hide 触发 EnsureVisible", TestBindHideTriggersEnsureVisible);
            Run("⑦ 收到 TaskbarCreated 后图标自动恢复", TestTaskbarCreatedRestoresIcon);
            Run("⑧ Hide/Show 50 次图标句柄始终有效", TestHideShowKeepsHandle);
            Run("⑨ 公开契约(成员签名)未被改动", TestPublicContract);

            Line("");
            Line(string.Format("RESULT {0} PASS / {1} FAIL", _pass, _fail));
            if (_fail > 0)
            {
                for (int i = 0; i < _failed.Count; i++) Line("  FAILED: " + _failed[i]);
            }

            WriteFile();
            try { if (Directory.Exists(_tmpRoot)) Directory.Delete(_tmpRoot, true); }
            catch (Exception) { }
            return _fail == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------ 用例

        private static void TestIconHandleNonZero()
        {
            Store store = NewStore();
            using (TrayController tray = new TrayController(store))
            {
                Assert(tray.Icon != null, "NotifyIcon 实例为 null");
                Assert(tray.Icon.Icon != null, "NotifyIcon.Icon 为 null");
                IntPtr h = tray.Icon.Icon.Handle;
                Line("    icon handle = " + h.ToInt64() + " fromBranding=" + tray.IconFromBranding);
                Assert(h != IntPtr.Zero, "Icon.Handle == IntPtr.Zero（句柄已失效）");
            }
        }

        private static void TestIconNotBlank()
        {
            Store store = NewStore();
            using (TrayController tray = new TrayController(store))
            {
                IntPtr h = tray.Icon.Icon.Handle;
                Assert(h != IntPtr.Zero, "句柄为 0，无法继续");

                IconInfo info;
                bool ok = GetIconInfo(h, out info);
                Line("    GetIconInfo = " + ok);
                Assert(ok, "GetIconInfo 失败 → 句柄无效");
                if (info.MaskBitmap != IntPtr.Zero) DeleteObject(info.MaskBitmap);
                if (info.ColorBitmap != IntPtr.Zero) DeleteObject(info.ColorBitmap);

                int opaque;
                int w, ht;
                CountOpaque(h, out opaque, out w, out ht);
                Line(string.Format("    toBitmap {0}x{1} opaque = {2}", w, ht, opaque));
                Assert(opaque >= 50, "不透明像素只有 " + opaque + "（图标是空白的）");
            }
        }

        private static void TestConstructDisposeConstruct()
        {
            {
                Store store = NewStore();
                TrayController a = new TrayController(store);
                IntPtr ha = a.Icon.Icon.Handle;
                a.Dispose();
                Line("    first handle = " + ha.ToInt64() + " (disposed)");

                TrayController b = new TrayController(store);
                IntPtr hb = b.Icon.Icon.Handle;
                Assert(hb != IntPtr.Zero, "第二次构造后句柄为 0");
                IconInfo info;
                Assert(GetIconInfo(hb, out info), "第二次构造的图标句柄无效");
                if (info.MaskBitmap != IntPtr.Zero) DeleteObject(info.MaskBitmap);
                if (info.ColorBitmap != IntPtr.Zero) DeleteObject(info.ColorBitmap);
                b.Dispose();
                Line("    second handle = " + hb.ToInt64() + " ok");
            }
        }

        private static void TestSelfDrawnOwnership()
        {
            bool old = TrayController.ForceSelfDrawnIcon;
            TrayController.ForceSelfDrawnIcon = true;
            try
            {
                {
                    Store store = NewStore();
                    TrayController tray = new TrayController(store);
                    IntPtr own = tray.OwnedHicon;
                    IntPtr handle = tray.Icon.Icon.Handle;
                    Line(string.Format("    ownHicon = {0} icon.Handle = {1} fromBranding = {2}",
                        own.ToInt64(), handle.ToInt64(), tray.IconFromBranding));
                    Assert(!tray.IconFromBranding, "强制自绘时仍走了 Branding 分支");
                    Assert(own != IntPtr.Zero, "自绘路径没有持有 HICON（_ownHicon == 0）");
                    Assert(own == handle, "持有的 HICON 与托盘在用句柄不一致（说明被 clone/销毁过）");

                    IconInfo info;
                    Assert(GetIconInfo(handle, out info), "自绘图标句柄无效");
                    if (info.MaskBitmap != IntPtr.Zero) DeleteObject(info.MaskBitmap);
                    if (info.ColorBitmap != IntPtr.Zero) DeleteObject(info.ColorBitmap);

                    int opaque, w, ht;
                    CountOpaque(handle, out opaque, out w, out ht);
                    Line(string.Format("    self-drawn {0}x{1} opaque = {2}", w, ht, opaque));
                    Assert(opaque >= 50, "自绘图标不透明像素只有 " + opaque);

                    tray.Dispose();
                    Line(string.Format("    after Dispose: ownHicon = {0} destroyed = {1}",
                        tray.OwnedHicon.ToInt64(), tray.DestroyedHicons));
                    Assert(tray.OwnedHicon == IntPtr.Zero, "Dispose 后仍持有 HICON");
                    Assert(tray.DestroyedHicons == 1, "DestroyIcon 调用次数 = " + tray.DestroyedHicons + "（应为 1）");
                }
            }
            finally
            {
                TrayController.ForceSelfDrawnIcon = old;
            }
        }

        private static void TestEnsureVisibleIdempotent()
        {
            Store store = NewStore();
            using (TrayController tray = new TrayController(store))
            {
                tray.EnsureVisible();
                tray.EnsureVisible();
                tray.EnsureVisible();
                Line("    visibleEnsures = " + tray.VisibleEnsures + " Visible = " + tray.Icon.Visible);
                Assert(tray.VisibleEnsures == 3, "EnsureVisible 计数 = " + tray.VisibleEnsures);
                Assert(tray.Icon.Visible, "连调 3 次后 NotifyIcon.Visible != true");
                Assert(tray.Icon.Icon != null && tray.Icon.Icon.Handle != IntPtr.Zero, "EnsureVisible 后句柄失效");
            }
        }

        private static void TestTaskbarCreatedId()
        {
            int sys = RegisterWindowMessage("TaskbarCreated");
            int mine = TrayController.TaskbarCreatedMessageId;
            Line("    system = " + sys + " controller = " + mine);
            Assert(sys != 0, "系统 RegisterWindowMessage 返回 0");
            Assert(mine == sys, "TaskbarCreated 消息 id 不一致");

            Store store = NewStore();
            using (TrayController tray = new TrayController(store))
            {
                Line("    message window = " + tray.MessageWindowHandle.ToInt64());
                Assert(tray.MessageWindowHandle != IntPtr.Zero, "隐藏消息窗口未创建");
            }
        }

        private static void TestBindHideTriggersEnsureVisible()
        {
            Store store = NewStore();
            using (TrayController tray = new TrayController(store))
            using (Form f = new Form())
            {
                f.ShowInTaskbar = false;
                f.WindowState = FormWindowState.Minimized;
                f.Show();
                tray.Bind(f);
                int before = tray.VisibleEnsures;
                f.Hide();
                int after = tray.VisibleEnsures;
                Line("    ensures before = " + before + " after = " + after + " visible = " + tray.Icon.Visible);
                Assert(after == before + 1, "Hide() 没有触发 EnsureVisible（" + before + " -> " + after + "）");
                Assert(tray.Icon.Visible, "Hide() 后托盘不可见");
                tray.Bind(null);
            }
        }

        private static void TestTaskbarCreatedRestoresIcon()
        {
            Store store = NewStore();
            using (TrayController tray = new TrayController(store))
            {
                IntPtr hwnd = tray.MessageWindowHandle;
                Assert(hwnd != IntPtr.Zero, "没有消息窗口，无法模拟 TaskbarCreated");

                // 模拟壳层重启：先手动把托盘图标撤掉
                tray.Icon.Visible = false;
                Assert(!tray.Icon.Visible, "前置条件失败：图标应已隐藏");

                SendMessage(hwnd, TrayController.TaskbarCreatedMessageId, IntPtr.Zero, IntPtr.Zero);
                Line("    recoveries = " + tray.TaskbarRecoveries + " Visible = " + tray.Icon.Visible);
                Assert(tray.TaskbarRecoveries == 1, "TaskbarCreated 未被处理（recoveries = " + tray.TaskbarRecoveries + "）");
                Assert(tray.Icon.Visible, "TaskbarCreated 后图标仍不可见");
                Assert(tray.Icon.Icon != null && tray.Icon.Icon.Handle != IntPtr.Zero, "TaskbarCreated 后图标句柄失效");
            }
        }

        private static void TestHideShowKeepsHandle()
        {
            Store store = NewStore();
            using (TrayController tray = new TrayController(store))
            using (Form f = new Form())
            {
                f.ShowInTaskbar = false;
                f.WindowState = FormWindowState.Minimized;
                f.Show();
                tray.Bind(f);

                IntPtr first = tray.Icon.Icon.Handle;
                int bad = 0;
                for (int i = 0; i < 50; i++)
                {
                    f.Hide();
                    f.Show();
                    IntPtr h = tray.Icon.Icon.Handle;
                    IconInfo info = new IconInfo();
                    bool ok = false;
                    if (h != IntPtr.Zero) ok = GetIconInfo(h, out info);
                    if (ok)
                    {
                        if (info.MaskBitmap != IntPtr.Zero) DeleteObject(info.MaskBitmap);
                        if (info.ColorBitmap != IntPtr.Zero) DeleteObject(info.ColorBitmap);
                    }
                    if (!ok) bad++;
                }
                Line(string.Format("    rounds = 50 handle {0} -> {1} invalid = {2}",
                    first.ToInt64(), tray.Icon.Icon.Handle.ToInt64(), bad));
                Assert(bad == 0, bad + " 次 Hide/Show 后图标句柄失效");
                Assert(tray.Icon.Visible, "Hide/Show 50 次后托盘不可见");
                tray.Bind(null);
            }
        }

        private static void TestPublicContract()
        {
            Type t = typeof(TrayController);

            PropertyInfo icon = t.GetProperty("Icon", BindingFlags.Public | BindingFlags.Instance);
            Assert(icon != null && icon.PropertyType == typeof(NotifyIcon) && icon.CanRead, "NotifyIcon Icon { get; } 缺失");
            Assert(icon.GetGetMethod() != null, "Icon 没有公开 getter");

            Assert(HasMethod(t, "Bind", typeof(void), new Type[] { typeof(Form) }), "void Bind(Form) 签名变了");
            Assert(HasMethod(t, "PrepareExit", typeof(void), new Type[0]), "void PrepareExit() 签名变了");
            Assert(HasMethod(t, "ShowBalloon", typeof(void), new Type[] { typeof(string), typeof(string) }), "void ShowBalloon(string,string) 签名变了");
            Assert(HasMethod(t, "SetTooltip", typeof(void), new Type[] { typeof(string) }), "void SetTooltip(string) 签名变了");
            Assert(HasMethod(t, "Dispose", typeof(void), new Type[0]), "IDisposable.Dispose() 签名变了");
            Assert(HasMethod(t, "EnsureVisible", typeof(void), new Type[0]), "public void EnsureVisible() 缺失");
            Assert(typeof(IDisposable).IsAssignableFrom(t), "TrayController 不再是 IDisposable");

            ConstructorInfo ctor = t.GetConstructor(new Type[] { typeof(Store) });
            Assert(ctor != null, "构造 TrayController(Store) 缺失");

            EventInfo show = t.GetEvent("ShowMainRequested", BindingFlags.Public | BindingFlags.Instance);
            Assert(show != null && show.EventHandlerType == typeof(Action), "event Action ShowMainRequested 缺失");
            EventInfo exit = t.GetEvent("ExitRequested", BindingFlags.Public | BindingFlags.Instance);
            Assert(exit != null && exit.EventHandlerType == typeof(Action), "event Action ExitRequested 缺失");
        }

        // ------------------------------------------------------------ 工具

        private static bool HasMethod(Type t, string name, Type ret, Type[] ps)
        {
            MethodInfo mi = t.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, ps, null);
            return mi != null && mi.ReturnType == ret;
        }

        private static void CountOpaque(IntPtr hIcon, out int opaque, out int width, out int height)
        {
            opaque = 0;
            width = 0;
            height = 0;
            try
            {
                using (Icon probe = Icon.FromHandle(hIcon))
                using (Bitmap bmp = probe.ToBitmap())
                {
                    width = bmp.Width;
                    height = bmp.Height;
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            if (bmp.GetPixel(x, y).A > 128) opaque++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Line("    CountOpaque exception: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static Store NewStore()
        {
            string dir = Path.Combine(_tmpRoot, "s" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try { Directory.CreateDirectory(dir); }
            catch (Exception) { }
            return new Store(dir);
        }

        private static void Assert(bool cond, string message)
        {
            if (cond) return;
            throw new AssertFailed(message);
        }

        private sealed class AssertFailed : Exception
        {
            public AssertFailed(string message) : base(message) { }
        }

        private static void Run(string name, Action body)
        {
            try
            {
                body();
                _pass++;
                Line(string.Format("PASS  {0}", name));
            }
            catch (Exception ex)
            {
                _fail++;
                string msg = ex is AssertFailed ? ex.Message : ex.GetType().Name + ": " + ex.Message;
                _failed.Add(name + " → " + msg);
                Line(string.Format("FAIL  {0} → {1}", name, msg));
                if (!(ex is AssertFailed))
                {
                    Line("      " + (ex.StackTrace == null ? "" : ex.StackTrace.Replace("\n", " | ")));
                }
            }
        }

        private static void Line(string s)
        {
            Console.WriteLine(s);
            _log.AppendLine(s);
        }

        private static void WriteFile()
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(_logPath));
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_logPath, _log.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Console.WriteLine("write log failed: " + ex.Message);
            }
        }
    }
}
