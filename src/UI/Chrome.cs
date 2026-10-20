using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopCalendar.UI
{
    /// <summary>无边框窗口的 Win11 原生观感：DWM 圆角、标题栏拖动、右下角拉伸。全部失败静默忽略。</summary>
    public static class Chrome
    {
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const int DWMWCP_ROUNDSMALL = 3;

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTBOTTOMRIGHT = 17;

        private static readonly List<Form> _hooked = new List<Form>();

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>Win11 DWM 圆角 + 系统阴影。句柄可能还没创建，所以两种情况都接。</summary>
        public static void EnableRounded(Form f)
        {
            if (f == null) return;
            if (f.IsHandleCreated)
            {
                ApplyRounded(f);
            }
            f.HandleCreated += delegate(object sender, EventArgs e)
            {
                if (sender is Form) ApplyRounded((Form)sender);
            };
        }

        private static void ApplyRounded(Form f)
        {
            try
            {
                int pref = DWMWCP_ROUND;
                DwmSetWindowAttribute(f.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch
            {
                // 非 Win11 / dwmapi 不可用：保持方角，忽略
            }
        }

        /// <summary>无边框窗口拖动：命中控件即交给系统标题栏拖拽。</summary>
        public static void EnableDrag(Control c, Form f)
        {
            if (c == null || f == null) return;
            c.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                try
                {
                    ReleaseCapture();
                    SendMessage(f.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                }
                catch
                {
                    // 拖动失败不影响其他功能
                }
            };
        }

        /// <summary>右下角 18px 抓取区返回 HTBOTTOMRIGHT，交给系统 resizing 循环。</summary>
        public static void EnableResize(Form f)
        {
            if (f == null) return;
            for (int i = 0; i < _hooked.Count; i++)
            {
                if (object.ReferenceEquals(_hooked[i], f)) return;
            }
            _hooked.Add(f);
            try
            {
                Application.AddMessageFilter(new CornerResizeFilter(f));
            }
            catch
            {
                // 无消息泵（设计期）时忽略
            }
        }

        private sealed class CornerResizeFilter : IMessageFilter
        {
            private readonly Form _form;
            private int _grip = 18;

            public CornerResizeFilter(Form form)
            {
                _form = form;
                try
                {
                    if (form.IsHandleCreated)
                    {
                        using (Graphics g = form.CreateGraphics())
                        {
                            _grip = Draw.Scale(g, 18);
                        }
                    }
                }
                catch
                {
                }
            }

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_NCHITTEST) return false;
                Form f = _form;
                if (f == null || !f.IsHandleCreated || f.IsDisposed) return false;
                if (m.HWnd != f.Handle) return false;
                if (f.WindowState == FormWindowState.Maximized) return false;

                long lp = m.LParam.ToInt64();
                int sx = unchecked((short)(lp & 0xFFFF));
                int sy = unchecked((short)((lp >> 16) & 0xFFFF));
                Rectangle b = f.Bounds;
                int grip = _grip;

                if (sx >= b.Right - grip && sx < b.Right + grip &&
                    sy >= b.Bottom - grip && sy < b.Bottom + grip)
                {
                    m.Result = (IntPtr)HTBOTTOMRIGHT;
                    return true;
                }
                return false;
            }
        }

        /// <summary>保留：Win11 小圆角（弹窗用），未在别处调用。</summary>
        internal static void ApplySmallRounded(Form f)
        {
            if (f == null || !f.IsHandleCreated) return;
            try
            {
                int pref = DWMWCP_ROUNDSMALL;
                DwmSetWindowAttribute(f.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch
            {
            }
        }
    }
}
