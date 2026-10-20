using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DesktopCalendar.Data;
using DesktopCalendar.Services;
using DesktopCalendar.UI;

namespace DesktopCalendar
{
    internal static class Program
    {
        private const string MutexName = "Local\\DesktopCalendar.SingleInstance";
        private const string ShowEventName = "Local\\DesktopCalendar.ShowMain";
        private const string SilentFlag = "--silent";

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                SetProcessDPIAware();
            }
            catch (Exception)
            {
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool silent = HasFlag(args, SilentFlag);

            string dataDir = null;
            try
            {
                dataDir = Store.ResolveDataDir(args);
            }
            catch (Exception)
            {
                dataDir = null;
            }

            Mutex mutex = null;
            bool createdNew = true;
            try
            {
                mutex = new Mutex(true, MutexName, out createdNew);
            }
            catch (Exception)
            {
                createdNew = true;
            }

            if (!createdNew)
            {
                SignalExistingInstance();
                if (mutex != null)
                {
                    try { mutex.Dispose(); }
                    catch (Exception) { }
                }
                return;
            }

            InstallExceptionHandlers(dataDir);

            CalendarContext context = null;
            try
            {
                context = new CalendarContext(dataDir, silent);
                Application.Run(context);
            }
            catch (Exception ex)
            {
                LogError(dataDir, ex);
            }
            finally
            {
                if (context != null)
                {
                    try { context.Shutdown(); }
                    catch (Exception) { }
                }
                if (mutex != null)
                {
                    try { mutex.ReleaseMutex(); }
                    catch (Exception) { }
                    try { mutex.Dispose(); }
                    catch (Exception) { }
                }
            }
        }

        // ---------------------------------------------------------------- 单实例

        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == null) continue;
                if (string.Equals(a.Trim(), flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>通知已在运行的实例把主窗口显示出来，然后本进程立即退出。</summary>
        private static void SignalExistingInstance()
        {
            try
            {
                EventWaitHandle handle;
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out handle))
                {
                    using (handle)
                    {
                        handle.Set();
                    }
                    return;
                }
            }
            catch (Exception)
            {
            }

            try
            {
                using (EventWaitHandle created = new EventWaitHandle(false,
                    EventResetMode.AutoReset, ShowEventName))
                {
                    created.Set();
                }
            }
            catch (Exception)
            {
            }
        }

        // ---------------------------------------------------------------- 兜底

        private static void InstallExceptionHandlers(string dataDir)
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            }
            catch (Exception)
            {
            }

            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                Exception ex = e == null ? null : e.Exception;
                LogError(dataDir, ex);
                string msg = ex == null ? "未知错误" : ex.Message;
                try
                {
                    MessageBox.Show("程序遇到一个错误，但已继续运行。\r\n\r\n" + msg, "桌面日历",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception)
                {
                }
            };

            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Exception ex = e == null ? null : e.ExceptionObject as Exception;
                LogError(dataDir, ex);
                string msg = ex == null ? "未知错误" : ex.Message;
                try
                {
                    MessageBox.Show("程序遇到无法恢复的错误，即将退出。\r\n\r\n" + msg, "桌面日历",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception)
                {
                }
            };
        }

        internal static void LogError(string dataDir, Exception ex)
        {
            if (ex == null) return;

            string body = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}\r\n{2}\r\n\r\n",
                DateTime.Now, ex.GetType().FullName, ex.ToString());

            string temp = null;
            try
            {
                temp = Path.Combine(Path.GetTempPath(), "DesktopCalendar-error.log");
            }
            catch (Exception)
            {
                temp = null;
            }
            if (TryAppend(temp, body)) return;

            string dir = dataDir;
            if (string.IsNullOrEmpty(dir))
            {
                try
                {
                    dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "DesktopCalendar");
                }
                catch (Exception)
                {
                    dir = null;
                }
            }
            if (string.IsNullOrEmpty(dir)) return;
            TryAppend(Path.Combine(dir, "error.log"), body);
        }

        private static bool TryAppend(string path, string body)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.AppendAllText(path, body, Encoding.UTF8);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>没有主窗体的应用上下文：持有 store / scheduler / tray / MainForm。</summary>
    internal sealed class CalendarContext : ApplicationContext
    {
        private readonly Store _store;
        private readonly Scheduler _scheduler;
        private readonly TrayController _tray;
        private readonly MainForm _main;
        private readonly SynchronizationContext _sync;
        private readonly EventWaitHandle _showEvent;
        private readonly Thread _waiter;
        private volatile bool _stopping;
        private bool _shutdownDone;

        public CalendarContext(string dataDir, bool silent)
        {
            _sync = SynchronizationContext.Current;

            _store = new Store(dataDir);

            // 首次运行即落盘，保证数据文件立即可见（Save 内部已吞掉 IO 异常）。
            try
            {
                if (!File.Exists(_store.DataFilePath)) _store.Save();
            }
            catch (Exception)
            {
            }

            _scheduler = new Scheduler(_store);
            _tray = new TrayController(_store);

            _main = new MainForm(_store, _scheduler);
            _tray.Bind(_main);
            _tray.ShowMainRequested += ShowMain;
            _tray.ExitRequested += Shutdown;

            // 提前建句柄：--silent 下窗体不显示，但提醒卡片仍需消息泵切回 UI 线程。
            IntPtr forced = _main.Handle;
            GC.KeepAlive(forced);

            try
            {
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ProgramShowEventName);
            }
            catch (Exception)
            {
            }

            _scheduler.Start();

            if (!silent) ShowMain();

            _waiter = new Thread(WaitLoop);
            _waiter.IsBackground = true;
            _waiter.Name = "DesktopCalendar.ShowWaiter";
            _waiter.Start();
        }

        private const string ProgramShowEventName = "Local\\DesktopCalendar.ShowMain";

        public void ShowMain()
        {
            if (_main == null || _main.IsDisposed) return;
            try
            {
                if (!_main.Visible) _main.Show();
                if (_main.WindowState == FormWindowState.Minimized)
                {
                    _main.WindowState = FormWindowState.Normal;
                }
                _main.Show();
                _main.Activate();
                _main.BringToFront();
                _main.Refresh();
            }
            catch (Exception)
            {
            }
        }

        private void WaitLoop()
        {
            while (!_stopping)
            {
                bool signaled;
                try
                {
                    signaled = _showEvent != null && _showEvent.WaitOne(200);
                }
                catch (Exception)
                {
                    return;
                }
                if (_stopping) return;
                if (!signaled) continue;

                SynchronizationContext sc = _sync;
                if (sc != null)
                {
                    try
                    {
                        sc.Post(delegate(object state) { ShowMain(); }, null);
                        continue;
                    }
                    catch (Exception)
                    {
                    }
                }
                try
                {
                    if (_main != null && _main.IsHandleCreated)
                    {
                        _main.BeginInvoke((MethodInvoker)delegate { ShowMain(); });
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        public void Shutdown()
        {
            if (_shutdownDone) return;
            _shutdownDone = true;
            _stopping = true;

            try { if (_scheduler != null) _scheduler.Stop(); }
            catch (Exception) { }
            try { if (_store != null) _store.Save(); }
            catch (Exception) { }
            try { if (_showEvent != null) _showEvent.Set(); }
            catch (Exception) { }
            try
            {
                if (_tray != null)
                {
                    _tray.PrepareExit();
                    _tray.Dispose();
                }
            }
            catch (Exception) { }
            try
            {
                if (_main != null && !_main.IsDisposed) _main.Hide();
            }
            catch (Exception) { }
            try { ExitThread(); }
            catch (Exception) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stopping = true;
                try { if (_showEvent != null) _showEvent.Set(); }
                catch (Exception) { }
                Thread t = _waiter;
                if (t != null)
                {
                    try { t.Join(300); }
                    catch (Exception) { }
                }
                try { if (_showEvent != null) _showEvent.Close(); }
                catch (Exception) { }
                try
                {
                    if (_tray != null) _tray.Dispose();
                }
                catch (Exception) { }
                try
                {
                    if (_main != null && !_main.IsDisposed) _main.Dispose();
                }
                catch (Exception) { }
            }
            base.Dispose(disposing);
        }
    }
}
