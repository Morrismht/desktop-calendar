using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DesktopCalendar.Data;

namespace DesktopCalendar.UI
{
    /// <summary>
    /// 「每日提示」编辑对话框：列表 + 新增/删除 + 多行输入 + 恢复默认/取消/保存。
    /// 视觉语言与 EventEditor 一致：无边框、Chrome 圆角、Theme.Current 取色、Draw 画圆角与按钮。
    /// 编辑的是一份内存副本，点「保存」且全部校验通过后才写回 TipStore（由调用方落盘）。
    /// </summary>
    public class TipEditor : Form
    {
        private const int W = 520;
        private const int H = 560;
        private const int PAD = 24;
        private const int RADIUS = 14;
        private const int FIELD_RADIUS = 8;
        private const int BTN_RADIUS = 10;
        private const int ROW_H = 36;
        private const int ROW_STEP = 40;

        private const int WM_NCHITTEST = 0x0084;
        private const int HTCAPTION = 2;

        // 按钮/关闭按钮的命中 id
        private const int HOVER_NONE = -1;
        private const int HOVER_ADD = 0;
        private const int HOVER_DEL = 1;
        private const int HOVER_RESET = 2;
        private const int HOVER_CANCEL = 3;
        private const int HOVER_SAVE = 4;
        private const int HOVER_CLOSE = 5;
        // 列表行命中基数：HIT_ROW + 可见槽位
        private const int HIT_ROW = 100;

        private readonly TipStore _store;
        private readonly List<string> _tips = new List<string>();

        private TextBox _txtTip;
        private bool _syncing;
        private bool _ready;

        private int _editIndex = -1;
        private int _scroll;
        private int _hover = HOVER_NONE;
        private string _error = "";

        private Rectangle _rList;
        private Rectangle _rAdd;
        private Rectangle _rDel;
        private Rectangle _rCount;
        private Rectangle _rBox;
        private Rectangle _rErr;
        private Rectangle _rReset;
        private Rectangle _rCancel;
        private Rectangle _rSave;
        private Rectangle _rClose;

        /// <summary>点过「保存」且校验通过（TipStore 已被改写，调用方负责 Save 到磁盘）。</summary>
        public bool Saved { get; private set; }

        public TipEditor(TipStore store)
        {
            _store = store;
            if (_store != null && _store.Tips != null) _tips.AddRange(_store.Tips);
            if (_tips.Count > 0) _editIndex = 0;

            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ShowIcon = false;
            MinimizeBox = false;
            MaximizeBox = false;
            KeyPreview = true;
            Text = "编辑每日提示";
            ClientSize = new Size(W, H);
            BackColor = Theme.Current.Bg;

            _txtTip = MakeTextBox();
            _txtTip.TextChanged += HandleTextChanged;
            Controls.Add(_txtTip);

            _ready = true;
            LayoutRegions();
            ApplyRoundedRegion();
            SyncBox();
            try
            {
                Chrome.EnableRounded(this);
            }
            catch (Exception)
            {
            }
        }

        // ---------------------------------------------------------------- 布局

        private void LayoutRegions()
        {
            _rClose = new Rectangle(W - PAD - 28, 16, 28, 28);
            _rList = new Rectangle(PAD, 72, W - PAD * 2, 232);
            _rAdd = new Rectangle(PAD, 312, 72, 30);
            _rDel = new Rectangle(PAD + 80, 312, 72, 30);
            _rCount = new Rectangle(PAD + 164, 312, W - PAD * 2 - 164, 30);
            _rBox = new Rectangle(PAD, 360, W - PAD * 2, 112);
            _rErr = new Rectangle(PAD, 478, W - PAD * 2, 18);
            _rReset = new Rectangle(PAD, 502, 96, 36);
            _rSave = new Rectangle(W - PAD - 88, 502, 88, 36);
            _rCancel = new Rectangle(_rSave.X - 12 - 88, 502, 88, 36);

            if (_txtTip != null)
            {
                _txtTip.Bounds = new Rectangle(_rBox.X + 12, _rBox.Y + 10,
                    _rBox.Width - 24, _rBox.Height - 20);
            }
        }

        private void ApplyRoundedRegion()
        {
            try
            {
                using (GraphicsPath p = Draw.Round(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), RADIUS))
                {
                    Region prev = Region;
                    Region = new Region(p);
                    if (prev != null) prev.Dispose();
                }
            }
            catch (Exception)
            {
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!_ready) return;
            LayoutRegions();
            ApplyRoundedRegion();
            Invalidate();
        }

        private int VisibleRows()
        {
            int n = (_rList.Height - 8) / ROW_STEP;
            if (n < 1) n = 1;
            return n;
        }

        private int MaxScroll()
        {
            int n = _tips.Count - VisibleRows();
            return n < 0 ? 0 : n;
        }

        private Rectangle RowRect(int slot)
        {
            return new Rectangle(_rList.X + 2, _rList.Y + 4 + slot * ROW_STEP,
                _rList.Width - 4, ROW_H);
        }

        private void ScrollTo(int index)
        {
            int visible = VisibleRows();
            if (index >= 0)
            {
                if (index < _scroll) _scroll = index;
                if (index >= _scroll + visible) _scroll = index - visible + 1;
            }
            if (_scroll < 0) _scroll = 0;
            if (_scroll > MaxScroll()) _scroll = MaxScroll();
        }

        private Font Use(Font f)
        {
            return f != null ? f : this.Font;
        }

        private TextBox MakeTextBox()
        {
            Theme t = Theme.Current;
            TextBox box = new TextBox();
            box.BorderStyle = BorderStyle.None;
            box.Multiline = true;
            box.AcceptsReturn = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.MaxLength = 400;
            box.BackColor = t.SurfaceAlt;
            box.ForeColor = t.TextPrimary;
            box.Font = Use(t.Body);
            box.Enter += delegate { Invalidate(); };
            box.Leave += delegate { Invalidate(); };
            return box;
        }

        // ---------------------------------------------------------------- 绘制

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme t = Theme.Current;
            Draw.Smooth(g);

            Rectangle full = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            using (GraphicsPath path = Draw.Round(full, RADIUS))
            {
                using (SolidBrush b = new SolidBrush(t.Surface)) g.FillPath(b, path);
                using (Pen p = new Pen(t.Border, 1f)) g.DrawPath(p, path);
            }

            Draw.Text(g, "每日提示", Use(t.Title), t.TextPrimary,
                new Rectangle(PAD, 14, 300, 30), StringAlignment.Near, StringAlignment.Center);
            DrawClose(g, _rClose, _hover == HOVER_CLOSE ? t.Danger : t.TextMuted);

            using (Pen p = new Pen(t.Border, 1f)) g.DrawLine(p, PAD, 56, W - PAD, 56);

            DrawList(g, t);

            bool hasSel = _editIndex >= 0 && _editIndex < _tips.Count;
            DrawChip(g, _rAdd, "新增", _hover == HOVER_ADD, true, t);
            DrawChip(g, _rDel, "删除", _hover == HOVER_DEL, hasSel, t);

            string counter;
            if (hasSel)
            {
                counter = string.Format("共 {0} 条 · 单条 {1} 字以内 · 当前 {2} 字",
                    _tips.Count, TipStore.MaxLength, _tips[_editIndex] == null ? 0 : _tips[_editIndex].Length);
            }
            else
            {
                counter = string.Format("共 {0} 条 · 单条 {1} 字以内", _tips.Count, TipStore.MaxLength);
            }
            Draw.Text(g, counter, Use(t.Small), t.TextMuted, _rCount,
                StringAlignment.Far, StringAlignment.Center);

            Draw.Text(g, "提示内容", Use(t.Small), t.TextMuted,
                new Rectangle(PAD, 342, 200, 16), StringAlignment.Near, StringAlignment.Center);
            DrawFieldBg(g, _rBox, t, _txtTip != null && _txtTip.Focused);

            string msg = _error;
            bool bad = msg.Length > 0;
            if (!bad)
            {
                if (!hasSel) msg = "点列表里的条目，或点「新增」开始写";
                else msg = "回车保存，Esc 取消";
            }
            Draw.Text(g, msg, Use(t.Small), bad ? t.Danger : t.TextMuted, _rErr,
                StringAlignment.Near, StringAlignment.Center);

            DrawGhost(g, _rReset, _hover == HOVER_RESET, "恢复默认", t, Use(t.Body));
            DrawGhost(g, _rCancel, _hover == HOVER_CANCEL, "取消", t, Use(t.Body));

            Draw.FillRound(g, _rSave, BTN_RADIUS, _hover == HOVER_SAVE ? Draw.Blend(t.Accent, t.TextPrimary, 0.10f) : t.Accent);
            Draw.Text(g, "保存", Use(t.Body), Color.White, _rSave,
                StringAlignment.Center, StringAlignment.Center);
        }

        private void DrawList(Graphics g, Theme t)
        {
            Draw.FillRound(g, _rList, FIELD_RADIUS, t.SurfaceAlt);
            Draw.StrokeRound(g, _rList, FIELD_RADIUS, t.Border, 1f);

            GraphicsState st = g.Save();
            g.SetClip(_rList, CombineMode.Intersect);
            try
            {
                if (_tips.Count == 0)
                {
                    Draw.Text(g, "还没有自定义提示 · 点「新增」写第一条", Use(t.Small), t.TextMuted,
                        new Rectangle(_rList.X, _rList.Y, _rList.Width, _rList.Height),
                        StringAlignment.Center, StringAlignment.Center);
                }
                int visible = VisibleRows();
                for (int slot = 0; slot < visible; slot++)
                {
                    int idx = _scroll + slot;
                    if (idx >= _tips.Count) break;

                    Rectangle row = RowRect(slot);
                    bool sel = idx == _editIndex;
                    bool hot = _hover == HIT_ROW + slot;
                    Color face;
                    if (sel) face = Draw.Blend(t.SurfaceAlt, t.Accent, 0.20f);
                    else if (hot) face = Draw.Blend(t.SurfaceAlt, t.TextPrimary, 0.06f);
                    else face = t.SurfaceAlt;

                    Draw.FillRound(g, row, 6, face);
                    if (sel)
                    {
                        Draw.StrokeRound(g, row, 6, t.Accent, 1f);
                        Draw.FillRound(g, new Rectangle(row.X + 4, row.Y + 9, 3, row.Height - 18), 2, t.Accent);
                    }

                    string s = _tips[idx];
                    string label = (s == null || s.Length == 0) ? "（空条目）" : s;
                    Color fg = (s == null || s.Length == 0) ? t.TextMuted : (sel ? t.TextPrimary : t.TextSecondary);
                    Draw.Ellipsis(g, label, Use(t.Body), fg,
                        new Rectangle(row.X + 16, row.Y, row.Width - 16 - 44, row.Height));
                    Draw.Text(g, (idx + 1).ToString(), Use(t.Small), t.TextMuted,
                        new Rectangle(row.Right - 40, row.Y, 30, row.Height),
                        StringAlignment.Far, StringAlignment.Center);
                }

                int max = MaxScroll();
                if (max > 0)
                {
                    int trackH = _rList.Height - 16;
                    int barH = trackH * VisibleRows() / _tips.Count;
                    if (barH < 24) barH = 24;
                    if (barH > trackH) barH = trackH;
                    int barY = _rList.Y + 8 + (trackH - barH) * _scroll / max;
                    Draw.FillRound(g, new Rectangle(_rList.Right - 7, barY, 3, barH), 2,
                        Draw.Blend(t.Border, t.TextMuted, 0.35f));
                }
            }
            finally
            {
                g.Restore(st);
            }
        }

        private void DrawChip(Graphics g, Rectangle r, string label, bool hot, bool enabled, Theme t)
        {
            Color face = hot && enabled ? Draw.Blend(t.SurfaceAlt, t.Accent, 0.14f) : t.SurfaceAlt;
            Draw.FillRound(g, r, 8, face);
            Draw.StrokeRound(g, r, 8, hot && enabled ? t.Accent : t.Border, 1f);
            Color fg = !enabled ? t.TextMuted : (hot ? t.Accent : t.TextPrimary);
            Draw.Text(g, label, Use(t.Small), fg, r, StringAlignment.Center, StringAlignment.Center);
        }

        private void DrawGhost(Graphics g, Rectangle r, bool hot, string label, Theme t, Font f)
        {
            Draw.FillRound(g, r, BTN_RADIUS, hot ? t.SurfaceAlt : t.Surface);
            Draw.StrokeRound(g, r, BTN_RADIUS, hot ? t.Accent : t.Border, 1f);
            Draw.Text(g, label, f, hot ? t.Accent : t.TextPrimary, r,
                StringAlignment.Center, StringAlignment.Center);
        }

        private void DrawFieldBg(Graphics g, Rectangle r, Theme t, bool active)
        {
            Draw.FillRound(g, r, FIELD_RADIUS, t.SurfaceAlt);
            Draw.StrokeRound(g, r, FIELD_RADIUS, active ? t.Accent : t.Border, 1f);
        }

        private void DrawClose(Graphics g, Rectangle r, Color c)
        {
            int pad = 9;
            using (Pen p = new Pen(c, 1.6f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                g.DrawLine(p, r.X + pad, r.Y + pad, r.Right - pad, r.Bottom - pad);
                g.DrawLine(p, r.Right - pad, r.Y + pad, r.X + pad, r.Bottom - pad);
            }
        }

        // ---------------------------------------------------------------- 命中与鼠标

        private int HitTest(Point p)
        {
            if (_rClose.Contains(p)) return HOVER_CLOSE;
            if (_rSave.Contains(p)) return HOVER_SAVE;
            if (_rCancel.Contains(p)) return HOVER_CANCEL;
            if (_rReset.Contains(p)) return HOVER_RESET;
            if (_rAdd.Contains(p)) return HOVER_ADD;
            if (_rDel.Contains(p)) return HOVER_DEL;
            if (_rList.Contains(p))
            {
                int slot = (p.Y - (_rList.Y + 4)) / ROW_STEP;
                if (slot >= 0 && slot < VisibleRows())
                {
                    Rectangle row = RowRect(slot);
                    if (row.Contains(p) && _scroll + slot < _tips.Count) return HIT_ROW + slot;
                }
            }
            return HOVER_NONE;
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
            if (_hover != HOVER_NONE)
            {
                _hover = HOVER_NONE;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            int hit = HitTest(e.Location);
            if (hit >= HIT_ROW)
            {
                SelectRow(_scroll + hit - HIT_ROW);
                return;
            }
            if (hit == HOVER_CLOSE) { DoCancel(); return; }
            if (hit == HOVER_SAVE) { DoSave(); return; }
            if (hit == HOVER_CANCEL) { DoCancel(); return; }
            if (hit == HOVER_RESET) { DoReset(); return; }
            if (hit == HOVER_ADD) { DoAdd(); return; }
            if (hit == HOVER_DEL) { DoDelete(); return; }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int max = MaxScroll();
            if (max <= 0) return;
            int next = _scroll + (e.Delta > 0 ? -1 : 1);
            if (next < 0) next = 0;
            if (next > max) next = max;
            if (next != _scroll)
            {
                _scroll = next;
                Invalidate(_rList);
            }
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
            if (keyData == Keys.Escape)
            {
                DoCancel();
                return true;
            }
            if (keyData == Keys.Enter && ActiveControl != _txtTip)
            {
                DoSave();
                return true;
            }
            if (keyData == Keys.Insert)
            {
                DoAdd();
                return true;
            }
            if (keyData == Keys.Delete && ActiveControl != _txtTip)
            {
                DoDelete();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------------------------------------------------------------- 动作

        private void HandleTextChanged(object sender, EventArgs e)
        {
            if (_syncing) return;
            if (_editIndex >= 0 && _editIndex < _tips.Count)
            {
                _tips[_editIndex] = _txtTip.Text;
                if (_error.Length > 0) _error = "";
            }
            Invalidate();
        }

        private void SyncBox()
        {
            if (_txtTip == null) return;
            _syncing = true;
            try
            {
                if (_editIndex >= 0 && _editIndex < _tips.Count) _txtTip.Text = _tips[_editIndex];
                else _txtTip.Text = "";
            }
            finally
            {
                _syncing = false;
            }
        }

        private void SelectRow(int index)
        {
            if (index < 0 || index >= _tips.Count) return;
            _editIndex = index;
            _error = "";
            SyncBox();
            _txtTip.Focus();
            Invalidate();
        }

        private void DoAdd()
        {
            if (_tips.Count >= TipStore.MaxTips)
            {
                _error = string.Format("最多 {0} 条提示，先删掉一条再加", TipStore.MaxTips);
                Invalidate();
                return;
            }
            _tips.Add("");
            _editIndex = _tips.Count - 1;
            _error = "";
            ScrollTo(_editIndex);
            SyncBox();
            Invalidate();
            _txtTip.Focus();
        }

        private void DoDelete()
        {
            if (_editIndex < 0 || _editIndex >= _tips.Count)
            {
                _error = "先在列表里选中要删除的条目";
                Invalidate();
                return;
            }
            _tips.RemoveAt(_editIndex);
            if (_editIndex >= _tips.Count) _editIndex = _tips.Count - 1;
            _error = "";
            ScrollTo(_editIndex);
            SyncBox();
            Invalidate();
        }

        private void DoReset()
        {
            _tips.Clear();
            _tips.AddRange(TipStore.BuiltinDefaults);
            _editIndex = _tips.Count > 0 ? 0 : -1;
            _scroll = 0;
            _error = "";
            SyncBox();
            Invalidate();
        }

        private static string OneLine(string s)
        {
            if (s == null) return "";
            return s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private bool Fail(int index, string message)
        {
            _error = message;
            if (index >= 0) _editIndex = index;
            ScrollTo(_editIndex);
            SyncBox();
            Invalidate();
            if (_txtTip != null) _txtTip.Focus();
            return false;
        }

        /// <summary>逐条校验（非空 / 不超过 60 字 / 不重复 / 不超过 50 条），通过则回写 TipStore。</summary>
        private bool Commit()
        {
            List<string> clean = new List<string>();
            for (int i = 0; i < _tips.Count; i++)
            {
                string s = OneLine(_tips[i]);
                if (s.Length == 0)
                {
                    return Fail(i, string.Format("第 {0} 条是空的，填上或删掉它", i + 1));
                }
                if (s.Length > TipStore.MaxLength)
                {
                    return Fail(i, string.Format("第 {0} 条 {1} 字，超过 {2} 字上限",
                        i + 1, s.Length, TipStore.MaxLength));
                }
                for (int j = 0; j < clean.Count; j++)
                {
                    if (string.Equals(clean[j], s, StringComparison.Ordinal))
                    {
                        return Fail(i, string.Format("第 {0} 条和第 {1} 条重复了", i + 1, j + 1));
                    }
                }
                clean.Add(s);
            }
            if (clean.Count == 0)
            {
                return Fail(-1, "至少保留一条提示");
            }
            if (clean.Count > TipStore.MaxTips)
            {
                return Fail(-1, string.Format("最多 {0} 条提示", TipStore.MaxTips));
            }

            _tips.Clear();
            _tips.AddRange(clean);
            if (_store != null)
            {
                _store.Tips.Clear();
                _store.Tips.AddRange(clean);
            }
            Saved = true;
            return true;
        }

        private void DoSave()
        {
            if (!Commit()) return;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void DoCancel()
        {
            Saved = false;
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
