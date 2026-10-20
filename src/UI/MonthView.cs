using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DesktopCalendar.Data;
using DesktopCalendar.Models;

namespace DesktopCalendar.UI
{
    /// <summary>自绘月历：7 列 × 6 行、周一起始、圆角日期格、日程色条 + 截断标题。</summary>
    public class MonthView : Control
    {
        private const int TotalCells = 42;
        private const int WeekCount = 7;
        private const int WmPrintClient = 0x0318;

        private static readonly string[] WeekNames = new string[] { "一", "二", "三", "四", "五", "六", "日" };

        private DateTime _month;
        private DateTime _selected;
        private Store _store;
        private int _hover = -1;
        private bool _laidOut;
        private readonly Rectangle[] _cells;
        private readonly DateTime[] _dates;
        private Rectangle _header;

        public event Action<DateTime> DateSelected;
        public event Action<ScheduleItem> ItemActivated;
        /// <summary>滚轮/外部切换月份后触发（附加成员，便于标题栏同步）。</summary>
        public event Action<DateTime> MonthChanged;

        public MonthView()
        {
            _cells = new Rectangle[TotalCells];
            _dates = new DateTime[TotalCells];
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
            DateTime t = DateTime.Today;
            _month = new DateTime(t.Year, t.Month, 1);
            _selected = t.Date;
            this.BackColor = Theme.Current.Bg;
            Theme.Changed += HandleThemeChanged;
        }

        public DateTime Month
        {
            get { return _month; }
            set
            {
                SetMonth(new DateTime(value.Year, value.Month, 1), true);
            }
        }

        public DateTime SelectedDate
        {
            get { return _selected; }
            set
            {
                DateTime d = value.Date;
                bool changed = d != _selected;
                _selected = d;
                SetMonth(new DateTime(d.Year, d.Month, 1), true);
                if (changed) this.Invalidate();
            }
        }

        public Store Store
        {
            get { return _store; }
            set
            {
                _store = value;
                this.Invalidate();
            }
        }

        /// <summary>当前悬停日期，无则返回 DateTime.MinValue（附加成员）。</summary>
        public DateTime HoverDate
        {
            get
            {
                if (_laidOut && _hover >= 0 && _hover < TotalCells) return _dates[_hover];
                return DateTime.MinValue;
            }
        }

        private void SetMonth(DateTime firstDay, bool raiseEvent)
        {
            if (firstDay.Year == _month.Year && firstDay.Month == _month.Month) return;
            _month = firstDay;
            this.Invalidate();
            if (raiseEvent)
            {
                Action<DateTime> h = MonthChanged;
                if (h != null) h(_month);
            }
        }

        private void HandleThemeChanged(object sender, EventArgs e)
        {
            this.BackColor = Theme.Current.Bg;
            this.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Theme.Changed -= HandleThemeChanged;
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // 整幅自绘，跳过背景以消除闪烁
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            RenderTo(e.Graphics);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmPrintClient && m.WParam != IntPtr.Zero)
            {
                using (Graphics g = Graphics.FromHdc(m.WParam))
                {
                    RenderTo(g);
                }
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>把整个月历画到任意 Graphics（Paint 与离屏渲染共用同一份绘制代码）。</summary>
        public void RenderTo(Graphics g)
        {
            if (g == null) return;
            Theme th = Theme.Current;
            Draw.Smooth(g);
            using (SolidBrush bg = new SolidBrush(th.Bg))
            {
                g.FillRectangle(bg, ClientRectangle);
            }
            ComputeLayout(g);
            if (!_laidOut) return;
            DrawWeekHeader(g);
            DrawCells(g);
        }

        private void ComputeLayout(Graphics g)
        {
            _laidOut = false;
            Rectangle cr = ClientRectangle;
            int pad = Draw.Scale(g, 10);
            int headerH = Draw.Scale(g, 24);
            int topGap = Draw.Scale(g, 8);
            int gap = Draw.Scale(g, 6);

            int gridW = cr.Width - pad * 2;
            int gridH = cr.Height - pad * 2 - headerH - topGap;
            if (gridW < WeekCount * 24 || gridH < 6 * 20) return;

            float cellWf = (gridW - gap * (WeekCount - 1)) / (float)WeekCount;
            float cellHf = (gridH - gap * 5) / 6f;
            float maxW = cellHf * 1.5f;                 // 拉宽窗口时保持接近正方形
            if (cellWf > maxW) cellWf = maxW;

            int cellW = (int)Math.Floor(cellWf);
            int cellH = (int)Math.Floor(cellHf);
            if (cellW < 20 || cellH < 20) return;

            int totalW = cellW * WeekCount + gap * (WeekCount - 1);
            int totalH = cellH * 6 + gap * 5;
            int x0 = cr.X + (cr.Width - totalW) / 2;
            int y0 = cr.Y + pad + headerH + topGap + (gridH - totalH) / 2;

            _header = new Rectangle(x0, cr.Y + pad, totalW, headerH);

            DateTime first = _month;
            int offset = ((int)first.DayOfWeek + 6) % 7;   // 周一 = 0
            DateTime start = first.AddDays(-offset);

            for (int i = 0; i < TotalCells; i++)
            {
                int row = i / WeekCount;
                int col = i - row * WeekCount;
                _cells[i] = new Rectangle(x0 + col * (cellW + gap), y0 + row * (cellH + gap), cellW, cellH);
                _dates[i] = start.AddDays(i);
            }
            _laidOut = true;
        }

        private void DrawWeekHeader(Graphics g)
        {
            Theme th = Theme.Current;
            int h = _header.Height;
            for (int c = 0; c < WeekCount; c++)
            {
                Rectangle r = new Rectangle(_cells[c].X, _header.Y, _cells[c].Width, h);
                Color c2 = c >= 5 ? th.TextMuted : th.TextSecondary;
                Draw.Text(g, WeekNames[c], th.Small, c2, r, StringAlignment.Center, StringAlignment.Center);
            }

            float lineY = _header.Bottom + Draw.Scale(g, 2) + 0.5f;
            Draw.Line(g, _header.X, lineY, _header.Right, lineY, Draw.Blend(th.Border, th.Bg, 0.35f), 1f);
        }

        private void DrawCells(Graphics g)
        {
            Theme th = Theme.Current;
            int padIn = Draw.Scale(g, 10);
            int numTop = Draw.Scale(g, 5);
            int numH = Draw.Scale(g, 20);
            int rowH = Draw.Scale(g, 16);
            int barW = Math.Max(2, Draw.Scale(g, 3));
            int barGap = Draw.Scale(g, 6);
            int barPadY = Draw.Scale(g, 3);
            int bottomPad = Draw.Scale(g, 4);
            int radius = Draw.Scale(g, 10);
            DateTime today = DateTime.Today;

            for (int i = 0; i < TotalCells; i++)
            {
                Rectangle box = _cells[i];
                DateTime day = _dates[i];
                bool inMonth = day.Month == _month.Month && day.Year == _month.Year;
                bool isToday = day.Date == today;
                bool isSel = day.Date == _selected.Date;
                bool isHover = (i == _hover);

                if (isSel)
                {
                    Draw.FillRound(g, box, radius, th.Surface);
                    Draw.StrokeRound(g, box, radius, th.Accent, 1.5f);
                }
                else if (isToday)
                {
                    Draw.FillRound(g, box, radius, th.TodayBg);
                    Draw.StrokeRound(g, box, radius, Draw.Blend(th.Accent, th.TodayBg, 0.72f), 1f);
                }
                else if (isHover)
                {
                    Draw.FillRound(g, box, radius, th.SurfaceAlt);
                    Draw.StrokeRound(g, box, radius, Draw.Blend(th.Border, th.SurfaceAlt, 0.45f), 1f);
                }
                else if (inMonth)
                {
                    // 极淡的发丝边框：只给日期格一点结构，不抢内容注意力
                    Draw.StrokeRound(g, box, radius, Draw.Blend(th.Border, th.Bg, 0.30f), 1f);
                }
                else
                {
                    // 非本月格保留更淡的轮廓，让整张网格不缺角
                    Draw.StrokeRound(g, box, radius, Draw.Blend(th.Border, th.Bg, 0.68f), 1f);
                }

                Rectangle numRect = new Rectangle(box.X + padIn, box.Y + numTop, box.Width - padIn * 2, numH);
                Font numFont = isToday ? th.BodyBold : th.Body;
                Color numColor = isToday ? th.Accent : (inMonth ? th.TextPrimary : th.TextMuted);
                Draw.Text(g, day.Day.ToString(), numFont, numColor, numRect,
                          StringAlignment.Near, StringAlignment.Center);

                int y = numRect.Bottom + Draw.Scale(g, 1);
                int cap = (box.Bottom - bottomPad - y) / rowH;
                if (cap > 2) cap = 2;
                if (cap <= 0) continue;

                List<ScheduleItem> items = _store == null ? null : _store.ForDay(day);
                int total = items == null ? 0 : items.Count;
                if (total == 0) continue;

                int shown = total < cap ? total : cap;
                int tx = box.X + padIn + barW + barGap;
                int tw = box.Right - padIn - tx;
                for (int k = 0; k < shown; k++)
                {
                    ScheduleItem it = items[k];
                    Color c = th.ResolveCategory(it.Color);
                    if (!inMonth) c = Draw.Blend(c, th.Bg, 0.55f);

                    int barH = rowH - barPadY * 2;
                    if (barH < 3) barH = 3;
                    Rectangle bar = new Rectangle(box.X + padIn, y + barPadY, barW, barH);
                    Draw.Fill(g, bar, c);

                    string title = it.Title;
                    if (title == null) title = "";
                    Color tc = inMonth ? th.TextSecondary : th.TextMuted;
                    Draw.Ellipsis(g, title, th.Small, tc, new Rectangle(tx, y, tw, rowH));
                    y += rowH;
                }

                if (total > shown)
                {
                    Rectangle more = new Rectangle(tx, y, tw, rowH);
                    if (more.Bottom <= box.Bottom - bottomPad + Draw.Scale(g, 1))
                    {
                        Draw.Text(g, "+" + (total - shown).ToString(), th.Small, th.TextMuted,
                                  more, StringAlignment.Near, StringAlignment.Center);
                    }
                }
            }
        }

        private int HitCell(Point p)
        {
            if (!_laidOut) return -1;
            for (int i = 0; i < TotalCells; i++)
            {
                if (_cells[i].Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = HitCell(e.Location);
            if (i != _hover)
            {
                _hover = i;
                this.Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1)
            {
                _hover = -1;
                this.Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            int i = HitCell(e.Location);
            if (i < 0) return;
            Select(_dates[i]);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left) return;
            int i = HitCell(e.Location);
            if (i < 0) return;
            if (_store == null) return;
            List<ScheduleItem> items = _store.ForDay(_dates[i]);
            if (items == null || items.Count == 0) return;
            Action<ScheduleItem> h = ItemActivated;
            if (h != null) h(items[0]);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Delta == 0) return;
            SetMonth(_month.AddMonths(e.Delta > 0 ? -1 : 1), true);
        }

        private void Select(DateTime day)
        {
            _selected = day.Date;
            this.Invalidate();
            Action<DateTime> h = DateSelected;
            if (h != null) h(_selected);
        }
    }
}
