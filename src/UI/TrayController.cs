using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DesktopCalendar.Data;
using DesktopCalendar.Services;

namespace DesktopCalendar.UI
{
    /// <summary>
    /// 托盘图标 + 右键菜单。
    /// 图标优先直接用 Branding.AppIcon（由 exe 自带 ico 派生的 32×32 独立图标、进程级单例）；
    /// 只有它取不到时才回落到自绘，自绘路径的 HICON 由本对象持有到 Dispose 时才销毁。
    /// 另外监听 TaskbarCreated：explorer 重启后自己把图标重新加回托盘。
    /// </summary>
    public class TrayController : IDisposable
    {
        private const int TOOLTIP_MAX = 63;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string name);

        /// <summary>explorer 重建任务栏时广播的消息 id（0 表示注册失败）。</summary>
        internal static readonly int WmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");

        /// <summary>测试钩子：置 true 时跳过 Branding，强制走自绘图标分支。</summary>
        internal static bool ForceSelfDrawnIcon = false;

        private readonly Store _store;

        private NotifyIcon _notify;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _miOpen;
        private ToolStripMenuItem _miToday;
        private ToolStripMenuItem _miAuto;
        private ToolStripMenuItem _miExit;

        private Form _main;
        private bool _exiting;
        private bool _disposed;
        private Icon _appIcon;

        /// <summary>自绘图标的 HICON：只有这个句柄归我们所有，Dispose 时才销毁。</summary>
        private IntPtr _ownHicon = IntPtr.Zero;
        private bool _iconFromBranding;
        private TrayMessageWindow _msgWindow;

        private int _taskbarRecoveries;
        private int _visibleEnsures;
        private int _destroyedHicons;

        private string _tooltip = "桌面日历";
        private string _lastBalloon = "";

        public event Action ShowMainRequested;
        public event Action ExitRequested;

        public TrayController(Store store)
        {
            _store = store;

            _appIcon = BuildAppIcon();

            _menu = new ContextMenuStrip();
            _menu.Font = Theme.Current.Small != null ? Theme.Current.Small : SystemFonts.MenuFont;
            _menu.ShowImageMargin = false;

            _miOpen = new ToolStripMenuItem("打开日历");
            _miOpen.Font = BoldOf(_menu.Font);
            _miOpen.Click += delegate { RaiseShowMain(); };

            _miToday = new ToolStripMenuItem("今日待办(0)");
            _miToday.Click += delegate { RaiseShowMain(); };

            _miAuto = new ToolStripMenuItem("开机自启");
            _miAuto.CheckOnClick = false;
            _miAuto.Click += AutoStartClicked;

            _miExit = new ToolStripMenuItem("退出");
            _miExit.Click += delegate { RequestExit(); };

            _menu.Items.Add(_miOpen);
            _menu.Items.Add(_miToday);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miAuto);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miExit);

            _menu.Opening += delegate { RefreshMenuState(); };

            _notify = new NotifyIcon();
            _notify.Icon = _appIcon;
            _notify.Text = _tooltip;
            _notify.ContextMenuStrip = _menu;
            _notify.Visible = true;
            _notify.DoubleClick += delegate { RaiseShowMain(); };
            _notify.MouseDoubleClick += delegate { RaiseShowMain(); };

            // 隐藏窗口只干一件事：接 TaskbarCreated，explorer 重启后把图标重新加回托盘
            _msgWindow = CreateMessageWindow();

            RefreshMenuState();
        }

        // ---------------------------------------------------------------- 公开成员

        public NotifyIcon Icon { get { return _notify; } }

        /// <summary>主窗口关闭按钮 = 收起到托盘；真正退出时才放行。</summary>
        public void Bind(Form main)
        {
            if (_main != null)
            {
                try { _main.FormClosing -= MainFormClosing; } catch (Exception) { }
                try { _main.VisibleChanged -= MainVisibleChanged; } catch (Exception) { }
                _main = null;
            }

            _main = main;
            if (_main == null) return;
            _main.FormClosing += MainFormClosing;
            _main.VisibleChanged += MainVisibleChanged;
        }

        /// <summary>标记为「真正退出」，之后主窗口的关闭不再被拦截。</summary>
        public void PrepareExit()
        {
            _exiting = true;
        }

        /// <summary>把托盘图标重设一遍（幂等）：可见性掉了或图标句柄丢了都靠它救回来。</summary>
        public void EnsureVisible()
        {
            if (_disposed || _notify == null) return;
            try
            {
                if (_appIcon != null && (_notify.Icon == null || _notify.Icon.Handle != _appIcon.Handle))
                {
                    _notify.Icon = _appIcon;
                }
                if (_notify.Text != _tooltip) _notify.Text = _tooltip;
                if (!_notify.Visible) _notify.Visible = true;
                _visibleEnsures++;
            }
            catch (Exception) { }
        }

        public void ShowBalloon(string title, string text)
        {
            if (_notify == null) return;
            string body = text == null ? "" : text;
            if (body.Length > 250) body = body.Substring(0, 250);
            try
            {
                _notify.ShowBalloonTip(5000, title == null ? "桌面日历" : title, body, ToolTipIcon.Info);
                _lastBalloon = body;
            }
            catch (Exception) { }
        }

        public void SetTooltip(string text)
        {
            string t = text == null ? "" : text.Trim();
            if (t.Length == 0) t = "桌面日历";
            if (t.Length > TOOLTIP_MAX) t = t.Substring(0, TOOLTIP_MAX);
            _tooltip = t;
            if (_notify != null) _notify.Text = t;
        }

        // ---------------------------------------------------------------- 测试钩子

        internal static int TaskbarCreatedMessageId { get { return WmTaskbarCreated; } }
        internal IntPtr OwnedHicon { get { return _ownHicon; } }
        internal bool IconFromBranding { get { return _iconFromBranding; } }
        internal int TaskbarRecoveries { get { return _taskbarRecoveries; } }
        internal int VisibleEnsures { get { return _visibleEnsures; } }
        internal int DestroyedHicons { get { return _destroyedHicons; } }
        internal IntPtr MessageWindowHandle { get { return _msgWindow == null ? IntPtr.Zero : _msgWindow.Handle; } }

        // ---------------------------------------------------------------- 内部

        private static Font BoldOf(Font f)
        {
            if (f == null) return null;
            try { return new Font(f, FontStyle.Bold); }
            catch (Exception) { return f; }
        }

        private void RaiseShowMain()
        {
            Action handler = ShowMainRequested;
            if (handler != null)
            {
                try { handler(); }
                catch (Exception) { }
            }
            else if (_main != null)
            {
                _main.Show();
                _main.WindowState = FormWindowState.Normal;
                _main.Activate();
            }
        }

        private void RaiseExit()
        {
            Action handler = ExitRequested;
            if (handler != null)
            {
                try { handler(); }
                catch (Exception) { }
                return;
            }
            Application.Exit();
        }

        private void RequestExit()
        {
            _exiting = true;
            RaiseExit();
        }

        private void AutoStartClicked(object sender, EventArgs e)
        {
            string error = null;
            bool ok;
            if (AutoStart.IsEnabled)
            {
                ok = AutoStart.TryDisable(out error);
            }
            else
            {
                ok = AutoStart.TryEnable(out error);
            }

            if (!ok)
            {
                string msg = string.IsNullOrEmpty(error) ? "无法修改开机自启设置" : error;
                ShowBalloon("开机自启", msg);
            }
            else
            {
                ShowBalloon("开机自启", AutoStart.IsEnabled ? "已开启，登录 Windows 后静默启动" : "已关闭");
            }
            RefreshMenuState();
        }

        private void RefreshMenuState()
        {
            if (_miAuto != null)
            {
                bool on = false;
                try { on = AutoStart.IsEnabled; }
                catch (Exception) { }
                _miAuto.Checked = on;
            }

            int total = 0;
            int done = 0;
            if (_store != null)
            {
                try
                {
                    total = _store.TotalForDay(DateTime.Today);
                    done = _store.DoneForDay(DateTime.Today);
                }
                catch (Exception) { }
            }

            if (_miToday != null)
            {
                _miToday.Text = string.Format("今日待办({0})", total);
            }

            SetTooltip(string.Format("桌面日历 · 今日 {0}/{1} 已完成", done, total));
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_exiting) return;
            if (e.CloseReason == CloseReason.WindowsShutDown) return;
            if (e.CloseReason == CloseReason.TaskManagerClosing) return;
            if (e.CloseReason == CloseReason.ApplicationExitCall) return;

            e.Cancel = true;
            if (_main != null) _main.Hide();

            if (_lastBalloon.Length == 0)
            {
                ShowBalloon("桌面日历", "已收进右下角托盘，双击图标即可重新打开。\n若看不到图标：点任务栏的 ^ 展开，把日历图标拖到任务栏上就会常驻。");
                _lastBalloon = "hinted";
            }
        }

        /// <summary>主窗口被隐藏（收起到托盘）时，把托盘图标重新确认一遍。</summary>
        private void MainVisibleChanged(object sender, EventArgs e)
        {
            if (_disposed) return;
            Form f = _main;
            if (f == null) return;
            if (!f.Visible) EnsureVisible();
        }

        /// <summary>explorer 重建了任务栏：壳层那边的图标已经没了，重新走一遍 NIM_ADD。</summary>
        private void OnTaskbarCreated()
        {
            if (_disposed || _notify == null) return;
            try
            {
                // NotifyIcon.Visible 的 setter 在值没变时是空操作，
                // 所以必须先置 false 再置 true，才会真的重新 SendNotification(NIM_ADD)。
                if (_notify.Visible) _notify.Visible = false;
                _notify.Icon = _appIcon;
                _notify.Text = _tooltip;
                _notify.ContextMenuStrip = _menu;
                _notify.Visible = true;
                _taskbarRecoveries++;
            }
            catch (Exception) { }
        }

        private TrayMessageWindow CreateMessageWindow()
        {
            if (WmTaskbarCreated == 0) return null;
            try { return new TrayMessageWindow(this); }
            catch (Exception) { return null; }
        }

        // ---------------------------------------------------------------- 图标

        /// <summary>
        /// 优先用 exe 自带图标（Branding.AppIcon，32×32、进程级单例、句柄不归我们销毁）；
        /// 拿不到才回落到 Branding.TrayBitmap，最后才用代码绘制。
        /// </summary>
        private Icon BuildAppIcon()
        {
            if (!ForceSelfDrawnIcon)
            {
                try
                {
                    Icon brand = Branding.AppIcon;
                    if (brand != null && brand.Handle != IntPtr.Zero)
                    {
                        _iconFromBranding = true;
                        return brand;
                    }
                }
                catch (Exception) { }

                try
                {
                    Bitmap tray = Branding.TrayBitmap;
                    if (tray != null)
                    {
                        Icon fromBrand = IconFromBitmapOwned(tray);
                        if (fromBrand != null) return fromBrand;
                    }
                }
                catch (Exception) { }
            }

            return BuildIcon();
        }

        /// <summary>
        /// 位图转 Icon：HICON 由本对象持有（存进 _ownHicon），只到 Dispose 才 DestroyIcon。
        /// 以前的写法是 clone 之后立刻 DestroyIcon(h)，托盘手里那份句柄就悬空了。
        /// </summary>
        private Icon IconFromBitmapOwned(Bitmap bmp)
        {
            if (bmp == null) return null;
            IntPtr h = IntPtr.Zero;
            try
            {
                h = bmp.GetHicon();
                // Icon.FromHandle 得到的 Icon 不拥有句柄，句柄生命期由我们管
                // （成员属性 `Icon` 遮蔽了类型名，所以这里必须写全 System.Drawing.Icon）
                System.Drawing.Icon icon = System.Drawing.Icon.FromHandle(h);
                _ownHicon = h;
                return icon;
            }
            catch (Exception)
            {
                if (h != IntPtr.Zero)
                {
                    try { DestroyIcon(h); }
                    catch (Exception) { }
                }
                return null;
            }
        }

        private Icon BuildIcon()
        {
            Color baseColor = Theme.Current.Accent;
            using (Bitmap bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                    Draw.FillRound(g, new Rectangle(0, 0, 32, 32), 8, baseColor);

                    // 白色日历轮廓：表头 + 两枚挂环
                    Draw.FillRound(g, new Rectangle(6, 10, 20, 18), 4, Color.White);
                    using (SolidBrush b = new SolidBrush(baseColor))
                    {
                        g.FillRectangle(b, 8, 14, 16, 2);
                    }
                    Draw.FillRound(g, new Rectangle(10, 6, 4, 6), 2, Color.White);
                    Draw.FillRound(g, new Rectangle(18, 6, 4, 6), 2, Color.White);
                }

                Icon drawn = IconFromBitmapOwned(bmp);
                if (drawn != null) return drawn;
                return SystemIcons.Application;
            }
        }

        // ---------------------------------------------------------------- 隐藏窗口

        /// <summary>只用来接 TaskbarCreated 的隐藏顶层窗口（广播消息不会送到 message-only 窗口）。</summary>
        private sealed class TrayMessageWindow : NativeWindow, IDisposable
        {
            private readonly TrayController _owner;
            private bool _disposed;

            public TrayMessageWindow(TrayController owner)
            {
                _owner = owner;
                CreateParams cp = new CreateParams();
                cp.Caption = "DesktopCalendar.TrayController";
                cp.Style = 0;
                cp.ExStyle = 0;
                cp.ClassStyle = 0;
                CreateHandle(cp);
            }

            protected override void WndProc(ref Message m)
            {
                if (_owner != null && WmTaskbarCreated != 0 && m.Msg == WmTaskbarCreated)
                {
                    _owner.OnTaskbarCreated();
                }
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try { DestroyHandle(); }
                catch (Exception) { }
            }
        }

        // ---------------------------------------------------------------- 释放

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (_main != null)
            {
                try { _main.FormClosing -= MainFormClosing; }
                catch (Exception) { }
                try { _main.VisibleChanged -= MainVisibleChanged; }
                catch (Exception) { }
                _main = null;
            }

            if (_msgWindow != null)
            {
                try { _msgWindow.Dispose(); }
                catch (Exception) { }
                _msgWindow = null;
            }

            if (_notify != null)
            {
                try
                {
                    _notify.Visible = false;
                    _notify.ContextMenuStrip = null;
                    _notify.Dispose();
                }
                catch (Exception) { }
                _notify = null;
            }

            if (_menu != null)
            {
                try { _menu.Dispose(); }
                catch (Exception) { }
                _menu = null;
            }

            // 自绘图标的句柄在这里才销毁（而且只销毁一次）；
            // Branding.AppIcon / SystemIcons.Application 都不归我们所有，不能 Dispose
            if (_ownHicon != IntPtr.Zero && _appIcon != null)
            {
                try { _appIcon.Dispose(); }
                catch (Exception) { }
            }
            _appIcon = null;

            if (_ownHicon != IntPtr.Zero)
            {
                IntPtr h = _ownHicon;
                _ownHicon = IntPtr.Zero;
                try
                {
                    DestroyIcon(h);
                    _destroyedHicons++;
                }
                catch (Exception) { }
            }
        }
    }
}
