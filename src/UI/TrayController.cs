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
    /// 托盘图标 + 右键菜单。图标完全由代码绘制（16×16），不依赖外部 .ico 文件。
    /// </summary>
    public class TrayController : IDisposable
    {
        private const int TOOLTIP_MAX = 63;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

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
        private string _tooltip = "桌面日历";
        private string _lastBalloon = "";

        public event Action ShowMainRequested;
        public event Action ExitRequested;

        public TrayController(Store store)
        {
            _store = store;

            _appIcon = BuildIcon();

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

            RefreshMenuState();
        }

        // ---------------------------------------------------------------- 公开成员

        public NotifyIcon Icon { get { return _notify; } }

        /// <summary>主窗口关闭按钮 = 收起到托盘；真正退出时才放行。</summary>
        public void Bind(Form main)
        {
            _main = main;
            if (_main == null) return;
            _main.FormClosing += MainFormClosing;
        }

        /// <summary>标记为「真正退出」，之后主窗口的关闭不再被拦截。</summary>
        public void PrepareExit()
        {
            _exiting = true;
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
                ShowBalloon("桌面日历", "已最小化到托盘，双击图标可重新打开");
                _lastBalloon = "hinted";
            }
        }

        // ---------------------------------------------------------------- 图标

        private static Icon BuildIcon()
        {
            Color baseColor = Theme.Current.Accent;
            using (Bitmap bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                    Draw.FillRound(g, new Rectangle(0, 0, 16, 16), 4, baseColor);

                    // 白色日历轮廓：表头 + 两枚挂环
                    Draw.FillRound(g, new Rectangle(3, 5, 10, 9), 2, Color.White);
                    using (SolidBrush b = new SolidBrush(baseColor))
                    {
                        g.FillRectangle(b, 4, 7, 8, 1);
                    }
                    Draw.FillRound(g, new Rectangle(5, 3, 2, 3), 1, Color.White);
                    Draw.FillRound(g, new Rectangle(9, 3, 2, 3), 1, Color.White);
                }

                IntPtr h = IntPtr.Zero;
                try
                {
                    h = bmp.GetHicon();
                    using (System.Drawing.Icon tmp = System.Drawing.Icon.FromHandle(h))
                    {
                        return (System.Drawing.Icon)tmp.Clone();
                    }
                }
                catch (Exception)
                {
                    return SystemIcons.Application;
                }
                finally
                {
                    if (h != IntPtr.Zero) DestroyIcon(h);
                }
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
                _main = null;
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

            if (_appIcon != null)
            {
                try { _appIcon.Dispose(); }
                catch (Exception) { }
                _appIcon = null;
            }
        }
    }
}
