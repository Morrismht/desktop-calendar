using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using DesktopCalendar.Data;
using DesktopCalendar.Models;

namespace DesktopCalendar.UI
{
    /// <summary>
    /// 新建 / 编辑日程模态对话框，420×460 无边框圆角（选择「每周」时 420×502）。
    /// 所有输入控件都裁掉系统边框（BorderStyle=None）放进自绘圆角容器。
    /// 「重复」下拉是全自绘弹出列表：原生 ComboBox 的系统下拉比自绘字段宽、
    /// 并且会盖住右侧「颜色」行（build\verify-edit\editor-dropdown-open.png 已取证），故整体删除。
    /// </summary>
    public class EventEditor : Form
    {
        private const int W = 420;
        private const int H = 460;
        private const int H_WEEK = 502;
        private const int PAD = 24;
        private const int RADIUS = 14;
        private const int FIELD_RADIUS = 8;
        private const int BTN_RADIUS = 10;

        /// <summary>星期行占用的高度；出现时窗口高度 = H + WEEK_ROW_H。</summary>
        private const int WEEK_ROW_H = 42;
        private const int CHIP_W = 34;
        private const int CHIP_H = 28;
        private const int CHIP_GAP = 6;

        private const int DROP_ITEM_H = 30;
        private const int DROP_H = 4 * DROP_ITEM_H + 8;
        private const int DROP_RADIUS = 10;

        private const int WM_NCHITTEST = 0x0084;
        private const int HTCAPTION = 2;

        private static readonly string[] RepeatTexts = new string[] { "单次", "每天", "每周", "每月" };
        private static readonly string[] WeekLabels = new string[] { "一", "二", "三", "四", "五", "六", "日" };

        private readonly Store _store;
        private readonly ScheduleItem _existing;
        private readonly bool _isEdit;

        private TextBox _txtTitle;
        private TextBox _txtDate;
        private TextBox _txtTime;
        private TextBox _txtNotes;

        /// <summary>重复方式序号，与 RepeatKind 的 0..3 一一对应。</summary>
        private int _repeatIndex;
        /// <summary>下拉是否展开。</summary>
        private bool _dropOpen;
        private int _dropHover = -1;
        /// <summary>每周重复的星期掩码，bit0=周一 … bit6=周日；恒不为 0。</summary>
        private int _weekMask;
        private bool _showWeekRow;
        private bool _layoutApplied;

        private int _colorIndex;

        private Rectangle _boxTitle;
        private Rectangle _boxDate;
        private Rectangle _boxTime;
        private Rectangle _boxRepeat;
        private Rectangle _boxNotes;
        private Rectangle _btnPrevDay;
        private Rectangle _btnNextDay;
        private Rectangle _btnCancel;
        private Rectangle _btnSave;
        private Rectangle _btnClose;
        private Rectangle[] _dots;
        private Rectangle[] _weekChips;
        private Rectangle _weekLabel;
        private Rectangle _errRect;

        // -1 无；0 取消；1 保存；2 ‹；3 ›；4 关闭；5 重复字段；10+n 第 n 个色点；20+n 第 n 个星期
        private int _hover = -1;
        private string _error = "";

        /// <summary>保存后的结果；取消时为 null。</summary>
        public ScheduleItem Result { get; private set; }

        public EventEditor(Store store) : this(store, null) { }

        public EventEditor(Store store, ScheduleItem existing)
        {
            _store = store;
            _existing = existing;
            _isEdit = existing != null;

            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowIcon = false;
            MinimizeBox = false;
            MaximizeBox = false;
            KeyPreview = true;
            BackColor = Theme.Current.Bg;
            ClientSize = new Size(W, H);
            Text = _isEdit ? "编辑日程" : "新建日程";

            BuildLayout();
            BuildControls();
            FillFrom(existing);
            ApplyWeekLayout();

            try { Chrome.EnableRounded(this); }
            catch (Exception) { }
        }

        // ---------------------------------------------------------------- 布局

        private void BuildLayout()
        {
            int right = W - PAD;
            int dy = _showWeekRow ? WEEK_ROW_H : 0;

            _btnClose = new Rectangle(right - 28, 16, 28, 28);

            _boxTitle = new Rectangle(PAD, 79, right - PAD, 34);

            _boxDate = new Rectangle(PAD, 138, 160, 34);
            _btnPrevDay = new Rectangle(188, 138, 26, 34);
            _btnNextDay = new Rectangle(216, 138, 26, 34);
            _boxTime = new Rectangle(268, 138, right - 268, 34);

            _boxRepeat = new Rectangle(PAD, 197, 176, 32);

            _dots = new Rectangle[6];
            for (int i = 0; i < 6; i++)
            {
                _dots[i] = new Rectangle(220 + i * 30, 203, 20, 20);
            }

            // 星期行紧贴「重复」字段下方，独占 WEEK_ROW_H
            int chipTop = 235 + (WEEK_ROW_H - CHIP_H) / 2;
            _weekLabel = new Rectangle(PAD, chipTop, 48, CHIP_H);
            _weekChips = new Rectangle[7];
            int chipX = _weekLabel.Right + 8;
            for (int i = 0; i < 7; i++)
            {
                _weekChips[i] = new Rectangle(chipX + i * (CHIP_W + CHIP_GAP), chipTop, CHIP_W, CHIP_H);
            }

            _boxNotes = new Rectangle(PAD, 256 + dy, right - PAD, 100);

            _errRect = new Rectangle(PAD, 362 + dy, right - PAD, 18);

            _btnCancel = new Rectangle(right - 88 - 12 - 88, 396 + dy, 88, 36);
            _btnSave = new Rectangle(right - 88, 396 + dy, 88, 36);
        }

        private void ApplyRoundedRegion()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            using (GraphicsPath p = Draw.Round(new Rectangle(0, 0, w, h), RADIUS))
            {
                Region prev = Region;
                Region = new Region(p);
                if (prev != null) prev.Dispose();
            }
        }

        /// <summary>按 _showWeekRow 重算全部矩形、窗口高度与圆角 Region。</summary>
        private void ApplyWeekLayout()
        {
            bool show = _repeatIndex == 2;
            if (show == _showWeekRow && _layoutApplied)
            {
                Invalidate();
                return;
            }
            _showWeekRow = show;
            _layoutApplied = true;

            BuildLayout();
            Reposition();
            ClientSize = new Size(W, show ? H_WEEK : H);
            ApplyRoundedRegion();
            Invalidate();
        }

        // ---------------------------------------------------------------- 控件

        private TextBox MakeTextBox(bool multiline)
        {
            Theme t = Theme.Current;
            TextBox tb = new TextBox();
            tb.BorderStyle = BorderStyle.None;
            tb.BackColor = t.SurfaceAlt;
            tb.ForeColor = t.TextPrimary;
            tb.Font = t.Body != null ? t.Body : this.Font;
            tb.Multiline = multiline;
            if (multiline)
            {
                tb.ScrollBars = ScrollBars.None;
                tb.AcceptsReturn = true;
            }
            tb.Enter += delegate { Invalidate(); };
            tb.Leave += delegate { Invalidate(); };
            return tb;
        }

        private void PlaceSingle(TextBox tb, Rectangle box)
        {
            tb.Left = box.X + 12;
            tb.Width = box.Width - 24;
            tb.Top = box.Y + (box.Height - tb.Height) / 2;
        }

        private void BuildControls()
        {
            _txtTitle = MakeTextBox(false);
            _txtTitle.MaxLength = 200;
            Controls.Add(_txtTitle);

            _txtDate = MakeTextBox(false);
            _txtDate.MaxLength = 10;
            Controls.Add(_txtDate);

            _txtTime = MakeTextBox(false);
            _txtTime.MaxLength = 5;
            Controls.Add(_txtTime);

            _txtNotes = MakeTextBox(true);
            Controls.Add(_txtNotes);

            Reposition();
        }

        /// <summary>把 TextBox 摆到当前布局矩形里；星期行出现/隐藏后必须重跑。</summary>
        private void Reposition()
        {
            // 控件加入父级后才具备真实高度，此时再做垂直居中
            PlaceSingle(_txtTitle, _boxTitle);
            PlaceSingle(_txtDate, _boxDate);
            PlaceSingle(_txtTime, _boxTime);

            _txtNotes.Bounds = new Rectangle(_boxNotes.X + 12, _boxNotes.Y + 8,
                _boxNotes.Width - 24, _boxNotes.Height - 16);

            // 自绘下拉要盖在备注文本框之上：展开期间必须先藏起这个子窗口
            _txtNotes.Visible = !_dropOpen;
        }

        // ---------------------------------------------------------------- 重复下拉

        private Rectangle DropRect()
        {
            return new Rectangle(_boxRepeat.X, _boxRepeat.Bottom + 6, _boxRepeat.Width, DROP_H);
        }

        private int DropItemAt(Point p)
        {
            Rectangle r = DropRect();
            if (!r.Contains(p)) return -1;
            if (p.Y < r.Y + 4) return -1;
            int i = (p.Y - (r.Y + 4)) / DROP_ITEM_H;
            if (i < 0 || i >= RepeatTexts.Length) return -1;
            return i;
        }

        private static Rectangle DropItemRect(Rectangle panel, int index)
        {
            return new Rectangle(panel.X + 4, panel.Y + 4 + index * DROP_ITEM_H,
                panel.Width - 8, DROP_ITEM_H);
        }

        private void SetDropOpen(bool open)
        {
            if (_dropOpen == open) return;
            _dropOpen = open;
            _dropHover = -1;
            Reposition();
            Rectangle r = DropRect();
            r.Inflate(10, 10);
            Invalidate(r);
        }

        private void SetRepeatIndex(int index)
        {
            if (index < 0) index = 0;
            if (index >= RepeatTexts.Length) index = RepeatTexts.Length - 1;
            _repeatIndex = index;
            if (_repeatIndex == 2) EnsureWeekMask();
            ApplyWeekLayout();
        }

        private void EnsureWeekMask()
        {
            if (_weekMask != 0) return;
            DateTime d;
            if (!TryParseDate(_txtDate.Text, out d)) d = DateTime.Today;
            _weekMask = RepeatRule.MaskOf(d.DayOfWeek);
        }

        // ---------------------------------------------------------------- 星期

        /// <summary>chip 序号 0..6 → 周一..周日。</summary>
        private static DayOfWeek DowAt(int index)
        {
            return (DayOfWeek)((index + 1) % 7);
        }

        private static int CountWeekDays(int mask)
        {
            int n = 0;
            for (int i = 0; i < 7; i++)
            {
                if (RepeatRule.MaskHas(mask, DowAt(i))) n++;
            }
            return n;
        }

        /// <summary>至少保 1 天：点掉最后一天时忽略该次点击。</summary>
        private void ToggleWeekDay(int index)
        {
            DayOfWeek dow = DowAt(index);
            if (RepeatRule.MaskHas(_weekMask, dow))
            {
                if (CountWeekDays(_weekMask) <= 1) return;
                _weekMask = _weekMask & ~RepeatRule.MaskOf(dow);
            }
            else
            {
                _weekMask = _weekMask | RepeatRule.MaskOf(dow);
            }
            Invalidate(new Rectangle(PAD, 235, W - PAD - 14, WEEK_ROW_H));
        }

        // ---------------------------------------------------------------- 数据

        private void FillFrom(ScheduleItem it)
        {
            _colorIndex = 0;
            if (it == null)
            {
                DateTime now = DateTime.Now;
                DateTime d = now.AddMinutes(30 - (now.Minute % 30));
                if (d.Minute == 60) d = d.AddMinutes(30);
                _txtTitle.Text = "";
                _txtDate.Text = d.ToString("yyyy-MM-dd");
                _txtTime.Text = d.ToString("HH:mm");
                _txtNotes.Text = "";
                _repeatIndex = 0;
                _weekMask = RepeatRule.MaskOf(d.DayOfWeek);
                return;
            }

            _txtTitle.Text = it.Title == null ? "" : it.Title;
            _txtDate.Text = it.Date.ToString("yyyy-MM-dd");
            _txtTime.Text = string.Format("{0:00}:{1:00}", it.Hour, it.Minute);
            _txtNotes.Text = it.Notes == null ? "" : it.Notes;
            _repeatIndex = (int)it.Repeat;
            if (_repeatIndex < 0) _repeatIndex = 0;
            if (_repeatIndex >= RepeatTexts.Length) _repeatIndex = RepeatTexts.Length - 1;
            _weekMask = it.WeekDays;
            // Weekly 且未指定星期 → 默认勾选 Date 的星期
            if (_weekMask == 0) _weekMask = RepeatRule.MaskOf(it.Date.DayOfWeek);
            _colorIndex = PaletteIndexOf(it.Color);
        }

        private static int PaletteIndexOf(string hex)
        {
            Color[] pal = Theme.Current.Palette;
            if (pal == null) return 0;
            Color want = ParseHex(hex, Color.Empty);
            if (want == Color.Empty) return 0;
            for (int i = 0; i < pal.Length; i++)
            {
                if (pal[i].ToArgb() == want.ToArgb()) return i;
            }
            return 0;
        }

        private Color SelectedColor()
        {
            Color[] pal = Theme.Current.Palette;
            if (pal == null || pal.Length == 0) return Theme.Current.Accent;
            int i = _colorIndex;
            if (i < 0) i = 0;
            if (i >= pal.Length) i = pal.Length - 1;
            return pal[i];
        }

        private static string HexOf(Color c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
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

        // ---------------------------------------------------------------- 绘制

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Theme.Current;
            Graphics g = e.Graphics;
            Draw.Smooth(g);

            Rectangle full = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            using (GraphicsPath p = Draw.Round(full, RADIUS))
            {
                using (SolidBrush b = new SolidBrush(t.Surface)) g.FillPath(b, p);
                using (Pen pen = new Pen(t.Border, 1f)) g.DrawPath(pen, p);
            }

            Font titleFont = t.Title != null ? t.Title : this.Font;
            Font bodyFont = t.Body != null ? t.Body : this.Font;
            Font smallFont = t.Small != null ? t.Small : this.Font;
            Font smallBold = t.SmallBold != null ? t.SmallBold : this.Font;

            int dy = _showWeekRow ? WEEK_ROW_H : 0;

            Draw.Ellipsis(g, _isEdit ? "编辑日程" : "新建日程", titleFont, t.TextPrimary,
                new Rectangle(PAD, 14, W - PAD * 2 - 40, 32));

            // 关闭 ×
            Color closeColor = _hover == 4 ? t.Danger : t.TextMuted;
            if (_hover == 4) Draw.FillRound(g, _btnClose, 6, t.SurfaceAlt);
            DrawClose(g, _btnClose, closeColor);

            using (Pen pen = new Pen(t.Border, 1f))
            {
                g.DrawLine(pen, PAD, 56, W - PAD, 56);
            }

            DrawLabel(g, "标题", new Rectangle(PAD, 62, 120, 15), smallFont, t.TextSecondary);
            DrawLabel(g, "日期", new Rectangle(PAD, 121, 120, 15), smallFont, t.TextSecondary);
            DrawLabel(g, "时间", new Rectangle(268, 121, 100, 15), smallFont, t.TextSecondary);
            DrawLabel(g, "重复", new Rectangle(PAD, 180, 120, 15), smallFont, t.TextSecondary);
            DrawLabel(g, "颜色", new Rectangle(220, 180, 100, 15), smallFont, t.TextSecondary);
            DrawLabel(g, "备注", new Rectangle(PAD, 239 + dy, 120, 15), smallFont, t.TextSecondary);

            DrawField(g, _boxTitle, _txtTitle, t);
            DrawField(g, _boxDate, _txtDate, t);
            DrawField(g, _boxTime, _txtTime, t);
            DrawField(g, _boxNotes, _txtNotes, t);

            // 日期微调按钮
            DrawGhost(g, _btnPrevDay, _hover == 2, t);
            Draw.Text(g, "\u2039", bodyFont, _hover == 2 ? t.Accent : t.TextSecondary, _btnPrevDay,
                StringAlignment.Center, StringAlignment.Center);
            DrawGhost(g, _btnNextDay, _hover == 3, t);
            Draw.Text(g, "\u203A", bodyFont, _hover == 3 ? t.Accent : t.TextSecondary, _btnNextDay,
                StringAlignment.Center, StringAlignment.Center);

            // 重复字段：容器边框、文字、箭头全部自绘，下拉列表见 DrawDropDown
            DrawFieldBg(g, _boxRepeat, t, _dropOpen || _hover == 5);
            Draw.Text(g, RepeatTexts[_repeatIndex], bodyFont, t.TextPrimary,
                new Rectangle(_boxRepeat.X + 12, _boxRepeat.Y, _boxRepeat.Width - 44, _boxRepeat.Height),
                StringAlignment.Near, StringAlignment.Center);
            DrawChevron(g, new Rectangle(_boxRepeat.Right - 26, _boxRepeat.Y, 16, _boxRepeat.Height),
                t.TextMuted);

            // 颜色圆点
            if (t.Palette != null)
            {
                for (int i = 0; i < _dots.Length && i < t.Palette.Length; i++)
                {
                    Rectangle d = _dots[i];
                    bool sel = i == _colorIndex;
                    using (SolidBrush b = new SolidBrush(t.Palette[i])) g.FillEllipse(b, d);
                    if (sel)
                    {
                        Rectangle ring = new Rectangle(d.X - 3, d.Y - 3, d.Width + 6, d.Height + 6);
                        using (Pen pen = new Pen(t.TextPrimary, 2f)) g.DrawEllipse(pen, ring);
                    }
                    else if (_hover == 10 + i)
                    {
                        Rectangle ring = new Rectangle(d.X - 3, d.Y - 3, d.Width + 6, d.Height + 6);
                        using (Pen pen = new Pen(t.Border, 2f)) g.DrawEllipse(pen, ring);
                    }
                }
            }

            DrawWeekRow(g, t, smallFont);

            // 校验红字
            if (_error != null && _error.Length > 0)
            {
                Draw.Text(g, _error, smallFont, t.Danger, _errRect,
                    StringAlignment.Near, StringAlignment.Center);
            }

            // [取消] 描边
            Draw.FillRound(g, _btnCancel, BTN_RADIUS, _hover == 0 ? t.SurfaceAlt : t.Surface);
            Draw.StrokeRound(g, _btnCancel, BTN_RADIUS, _hover == 0 ? t.Accent : t.Border, 1f);
            Draw.Text(g, "取消", smallBold, _hover == 0 ? t.Accent : t.TextPrimary, _btnCancel,
                StringAlignment.Center, StringAlignment.Center);

            // [保存] 实心
            Draw.FillRound(g, _btnSave, BTN_RADIUS, t.Accent);
            Draw.Text(g, "保存", smallBold, Color.White, _btnSave,
                StringAlignment.Center, StringAlignment.Center);

            // 弹出列表最后画，压在其它内容之上
            if (_dropOpen) DrawDropDown(g, t, bodyFont);
        }

        private void DrawWeekRow(Graphics g, Theme t, Font smallFont)
        {
            if (!_showWeekRow) return;

            DrawLabel(g, "重复日", _weekLabel, smallFont, t.TextSecondary);
            for (int i = 0; i < _weekChips.Length; i++)
            {
                Rectangle c = _weekChips[i];
                bool sel = RepeatRule.MaskHas(_weekMask, DowAt(i));
                Draw.FillRound(g, c, FIELD_RADIUS, sel ? t.Accent : t.SurfaceAlt);
                Draw.Text(g, WeekLabels[i], smallFont, sel ? Color.White : t.TextSecondary, c,
                    StringAlignment.Center, StringAlignment.Center);
            }
        }

        private void DrawDropDown(Graphics g, Theme t, Font bodyFont)
        {
            Rectangle r = DropRect();

            // 裁剪到字段的横向范围：填充、描边抗锯齿和 5px 阴影都不得越过 _boxRepeat 的左右边界
            using (Region old = g.Clip)
            {
                g.SetClip(new Rectangle(_boxRepeat.X, 0, _boxRepeat.Width, ClientSize.Height),
                    CombineMode.Replace);

                Draw.Shadow(g, r, DROP_RADIUS, t.Shadow, 5, 0.18f);
                Draw.FillRound(g, r, DROP_RADIUS, t.Surface);
                Draw.StrokeRound(g, r, DROP_RADIUS, t.Border, 1f);

                for (int i = 0; i < RepeatTexts.Length; i++)
                {
                    Rectangle ir = DropItemRect(r, i);
                    bool sel = i == _repeatIndex;
                    bool hot = i == _dropHover && !sel;
                    if (sel) Draw.FillRound(g, ir, 6, t.AccentSoft);
                    else if (hot) Draw.FillRound(g, ir, 6, t.SurfaceAlt);
                    if (sel)
                    {
                        Draw.FillRound(g, new Rectangle(ir.X + 3, ir.Y + 6, 3, ir.Height - 12), 1, t.Accent);
                    }
                    Draw.Text(g, RepeatTexts[i], bodyFont,
                        sel ? t.TextPrimary : (hot ? t.TextPrimary : t.TextSecondary),
                        new Rectangle(ir.X + 14, ir.Y, ir.Width - 18, ir.Height),
                        StringAlignment.Near, StringAlignment.Center);
                }

                g.Clip = old;
            }
        }

        private static void DrawLabel(Graphics g, string s, Rectangle r, Font f, Color c)
        {
            Draw.Text(g, s, f, c, r, StringAlignment.Near, StringAlignment.Center);
        }

        private static void DrawClose(Graphics g, Rectangle r, Color c)
        {
            int pad = 9;
            using (Pen pen = new Pen(c, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, r.X + pad, r.Y + pad, r.Right - pad, r.Bottom - pad);
                g.DrawLine(pen, r.Right - pad, r.Y + pad, r.X + pad, r.Bottom - pad);
            }
        }

        private static void DrawGhost(Graphics g, Rectangle r, bool hot, Theme t)
        {
            Draw.FillRound(g, r, 6, hot ? t.SurfaceAlt : t.Surface);
            Draw.StrokeRound(g, r, 6, hot ? t.Accent : t.Border, 1f);
        }

        private static void DrawChevron(Graphics g, Rectangle r, Color c)
        {
            int cx = r.X + r.Width / 2;
            int cy = r.Y + r.Height / 2;
            using (Pen pen = new Pen(c, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                Point[] pts = new Point[3];
                pts[0] = new Point(cx - 4, cy - 2);
                pts[1] = new Point(cx, cy + 2);
                pts[2] = new Point(cx + 4, cy - 2);
                g.DrawLines(pen, pts);
            }
        }

        private static void DrawFieldBg(Graphics g, Rectangle r, Theme t, bool active)
        {
            Draw.FillRound(g, r, FIELD_RADIUS, t.SurfaceAlt);
            Draw.StrokeRound(g, r, FIELD_RADIUS, active ? t.Accent : t.Border, 1f);
        }

        private void DrawField(Graphics g, Rectangle box, TextBox tb, Theme t)
        {
            bool focused = tb != null && ActiveControl == tb;
            DrawFieldBg(g, box, t, focused);
        }

        // ---------------------------------------------------------------- 命中

        private int HitTest(Point p)
        {
            if (_btnCancel.Contains(p)) return 0;
            if (_btnSave.Contains(p)) return 1;
            if (_btnPrevDay.Contains(p)) return 2;
            if (_btnNextDay.Contains(p)) return 3;
            if (_btnClose.Contains(p)) return 4;
            if (_boxRepeat.Contains(p)) return 5;
            if (_dots != null)
            {
                for (int i = 0; i < _dots.Length; i++)
                {
                    Rectangle hit = new Rectangle(_dots[i].X - 5, _dots[i].Y - 4,
                        _dots[i].Width + 10, _dots[i].Height + 8);
                    if (hit.Contains(p)) return 10 + i;
                }
            }
            if (_showWeekRow && _weekChips != null)
            {
                for (int i = 0; i < _weekChips.Length; i++)
                {
                    if (_weekChips[i].Contains(p)) return 20 + i;
                }
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hit = HitTest(e.Location);
            int dh = _dropOpen ? DropItemAt(e.Location) : -1;
            if (hit != _hover || dh != _dropHover)
            {
                _hover = hit;
                _dropHover = dh;
                if (_dropOpen)
                {
                    Rectangle r = DropRect();
                    r.Inflate(12, 12);
                    Invalidate(r);
                }
                else
                {
                    Invalidate();
                }
            }
            Cursor = (hit >= 0 || dh >= 0) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover != -1 || _dropHover != -1)
            {
                _hover = -1;
                _dropHover = -1;
                if (_dropOpen)
                {
                    Rectangle r = DropRect();
                    r.Inflate(12, 12);
                    Invalidate(r);
                }
                else
                {
                    Invalidate();
                }
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (_dropOpen)
                {
                    int idx = DropItemAt(e.Location);
                    SetDropOpen(false);
                    if (idx >= 0) { SetRepeatIndex(idx); return; }
                    // 点在字段上：面板已收起，本次不再展开；点在外部：仅收起
                    return;
                }

                int hit = HitTest(e.Location);
                if (hit == 0) { DoCancel(); return; }
                if (hit == 1) { DoSave(); return; }
                if (hit == 2) { ShiftDay(-1); return; }
                if (hit == 3) { ShiftDay(1); return; }
                if (hit == 4) { DoCancel(); return; }
                if (hit == 5) { SetDropOpen(true); return; }
                if (hit >= 20 && hit <= 26)
                {
                    ToggleWeekDay(hit - 20);
                    return;
                }
                if (hit >= 10)
                {
                    _colorIndex = hit - 10;
                    Invalidate();
                    return;
                }
            }
            base.OnMouseDown(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                int lp = m.LParam.ToInt32();
                int sx = (short)(lp & 0xFFFF);
                int sy = (short)((lp >> 16) & 0xFFFF);
                Point cp = PointToClient(new Point(sx, sy));
                if (HitTest(cp) < 0 && cp.Y < 60)
                {
                    m.Result = new IntPtr(HTCAPTION);
                    return;
                }
            }
            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_dropOpen)
            {
                if (keyData == Keys.Escape)
                {
                    SetDropOpen(false);
                    return true;
                }
                if (keyData == Keys.Down)
                {
                    SetRepeatIndex(_repeatIndex + 1);
                    return true;
                }
                if (keyData == Keys.Up)
                {
                    SetRepeatIndex(_repeatIndex - 1);
                    return true;
                }
                if (keyData == Keys.Enter)
                {
                    SetDropOpen(false);
                    return true;
                }
            }
            else
            {
                if (keyData == Keys.Escape)
                {
                    DoCancel();
                    return true;
                }
                if (keyData == Keys.Enter && ActiveControl != _txtNotes)
                {
                    DoSave();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------------------------------------------------------------- 动作

        private void ShiftDay(int delta)
        {
            DateTime d;
            if (!TryParseDate(_txtDate.Text, out d)) d = DateTime.Today;
            _txtDate.Text = d.AddDays(delta).ToString("yyyy-MM-dd");
            Invalidate();
        }

        private static bool TryParseDate(string s, out DateTime value)
        {
            value = DateTime.MinValue;
            if (s == null) return false;
            string t = s.Trim();
            if (t.Length == 0) return false;
            if (DateTime.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out value)) return true;
            if (DateTime.TryParseExact(t, "yyyy-M-d", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out value)) return true;
            return false;
        }

        private static bool TryParseTime(string s, out int hour, out int minute)
        {
            hour = 0;
            minute = 0;
            if (s == null) return false;
            string t = s.Trim();
            if (t.Length == 0) return false;
            DateTime value;
            if (!DateTime.TryParseExact(t, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out value))
            {
                return false;
            }
            hour = value.Hour;
            minute = value.Minute;
            return true;
        }

        private void DoCancel()
        {
            Result = null;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void DoSave()
        {
            string title = _txtTitle.Text == null ? "" : _txtTitle.Text.Trim();
            if (title.Length == 0)
            {
                Fail("请填写日程标题");
                _txtTitle.Focus();
                return;
            }

            DateTime date;
            if (!TryParseDate(_txtDate.Text, out date))
            {
                Fail("日期格式不正确，请使用 yyyy-MM-dd，例如 2025-03-08");
                _txtDate.Focus();
                return;
            }

            int hour;
            int minute;
            if (!TryParseTime(_txtTime.Text, out hour, out minute))
            {
                Fail("时间格式不正确，请使用 HH:mm，例如 09:30");
                _txtTime.Focus();
                return;
            }

            string notes = _txtNotes.Text == null ? "" : _txtNotes.Text;
            int repeat = _repeatIndex;
            if (repeat < 0) repeat = 0;
            if (repeat >= RepeatTexts.Length) repeat = RepeatTexts.Length - 1;

            // 只有每周才写星期掩码；mask 恰为 0 时按 Date 的星期补成 1 位
            int weekMask = 0;
            if ((RepeatKind)repeat == RepeatKind.Weekly)
            {
                weekMask = _weekMask;
                if (weekMask == 0) weekMask = RepeatRule.MaskOf(date.DayOfWeek);
            }

            string color = HexOf(SelectedColor());

            if (_existing == null)
            {
                ScheduleItem it = new ScheduleItem();
                it.Id = Guid.NewGuid().ToString("N");
                it.CreatedAt = DateTime.Now;
                it.Enabled = true;
                it.Title = title;
                it.Notes = notes;
                it.Date = date.Date;
                it.Hour = hour;
                it.Minute = minute;
                it.Repeat = (RepeatKind)repeat;
                it.RepeatUntil = null;
                it.WeekDays = weekMask;
                it.Color = color;
                if (_store != null) _store.Add(it);
                Result = it;
            }
            else
            {
                // 编辑模式保留 Id / CreatedAt / 提醒状态
                _existing.Title = title;
                _existing.Notes = notes;
                _existing.Date = date.Date;
                _existing.Hour = hour;
                _existing.Minute = minute;
                _existing.Repeat = (RepeatKind)repeat;
                _existing.WeekDays = weekMask;
                _existing.Color = color;
                _existing.Enabled = true;
                if (_store != null) _store.Update(_existing);
                Result = _existing;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void Fail(string message)
        {
            _error = message;
            Invalidate();
        }
    }
}
