using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DesktopCalendar.Data;
using DesktopCalendar.Models;
using DesktopCalendar.Services;

namespace DesktopCalendar.UI
{
    /// <summary>主窗口：无边框 1000×700，左侧自绘月历，右侧当日侧栏，底部 44 高状态条。</summary>
    public class MainForm : Form
    {
        private const int PAD = 20;
        private const int SIDEBAR_W = 320;
        private const int STATUS_H = 44;
        private const int HEADER_TOP = 10;
        private const int HEADER_H = 34;
        private const int MONTH_TOP = 52;
        private const int ROW_H = 46;
        private const int ROW_STEP = 50;
        private const int ROW_RADIUS = 10;
        private const int PILL_RADIUS = 8;
        private const int LIST_TOP = 90;

        private static readonly string[] WeekNames = new string[]
        {
            "周日", "周一", "周二", "周三", "周四", "周五", "周六"
        };

        private readonly Store _store;
        private readonly Scheduler _scheduler;
        private readonly MonthView _monthView;
        private readonly Panel _dragBar;
        private readonly List<ReminderCard> _cards = new List<ReminderCard>();

        private List<ScheduleItem> _dayItems = new List<ScheduleItem>();
        private DateTime _selected;
        private string _monthTitle = "";
        private int _leftW = 640;
        private int _sideX = 660;
        private int _statusTop = 656;

        private Rectangle _rPrev;
        private Rectangle _rNext;
        private Rectangle _rToday;
        private Rectangle _rNew;
        private Rectangle _rAutoStart;
        private Rectangle _rTheme;
        private Rectangle _rTray;
        private Rectangle _rList;
        private Rectangle _rMin;
        private Rectangle _rClose;

        private int _hover = -1;
        private int _lastRowHit = -1;
        private DateTime _lastRowClick = DateTime.MinValue;

        public MainForm(Store store, Scheduler scheduler)
        {
            _store = store;
            _scheduler = scheduler;
            _selected = DateTime.Today;

            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowIcon = false;
            KeyPreview = true;
            Text = "桌面日历";
            MinimumSize = new Size(1000, 700);
            ClientSize = new Size(1000, 700);
            BackColor = Theme.Current.Bg;

            _dragBar = new Panel();
            _dragBar.BackColor = Theme.Current.Bg;
            _dragBar.Paint += DragBarPaint;
            Controls.Add(_dragBar);

            _monthView = new MonthView();
            _monthView.Store = store;
            _monthView.DateSelected += HandleDateSelected;
            _monthView.ItemActivated += HandleItemActivated;
            _monthView.MonthChanged += HandleMonthChanged;
            Controls.Add(_monthView);

            LayoutRegions();
            _monthView.SelectedDate = _selected;
            _monthTitle = string.Format("{0}年{1}月", _monthView.Month.Year, _monthView.Month.Month);

            Chrome.EnableDrag(_dragBar, this);
            Chrome.EnableRounded(this);
            Chrome.EnableResize(this);

            Theme.Changed += HandleThemeChanged;
            if (_scheduler != null) _scheduler.RemindersDue += HandleRemindersDue;

            RefreshSidebar();
        }

        // ---------------------------------------------------------------- 布局

        private void LayoutRegions()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            _leftW = w - SIDEBAR_W - PAD * 2;
            if (_leftW < 200) _leftW = 200;
            _sideX = _leftW + PAD;
            _statusTop = h - STATUS_H;

            int headerRight = PAD + _leftW;
            _rPrev = new Rectangle(PAD, HEADER_TOP, 32, HEADER_H);
            _rNext = new Rectangle(PAD + 36, HEADER_TOP, 32, HEADER_H);
            _rToday = new Rectangle(headerRight - 76, HEADER_TOP, 76, HEADER_H);

            int dragW = _leftW - 188;
            if (dragW < 40) dragW = 40;

            int monthH = _statusTop - MONTH_TOP - 12;
            if (monthH < 120) monthH = 120;

            // 构造期 SetBoundsCore（MinimumSize/ClientSize）会早于子控件创建触发 OnResize。
            if (_dragBar != null)
            {
                _dragBar.Bounds = new Rectangle(PAD + 88, HEADER_TOP, dragW, HEADER_H);
            }
            if (_monthView != null)
            {
                _monthView.Bounds = new Rectangle(PAD, MONTH_TOP, _leftW, monthH);
            }

            _rNew = new Rectangle(_sideX, _statusTop - PAD - 40, SIDEBAR_W, 40);
            int listH = _rNew.Y - 16 - LIST_TOP;
            if (listH < 0) listH = 0;
            _rList = new Rectangle(_sideX, LIST_TOP, SIDEBAR_W, listH);

            int pillY = _statusTop + (STATUS_H - 30) / 2;
            _rTray = new Rectangle(w - PAD - 96, pillY, 96, 30);
            _rTheme = new Rectangle(_rTray.X - 10 - 104, pillY, 104, 30);
            _rAutoStart = new Rectangle(_rTheme.X - 10 - 128, pillY, 128, 30);

            // 右上角窗口控制：从窗体宽度实时算，随拉伸移动
            _rMin = new Rectangle(w - 44 - 30, HEADER_TOP, 30, 30);
            _rClose = new Rectangle(w - 14 - 30, HEADER_TOP, 30, 30);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutRegions();
            Invalidate();
        }

        // ---------------------------------------------------------------- 数据

        private void RefreshSidebar()
        {
            List<ScheduleItem> list = null;
            try
            {
                if (_store != null) list = _store.ForDay(_selected);
            }
            catch (Exception)
            {
                list = null;
            }
            _dayItems = list == null ? new List<ScheduleItem>() : list;
            Invalidate();
        }

        private void RefreshAll()
        {
            RefreshSidebar();
            if (_monthView != null) _monthView.Invalidate();
        }

        private DateTime OccurrenceOf(ScheduleItem it, DateTime day)
        {
            try
            {
                return RepeatRule.OccurrenceStart(it, day);
            }
            catch (Exception)
            {
                return it == null ? day.Date : it.StartOf(day);
            }
        }

        private void UpdateMonthTitle(DateTime month)
        {
            _monthTitle = string.Format("{0}年{1}月", month.Year, month.Month);
            if (_dragBar != null) _dragBar.Invalidate();
        }

        // ---------------------------------------------------------------- 事件

        private void HandleDateSelected(DateTime day)
        {
            _selected = day.Date;
            RefreshSidebar();
        }

        private void HandleMonthChanged(DateTime month)
        {
            UpdateMonthTitle(month);
        }

        private void HandleItemActivated(ScheduleItem item)
        {
            OpenEditor(item);
        }

        private void HandleThemeChanged(object sender, EventArgs e)
        {
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            Theme t = Theme.Current;
            BackColor = t.Bg;
            if (_monthView != null) _monthView.BackColor = t.Bg;
            if (_dragBar != null)
            {
                _dragBar.BackColor = t.Bg;
                _dragBar.Invalidate();
            }
            Refresh();
            Invalidate(true);
        }

        // ---------------------------------------------------------------- 动作

        private void OpenEditor(ScheduleItem existing)
        {
            try
            {
                using (EventEditor editor = new EventEditor(_store, existing))
                {
                    editor.ShowDialog(this);
                    if (editor.Result != null) RefreshAll();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开编辑器失败：" + ex.Message, "桌面日历",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CreateItem()
        {
            OpenEditor(null);
        }

        private void ToggleDone(int index)
        {
            if (_dayItems == null || index < 0 || index >= _dayItems.Count) return;
            ScheduleItem it = _dayItems[index];
            DateTime occ = OccurrenceOf(it, _selected);
            bool done = it.IsDoneOn(occ);
            it.DoneOccurrence = done ? (DateTime?)null : occ;
            try
            {
                if (_store != null) _store.Update(it);
            }
            catch (Exception)
            {
            }
            RefreshAll();
        }

        private void ShiftMonth(int delta)
        {
            DateTime next = _monthView.Month.AddMonths(delta);
            _monthView.Month = next;
            UpdateMonthTitle(next);
        }

        private void GoToday()
        {
            DateTime today = DateTime.Today;
            _monthView.SelectedDate = today;
            _selected = today;
            UpdateMonthTitle(new DateTime(today.Year, today.Month, 1));
            RefreshSidebar();
        }

        private void ToggleAutoStart()
        {
            string error = null;
            bool ok;
            bool wasOn;
            try
            {
                wasOn = AutoStart.IsEnabled;
            }
            catch (Exception)
            {
                wasOn = false;
            }

            try
            {
                ok = wasOn ? AutoStart.TryDisable(out error) : AutoStart.TryEnable(out error);
            }
            catch (Exception ex)
            {
                ok = false;
                error = ex.Message;
            }

            if (!ok)
            {
                MessageBox.Show(string.IsNullOrEmpty(error) ? "无法修改开机自启设置。" : error,
                    "开机自启", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (AutoStart.IsEnabled && AutoStart.ActiveMechanism == "startup")
            {
                string extra = string.IsNullOrEmpty(error) ? "" : "\r\n\r\n" + error;
                MessageBox.Show("已开启开机自启，但注册表写入被拒绝，已改用启动文件夹方式。"
                    + "\r\n\r\n生效方式：登录 Windows 后以 --silent 静默启动到托盘。" + extra,
                    "开机自启", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            Invalidate();
        }

        private void ToggleTheme()
        {
            Theme.Toggle();
        }

        private void MinimizeToTray()
        {
            Hide();
        }

        // ---------------------------------------------------------------- 提醒卡片

        private void HandleRemindersDue(List<DueReminder> due)
        {
            if (due == null || due.Count == 0) return;
            try
            {
                if (!IsHandleCreated || IsDisposed) return;
                BeginInvoke((MethodInvoker)delegate { ShowReminderCards(due); });
            }
            catch (Exception)
            {
            }
        }

        private void ShowReminderCards(List<DueReminder> due)
        {
            try
            {
                if (IsDisposed || due == null) return;
                int baseX = 0;
                int baseY = 0;
                Screen sc = Screen.PrimaryScreen;
                if (sc != null)
                {
                    Rectangle wa = sc.WorkingArea;
                    baseX = wa.Right - 16;
                    baseY = wa.Bottom - 16;
                }

                for (int i = 0; i < due.Count; i++)
                {
                    if (_cards.Count >= 4) break;
                    ReminderCard card = new ReminderCard(due[i], _scheduler);
                    card.Closed += CardClosed;
                    int offset = _cards.Count * 142;
                    _cards.Add(card);
                    card.ShowAt(new Point(baseX, baseY - offset));
                }
            }
            catch (Exception)
            {
            }
        }

        private void CardClosed(ReminderCard card)
        {
            if (card != null) _cards.Remove(card);
            RefreshAll();
        }

        // ---------------------------------------------------------------- 绘制

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Theme.Current;
            Graphics g = e.Graphics;
            Draw.Smooth(g);

            int w = ClientSize.Width;

            Draw.Fill(g, new Rectangle(0, 0, w, ClientSize.Height), t.Bg);

            Rectangle sideCard = new Rectangle(_sideX - 10, 10, SIDEBAR_W + 20, _statusTop - 20);
            Draw.FillRound(g, sideCard, 12, t.Surface);
            Draw.StrokeRound(g, sideCard, 12, Draw.Blend(t.Border, t.Bg, 0.25f), 1f);

            Draw.Line(g, 0, _statusTop + 0.5f, w, _statusTop + 0.5f, t.Border, 1f);

            DrawHeader(g, t);
            DrawSidebar(g, t);
            DrawStatus(g, t);
            DrawWindowButtons(g, t);
        }

        private void DrawHeader(Graphics g, Theme t)
        {
            DrawSquareButton(g, _rPrev, "\u2039", _hover == 0, t);
            DrawSquareButton(g, _rNext, "\u203A", _hover == 1, t);
            DrawPill(g, _rToday, "今天", _hover == 2, false, t);
        }

        private static void DrawSquareButton(Graphics g, Rectangle r, string glyph, bool hot, Theme t)
        {
            Draw.FillRound(g, r, PILL_RADIUS, hot ? t.SurfaceAlt : t.Surface);
            Draw.StrokeRound(g, r, PILL_RADIUS, hot ? t.Accent : t.Border, 1f);
            Draw.Text(g, glyph, t.Body, hot ? t.Accent : t.TextSecondary, r,
                StringAlignment.Center, StringAlignment.Center);
        }

        private static void DrawPill(Graphics g, Rectangle r, string label, bool hot, bool on, Theme t)
        {
            Color bg = on ? t.AccentSoft : (hot ? t.SurfaceAlt : t.Surface);
            Draw.FillRound(g, r, PILL_RADIUS, bg);
            Draw.StrokeRound(g, r, PILL_RADIUS, hot ? t.Accent : t.Border, 1f);
            Color fg = (on || hot) ? t.Accent : t.TextPrimary;
            Draw.Text(g, label, t.Small, fg, r, StringAlignment.Center, StringAlignment.Center);
        }

        private void DrawWindowButtons(Graphics g, Theme t)
        {
            DrawWindowButton(g, t, _rMin, _hover == 7, false);
            DrawWindowButton(g, t, _rClose, _hover == 8, true);
        }

        private static void DrawWindowButton(Graphics g, Theme t, Rectangle r, bool hot, bool isClose)
        {
            if (hot)
            {
                Draw.FillRound(g, r, 8, isClose ? t.Danger : t.SurfaceAlt);
            }
            Color fg = (hot && isClose) ? t.Surface : t.TextSecondary;
            float cx = r.X + r.Width / 2f;
            float cy = r.Y + r.Height / 2f;
            using (Pen p = new Pen(fg, 1.5f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                if (isClose)
                {
                    g.DrawLine(p, cx - 4f, cy - 4f, cx + 4f, cy + 4f);
                    g.DrawLine(p, cx + 4f, cy - 4f, cx - 4f, cy + 4f);
                }
                else
                {
                    g.DrawLine(p, cx - 4.5f, cy, cx + 4.5f, cy);
                }
            }
        }

        private void DragBarPaint(object sender, PaintEventArgs e)
        {
            Theme t = Theme.Current;
            Draw.Smooth(e.Graphics);
            using (SolidBrush b = new SolidBrush(t.Bg))
            {
                e.Graphics.FillRectangle(b, _dragBar.ClientRectangle);
            }
            Draw.Ellipsis(e.Graphics, _monthTitle, t.Title, t.TextPrimary,
                new Rectangle(0, 0, _dragBar.Width, _dragBar.Height));
        }

        private void DrawSidebar(Graphics g, Theme t)
        {
            int x = _sideX;
            int w = SIDEBAR_W;

            string title = string.Format("{0}年{1}月{2}日  {3}", _selected.Year, _selected.Month,
                _selected.Day, WeekNames[(int)_selected.DayOfWeek]);
            Draw.Ellipsis(g, title, t.BodyBold, t.TextPrimary, new Rectangle(x, 24, w, 24));

            int total = _dayItems == null ? 0 : _dayItems.Count;
            int done = 0;
            for (int i = 0; i < total; i++)
            {
                ScheduleItem it = _dayItems[i];
                if (it != null && it.IsDoneOn(OccurrenceOf(it, _selected))) done++;
            }

            Draw.Text(g, string.Format("共 {0} 项 / 已完成 {1}", total, done), t.Small, t.TextSecondary,
                new Rectangle(x, 52, w, 18), StringAlignment.Near, StringAlignment.Center);

            Draw.Line(g, x, 78.5f, x + w, 78.5f, Draw.Blend(t.Border, t.Surface, 0.35f), 1f);

            if (total == 0)
            {
                Draw.Text(g, "这一天还没有日程", t.Small, t.TextMuted,
                    new Rectangle(x, _rList.Y, w, 40), StringAlignment.Center, StringAlignment.Center);
            }

            int cap = (_rList.Height + (ROW_STEP - ROW_H)) / ROW_STEP;
            if (cap < 0) cap = 0;
            int shown = total < cap ? total : cap;

            for (int i = 0; i < shown; i++)
            {
                DrawRow(g, t, i, RowRect(i));
            }

            if (total > shown && _rList.Height > 0)
            {
                int y = _rList.Y + shown * ROW_STEP;
                if (y + 18 <= _rList.Bottom)
                {
                    Draw.Text(g, string.Format("还有 {0} 项未显示", total - shown), t.Small, t.TextMuted,
                        new Rectangle(x, y, w, 18), StringAlignment.Near, StringAlignment.Center);
                }
            }

            Rectangle r = _rNew;
            bool hot = _hover == 3;
            Color bg = hot ? Draw.Blend(t.Accent, t.TextPrimary, 0.10f) : t.Accent;
            Draw.FillRound(g, r, ROW_RADIUS, bg);
            Draw.Text(g, "+ 新建日程", t.SmallBold, t.Bg, r,
                StringAlignment.Center, StringAlignment.Center);
        }

        private Rectangle RowRect(int index)
        {
            return new Rectangle(_rList.X, _rList.Y + index * ROW_STEP, _rList.Width, ROW_H);
        }

        private Rectangle CircleRect(Rectangle row)
        {
            return new Rectangle(row.X + 14, row.Y + (ROW_H - 18) / 2, 18, 18);
        }

        private void DrawRow(Graphics g, Theme t, int index, Rectangle row)
        {
            ScheduleItem it = _dayItems[index];
            if (it == null) return;

            bool hot = _hover == 10 + index;
            bool circleHot = _hover == 100 + index;
            DateTime occ = OccurrenceOf(it, _selected);
            bool done = it.IsDoneOn(occ);

            Color fill = hot ? Draw.Blend(t.SurfaceAlt, t.AccentSoft, 0.55f) : t.SurfaceAlt;
            Draw.FillRound(g, row, ROW_RADIUS, fill);
            Draw.StrokeRound(g, row, ROW_RADIUS, Draw.Blend(t.Border, t.SurfaceAlt, 0.35f), 1f);

            Rectangle bar = new Rectangle(row.X + 10, row.Y + 12, 3, ROW_H - 24);
            Draw.FillRound(g, bar, 2, t.ResolveCategory(it.Color));

            Rectangle circle = CircleRect(row);
            if (done)
            {
                Draw.FillRound(g, circle, 9, t.Accent);
                DrawCheck(g, circle, t.Bg);
            }
            else
            {
                Draw.StrokeRound(g, circle, 9, circleHot ? t.Accent : t.TextMuted, 1.4f);
            }

            string time = string.Format("{0:00}:{1:00}", it.Hour, it.Minute);
            Draw.Text(g, time, t.Small, done ? t.TextMuted : t.TextSecondary,
                new Rectangle(row.X + 40, row.Y, 44, ROW_H), StringAlignment.Near, StringAlignment.Center);

            string title = it.Title == null ? "" : it.Title;
            Draw.Ellipsis(g, title, t.Body, done ? t.TextMuted : t.TextPrimary,
                new Rectangle(row.X + 92, row.Y, row.Width - 92 - 12, ROW_H));
        }

        private static void DrawCheck(Graphics g, Rectangle c, Color color)
        {
            using (Pen pen = new Pen(color, 2f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                Point[] pts = new Point[3];
                pts[0] = new Point(c.X + 5, c.Y + c.Height / 2 + 1);
                pts[1] = new Point(c.X + 8, c.Y + c.Height - 5);
                pts[2] = new Point(c.X + c.Width - 4, c.Y + 5);
                g.DrawLines(pen, pts);
            }
        }

        private void DrawStatus(Graphics g, Theme t)
        {
            DateTime today = DateTime.Today;
            int total = 0;
            int done = 0;
            try
            {
                if (_store != null)
                {
                    total = _store.TotalForDay(today);
                    done = _store.DoneForDay(today);
                }
            }
            catch (Exception)
            {
            }

            Draw.Text(g, string.Format("今日 {0}/{1}", done, total), t.Small, t.TextSecondary,
                new Rectangle(PAD, _statusTop, 96, STATUS_H), StringAlignment.Near, StringAlignment.Center);

            Rectangle track = new Rectangle(PAD + 100, _statusTop + (STATUS_H - 6) / 2, 120, 6);
            Draw.FillRound(g, track, 3, Draw.Blend(t.Border, t.Surface, 0.30f));
            if (total > 0 && done > 0)
            {
                int pw = track.Width * done / total;
                if (pw < 4) pw = 4;
                if (pw > track.Width) pw = track.Width;
                Draw.FillRound(g, new Rectangle(track.X, track.Y, pw, track.Height), 3, t.Accent);
            }

            bool auto = false;
            try
            {
                auto = AutoStart.IsEnabled;
            }
            catch (Exception)
            {
            }

            DrawPill(g, _rAutoStart, auto ? "开机自启 · 开" : "开机自启 · 关", _hover == 4, auto, t);
            DrawPill(g, _rTheme, Theme.IsDark ? "浅色模式" : "深色模式", _hover == 5, false, t);
            DrawPill(g, _rTray, "最小化到托盘", _hover == 6, false, t);
        }

        // ---------------------------------------------------------------- 命中

        private int HitTest(Point p)
        {
            if (_rPrev.Contains(p)) return 0;
            if (_rNext.Contains(p)) return 1;
            if (_rToday.Contains(p)) return 2;
            if (_rNew.Contains(p)) return 3;
            if (_rAutoStart.Contains(p)) return 4;
            if (_rTheme.Contains(p)) return 5;
            if (_rTray.Contains(p)) return 6;
            if (_rMin.Contains(p)) return 7;
            if (_rClose.Contains(p)) return 8;

            if (_dayItems != null && _dayItems.Count > 0 && _rList.Contains(p))
            {
                int idx = (p.Y - _rList.Y) / ROW_STEP;
                if (idx >= 0 && idx < _dayItems.Count)
                {
                    Rectangle row = RowRect(idx);
                    if (row.Contains(p))
                    {
                        if (CircleRect(row).Contains(p)) return 100 + idx;
                        return 10 + idx;
                    }
                }
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = HitTest(e.Location);
            if (hit != _hover)
            {
                _hover = hit;
                Invalidate();
            }
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1)
            {
                _hover = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;

            int hit = HitTest(e.Location);
            if (hit < 0) return;

            if (hit == 0) { ShiftMonth(-1); return; }
            if (hit == 1) { ShiftMonth(1); return; }
            if (hit == 2) { GoToday(); return; }
            if (hit == 3) { CreateItem(); return; }
            if (hit == 4) { ToggleAutoStart(); return; }
            if (hit == 5) { ToggleTheme(); return; }
            if (hit == 6) { MinimizeToTray(); return; }
            if (hit == 7) { MinimizeToTray(); return; }
            if (hit == 8) { Close(); return; }

            if (hit >= 100)
            {
                ToggleDone(hit - 100);
                return;
            }

            if (hit >= 10)
            {
                int idx = hit - 10;
                DateTime now = DateTime.Now;
                if (_lastRowHit == hit && (now - _lastRowClick).TotalMilliseconds < 450)
                {
                    _lastRowHit = -1;
                    _lastRowClick = DateTime.MinValue;
                    if (idx < _dayItems.Count) OpenEditor(_dayItems[idx]);
                    return;
                }
                _lastRowHit = hit;
                _lastRowClick = now;
            }
        }

        // ---------------------------------------------------------------- 释放

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Theme.Changed -= HandleThemeChanged;
                if (_scheduler != null) _scheduler.RemindersDue -= HandleRemindersDue;
                for (int i = 0; i < _cards.Count; i++)
                {
                    try
                    {
                        if (!_cards[i].IsDisposed) _cards[i].Dispose();
                    }
                    catch (Exception)
                    {
                    }
                }
                _cards.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
