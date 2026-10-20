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
            string lightPath = Path.Combine(previewDir, "month-light.png");
            RenderOne(store, monthFirst, selected, hoverDay, false, lightPath, out lightDistinct, out lightToday, out lightAccent);

            // ---------- 渲染深色 ----------
            int darkDistinct;
            int darkToday;
            int darkAccent;
            string darkPath = Path.Combine(previewDir, "month-dark.png");
            RenderOne(store, monthFirst, selected, hoverDay, true, darkPath, out darkDistinct, out darkToday, out darkAccent);

            Console.WriteLine();
            Console.WriteLine("== 产物 ==");
            ReportFile(lightPath);
            ReportFile(darkPath);

            Console.WriteLine();
            Console.WriteLine("== 像素自检 ==");
            PixelChecks(lightPath, "浅色",
                        Color.FromArgb(0xF4, 0xF6, 0xFB), Color.FromArgb(0xE8, 0xEE, 0xFF), Color.FromArgb(0x4F, 0x7C, 0xFF));
            PixelChecks(darkPath, "深色",
                        Color.FromArgb(0x12, 0x14, 0x1A), Color.FromArgb(0x23, 0x30, 0x4F), Color.FromArgb(0x6E, 0x92, 0xFF));
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

        // ---------------- 工具 ----------------

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
