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
    /// <summary>
    /// 主窗口：无边框 1000×700。头部「品牌标记 + 月份标题 + 今天/窗口按钮」；
    /// 主体是两张独立的卡（左月历卡、右日程卡，各自 Draw.Card 出面 + 描边 + 阴影，
    /// 中间留窗口底色间隙）；日程卡底部嵌一张「每日提示」小卡；最底部 44 高状态条。
    /// </summary>
    public class MainForm : Form
    {
        // ---------------------------------------------------------------- 栅格（CONTRACT-V2 §7）
        // 硬约束：两卡左右各距窗口 20；两卡水平间距 ≥14；月历卡内 padding ≥12；状态条 44。
        private const int PAD = 20;              // 卡片左右边距
        private const int GAP = 16;              // 两卡之间的窗口底色间隙
        private const int CARD_TOP = 68;         // 卡片顶边（头部 0..64）
        private const int CARD_RADIUS = 12;
        private const int CARD_PAD = 16;         // 卡内 padding
        private const int STATUS_H = 44;
        private const int MIN_MONTH_W = 700;     // 月历卡优先宽度：窄窗口先压侧栏
        private const int MIN_SIDE_W = 300;      // 侧栏下限，再窄就允许月历卡小于 MIN_MONTH_W
        private const int MIN_MONTH_HARD = 260;  // 极端窄时的兜底，保证不出现负宽度/重叠
        private const int BTN_TOP = 17;          // 头部按钮/胶囊的 y
        private const int BTN_H = 30;
        private const int SIDEBAR_W = 344;
        private const int ROW_H = 46;
        private const int ROW_STEP = 50;
        private const int ROW_RADIUS = 10;
        private const int PILL_RADIUS = 8;
        private const int TIP_LINE_H = 20;
        private const int TIP_ROW_H = 44;        // 提示行高（两行正文）
        private const int TIP_ROW_STEP = 50;
        private const int TIP_HEAD_H = 32;       // 面板头：标题 + 编辑
        private const int TIP_MIN_H = 150;       // 面板高度钳制：目标约侧栏 1/3
        private const int TIP_MAX_H = 320;
        private const int WHEEL_ROWS = 3;        // 每格滚轮滚 3 行
        private const int NEW_H = 44;
        private const int DATE_HEAD_H = 56;
        private const int LIST_GAP = 10;
        private const int MARK_X = 24;           // Branding.DrawMark 28×28 @ (24,18)
        private const int MARK_Y = 18;
        private const int MARK_SIZE = 28;
        private const int TITLE_X = 64;          // 月份标题左边（标记右侧）
        private const int TIP_EDIT_W = 44;

        // ---------------------------------------------------------------- 命中 id
        private const int HIT_PREV = 0;
        private const int HIT_NEXT = 1;
        private const int HIT_TODAY = 2;
        private const int HIT_NEW = 3;
        private const int HIT_AUTOSTART = 4;
        private const int HIT_THEME = 5;
        private const int HIT_TRAY = 6;
        private const int HIT_MIN = 7;
        private const int HIT_CLOSE = 8;
        private const int HIT_TIP_EDIT = 10;
        private const int HIT_ROW_BASE = 11;   // 10 让给 HIT_TIP_EDIT，行命中从 11 起
        private const int HIT_CIRCLE_BASE = 100;

        private const string TIP_EDIT_LABEL = "编辑";

        private static readonly string[] WeekNames = new string[]
        {
            "周日", "周一", "周二", "周三", "周四", "周五", "周六"
        };

        // 内置 26 条提示已搬到 TipStore.BuiltinDefaults，MainForm 只引用，不再维护第二份。

        private readonly Store _store;
        private readonly TipStore _tipStore;
        private readonly Scheduler _scheduler;
        private readonly MonthView _monthView;
        private readonly Panel _dragBar;
        private readonly List<ReminderCard> _cards = new List<ReminderCard>();

        private List<ScheduleItem> _dayItems = new List<ScheduleItem>();
        private DateTime _selected;
        private string _monthTitle = "";
        private int _leftW = 600;
        private int _sideX = 636;
        private int _statusTop = 656;
        private int _tipIndex;                  // 翻页游标：UI 已改为整列展示，仅供 TipText 测试契约使用，不落盘

        private Rectangle _monthCard;
        private Rectangle _sideCard;
        private Rectangle _tipCard;             // 每日提示卡：与日程卡并列的独立卡片

        private Rectangle _rPrev;
        private Rectangle _rNext;
        private Rectangle _rToday;
        private Rectangle _rNew;
        private Rectangle _rAutoStart;
        private Rectangle _rTheme;
        private Rectangle _rTray;
        private Rectangle _rList;
        private Rectangle _rTipPanel;
        private Rectangle _rTipList;
        private Rectangle _rTipEdit;
        private int _listScroll;                 // 日程列表滚动（px）
        private int _tipScroll;                  // 提示面板滚动（px）
        private readonly WheelRouter _wheelRouter;
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
            // 与 _store 同目录：自定义提示不进 data.json，单独一个 tips.json
            _tipStore = new TipStore(store != null ? store.DataDir : null);

            // 悬停即滚：光标在列表/提示面板上滚轮就生效，不要求先点一下拿焦点
            _wheelRouter = new WheelRouter(this);
            Application.AddMessageFilter(_wheelRouter);

            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowIcon = false;
            KeyPreview = true;
            Text = "桌面日历";
            MinimumSize = new Size(1040, 700);
            ClientSize = new Size(1180, 780);
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

        /// <summary>卡片内缩 CARD_PAD 后的内容矩形：卡面排版与命中共用的唯一公式。</summary>
        private static Rectangle CardInner(Rectangle card)
        {
            return new Rectangle(card.X + CARD_PAD, card.Y + CARD_PAD,
                                 card.Width - CARD_PAD * 2, card.Height - CARD_PAD * 2);
        }

        private static Rectangle Inset(Rectangle r, int px)
        {
            return new Rectangle(r.X + px, r.Y + px, r.Width - px * 2, r.Height - px * 2);
        }

        private void LayoutRegions()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            _statusTop = h - STATUS_H;

            int avail = w - PAD * 2 - GAP;
            if (avail < 420) avail = 420;

            // 宽度不足时先压侧栏、不压月历：月历卡要 ≥MIN_MONTH_W，侧栏从 344 起收；
            // 侧栏收到 MIN_SIDE_W 仍不够，就保持 MIN_SIDE_W 并让月历卡小于 MIN_MONTH_W。
            int sideW = SIDEBAR_W;
            int roomForSide = avail - MIN_MONTH_W;
            if (sideW > roomForSide) sideW = roomForSide;
            if (sideW < MIN_SIDE_W) sideW = MIN_SIDE_W;

            // 兜底：极端窄窗口下先保证两卡都还有宽度、不重叠。
            if (avail - sideW < MIN_MONTH_HARD)
            {
                sideW = avail - MIN_MONTH_HARD;
            }
            if (sideW < 160) sideW = 160;

            _leftW = avail - sideW;
            if (_leftW < MIN_MONTH_HARD) _leftW = MIN_MONTH_HARD;
            _sideX = PAD + _leftW + GAP;

            int cardH = _statusTop - 12 - CARD_TOP;
            if (cardH < 260) cardH = 260;

            // 侧栏是并列两张卡：日程卡在上、每日提示卡在下，中间走一条 GAP 的窗口底色。
            // 夹的是卡高：提示卡内容矩形（卡高 - CARD_PAD*2）才是 TIP_MIN_H..TIP_MAX_H 那一块。
            int tipH = cardH / 3;
            if (tipH < TIP_MIN_H + CARD_PAD * 2) tipH = TIP_MIN_H + CARD_PAD * 2;
            if (tipH > TIP_MAX_H + CARD_PAD * 2) tipH = TIP_MAX_H + CARD_PAD * 2;

            int schedH = cardH - tipH - GAP;
            if (schedH < 240)
            {
                schedH = 240;
                tipH = cardH - schedH - GAP;
                if (tipH < 0)
                {
                    tipH = 0;
                    schedH = cardH;
                }
            }

            _monthCard = new Rectangle(PAD, CARD_TOP, _leftW, cardH);
            _sideCard = new Rectangle(_sideX, CARD_TOP, sideW, schedH);
            _tipCard = new Rectangle(_sideX, _sideCard.Bottom + GAP, sideW, tipH);

            // 头部：品牌标记在 (24,18,28,28)，月份标题在标记右侧的拖拽条上，
            // ‹ › 收到月历卡右端，今天胶囊再往右，— / × 贴窗口右上（右边距与 PAD 对称）。
            _rPrev = new Rectangle(_monthCard.Right - 30 - 8 - 30, BTN_TOP, 30, BTN_H);
            _rNext = new Rectangle(_monthCard.Right - 30, BTN_TOP, 30, BTN_H);

            int todayW = 88;
            _rClose = new Rectangle(w - PAD - 30, BTN_TOP, 30, BTN_H);
            _rMin = new Rectangle(_rClose.X - 8 - 30, BTN_TOP, 30, BTN_H);
            _rToday = new Rectangle(_rMin.X - 14 - todayW, BTN_TOP, todayW, BTN_H);

            int dragW = _rPrev.X - 16 - TITLE_X;
            if (dragW < 60) dragW = 60;

            // 构造期 SetBoundsCore（MinimumSize/ClientSize）会早于子控件创建触发 OnResize。
            if (_dragBar != null)
            {
                _dragBar.Bounds = new Rectangle(TITLE_X, BTN_TOP - 1, dragW, BTN_H + 2);
            }
            if (_monthView != null)
            {
                // 月历嵌在月历卡内，按卡的 padding 内缩后整块交给它
                _monthView.Bounds = CardInner(_monthCard);
            }

            Rectangle side = CardInner(_sideCard);
            _rNew = new Rectangle(side.X, side.Bottom - NEW_H, side.Width, NEW_H);

            int contentTop = side.Y + DATE_HEAD_H + LIST_GAP;
            int listH = _rNew.Y - 12 - contentTop;
            if (listH < 0) listH = 0;
            _rList = new Rectangle(side.X, contentTop, side.Width, listH);

            // 提示卡内容直接用卡的 CardInner：头部「每日提示 / 编辑」占 TIP_HEAD_H，其余给逐条提示
            Rectangle tip = CardInner(_tipCard);
            _rTipPanel = tip;
            _rTipEdit = new Rectangle(tip.Right - TIP_EDIT_W - 4, tip.Y + 8, TIP_EDIT_W, 18);
            int tipListTop = tip.Y + TIP_HEAD_H;
            int tipListH = tip.Bottom - 4 - tipListTop;
            if (tipListH < 0) tipListH = 0;
            _rTipList = new Rectangle(tip.X, tipListTop, tip.Width - 6, tipListH);
            ClampScrolls();

            int pillY = _statusTop + (STATUS_H - BTN_H) / 2;
            _rTray = new Rectangle(w - PAD - 96, pillY, 96, BTN_H);
            _rTheme = new Rectangle(_rTray.X - 10 - 104, pillY, 104, BTN_H);
            _rAutoStart = new Rectangle(_rTheme.X - 10 - 128, pillY, 128, BTN_H);
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
            ClampScrolls();
            Invalidate();
        }

        // ---------------------------------------------------------------- 滚轮

        /// <summary>滚轮：光标在提示面板内滚提示，在日程列表内滚日程，其它区域不消费。</summary>
        internal void HandleWheel(Point screen, int delta)
        {
            Point p = PointToClient(screen);
            bool inTip = _tipCard.Contains(p);
            bool inList = _rList.Contains(p);
            if (!inTip && !inList) return;

            int rows = delta > 0 ? -WHEEL_ROWS : WHEEL_ROWS;
            if (inTip)
            {
                _tipScroll += rows * TIP_ROW_STEP;
            }
            else
            {
                _listScroll += rows * ROW_STEP;
            }
            ClampScrolls();
            Invalidate();
        }

        private int TipRowCount()
        {
            IList<string> pool = ActiveTips();
            return pool == null ? 0 : pool.Count;
        }

        private void ClampScrolls()
        {
            int total = _dayItems == null ? 0 : _dayItems.Count;
            int maxList = total * ROW_STEP - _rList.Height;
            if (maxList < 0) maxList = 0;
            if (_listScroll > maxList) _listScroll = maxList;
            if (_listScroll < 0) _listScroll = 0;

            int maxTip = TipRowCount() * TIP_ROW_STEP - _rTipList.Height;
            if (maxTip < 0) maxTip = 0;
            if (_tipScroll > maxTip) _tipScroll = maxTip;
            if (_tipScroll < 0) _tipScroll = 0;
        }

        /// <summary>把光标下的 WM_MOUSEWHEEL 转给主窗体（应用级过滤器，悬停即滚）。</summary>
        private sealed class WheelRouter : IMessageFilter
        {
            private readonly MainForm _owner;
            public WheelRouter(MainForm owner) { _owner = owner; }

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != 0x20A) return false;   // WM_MOUSEWHEEL
                if (_owner == null || _owner.IsDisposed || !_owner.Visible) return false;

                long lp = m.LParam.ToInt64();
                Point screen = new Point((int)(short)(lp & 0xFFFF), (int)(short)((lp >> 16) & 0xFFFF));
                if (!_owner.Bounds.Contains(screen)) return false;
                int delta = (short)(((long)m.WParam.ToInt64() >> 16) & 0xFFFF);
                _owner.HandleWheel(screen, delta);
                return true;   // 已消费，避免再进一次焦点控件的滚轮处理
            }
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

        // ---------------------------------------------------------------- 每日提示文案

        /// <summary>
        /// 当前「每日提示」文案。活动池 = 用户池非空则用户池，否则 TipStore.BuiltinDefaults。
        /// _tipIndex ≤ 0（用户还没点过「换一条」）时先给状态提醒，与是否自定义过无关：
        /// ① 有逾期 → 先清逾期；② 今天未完成 → 还剩几项；③ 今天全完成 → 都完成了；
        /// 以上都不成立（今天没有日程）才走活动池。_tipIndex > 0 直接走活动池，
        /// 「换一条」只在同一条活动池内往后翻，保证一次点击一定换文案。
        /// </summary>
        internal string TipText(DateTime now)
        {
            if (_tipIndex <= 0)
            {
                int overdue = 0;
                try
                {
                    if (_scheduler != null)
                    {
                        List<DueReminder> od = _scheduler.OverdueToday(now);
                        if (od != null) overdue = od.Count;
                    }
                }
                catch (Exception)
                {
                    overdue = 0;
                }
                if (overdue > 0) return string.Format("有 {0} 项已逾期，先清掉最旧的", overdue);

                int total = 0;
                int done = 0;
                try
                {
                    if (_store != null)
                    {
                        total = _store.TotalForDay(now);
                        done = _store.DoneForDay(now);
                    }
                }
                catch (Exception)
                {
                    total = 0;
                    done = 0;
                }
                if (total > 0 && done < total) return string.Format("今天还剩 {0} 项", total - done);
                if (total > 0) return string.Format("今天的 {0} 项都完成了", total);
            }
            IList<string> pool = ActiveTips();
            if (pool == null || pool.Count == 0) return "";
            return pool[TipIndex(pool.Count, now)];
        }

        /// <summary>当前活动池：用户有自己的就给用户池，否则回落内置 26 条。</summary>
        private IList<string> ActiveTips()
        {
            if (HasUserTips()) return _tipStore.Tips;
            return TipStore.BuiltinDefaults;
        }

        private bool HasUserTips()
        {
            return _tipStore != null && _tipStore.Tips != null && _tipStore.Tips.Count > 0;
        }

        private int TipIndex(int count, DateTime now)
        {
            if (count <= 0) return 0;
            int i = (now.DayOfYear + _tipIndex) % count;
            if (i < 0) i += count;
            return i;
        }

        // 测试入口：只在同程序集内可见，UI 之外不给用。
        internal Rectangle TipBounds { get { return _rTipPanel; } }
        internal Rectangle TipCardBounds { get { return _tipCard; } }
        internal Rectangle SideCardBounds { get { return _sideCard; } }
        internal Rectangle TipEditBounds { get { return _rTipEdit; } }
        internal Rectangle TipListBounds { get { return _rTipList; } }
        internal TipStore TipStoreRef { get { return _tipStore; } }

        internal int TipIndexValue
        {
            get { return _tipIndex; }
            set
            {
                _tipIndex = value;
                Invalidate();
            }
        }

        internal void NextTip()
        {
            _tipIndex++;
            if (_tipIndex > 100000) _tipIndex = 1;
            Invalidate();
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
            if (_monthView != null) _monthView.BackColor = t.Card;   // 月历自身铺卡片面
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

        /// <summary>打开提示编辑器；保存过才落盘并从头开始翻（_tipIndex 归零 = 回到当天第一句）。</summary>
        private void OpenTipEditor()
        {
            try
            {
                using (TipEditor editor = new TipEditor(_tipStore))
                {
                    editor.ShowDialog(this);
                    if (editor.Saved)
                    {
                        _tipStore.Save();
                        _tipIndex = 0;
                        Invalidate();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开提示编辑器失败：" + ex.Message, "桌面日历",
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
                // 失败：把 AutoStart 拼好的逐机制原因原样展示（含机制名 + 异常类型/退出码 + 路径），可换行。
                string full = string.IsNullOrEmpty(error) ? "无法修改开机自启设置。" : error;
                DiagnosticLog.Write("MainForm.ToggleAutoStart " + (wasOn ? "关闭" : "开启") + "失败："
                    + full.Replace("\r\n", " / ").Replace("\n", " / "));
                MessageBox.Show(full, "开机自启", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (!wasOn)
            {
                // 成功且是「开启」动作：只有降级到 startup / task 时才弹一次说明，进度条式提示不重复弹。
                string mech = AutoStart.ActiveMechanism;
                if (mech == AutoStart.MechanismStartup || mech == AutoStart.MechanismTask)
                {
                    string how = mech == AutoStart.MechanismStartup
                        ? "启动文件夹（DesktopCalendar.cmd）"
                        : "登录计划任务（DesktopCalendar，ONLOGON / RL LIMITED）";
                    string reason = string.IsNullOrEmpty(error) ? "（未记录到原因）" : error;
                    MessageBox.Show("已用 " + how + " 方式实现开机自启。"
                        + "\r\n\r\n生效方式：登录 Windows 后以 --silent 静默启动到托盘。"
                        + "\r\n\r\n原因：" + reason,
                        "开机自启", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            Invalidate();
        }

        private void ToggleTheme()
        {
            Theme.Toggle();
        }

        /// <summary>托盘注册成功才允许隐藏主窗口；失败时必须留在任务栏上。</summary>
        internal static bool ShouldHideOnMinimize(bool registrationOk)
        {
            return registrationOk;
        }

        private void MinimizeToTray()
        {
            TrayController tray = TrayController.Current;
            bool registrationOk = true;
            if (tray != null) registrationOk = tray.TryAdd();

            if (ShouldHideOnMinimize(registrationOk))
            {
                Hide();
                return;
            }

            // 托盘里没图标：绝不能 Hide()，否则用户以为程序被关掉了
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = true;
            try
            {
                DesktopCalendar.Services.DiagnosticLog.Write("ui: MinimizeToTray 兜底 → 最小化到任务栏 "
                    + (tray == null ? "tray=null" : tray.DiagnosticLine()));
            }
            catch (Exception) { }
            if (tray != null) tray.NoticeFallbackToTaskbar();
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
            int h = ClientSize.Height;

            // 整幅只铺一层窗口底色：卡片间隙、头部、状态条下面都只有它，不存在任何桥接填充
            Draw.Fill(g, new Rectangle(0, 0, w, h), t.Bg);

            Rectangle mCard = Draw.Card(g, _monthCard, CARD_RADIUS, t.Card, t.CardLine, t.Shadow);
            Rectangle sCard = Draw.Card(g, _sideCard, CARD_RADIUS, t.Card, t.CardLine, t.Shadow);
            Rectangle tCard = Draw.Card(g, _tipCard, CARD_RADIUS, t.TipBg, t.TipLine, t.Shadow);

            // 状态条顶边：一条 1px 混合线（不是填充），左右让开 PAD 保持边距干净
            Draw.Line(g, PAD, _statusTop + 0.5f, w - PAD, _statusTop + 0.5f,
                      Draw.Blend(t.Border, t.Bg, 0.55f), 1f);

            DrawHeader(g, t);

            // Draw.Card 返回卡片内缩 1px 的内容矩形；再让开 CARD_PAD-1 正好等于 CardInner()，
            // 也就是 LayoutRegions 用来排 _rList / _rTipPanel / _rNew 的同一条公式。
            // 每日提示是并列的第二张卡，卡面/描边走 TipBg / TipLine。
            Rectangle sideInner = Inset(sCard, CARD_PAD - 1);
            Rectangle tipInner = Inset(tCard, CARD_PAD - 1);
            if (mCard.Width > 0 && sideInner.Width > 0 && sideInner.Height > 0)
            {
                DrawSidebar(g, t, sideInner);
            }
            if (tipInner.Width > 40 && tipInner.Height > 30)
            {
                DrawTipPanel(g, t, tipInner);
            }

            DrawStatus(g, t);
            DrawWindowButtons(g, t);
        }

        private void DrawHeader(Graphics g, Theme t)
        {
            DrawBrandMark(g);
            DrawSquareButton(g, _rPrev, "\u2039", _hover == HIT_PREV, t);
            DrawSquareButton(g, _rNext, "\u203A", _hover == HIT_NEXT, t);
            DrawPill(g, _rToday, "今天", _hover == HIT_TODAY, false, t);
        }

        /// <summary>头部品牌标记：Branding 若拿不到图标/抛异常，仅仅不画，不影响其余绘制。</summary>
        private static void DrawBrandMark(Graphics g)
        {
            if (g == null) return;
            try
            {
                Branding.DrawMark(g, new Rectangle(MARK_X, MARK_Y, MARK_SIZE, MARK_SIZE));
            }
            catch (Exception)
            {
            }
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
            DrawWindowButton(g, t, _rMin, _hover == HIT_MIN, false);
            DrawWindowButton(g, t, _rClose, _hover == HIT_CLOSE, true);
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

        /// <summary>日程卡内容：日期头 56 → 统计行 → 日程列表（滚动）→ 每日提示面板（约 1/3 高，滚动）→ + 新建日程。</summary>
        private void DrawSidebar(Graphics g, Theme t, Rectangle inner)
        {
            int x = inner.X;
            int w = inner.Width;

            string title = string.Format("{0}年{1}月{2}日  {3}", _selected.Year, _selected.Month,
                _selected.Day, WeekNames[(int)_selected.DayOfWeek]);
            Draw.Ellipsis(g, title, t.BodyBold, t.TextPrimary, new Rectangle(x, inner.Y, w, 28));

            int total = _dayItems == null ? 0 : _dayItems.Count;
            int done = 0;
            for (int i = 0; i < total; i++)
            {
                ScheduleItem it = _dayItems[i];
                if (it != null && it.IsDoneOn(OccurrenceOf(it, _selected))) done++;
            }

            Draw.Text(g, string.Format("共 {0} 项 / 已完成 {1}", total, done), t.Small, t.TextSecondary,
                new Rectangle(x, inner.Y + 28, w, 24), StringAlignment.Near, StringAlignment.Center);

            if (_rList.Height > 0)
            {
                Draw.Line(g, x, _rList.Y - 8.5f, x + w, _rList.Y - 8.5f,
                          Draw.Blend(t.Border, t.Card, 0.45f), 1f);
            }

            if (total == 0)
            {
                Draw.Text(g, "这一天还没有日程", t.Small, t.TextMuted,
                    new Rectangle(x, _rList.Y, w, 40), StringAlignment.Center, StringAlignment.Center);
            }

            // 滚动视口：裁剪到列表区，只画可见行；内容超高时右侧画细滚动条
            if (total > 0 && _rList.Height > 0)
            {
                g.SetClip(_rList);
                int first = _listScroll / ROW_STEP;
                for (int i = first; i < total; i++)
                {
                    Rectangle row = RowRect(i);
                    if (row.Y >= _rList.Bottom) break;
                    DrawRow(g, t, i, row);
                }
                g.ResetClip();

                int contentH = total * ROW_STEP;
                if (contentH > _rList.Height) DrawScrollThumb(g, t, _rList, contentH, _listScroll);
            }

            Rectangle r = _rNew;
            bool hot = _hover == HIT_NEW;
            Color bg = hot ? Draw.Blend(t.Accent, t.TextPrimary, 0.10f) : t.Accent;
            Draw.FillRound(g, r, ROW_RADIUS, bg);
            Draw.Text(g, "+ 新建日程", t.SmallBold, t.Bg, r,
                StringAlignment.Center, StringAlignment.Center);
        }

        /// <summary>每日提示卡内容（卡的圆角/描边由 Draw.Card 画）：头部「每日提示 / 编辑」，下面把全部提示逐条列出，滚轮滚动。</summary>
        private void DrawTipPanel(Graphics g, Theme t, Rectangle r)
        {
            Draw.Text(g, "每日提示", t.SmallBold, t.Accent,
                new Rectangle(r.X, r.Y + 8, 120, 18),
                StringAlignment.Near, StringAlignment.Center);

            bool hotEdit = _hover == HIT_TIP_EDIT;
            Draw.Text(g, TIP_EDIT_LABEL, t.Small, hotEdit ? t.Accent : t.TextMuted, _rTipEdit,
                StringAlignment.Far, StringAlignment.Center);

            Draw.Line(g, r.X, r.Y + TIP_HEAD_H - 5f, r.Right, r.Y + TIP_HEAD_H - 5f,
                      Draw.Blend(t.TipLine, t.TipBg, 0.4f), 1f);

            int count = TipRowCount();
            if (count == 0)
            {
                Draw.Text(g, "还没有提示，点右上「编辑」添加", t.Small, t.TextMuted,
                    new Rectangle(_rTipList.X, _rTipList.Y, _rTipList.Width, 40),
                    StringAlignment.Center, StringAlignment.Center);
                return;
            }

            g.SetClip(_rTipList);
            int first = _tipScroll / TIP_ROW_STEP;
            for (int i = first; i < count; i++)
            {
                Rectangle row = TipRowRect(i);
                if (row.Y >= _rTipList.Bottom) break;
                DrawTipRow(g, t, i, row);
            }
            g.ResetClip();

            int tipContentH = count * TIP_ROW_STEP;
            if (tipContentH > _rTipList.Height) DrawScrollThumb(g, t, _rTipList, tipContentH, _tipScroll);
        }

        private Rectangle TipRowRect(int index)
        {
            return new Rectangle(_rTipList.X, _rTipList.Y + index * TIP_ROW_STEP - _tipScroll,
                                 _rTipList.Width, TIP_ROW_H);
        }

        /// <summary>提示行：卡片底 + 左侧 3px 强调条 + 两行正文；与日程行同款观感，无完成圆圈。</summary>
        private void DrawTipRow(Graphics g, Theme t, int index, Rectangle row)
        {
            IList<string> pool = ActiveTips();
            if (pool == null || index >= pool.Count) return;
            string text = pool[index] == null ? "" : pool[index];

            Draw.FillRound(g, row, ROW_RADIUS, t.Card);
            Draw.StrokeRound(g, row, ROW_RADIUS, Draw.Blend(t.TipLine, t.Card, 0.5f), 1f);

            Rectangle bar = new Rectangle(row.X + 10, row.Y + 12, 3, TIP_ROW_H - 24);
            Draw.FillRound(g, bar, 2, t.Accent);

            DrawTipBody(g, text, t.Small, t.TextSecondary,
                new Rectangle(row.X + 24, row.Y + 2, row.Width - 24 - 10, TIP_ROW_H - 4), TIP_LINE_H);
        }

        /// <summary>最多两行正文：按像素宽度折行，超出部分 EllipsisCharacter 截断。</summary>
        private static void DrawTipBody(Graphics g, string text, Font f, Color c, Rectangle box, int lineH)
        {
            if (g == null || f == null || string.IsNullOrEmpty(text)) return;
            if (box.Width <= 4 || box.Height <= 4) return;

            int split = BreakPoint(text, FitPrefix(g, text, f, box.Width));
            string first = text;
            string second = "";
            if (split > 0 && split < text.Length)
            {
                first = text.Substring(0, split);
                second = text.Substring(split);
            }

            Draw.Ellipsis(g, first, f, c, new Rectangle(box.X, box.Y, box.Width, lineH));
            if (!string.IsNullOrEmpty(second) && lineH * 2 <= box.Height)
            {
                Draw.Ellipsis(g, second, f, c, new Rectangle(box.X, box.Y + lineH, box.Width, lineH));
            }
        }

        private const string TIP_BREAKS = "，。、；：！？,.;:!? ";

        /// <summary>把折行点往前挪到最近的标点/空格之后，免得一个「。」被单独甩到第二行。</summary>
        private static int BreakPoint(string s, int split)
        {
            if (string.IsNullOrEmpty(s) || split <= 1 || split >= s.Length) return split;

            int from = split - 14;
            if (from < 0) from = 0;
            for (int i = split - 1; i >= from; i--)
            {
                if (TIP_BREAKS.IndexOf(s[i]) >= 0) return i + 1;
            }
            return split;
        }

        /// <summary>能放进 maxW 的最长前缀长度（至少 1，最多 len）。</summary>
        private static int FitPrefix(Graphics g, string s, Font f, int maxW)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (g.MeasureString(s, f).Width <= maxW) return s.Length;

            int k = 1;
            while (k < s.Length && g.MeasureString(s.Substring(0, k + 1), f).Width <= maxW)
            {
                k++;
            }
            return k;
        }

        private Rectangle RowRect(int index)
        {
            return new Rectangle(_rList.X, _rList.Y + index * ROW_STEP - _listScroll, _rList.Width, ROW_H);
        }

        /// <summary>细滚动条：视图右缘 3px 圆角拇指，按可视比例缩放。</summary>
        private static void DrawScrollThumb(Graphics g, Theme t, Rectangle view, int contentH, int scroll)
        {
            if (contentH <= view.Height) return;
            int trackH = view.Height - 8;
            int thumbH = (int)((long)view.Height * view.Height / contentH);
            if (thumbH < 24) thumbH = 24;
            if (thumbH > trackH) thumbH = trackH;
            int maxScroll = contentH - view.Height;
            if (maxScroll < 1) maxScroll = 1;
            int thumbY = view.Y + 4 + (trackH - thumbH) * scroll / maxScroll;
            Rectangle thumb = new Rectangle(view.Right - 6, thumbY, 3, thumbH);
            Draw.FillRound(g, thumb, 1, Draw.Blend(t.TextMuted, t.Card, 0.25f));
        }

        private Rectangle CircleRect(Rectangle row)
        {
            return new Rectangle(row.X + 14, row.Y + (ROW_H - 18) / 2, 18, 18);
        }

        private void DrawRow(Graphics g, Theme t, int index, Rectangle row)
        {
            ScheduleItem it = _dayItems[index];
            if (it == null) return;

            bool hot = _hover == HIT_ROW_BASE + index;
            bool circleHot = _hover == HIT_CIRCLE_BASE + index;
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
            Draw.FillRound(g, track, 3, Draw.Blend(t.Border, t.Bg, 0.30f));
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

            DrawPill(g, _rAutoStart, auto ? "开机自启 · 开" : "开机自启 · 关",
                     _hover == HIT_AUTOSTART, auto, t);
            UpdateAutoStartTip();
            DrawPill(g, _rTheme, Theme.IsDark ? "浅色模式" : "深色模式", _hover == HIT_THEME, false, t);
            DrawPill(g, _rTray, "最小化到托盘", _hover == HIT_TRAY, false, t);
        }

        // 「开机自启」胶囊的悬浮提示：只取 AutoStart.DescribeState() 的前两行，用 ToolTip 气泡，不新开窗口。
        private ToolTip _autoTip;
        private string _autoTipShown;

        private void UpdateAutoStartTip()
        {
            try
            {
                if (_hover != HIT_AUTOSTART || !IsHandleCreated || !Visible)
                {
                    _autoTipShown = null;
                    return;
                }
                string state;
                try
                {
                    state = AutoStart.DescribeState();
                }
                catch (Exception)
                {
                    return;
                }
                if (string.IsNullOrEmpty(state))
                {
                    return;
                }
                string[] lines = state.Replace("\r\n", "\n").Split('\n');
                string tip = lines.Length > 0 ? lines[0].Trim() : "";
                if (lines.Length > 1 && lines[1].Trim().Length > 0)
                {
                    tip = tip + "\r\n" + lines[1].Trim();
                }
                if (tip.Length == 0 || tip == _autoTipShown)
                {
                    return;
                }
                if (_autoTip == null)
                {
                    _autoTip = new ToolTip();
                    _autoTip.ShowAlways = true;
                }
                _autoTip.Show(tip, this, _rAutoStart.Left, _rAutoStart.Bottom + 4, 8000);
                _autoTipShown = tip;
            }
            catch (Exception)
            {
            }
        }

        // ---------------------------------------------------------------- 命中

        private int HitTest(Point p)
        {
            if (_rPrev.Contains(p)) return HIT_PREV;
            if (_rNext.Contains(p)) return HIT_NEXT;
            if (_rToday.Contains(p)) return HIT_TODAY;
            if (_rNew.Contains(p)) return HIT_NEW;
            if (_rTipEdit.Contains(p)) return HIT_TIP_EDIT;
            if (_rAutoStart.Contains(p)) return HIT_AUTOSTART;
            if (_rTheme.Contains(p)) return HIT_THEME;
            if (_rTray.Contains(p)) return HIT_TRAY;
            if (_rMin.Contains(p)) return HIT_MIN;
            if (_rClose.Contains(p)) return HIT_CLOSE;

            if (_dayItems != null && _dayItems.Count > 0 && _rList.Contains(p))
            {
                int idx = (p.Y - _rList.Y + _listScroll) / ROW_STEP;
                if (idx >= 0 && idx < _dayItems.Count)
                {
                    Rectangle row = RowRect(idx);
                    if (row.Contains(p))
                    {
                        if (CircleRect(row).Contains(p)) return HIT_CIRCLE_BASE + idx;
                        return HIT_ROW_BASE + idx;
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

            if (hit == HIT_PREV) { ShiftMonth(-1); return; }
            if (hit == HIT_NEXT) { ShiftMonth(1); return; }
            if (hit == HIT_TODAY) { GoToday(); return; }
            if (hit == HIT_NEW) { CreateItem(); return; }
            if (hit == HIT_TIP_EDIT) { OpenTipEditor(); return; }
            if (hit == HIT_AUTOSTART) { ToggleAutoStart(); return; }
            if (hit == HIT_THEME) { ToggleTheme(); return; }
            if (hit == HIT_TRAY) { MinimizeToTray(); return; }
            if (hit == HIT_MIN) { MinimizeToTray(); return; }
            if (hit == HIT_CLOSE) { Close(); return; }

            if (hit >= HIT_CIRCLE_BASE)
            {
                ToggleDone(hit - HIT_CIRCLE_BASE);
                return;
            }

            if (hit >= HIT_ROW_BASE)
            {
                int idx = hit - HIT_ROW_BASE;
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
                Application.RemoveMessageFilter(_wheelRouter);
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
