using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using DesktopCalendar.Data;
using DesktopCalendar.Models;
using DesktopCalendar.UI;

namespace DesktopCalendarTools
{
    /// <summary>
    /// 月历视觉验收：造样本数据 → 离屏渲染浅色/深色 960×620 PNG → 像素级与交互级自检。
    /// 仅编译自检用（引用 _scratch\stub_models.cs 的替身 Store）。
    /// </summary>
    internal static class PreviewMonth
    {
        private const int W = 960;
        private const int H = 620;

        private static int _pass;
        private static int _fail;

        [STAThread]
        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; }
            catch { }

            try { Application.EnableVisualStyles(); }
            catch { }
            try { Application.SetCompatibleTextRenderingDefault(false); }
            catch { }

            string previewDir = (args != null && args.Length > 0) ? args[0] : DefaultPreviewDir();
            string scratchDir = (args != null && args.Length > 1) ? args[1] : DefaultScratchDir();
            Directory.CreateDirectory(previewDir);
            Directory.CreateDirectory(scratchDir);

            DateTime today = DateTime.Today;
            DateTime monthFirst = new DateTime(today.Year, today.Month, 1);
            int daysInMonth = DateTime.DaysInMonth(monthFirst.Year, monthFirst.Month);
            DateTime[] grid = GridDays(monthFirst);

            Console.WriteLine("== 样本数据 ==");
            Console.WriteLine("月份 " + monthFirst.ToString("yyyy-MM") + "，今天 " + today.ToString("yyyy-MM-dd")
                              + "，本月 " + daysInMonth.ToString() + " 天");

            Store store = new Store(scratchDir);

            // 今天 3 条（超过 2 条 → 期望第二条后显示 "+1"）
            store.Add(Item("项目周会 · 对齐本周排期", today, 9, 0, "#4F7CFF"));
            store.Add(Item("季度汇报材料初稿", today, 14, 30, "#EF4444"));
            store.Add(Item("健身 · 上肢日", today, 19, 0, "#22C55E"));
            Console.WriteLine("[数据] 今天 " + today.ToString("yyyy-MM-dd") + " → 3 条（期望 2 条 + \"+1\"）");

            // 明天 1 条
            DateTime tomorrow = today.AddDays(1);
            store.Add(Item("产品评审", tomorrow, 10, 0, "#8B5CF6"));
            Console.WriteLine("[数据] 明天 " + tomorrow.ToString("yyyy-MM-dd") + " → 1 条");

            // 挤爆日：本月内挑一个空闲日塞 5 条
            DateTime burstDay = PickFreeDay(monthFirst, daysInMonth, store, today, tomorrow);
            string[] burstTitles = new string[] { "晨会", "需求梳理", "客户回访", "迭代复盘", "提交周报" };
            string[] burstColors = new string[] { "#4F7CFF", "#22C55E", "#F59E0B", "#EF4444", "#8B5CF6" };
            for (int i = 0; i < burstTitles.Length; i++)
            {
                store.Add(Item(burstTitles[i], burstDay, 8 + i * 3, 0, burstColors[i]));
            }
            Console.WriteLine("[数据] 挤爆日 " + burstDay.ToString("yyyy-MM-dd") + " → 5 条（期望 2 条 + \"+3\"）");

            // 无日程日：本月第一个没有任何日程的日子
            DateTime emptyDay = PickFreeDay(monthFirst, daysInMonth, store, today, tomorrow);
            Console.WriteLine("[数据] 无日程日 " + emptyDay.ToString("yyyy-MM-dd") + " → 0 条");

            // 31 号：本月有 31 就放本月 31，否则放到网格里能看到的某个 31 号（跨月格）
            DateTime day31 = DateTime.MinValue;
            if (daysInMonth == 31)
            {
                day31 = new DateTime(monthFirst.Year, monthFirst.Month, 31);
            }
            else
            {
                for (int i = 0; i < grid.Length; i++)
                {
                    if (grid[i].Day == 31) { day31 = grid[i]; break; }
                }
            }
            if (day31 != DateTime.MinValue)
            {
                string label = (day31.Month == monthFirst.Month) ? "月末结算 · 31 号" : "跨月 · 31 号";
                store.Add(Item(label, day31, 16, 0, "#F59E0B"));
                Console.WriteLine("[数据] 31 号 " + day31.ToString("yyyy-MM-dd")
                                  + (day31.Month == monthFirst.Month ? "（本月格）" : "（跨月格，期望淡显）") + " → 1 条");
            }
            else
            {
                Console.WriteLine("[数据] 本网格内没有 31 号，跳过该样本");
            }

            // 跨月格：下月 1 号在网格里就放一条，验证"非本月日期淡显 + 日程一起去饱和"
            DateTime nextFirst = monthFirst.AddMonths(1);
            if (IsInGrid(grid, nextFirst))
            {
                store.Add(Item("下月计划启动", nextFirst, 11, 0, "#06B6D4"));
                Console.WriteLine("[数据] 跨月格 " + nextFirst.ToString("yyyy-MM-dd") + " → 1 条（期望淡显）");
            }

            // ---------- 渲染浅色 ----------
            DateTime selected = tomorrow;
            DateTime hoverDay = today.AddDays(2);
            if (hoverDay.Month != monthFirst.Month) hoverDay = burstDay;

            int lightDistinct;
            int lightToday;
            int lightAccent;
            string lightPath = Path.Combine(previewDir, "month-light-v2.png");
            RenderOne(store, monthFirst, selected, hoverDay, false, lightPath, out lightDistinct, out lightToday, out lightAccent);

            // ---------- 渲染深色 ----------
            int darkDistinct;
            int darkToday;
            int darkAccent;
            string darkPath = Path.Combine(previewDir, "month-dark-v2.png");
            RenderOne(store, monthFirst, selected, hoverDay, true, darkPath, out darkDistinct, out darkToday, out darkAccent);

            Console.WriteLine();
            Console.WriteLine("== 产物 ==");
            ReportFile(lightPath);
            ReportFile(darkPath);

            Console.WriteLine();
            Console.WriteLine("== 像素自检 ==");
            PixelChecks(lightPath, "浅色",
                        Color.FromArgb(0xFF, 0xFF, 0xFF), Color.FromArgb(0xE8, 0xEE, 0xFF), Color.FromArgb(0x4F, 0x7C, 0xFF));
            PixelChecks(darkPath, "深色",
                        Color.FromArgb(0x1A, 0x1D, 0x25), Color.FromArgb(0x23, 0x30, 0x4F), Color.FromArgb(0x6E, 0x92, 0xFF));
            Console.WriteLine();
            Console.WriteLine("== 月历表面取样（卡片面 / 色带 / 色块 / 选中环）==");
            MonthSurfaceChecks(lightPath, "浅色", store, monthFirst, selected, hoverDay, false);
            MonthSurfaceChecks(darkPath, "深色", store, monthFirst, selected, hoverDay, true);

            Console.WriteLine("[info] 渲染期统计：浅色 颜色数 " + lightDistinct.ToString() + " TodayBg " + lightToday.ToString()
                + " Accent " + lightAccent.ToString() + "；深色 颜色数 " + darkDistinct.ToString() + " TodayBg "
                + darkToday.ToString() + " Accent " + darkAccent.ToString());
            byte[] lb = File.ReadAllBytes(lightPath);
            byte[] db = File.ReadAllBytes(darkPath);
            bool sameBytes = lb.Length == db.Length;
            if (sameBytes)
            {
                for (int i = 0; i < lb.Length; i++)
                {
                    if (lb[i] != db[i]) { sameBytes = false; break; }
                }
            }
            Check("浅色/深色两张图内容不同", !sameBytes);

            // ---------- 交互自检 ----------
            Console.WriteLine();
            Console.WriteLine("== 交互自检 ==");
            InteractionChecks(store, monthFirst, today, tomorrow, burstDay);

            // ---------- Draw 基础自检 ----------
            Console.WriteLine();
            Console.WriteLine("== Draw / Theme 自检 ==");
            DrawChecks();

            Console.WriteLine();
            Console.WriteLine("== 卡片 / 阴影 / 色块自检 ==");
            CardChecks();
            CardShadowComposite(store, monthFirst, selected, hoverDay, scratchDir);

            Console.WriteLine();
            Console.WriteLine("PASS " + _pass.ToString() + " / FAIL " + _fail.ToString());
            Console.WriteLine(_fail == 0 ? "RESULT: PASS" : "RESULT: FAIL");
            return _fail == 0 ? 0 : 1;
        }

        // ---------------- 渲染 ----------------

        private static void RenderOne(Store store, DateTime monthFirst, DateTime selected, DateTime hoverDay,
                                      bool dark, string path, out int distinct, out int todayPixels, out int accentPixels)
        {
            Theme.SetDark(dark);
            Theme th = Theme.Current;

            MonthView mv = new MonthView();
            mv.Size = new Size(W, H);
            mv.Store = store;
            mv.Month = monthFirst;
            mv.SelectedDate = selected;

            using (Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                // 第一遍只为了跑 Layout，拿到格子矩形后再把 _hover 设成目标格
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(th.Bg);
                    mv.RenderTo(g);
                }
                SetPrivateField(mv, "_hover", IndexOfDate(mv, hoverDay));

                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(th.Bg);
                    mv.RenderTo(g);
                }
                bmp.Save(path, ImageFormat.Png);

                Dictionary<int, int> counts;
                distinct = CountColors(bmp, new int[] { ToRgb(th.TodayBg), ToRgb(th.Accent) }, out counts);
                todayPixels = counts[ToRgb(th.TodayBg)];
                accentPixels = counts[ToRgb(th.Accent)];
            }

            // DrawToBitmap 通道（走 WM_PRINTCLIENT）留一份证据，不作为硬性门槛
            try
            {
                using (Bitmap b2 = new Bitmap(W, H))
                {
                    mv.DrawToBitmap(b2, new Rectangle(0, 0, W, H));
                    int d2;
                    Dictionary<int, int> c2;
                    d2 = CountColors(b2, new int[0], out c2);
                    string side = dark ? "dark" : "light";
                    string p2 = Path.Combine(DefaultScratchDir(), "drawtobitmap-" + side + ".png");
                    b2.Save(p2, ImageFormat.Png);
                    Console.WriteLine("[info] DrawToBitmap(" + side + ") 颜色数 " + d2.ToString() + " → " + p2);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[warn] DrawToBitmap 失败（不影响交付，主路径是 RenderTo）: " + ex.Message);
            }

            mv.Dispose();
        }

        // ---------------- 交互 ----------------

        private static void InteractionChecks(Store store, DateTime monthFirst, DateTime today, DateTime tomorrow, DateTime burstDay)
        {
            Theme.SetDark(false);
            MonthView mv = new MonthView();
            mv.Size = new Size(W, H);
            mv.Store = store;
            mv.Month = monthFirst;
            mv.SelectedDate = today;

            using (Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                mv.RenderTo(g);
            }

            DateTime? selectedHit = null;
            Action<DateTime> onSel = delegate(DateTime d) { selectedHit = d; };
            mv.DateSelected += onSel;

            ScheduleItem activated = null;
            Action<ScheduleItem> onAct = delegate(ScheduleItem it) { activated = it; };
            mv.ItemActivated += onAct;

            // 单击今天 → DateSelected
            Point pToday = CellCenter(mv, today);
            InvokeProtected(mv, "OnMouseClick", new MouseEventArgs(MouseButtons.Left, 1, pToday.X, pToday.Y, 0));
            Check("单击" + today.ToString("yyyy-MM-dd") + " 触发 DateSelected",
                  selectedHit.HasValue && selectedHit.Value.Date == today.Date);

            // 双击有日程的今天 → ItemActivated 且取第一条（09:00 项目周会）
            InvokeProtected(mv, "OnMouseDoubleClick", new MouseEventArgs(MouseButtons.Left, 2, pToday.X, pToday.Y, 0));
            Check("双击有日程的日子触发 ItemActivated",
                  activated != null && activated.Title != null && activated.Title.StartsWith("项目周会"));

            // 双击无日程的日子 → 不触发
            activated = null;
            DateTime emptyForDouble = FindDayWithoutItems(store, monthFirst, today);
            Point pEmpty = CellCenter(mv, emptyForDouble);
            InvokeProtected(mv, "OnMouseDoubleClick", new MouseEventArgs(MouseButtons.Left, 2, pEmpty.X, pEmpty.Y, 0));
            Check("双击无日程的日子不触发 ItemActivated: " + emptyForDouble.ToString("yyyy-MM-dd"),
                  activated == null);

            // 滚轮切月
            DateTime before = mv.Month;
            InvokeProtected(mv, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 0, 0, -120));
            DateTime afterNext = mv.Month;
            InvokeProtected(mv, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 0, 0, 120));
            Check("滚轮向下切到 " + afterNext.ToString("yyyy-MM") + "，向上切回 " + mv.Month.ToString("yyyy-MM"),
                  afterNext != before && mv.Month == before);

            // 悬停命中
            InvokeProtected(mv, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, pToday.X, pToday.Y, 0));
            Check("MouseMove 命中今天格 → HoverDate 正确", mv.HoverDate.Date == today.Date);

            // 超出网格的点不命中
            InvokeProtected(mv, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, W - 1, H - 1, 0));
            Check("右下角空白 → HoverDate 为 MinValue", mv.HoverDate == DateTime.MinValue);

            mv.DateSelected -= onSel;
            mv.ItemActivated -= onAct;
            mv.Dispose();
        }

        // ---------------- Draw / Theme ----------------

        private static void DrawChecks()
        {
            Theme.SetDark(false);
            Theme th = Theme.Current;
            Check("Theme 12 个颜色 Token 全部非空",
                  th.Bg != Color.Empty && th.Surface != Color.Empty && th.SurfaceAlt != Color.Empty &&
                  th.TextPrimary != Color.Empty && th.TextSecondary != Color.Empty && th.TextMuted != Color.Empty &&
                  th.Border != Color.Empty && th.Accent != Color.Empty && th.AccentSoft != Color.Empty &&
                  th.TodayBg != Color.Empty && th.Danger != Color.Empty && th.Success != Color.Empty);
            Check("Theme 分类板 6 色且首色 = #4F7CFF",
                  th.Palette.Length == 6 && ToRgb(th.Palette[0]) == ToRgb(Color.FromArgb(0x4F, 0x7C, 0xFF)));
            Check("浅色 Accent = #4F7CFF", ToRgb(th.Accent) == ToRgb(Color.FromArgb(0x4F, 0x7C, 0xFF)));
            Check("字体齐备（Title/Body/BodyBold/Small/SmallBold/Big）",
                  th.Title != null && th.Body != null && th.BodyBold != null && th.Small != null &&
                  th.SmallBold != null && th.Big != null);
            Check("字体族为 Microsoft YaHei UI 或回落族",
                  th.Body.FontFamily.Name.Length > 0);

            using (Bitmap bmp = new Bitmap(200, 120))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Draw.Smooth(g);
                Check("Draw.Smooth → AntiAlias + ClearTypeGridFit",
                      g.SmoothingMode == System.Drawing.Drawing2D.SmoothingMode.AntiAlias &&
                      g.TextRenderingHint == System.Drawing.Text.TextRenderingHint.ClearTypeGridFit);
                bool ok = true;
                try
                {
                    Rectangle r = new Rectangle(10, 10, 120, 60);
                    using (System.Drawing.Drawing2D.GraphicsPath p = Draw.Round(r, 12)) { ok &= p.PointCount > 4; }
                    using (System.Drawing.Drawing2D.GraphicsPath p = Draw.RoundTop(r, 12)) { ok &= p.PointCount > 4; }
                    Draw.FillRound(g, r, 12, th.AccentSoft);
                    Draw.StrokeRound(g, r, 12, th.Accent, 1.5f);
                    Draw.FillRoundTop(g, new Rectangle(20, 80, 60, 30), 12, th.Surface);
                    Draw.Text(g, "对齐", th.Body, th.TextPrimary, r, StringAlignment.Center, StringAlignment.Center);
                    Draw.Ellipsis(g, "一段很长很长很长的标题用来验证省略号是否生效", th.Small, th.TextSecondary,
                                  new Rectangle(10, 70, 90, 18));
                }
                catch (Exception ex)
                {
                    ok = false;
                    Console.WriteLine("  异常: " + ex.Message);
                }
                Check("Draw 圆角/填充/描边/对齐/省略号均不抛异常", ok);
            }

            Theme.SetDark(true);
            Check("SetDark(true) → IsDark = true", Theme.IsDark && Theme.Current.Bg == Color.FromArgb(0x12, 0x14, 0x1A));
            Theme.Toggle();
            Check("Toggle() → 回到浅色", !Theme.IsDark && Theme.Current.Bg == Color.FromArgb(0xF4, 0xF6, 0xFB));
            Theme.SetDark(false);

            // Chrome 只做冒烟：无消息泵环境下不能炸
            bool chromeOk = true;
            try
            {
                using (Form f = new Form())
                {
                    f.FormBorderStyle = FormBorderStyle.None;
                    Chrome.EnableRounded(f);
                    Chrome.EnableDrag(f, f);
                    Chrome.EnableResize(f);
                }
            }
            catch (Exception ex)
            {
                chromeOk = false;
                Console.WriteLine("  异常: " + ex.Message);
            }
            Check("Chrome.EnableRounded/EnableDrag/EnableResize 冒烟不抛异常", chromeOk);
        }

        // ---------------- 月历表面取样（CONTRACT-V2 §4）----------------

        private static void MonthSurfaceChecks(string path, string label, Store store, DateTime monthFirst,
                                               DateTime selected, DateTime hoverDay, bool dark)
        {
            if (!File.Exists(path))
            {
                Check(label + " v2 PNG 存在（表面取样）", false);
                return;
            }

            Theme.SetDark(dark);
            Theme th = Theme.Current;

            // 用与 RenderOne 完全相同的入参跑一遍，只为拿到格子/色带几何（取样仍取自已保存的 PNG）
            MonthView mv = new MonthView();
            mv.Size = new Size(W, H);
            mv.Store = store;
            mv.Month = monthFirst;
            mv.SelectedDate = selected;
            using (Bitmap probe = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(probe)) { g.Clear(th.Card); mv.RenderTo(g); }
                SetPrivateField(mv, "_hover", IndexOfDate(mv, hoverDay));
                using (Graphics g = Graphics.FromImage(probe)) { g.Clear(th.Card); mv.RenderTo(g); }
            }
            Rectangle[] cells = GetPrivateField(mv, "_cells") as Rectangle[];
            DateTime[] dates = GetPrivateField(mv, "_dates") as DateTime[];
            object hdr = GetPrivateField(mv, "_header");
            Rectangle header = (hdr is Rectangle) ? (Rectangle)hdr : Rectangle.Empty;
            mv.Dispose();

            if (cells == null || dates == null || header.Width <= 0)
            {
                Check(label + " 月历布局可取（表面取样）", false);
                return;
            }

            int idxSel = -1;
            int idxToday = -1;
            int idxHover = -1;
            int idxPlain = -1;
            int idxFaint = -1;
            for (int i = 0; i < TotalCellsPublic; i++)
            {
                DateTime d = dates[i].Date;
                bool inMonth = dates[i].Month == monthFirst.Month && dates[i].Year == monthFirst.Year;
                if (idxSel < 0 && d == selected.Date) idxSel = i;
                if (idxToday < 0 && d == DateTime.Today) idxToday = i;
                if (idxHover < 0 && d == hoverDay.Date) idxHover = i;
                if (idxPlain < 0 && inMonth && d != selected.Date && d != DateTime.Today && d != hoverDay.Date) idxPlain = i;
                if (idxFaint < 0 && !inMonth) idxFaint = i;
            }
            if (idxPlain < 0 || idxFaint < 0)
            {
                Check(label + " 找到本月/非本月样本格", false);
                return;
            }

            using (Bitmap bmp = new Bitmap(path))
            {
                Check(label + " 控件底色 = Theme.Card（MonthView 不再画外卡）",
                      SameAt(bmp, 2, 2, th.Card));
                Check(label + " 星期行色带 = Theme.HeaderBand（取样 " + (header.X + 4).ToString() + ","
                      + (header.Y + header.Height / 2).ToString() + " → "
                      + Hex(PixelAt(bmp, header.X + 4, header.Y + header.Height / 2)) + "）",
                      SameAt(bmp, header.X + 4, header.Y + header.Height / 2, th.HeaderBand));

                Rectangle box = cells[idxPlain];
                int px = box.Right - 6;
                int py = box.Y + 8;
                Check(label + " 本月格色块 = Theme.CellBg（取样 " + px.ToString() + "," + py.ToString()
                      + " → " + Hex(PixelAt(bmp, px, py)) + "）", SameAt(bmp, px, py, th.CellBg));
                // 格子贴边那一列是填充边界上的抗锯齿混色：只允许 Card↔CellBg 之间的颜色。
                // 有 1px 描边时这里会落到该线段之外（Border 比两端的蓝都淡/都深），所以本断言即「去描边」证据。
                Check(label + " 本月格贴边像素 = Card/CellBg 抗锯齿混色，无 1px 发丝描边（取样 "
                      + box.X + "," + (box.Y + box.Height / 2) + " → "
                      + Hex(PixelAt(bmp, box.X, box.Y + box.Height / 2)) + "，内侧 "
                      + Hex(PixelAt(bmp, box.X + 1, box.Y + box.Height / 2)) + "）",
                      IsBetween(PixelAt(bmp, box.X, box.Y + box.Height / 2), th.Card, th.CellBg) &&
                      SameAt(bmp, box.X + 1, box.Y + box.Height / 2, th.CellBg));

                Rectangle faint = cells[idxFaint];
                Check(label + " 非本月格色块 = Theme.CellBgFaint（取样 " + (faint.Right - 6).ToString() + ","
                      + (faint.Y + 8).ToString() + " → " + Hex(PixelAt(bmp, faint.Right - 6, faint.Y + 8)) + "）",
                      SameAt(bmp, faint.Right - 6, faint.Y + 8, th.CellBgFaint));

                if (idxToday >= 0)
                {
                    Rectangle t = cells[idxToday];
                    Check(label + " 今天格色块 = Theme.TodayBg",
                          SameAt(bmp, t.Right - 6, t.Y + 8, th.TodayBg));
                }
                if (idxHover >= 0 && idxHover != idxToday)
                {
                    Rectangle hv = cells[idxHover];
                    Check(label + " 悬停格色块 = Theme.SurfaceAlt",
                          SameAt(bmp, hv.Right - 6, hv.Y + 8, th.SurfaceAlt));
                }
                if (idxSel >= 0)
                {
                    Rectangle s = cells[idxSel];
                    int my = s.Y + s.Height / 2;
                    // 环宽 2px：外沿那列是圆角填充边界的抗锯齿混色（Card↔Accent），第 2 列是精确 Accent，
                    // 再往里第 3 列就是 Surface——三列连着读出来即「2px Accent 环」。
                    Check(label + " 选中格 2px Accent 环（" + s.X + "=" + Hex(PixelAt(bmp, s.X, my))
                          + " 混色 " + (s.X + 1) + "=" + Hex(PixelAt(bmp, s.X + 1, my)) + " 实色）",
                          SameAt(bmp, s.X + 1, my, th.Accent) &&
                          IsBetween(PixelAt(bmp, s.X, my), th.Card, th.Accent));
                    Check(label + " 选中环内侧 = Theme.Surface（环宽恰 2px，取样 " + Hex(PixelAt(bmp, s.X + 3, my)) + "）",
                          SameAt(bmp, s.X + 3, my, th.Surface));
                }
            }
        }

        // ---------------- 卡片 / 阴影 / 色块（CONTRACT-V2 §2 §3）----------------

        private static void CardChecks()
        {
            Theme.SetDark(false);
            Theme th = Theme.Current;
            Check("浅色 8 个新 Token 全部非空",
                  th.Card != Color.Empty && th.CardLine != Color.Empty && th.CellBg != Color.Empty &&
                  th.CellBgFaint != Color.Empty && th.HeaderBand != Color.Empty && th.TipBg != Color.Empty &&
                  th.TipLine != Color.Empty && th.Shadow != Color.Empty);
            Check("浅色新 Token hex: Card " + Hex(th.Card) + " CardLine " + Hex(th.CardLine)
                  + " CellBg " + Hex(th.CellBg) + " CellBgFaint " + Hex(th.CellBgFaint)
                  + " HeaderBand " + Hex(th.HeaderBand) + " TipBg " + Hex(th.TipBg)
                  + " TipLine " + Hex(th.TipLine) + " Shadow " + Hex(th.Shadow),
                  Hex(th.Card) == "#FFFFFF" && Hex(th.CardLine) == "#E6EBF5" && Hex(th.CellBg) == "#F5F7FC" &&
                  Hex(th.CellBgFaint) == "#FAFBFE" && Hex(th.HeaderBand) == "#EEF3FF" &&
                  Hex(th.TipBg) == "#EEF4FF" && Hex(th.TipLine) == "#DCE7FF" && Hex(th.Shadow) == "#1B2233");
            Check("既有 Token 语义未被改动（浅色 Bg/AccentSoft/TodayBg）",
                  Hex(th.Bg) == "#F4F6FB" && Hex(th.AccentSoft) == "#E8EEFF" && Hex(th.TodayBg) == "#E8EEFF");

            Theme.SetDark(true);
            Theme th2 = Theme.Current;
            Check("深色 8 个新 Token 全部非空",
                  th2.Card != Color.Empty && th2.CardLine != Color.Empty && th2.CellBg != Color.Empty &&
                  th2.CellBgFaint != Color.Empty && th2.HeaderBand != Color.Empty && th2.TipBg != Color.Empty &&
                  th2.TipLine != Color.Empty && th2.Shadow != Color.Empty);
            Check("深色新 Token hex: Card " + Hex(th2.Card) + " CardLine " + Hex(th2.CardLine)
                  + " CellBg " + Hex(th2.CellBg) + " CellBgFaint " + Hex(th2.CellBgFaint)
                  + " HeaderBand " + Hex(th2.HeaderBand) + " TipBg " + Hex(th2.TipBg)
                  + " TipLine " + Hex(th2.TipLine) + " Shadow " + Hex(th2.Shadow),
                  Hex(th2.Card) == "#1A1D25" && Hex(th2.CardLine) == "#262B35" && Hex(th2.CellBg) == "#20242D" &&
                  Hex(th2.CellBgFaint) == "#171A21" && Hex(th2.HeaderBand) == "#1E2537" &&
                  Hex(th2.TipBg) == "#202A42" && Hex(th2.TipLine) == "#2E3B57" && Hex(th2.Shadow) == "#000000");
            Check("既有 Token 语义未被改动（深色 Bg/AccentSoft/TodayBg）",
                  Hex(th2.Bg) == "#12141A" && Hex(th2.AccentSoft) == "#232E4D" && Hex(th2.TodayBg) == "#23304F");

            CardProbe(false);
            CardProbe(true);
            Theme.SetDark(false);
        }

        private static void CardProbe(bool dark)
        {
            Theme.SetDark(dark);
            Theme th = Theme.Current;
            string label = dark ? "深色" : "浅色";
            Rectangle card = new Rectangle(60, 60, 160, 80);
            int midY = card.Y + card.Height / 2;

            using (Bitmap bmp = new Bitmap(320, 200, PixelFormat.Format32bppArgb))
            {
                Rectangle content;
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(th.Bg);
                    Draw.Smooth(g);
                    content = Draw.Card(g, card, 12, th.Card, th.CardLine, th.Shadow);
                }
                Check(label + " Card 返回值 = r 内缩 1px 的内容矩形",
                      content == new Rectangle(card.X + 1, card.Y + 1, card.Width - 2, card.Height - 2));
                Check(label + " 卡片内 1px 描边 = Theme.CardLine（取样 " + card.X + "," + midY + " → "
                      + Hex(PixelAt(bmp, card.X, midY)) + "）", SameAt(bmp, card.X, midY, th.CardLine));
                Check(label + " 卡片面 = Theme.Card",
                      SameAt(bmp, card.X + card.Width / 2, midY, th.Card));

                ShadowRamp(bmp, label + " 左侧", card.X, midY, -1, 0, th.Bg);
                ShadowRamp(bmp, label + " 下侧", card.X + card.Width / 2, card.Bottom - 1, 0, 1, th.Bg);
            }

            using (Bitmap bmp = new Bitmap(120, 80, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(th.Bg);
                    Draw.Smooth(g);
                    Draw.Block(g, new Rectangle(20, 20, 80, 40), 10, th.AccentSoft);
                }
                // 贴边列 20 是填充边界的 50% 混色，第 21 列起是纯 AccentSoft；外面 19 仍是底色。
                // 「无描边」= 贴边色只能由 Bg↔AccentSoft 混出（有边缘描边就会引入第三种颜色）。
                Check(label + " Block 无描边：贴边列是 Bg/AccentSoft 混色、内侧纯色块色（" + Hex(PixelAt(bmp, 19, 40))
                      + " / " + Hex(PixelAt(bmp, 20, 40)) + " / " + Hex(PixelAt(bmp, 21, 40)) + "）",
                      IsBetween(PixelAt(bmp, 20, 40), th.Bg, th.AccentSoft) &&
                      SameAt(bmp, 21, 40, th.AccentSoft) && SameAt(bmp, 19, 40, th.Bg));
                Check(label + " Block 圆角外仍是底色（角点未被填充）", SameAt(bmp, 20, 20, th.Bg));
            }

            using (Bitmap bmp = new Bitmap(60, 60, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(th.Bg);
                    Draw.Shadow(g, new Rectangle(20, 20, 20, 20), 6, th.Shadow, 0, 0.10f);
                }
                Check(label + " Shadow(spread=0) 直接返回（周边仍是底色）",
                      SameAt(bmp, 18, 30, th.Bg) && SameAt(bmp, 30, 18, th.Bg));
            }
        }

        /// <summary>
        /// 成品视角预览（落 scratch，不入 preview 交付目录）：主窗用 Draw.Card 提供外卡 + 阴影，
        /// MonthView 不再自画外卡、只把月历画在 Card 返回的内容矩形里——即用户要的「色块 + 阴影」。
        /// </summary>
        private static void CardShadowComposite(Store store, DateTime monthFirst, DateTime selected,
                                                DateTime hoverDay, string scratchDir)
        {
            Theme.SetDark(false);
            Theme th = Theme.Current;
            int w = 1000;
            int h = 660;
            string path = Path.Combine(scratchDir, "card-shadow-light.png");
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                Rectangle card = new Rectangle(20, 20, w - 40, h - 40);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(th.Bg);
                    Draw.Smooth(g);
                    Rectangle content = Draw.Card(g, card, Draw.Scale(g, 14), th.Card, th.CardLine, th.Shadow);
                    MonthView mv = new MonthView();
                    mv.Size = content.Size;
                    mv.Store = store;
                    mv.Month = monthFirst;
                    mv.SelectedDate = selected;
                    SetPrivateField(mv, "_hover", IndexOfDate(mv, hoverDay));
                    System.Drawing.Drawing2D.GraphicsState st = g.Save();
                    g.SetClip(content);
                    g.TranslateTransform(content.X, content.Y);
                    mv.RenderTo(g);
                    g.Restore(st);
                    mv.Dispose();
                }
                bmp.Save(path, ImageFormat.Png);

                int midY = card.Y + card.Height / 2;
                int near = Delta(PixelAt(bmp, card.X - 4, midY), th.Bg);   // 外沿外 4px
                int far = Delta(PixelAt(bmp, card.X - 14, midY), th.Bg);   // 外沿外 14px
                Console.WriteLine("[composite] " + path);
                Console.WriteLine("[composite] 外沿外 4px Δ" + near.ToString() + " / 14px Δ" + far.ToString());
                Check("组合预览（外卡 + 阴影 + 月历）已生成且 > 8KB: " + new FileInfo(path).Length + " bytes",
                      File.Exists(path) && new FileInfo(path).Length > 8000);
                Check("组合预览里卡片外沿有阴影（4px Δ" + near.ToString() + " > 14px Δ" + far.ToString() + "）",
                      near > far && near > 0);
            }
            Theme.SetDark(false);
        }

        private static int Delta(Color a, Color b)
        {
            if (a.IsEmpty || b.IsEmpty) return -1;
            return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
        }

        private static void ShadowRamp(Bitmap bmp, string label, int x0, int y0, int dx, int dy, Color bg)
        {
            int[] dist = new int[] { 2, 4, 6, 8 };
            int[] mag = new int[dist.Length];
            StringBuilder sb = new StringBuilder();
            bool nonIncreasing = true;
            for (int scale = 0; scale < dist.Length; scale++)
            {
                Color c = PixelAt(bmp, x0 + dx * dist[scale], y0 + dy * dist[scale]);
                if (c.IsEmpty)
                {
                    Check(label + " 阴影取样点在图内", false);
                    return;
                }
                mag[scale] = Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B);
                if (scale > 0 && mag[scale] > mag[scale - 1]) nonIncreasing = false;
                if (scale > 0) sb.Append("  ");
                sb.Append(dist[scale].ToString() + "px=" + Hex(c) + "(Δ" + mag[scale].ToString() + ")");
            }
            Console.WriteLine("[shadow] " + label + "：" + sb.ToString());
            bool fade = nonIncreasing && mag[0] > mag[mag.Length - 1] && mag[0] > 0;
            Check(label + " 阴影 2/4/6/8px 单调由浓转淡（Δ " + mag[0].ToString() + " ≥ " + mag[1].ToString()
                  + " ≥ " + mag[2].ToString() + " ≥ " + mag[3].ToString() + "）", fade);
        }

        // ---------------- 工具 ----------------

        private const int TotalCellsPublic = 42;

        private static Color PixelAt(Bitmap b, int x, int y)
        {
            if (b == null || x < 0 || y < 0 || x >= b.Width || y >= b.Height) return Color.Empty;
            return b.GetPixel(x, y);
        }

        private static bool SameAt(Bitmap b, int x, int y, Color expected)
        {
            Color c = PixelAt(b, x, y);
            if (c.IsEmpty) return false;
            return ToRgb(c) == ToRgb(expected);
        }

        /// <summary>
        /// 像素是否落在 a→b 的合成线段上（每通道都在两者之间，容差 1）。
        /// 用来证明「某个边缘像素只是抗锯齿混色、不含第三种颜色」——例如格子贴边像素
        /// 只能由 Card 与 CellBg 混出，一旦有不透明描边就会落到这段之外。
        /// </summary>
        private static bool IsBetween(Color p, Color a, Color b)
        {
            if (p.IsEmpty) return false;
            return InRange(p.R, a.R, b.R) && InRange(p.G, a.G, b.G) && InRange(p.B, a.B, b.B);
        }

        private static bool InRange(int v, int x, int y)
        {
            int lo = x < y ? x : y;
            int hi = x < y ? y : x;
            return v >= lo - 1 && v <= hi + 1;
        }

        private static string Hex(Color c)
        {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        private static ScheduleItem Item(string title, DateTime date, int hour, int minute, string color)
        {
            ScheduleItem it = new ScheduleItem();
            it.Id = Guid.NewGuid().ToString("N");
            it.Title = title;
            it.Notes = "";
            it.Date = date.Date;
            it.Hour = hour;
            it.Minute = minute;
            it.Repeat = RepeatKind.None;
            it.Enabled = true;
            it.Color = color;
            it.CreatedAt = DateTime.Now;
            return it;
        }

        private static DateTime[] GridDays(DateTime monthFirst)
        {
            int offset = ((int)monthFirst.DayOfWeek + 6) % 7;
            DateTime start = monthFirst.AddDays(-offset);
            DateTime[] days = new DateTime[42];
            for (int i = 0; i < 42; i++) days[i] = start.AddDays(i);
            return days;
        }

        private static bool IsInGrid(DateTime[] grid, DateTime d)
        {
            return d.Date >= grid[0].Date && d.Date <= grid[41].Date;
        }

        private static DateTime PickFreeDay(DateTime monthFirst, int daysInMonth, Store store,
                                            DateTime today, DateTime tomorrow)
        {
            for (int d = 1; d <= daysInMonth; d++)
            {
                DateTime candidate = new DateTime(monthFirst.Year, monthFirst.Month, d);
                if (candidate.Date == today.Date) continue;
                if (candidate.Date == tomorrow.Date) continue;
                if (store.ForDay(candidate).Count > 0) continue;
                return candidate;
            }
            return new DateTime(monthFirst.Year, monthFirst.Month, 1);
        }

        private static DateTime FindDayWithoutItems(Store store, DateTime monthFirst, DateTime today)
        {
            int days = DateTime.DaysInMonth(monthFirst.Year, monthFirst.Month);
            for (int d = 1; d <= days; d++)
            {
                DateTime candidate = new DateTime(monthFirst.Year, monthFirst.Month, d);
                if (store.ForDay(candidate).Count == 0) return candidate;
            }
            return today;
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(target, value);
        }

        private static object GetPrivateField(object target, string name)
        {
            FieldInfo f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return f == null ? null : f.GetValue(target);
        }

        private static int IndexOfDate(MonthView mv, DateTime day)
        {
            DateTime[] dates = GetPrivateField(mv, "_dates") as DateTime[];
            if (dates == null) return -1;
            for (int i = 0; i < dates.Length; i++)
            {
                if (dates[i].Date == day.Date) return i;
            }
            return -1;
        }

        private static Point CellCenter(MonthView mv, DateTime day)
        {
            Rectangle[] cells = GetPrivateField(mv, "_cells") as Rectangle[];
            int idx = IndexOfDate(mv, day);
            if (cells == null || idx < 0) return new Point(1, 1);
            Rectangle r = cells[idx];
            return new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        }

        private static void InvokeProtected(object target, string name, object arg)
        {
            MethodInfo m = target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) throw new InvalidOperationException("找不到方法 " + name);
            m.Invoke(target, new object[] { arg });
        }

        private static int ToRgb(Color c)
        {
            return (c.R << 16) | (c.G << 8) | c.B;
        }

        private static int CountColors(Bitmap bmp, int[] targets, out Dictionary<int, int> counts)
        {
            counts = new Dictionary<int, int>();
            for (int i = 0; i < targets.Length; i++) counts[targets[i]] = 0;

            HashSet<int> distinct = new HashSet<int>();
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                         ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < bd.Height; y++)
                {
                    IntPtr row = new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride);
                    for (int x = 0; x < bd.Width; x++)
                    {
                        int v = Marshal.ReadInt32(row, x * 4) & 0x00FFFFFF;
                        distinct.Add(v);
                        int idx;
                        if (counts.TryGetValue(v, out idx)) counts[v] = idx + 1;
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
            return distinct.Count;
        }

        private static void PixelChecks(string path, string label, Color bg, Color todayBg, Color accent)
        {
            if (!File.Exists(path))
            {
                Check(label + " PNG 存在", false);
                return;
            }
            long len = new FileInfo(path).Length;
            Check(label + " PNG 存在且 > 8KB: " + len.ToString() + " bytes", len > 8000);

            using (Bitmap bmp = new Bitmap(path))
            {
                int distinct;
                int ink;
                int todayExact;
                int accentExact;
                int accentNear;
                Measure(bmp, ToRgb(bg), ToRgb(todayBg), ToRgb(accent),
                        out distinct, out ink, out todayExact, out accentExact, out accentNear);

                int total = bmp.Width * bmp.Height;
                double inkRatio = total <= 0 ? 0.0 : (double)ink / (double)total;
                Console.WriteLine("[pixel] " + label + " " + bmp.Width.ToString() + "×" + bmp.Height.ToString()
                    + "，颜色数 " + distinct.ToString() + "，着墨 " + inkRatio.ToString("0.00%")
                    + "，TodayBg " + todayExact.ToString() + "，Accent 精确 " + accentExact.ToString()
                    + " / 邻近 " + accentNear.ToString());

                Check(label + " 尺寸为 960×620", bmp.Width == 960 && bmp.Height == 620);
                Check(label + " 颜色数 > 40（抗锯齿文字/圆角）: " + distinct.ToString(), distinct > 40);
                Check(label + " 着墨比 > 1.5%（确实画了文字/色条/高亮）: " + inkRatio.ToString("0.00%"), inkRatio > 0.015);
                Check(label + " TodayBg 像素 > 500: " + todayExact.ToString(), todayExact > 500);
                Check(label + " Accent 族蓝色着墨像素 > 600（今天数字/选中描边/色条）: " + accentNear.ToString(), accentNear > 600);
            }
        }

        private static void Measure(Bitmap bmp, int bg, int today, int accent,
                                    out int distinct, out int ink, out int todayExact,
                                    out int accentExact, out int accentNear)
        {
            distinct = 0;
            ink = 0;
            todayExact = 0;
            accentExact = 0;
            accentNear = 0;

            HashSet<int> set = new HashSet<int>();
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                         ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < bd.Height; y++)
                {
                    IntPtr row = new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride);
                    for (int x = 0; x < bd.Width; x++)
                    {
                        int v = Marshal.ReadInt32(row, x * 4) & 0x00FFFFFF;
                        set.Add(v);
                        if (v != bg) ink++;
                        if (v == today) todayExact++;
                        if (v == accent) accentExact++;
                        int r = (v >> 16) & 0xFF;
                        int b = v & 0xFF;
                        // Accent 族的“蓝色系着墨”：抗锯齿后的描边/数字/色条也算进来
                        if (b - r > 70) accentNear++;
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
            distinct = set.Count;
        }

        private static void ReportFile(string path)
        {
            if (File.Exists(path))
            {
                FileInfo fi = new FileInfo(path);
                Console.WriteLine(path + " | " + fi.Length.ToString() + " bytes");
            }
            else
            {
                Console.WriteLine(path + " | 不存在");
            }
        }

        private static void Check(string name, bool ok)
        {
            if (ok) { _pass++; Console.WriteLine("[PASS] " + name); }
            else { _fail++; Console.WriteLine("[FAIL] " + name); }
        }

        private static string DefaultPreviewDir()
        {
            return @"C:\Users\30123\Desktop\agent工作区\项目文件\desktop-calendar\preview";
        }

        private static string DefaultScratchDir()
        {
            return @"C:\Users\30123\Desktop\agent工作区\项目文件\desktop-calendar\_scratch";
        }
    }
}
