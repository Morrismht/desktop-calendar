using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DesktopCalendar.Models;
using DesktopCalendar.Services;

namespace DesktopCalendar.UI
{
    /// <summary>
    /// 右下角到期提醒卡片：360×132，无边框、置顶、不进任务栏、淡入、左侧分类色条。
    /// 颜色与字体一律取自 Theme.Current；绘制原语一律使用 Draw。
    /// </summary>
    public class ReminderCard : Form
    {
        private const int CARD_W = 360;
        private const int CARD_H = 132;
        private const int CARD_RADIUS = 12;
        private const int BTN_RADIUS = 10;
        private const int BAR_W = 6;

        // WM_NCHITTEST / HTCAPTION
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCAPTION = 2;

        private readonly DueReminder _due;
        private readonly Scheduler _scheduler;

        private Rectangle _rComplete;
        private Rectangle _rSnooze;
        private Rectangle _rDismiss;

        // -1 无悬停；0 完成；1 +10 分钟；2 今天忽略
        private int _hover = -1;

        private Timer _fade;
        private bool _closing;

        /// <summary>按钮已触发并完成调度动作后抛出，随后卡片自行关闭。</summary>
        public new event Action<ReminderCard> Closed;

        public ReminderCard(DueReminder due, Scheduler scheduler)
        {
            _due = due;
            _scheduler = scheduler;

            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ShowIcon = false;
            MinimizeBox = false;
            MaximizeBox = false;
            KeyPreview = true;

            // 圆角区域由 Region 裁剪保证在 Win10/Win11 都成立；
            // BackColor 仅在离屏渲染（DrawToBitmap）时可见，用于凸显圆角。
            BackColor = Theme.Current.Bg;
            ClientSize = new Size(CARD_W, CARD_H);

            BuildLayout();
            ApplyRoundedRegion();

            try { Chrome.EnableRounded(this); }
            catch (Exception) { }
        }

        // ---------------------------------------------------------------- 布局

        private void BuildLayout()
        {
            int left = BAR_W + 16;
            int y = 84;
            int h = 32;
            _rComplete = new Rectangle(left, y, 74, h);
            _rSnooze = new Rectangle(_rComplete.Right + 8, y, 100, h);
            _rDismiss = new Rectangle(_rSnooze.Right + 8, y, 84, h);
        }

        private static Font FontOf(Font f, Font fallback)
        {
            if (f != null) return f;
            return fallback;
        }

        private void ApplyRoundedRegion()
        {
            using (GraphicsPath p = Draw.Round(new Rectangle(0, 0, CARD_W, CARD_H), CARD_RADIUS))
            {
                Region prev = Region;
                Region = new Region(p);
                if (prev != null) prev.Dispose();
            }
        }

        // ---------------------------------------------------------------- 数据

        private Color AccentColor()
        {
            Color fallback = Theme.Current.Accent;
            if (_due == null || _due.Item == null) return fallback;
            return ParseHex(_due.Item.Color, fallback);
        }

        private static Color ParseHex(string s, Color fallback)
        {
            if (s == null) return fallback;
            string t = s.Trim();
            if (t.Length == 0) return fallback;
            if (t[0] == '#') t = t.Substring(1);
            if (t.Length != 6) return fallback;
            try
            {
                int v = Convert.ToInt32(t, 16);
                return Color.FromArgb(255, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private string TitleText()
        {
            if (_due == null || _due.Item == null) return "";
            string s = _due.Item.Title;
            return s == null ? "" : s;
        }

        private string SubText()
        {
            DateTime occ = _due == null ? DateTime.Now : _due.Occurrence;
            string suffix = " · 今天";
            bool overdue = false;
            if (_due != null && _due.Kind == "overdue") overdue = true;
            if (occ.Date < DateTime.Today) overdue = true;
            if (overdue) suffix = " · 已逾期";
            return occ.ToString("HH:mm") + suffix;
        }

        // ---------------------------------------------------------------- 绘制

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Theme.Current;
            Graphics g = e.Graphics;
            Draw.Smooth(g);

            Rectangle full = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            // 外圈留 1px 当“微阴影”：先铺一层 Border/Surface 的混合色打底，
            // 再在内侧画 1px Border，卡片才真正从浅色背景里浮起来。
            Rectangle face = new Rectangle(full.X + 1, full.Y + 1, full.Width - 2, full.Height - 2);
            Color accent = AccentColor();
            // 极淡的外圈：Border 与 Surface 混合，营造 1px 微阴影
            Color halo = Draw.Blend(t.Border, t.Surface, 0.55f);

            using (GraphicsPath fullPath = Draw.Round(full, CARD_RADIUS))
            using (GraphicsPath facePath = Draw.Round(face, CARD_RADIUS - 1))
            {
                using (SolidBrush b = new SolidBrush(halo)) g.FillPath(b, fullPath);
                using (SolidBrush b = new SolidBrush(t.Surface)) g.FillPath(b, facePath);
                // 描边路径落在整数坐标上：AntiAlias 下 1px 钢笔才能整格覆盖（实测 = 单像素纯色）
                using (Pen pen = new Pen(t.Border, 1f)) g.DrawPath(pen, facePath);

                // 左侧分类色条：用与卡片同一套圆角参数构造路径（不是直角矩形 + 硬裁剪），
                // 四角因此严格跟随卡片圆角，左下角不会再顶出轮廓。
                using (GraphicsPath bar = BarPath(face, CARD_RADIUS - 1))
                using (SolidBrush sb = new SolidBrush(accent)) g.FillPath(sb, bar);
            }

            int tx = face.X + BAR_W + 16;
            int tw = face.Right - 18 - tx;   // 右侧永久保留 19px 内边距
            Rectangle rTitle = new Rectangle(tx, 16, tw, 26);
            Rectangle rSub = new Rectangle(tx, 44, tw, 18);

            // 标题：单行 + EllipsisCharacter，且先用 MeasureString 预裁一次，
            // 保证任何字号/DPI 下都不会画到卡片边界之外。
            Font titleFont = FontOf(t.BodyBold, this.Font);
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            {
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                string title = FitText(g, TitleText(), titleFont, sf, rTitle.Width);
                using (SolidBrush b = new SolidBrush(t.TextPrimary))
                {
                    g.DrawString(title, titleFont, b, rTitle, sf);
                }
            }
            Draw.Text(g, SubText(), FontOf(t.Small, this.Font), t.TextSecondary, rSub,
                StringAlignment.Near, StringAlignment.Center);

            // [完成] Accent 实心白字
            Color completeBg = _hover == 0 ? Lighten(t.Accent, 18) : t.Accent;
            Draw.FillRound(g, _rComplete, BTN_RADIUS, completeBg);
            Draw.Text(g, "完成", FontOf(t.SmallBold, this.Font), Color.White, _rComplete,
                StringAlignment.Center, StringAlignment.Center);

            // [+10 分钟] 描边
            Draw.FillRound(g, _rSnooze, BTN_RADIUS, _hover == 1 ? t.SurfaceAlt : t.Surface);
            Draw.StrokeRound(g, _rSnooze, BTN_RADIUS, _hover == 1 ? t.Accent : t.Border, 1f);
            Draw.Text(g, "+10 分钟", FontOf(t.SmallBold, this.Font),
                _hover == 1 ? t.Accent : t.TextPrimary, _rSnooze,
                StringAlignment.Center, StringAlignment.Center);

            // [今天忽略] 纯文字
            Draw.Text(g, "今天忽略", FontOf(t.Small, this.Font),
                _hover == 2 ? t.TextPrimary : t.TextMuted, _rDismiss,
                StringAlignment.Center, StringAlignment.Center);
        }

        private static Color Lighten(Color c, int delta)
        {
            int r = c.R + delta; if (r > 255) r = 255;
            int gg = c.G + delta; if (gg > 255) gg = 255;
            int b = c.B + delta; if (b > 255) b = 255;
            return Color.FromArgb(c.A, r, gg, b);
        }

        /// <summary>
        /// 左侧色条路径：上下两个左圆角与卡片的圆角同心同径，右缘是一条竖直线。
        /// phi 为卡片圆弧与色条右缘交点所对应的角度，保证色条严格被卡片轮廓包住。
        /// </summary>
        private static GraphicsPath BarPath(Rectangle face, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int r = radius;
            if (r < 2) r = 2;
            double cos = (double)(r - BAR_W) / r;
            if (cos < 0.0) cos = 0.0;
            if (cos > 1.0) cos = 1.0;
            double phi = Math.Acos(cos) * 180.0 / Math.PI;
            double dy = r * Math.Sin(phi * Math.PI / 180.0);
            float right = face.X + BAR_W;
            float topY = (float)(face.Y + r - dy);
            float botY = (float)(face.Bottom - r + dy);
            p.FillMode = FillMode.Winding;
            p.AddArc(face.X, face.Y, r * 2, r * 2, 180f, (float)phi);
            p.AddLine(right, topY, right, botY);
            p.AddArc(face.X, face.Bottom - r * 2, r * 2, r * 2, (float)(180.0 - phi), (float)phi);
            p.AddLine(face.X, face.Bottom - r, face.X, face.Y + r);
            p.CloseFigure();
            return p;
        }

        private static float TextWidth(Graphics g, string s, Font f, StringFormat sf)
        {
            return g.MeasureString(s, f, new Size(1000000, 1000000), sf).Width;
        }

        /// <summary>超宽就二分裁剪并补省略号，返回值一定画得进 maxWidth。</summary>
        private static string FitText(Graphics g, string s, Font f, StringFormat sf, int maxWidth)
        {
            if (s == null || s.Length == 0) return "";
            if (maxWidth <= 8) return "";
            if (TextWidth(g, s, f, sf) <= maxWidth) return s;
            int lo = 1;
            int hi = s.Length - 1;
            int best = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (TextWidth(g, s.Substring(0, mid) + "…", f, sf) <= maxWidth)
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return best <= 0 ? "" : s.Substring(0, best) + "…";
        }

        // ---------------------------------------------------------------- 命中

        private int HitTest(Point p)
        {
            if (_rComplete.Contains(p)) return 0;
            if (_rSnooze.Contains(p)) return 1;
            if (_rDismiss.Contains(p)) return 2;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hit = HitTest(e.Location);
            if (hit != _hover)
            {
                _hover = hit;
                Invalidate();
            }
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover != -1)
            {
                _hover = -1;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                int hit = HitTest(e.Location);
                if (hit == 0) { ActComplete(); return; }
                if (hit == 1) { ActSnooze(); return; }
                if (hit == 2) { ActDismiss(); return; }
            }
            base.OnMouseDown(e);
        }

        protected override void WndProc(ref Message m)
        {
            // 非按钮区域交给系统当标题栏处理 —— 无边框窗口的拖动。
            // 用 NCHITTEST 而不是 Chrome.EnableDrag，避免拖动逻辑吞掉按钮点击。
            if (m.Msg == WM_NCHITTEST)
            {
                int lp = m.LParam.ToInt32();
                int sx = (short)(lp & 0xFFFF);
                int sy = (short)((lp >> 16) & 0xFFFF);
                Point cp = PointToClient(new Point(sx, sy));
                if (HitTest(cp) < 0)
                {
                    m.Result = new IntPtr(HTCAPTION);
                    return;
                }
            }
            base.WndProc(ref m);
        }

        // ---------------------------------------------------------------- 动作

        private void ActComplete()
        {
            if (_scheduler != null && _due != null)
            {
                try { _scheduler.Complete(_due); }
                catch (Exception) { }
            }
            Finish();
        }

        private void ActSnooze()
        {
            if (_scheduler != null && _due != null)
            {
                try { _scheduler.Snooze(_due, 10); }
                catch (Exception) { }
            }
            Finish();
        }

        private void ActDismiss()
        {
            if (_scheduler != null && _due != null)
            {
                try { _scheduler.DismissToday(_due); }
                catch (Exception) { }
            }
            Finish();
        }

        private void Finish()
        {
            if (_closing) return;
            _closing = true;
            Action<ReminderCard> handler = Closed;
            if (handler != null)
            {
                try { handler(this); }
                catch (Exception) { }
            }
            Close();
        }

        // ---------------------------------------------------------------- 显示

        /// <summary>以 bottomRightAnchor 为右下角摆放并显示（多卡片堆叠由调用者算好 anchor）。</summary>
        public void ShowAt(Point bottomRightAnchor)
        {
            Rectangle wa = Screen.FromPoint(bottomRightAnchor).WorkingArea;
            int x = bottomRightAnchor.X - Width;
            int y = bottomRightAnchor.Y - Height;

            if (x + Width > wa.Right - 8) x = wa.Right - 8 - Width;
            if (x < wa.Left + 8) x = wa.Left + 8;
            if (y + Height > wa.Bottom - 8) y = wa.Bottom - 8 - Height;
            if (y < wa.Top + 8) y = wa.Top + 8;

            Location = new Point(x, y);
            Show();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            StartFade();
        }

        private void StartFade()
        {
            StopFade();
            try { Opacity = 0.0; }
            catch (Exception) { }
            _fade = new Timer();
            _fade.Interval = 20;
            _fade.Tick += FadeTick;
            _fade.Start();
        }

        private void FadeTick(object sender, EventArgs e)
        {
            double next = Opacity + 0.125;
            if (next >= 1.0)
            {
                try { Opacity = 1.0; }
                catch (Exception) { }
                StopFade();
                return;
            }
            try { Opacity = next; }
            catch (Exception) { StopFade(); }
        }

        private void StopFade()
        {
            if (_fade == null) return;
            _fade.Stop();
            _fade.Tick -= FadeTick;
            _fade.Dispose();
            _fade = null;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopFade();
            base.OnFormClosed(e);
        }
    }
}
