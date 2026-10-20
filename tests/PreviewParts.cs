// 编译自检 / 预览渲染：/target:exe
// 编译：csc /target:exe /codepage:65001 ... _scratch\stub_all.cs src\UI\*.cs tests\PreviewParts.cs
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using DesktopCalendar.Data;
using DesktopCalendar.Models;
using DesktopCalendar.Services;
using DesktopCalendar.UI;

namespace DesktopCalendar.Tests
{
    public static class PreviewParts
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private static string _outDir;
        private static int _fail;

        [STAThread]
        public static int Main(string[] args)
        {
            try { SetProcessDPIAware(); }
            catch (Exception) { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "preview");
            if (args != null && args.Length > 0 && args[0] != null && args[0].Length > 0)
            {
                _outDir = args[0];
            }
            _outDir = Path.GetFullPath(_outDir);
            if (!Directory.Exists(_outDir)) Directory.CreateDirectory(_outDir);
            Console.WriteLine("preview dir = " + _outDir);

            Store store = BuildStore();
            Scheduler scheduler = new Scheduler(store);

            // ---- 浅色提醒卡片
            Theme.SetDark(false);
            ReminderCard light = new ReminderCard(MakeDue(store, "due"), scheduler);
            Save(Render(light, new Point(500, 520)), "reminder-light.png");
            light.Close();

            // ---- 深色提醒卡片
            Theme.SetDark(true);
            ReminderCard dark = new ReminderCard(MakeDue(store, "overdue"), scheduler);
            Save(Render(dark, new Point(500, 520)), "reminder-dark.png");
            dark.Close();

            // ---- 浅色日程编辑器
            Theme.SetDark(false);
            EventEditor editor = new EventEditor(store);
            Save(Render(editor, new Point(60, 60)), "editor-light.png");
            editor.Close();

            // ---- 事件/行为自检（不打图，只验状态机）
            CheckBehaviors(store, scheduler);

            // ---- 编辑器 v2：自绘下拉 / 星期行 / 高度自适应（放在 CheckBehaviors 之后，它会改 store 计数）
            CheckEditorV2(store);

            Console.WriteLine(_fail == 0 ? "ALL PASS" : (_fail + " CHECK(S) FAILED"));
            return _fail == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------ 渲染

        private static Bitmap Render(Form f, Point at)
        {
            f.StartPosition = FormStartPosition.Manual;
            f.Location = at;
            f.Show();
            for (int i = 0; i < 24; i++)
            {
                Application.DoEvents();
                Thread.Sleep(8);
            }
            try { f.Opacity = 1.0; }
            catch (Exception) { }
            Application.DoEvents();

            Bitmap bmp = new Bitmap(f.Width, f.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Theme.Current.Bg);
            }
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));

            f.Hide();
            Application.DoEvents();
            return bmp;
        }

        private static void Save(Bitmap bmp, string name)
        {
            string path = Path.Combine(_outDir, name);
            int w = bmp.Width;
            int h = bmp.Height;
            try
            {
                bmp.Save(path, ImageFormat.Png);
            }
            catch (Exception ex)
            {
                _fail++;
                Console.WriteLine("FAIL  save " + name + " : " + ex.Message);
                bmp.Dispose();
                return;
            }
            bmp.Dispose();

            FileInfo fi = new FileInfo(path);
            bool ok = fi.Exists && fi.Length > 1024;
            if (!ok) _fail++;
            Console.WriteLine(string.Format("{0}  {1}  {2} bytes  ({3}x{4})",
                ok ? "PASS" : "FAIL", path, fi.Exists ? fi.Length : -1, w, h));
        }

        // ------------------------------------------------------------ 数据

        private static Store BuildStore()
        {
            Store st = new Store();
            ScheduleItem a = new ScheduleItem();
            a.Id = Guid.NewGuid().ToString("N");
            a.Title = "季度汇报材料初稿（含数据核对与排版检查，标题故意写长以验证省略号）";
            a.Notes = "带上笔记本电源\r\n提前十分钟到会议室";
            a.Date = DateTime.Today;
            a.Hour = 14;
            a.Minute = 30;
            a.Repeat = RepeatKind.None;
            a.Enabled = true;
            a.Color = "#8B5CF6";
            a.CreatedAt = DateTime.Now.AddDays(-2);
            st.Add(a);

            ScheduleItem b = new ScheduleItem();
            b.Id = Guid.NewGuid().ToString("N");
            b.Title = "晨会";
            b.Notes = "";
            b.Date = DateTime.Today;
            b.Hour = 9;
            b.Minute = 0;
            b.Repeat = RepeatKind.Daily;
            b.Enabled = true;
            b.Color = "#22C55E";
            b.CreatedAt = DateTime.Now.AddDays(-10);
            st.Add(b);
            return st;
        }

        private static DueReminder MakeDue(Store st, string kind)
        {
            DueReminder d = new DueReminder();
            d.Item = st.Items[0];
            d.Occurrence = DateTime.Today.AddHours(14).AddMinutes(30);
            d.Kind = kind;
            return d;
        }

        // ------------------------------------------------------------ 行为

        private static void CheckBehaviors(Store store, Scheduler scheduler)
        {
            // 完成按钮 -> Scheduler.Complete + Closed + 自行关闭
            Theme.SetDark(false);
            DueReminder due = MakeDue(store, "due");
            ReminderCard card = new ReminderCard(due, scheduler);
            int closedCount = 0;
            card.Closed += delegate { closedCount++; };
            card.ShowAt(new Point(Screen.PrimaryScreen.WorkingArea.Right - 20,
                                  Screen.PrimaryScreen.WorkingArea.Bottom - 20));
            for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(8); }

            Point p = ClientPoint(card, 40, 98);   // [完成] 按钮中心
            SendMouse(card, p, true);
            Application.DoEvents();

            Assert(closedCount == 1, "ReminderCard.Closed 触发一次（实际 " + closedCount + "）");
            Assert(due.Item.DoneOccurrence.HasValue, "Complete 写入 DoneOccurrence");

            // 淡入完成后 Opacity 归 1
            DueReminder d2 = MakeDue(store, "due");
            ReminderCard c2 = new ReminderCard(d2, scheduler);
            c2.ShowAt(new Point(400, 400));
            for (int i = 0; i < 60; i++) { Application.DoEvents(); Thread.Sleep(10); }
            Assert(c2.Opacity >= 0.999, "淡入结束 Opacity=1（实际 " + c2.Opacity + "）");
            Assert(c2.TopMost && !c2.ShowInTaskbar, "TopMost 且不进任务栏");
            Assert(c2.ClientSize.Width == 360 && c2.ClientSize.Height == 132, "卡片尺寸 360×132");
            c2.Close();

            // 编辑器校验：空标题不得保存
            EventEditor ed = new EventEditor(store);
            ed.Show();
            Application.DoEvents();
            EventEditor edRef = ed;
            Invoke(edRef, "DoSave");
            Assert(edRef.DialogResult != DialogResult.OK && edRef.Result == null, "空标题被拦截");
            ed.Close();

            // 编辑器保存：新日程写入 Store
            int before = store.Items.Count;
            EventEditor ed2 = new EventEditor(store);
            ed2.Show();
            Application.DoEvents();
            SetText(ed2, "_txtTitle", "自动化自检日程");
            SetText(ed2, "_txtDate", "2026-01-31");
            SetText(ed2, "_txtTime", "07:05");
            Invoke(ed2, "DoSave");
            Application.DoEvents();
            Assert(ed2.Result != null, "保存后 Result 非空");
            Assert(store.Items.Count == before + 1, "store.Add 生效（" + before + " -> " + store.Items.Count + "）");
            if (ed2.Result != null)
            {
                Assert(ed2.Result.Date == new DateTime(2026, 1, 31), "日期落库 2026-01-31");
                Assert(ed2.Result.Hour == 7 && ed2.Result.Minute == 5, "时间落库 07:05");
                Assert(ed2.Result.Repeat == RepeatKind.None, "重复默认单次");
            }
            ed2.Close();

            // 编辑器编辑模式：保留 Id / CreatedAt
            ScheduleItem target = store.Items[0];
            string id0 = target.Id;
            DateTime created0 = target.CreatedAt;
            int cnt0 = store.Items.Count;
            EventEditor ed3 = new EventEditor(store, target);
            ed3.Show();
            Application.DoEvents();
            SetText(ed3, "_txtTitle", "改过的标题");
            Invoke(ed3, "DoSave");
            Application.DoEvents();
            Assert(ed3.Result == target, "编辑模式 Result 就是原对象");
            Assert(target.Id == id0 && target.CreatedAt == created0, "编辑保留 Id/CreatedAt");
            Assert(store.Items.Count == cnt0, "编辑不新增条目");
            Assert(target.Title == "改过的标题", "标题已更新");
            ed3.Close();

            // 非法日期拦截
            EventEditor ed4 = new EventEditor(store);
            ed4.Show();
            Application.DoEvents();
            SetText(ed4, "_txtTitle", "日期非法");
            SetText(ed4, "_txtDate", "2026-13-45");
            Invoke(ed4, "DoSave");
            Assert(ed4.Result == null, "非法日期被拦截");
            ed4.Close();

            // 托盘：图标真实生成 + 菜单项 + tooltip 截断
            TrayController tray = new TrayController(store);
            Assert(tray.Icon != null && tray.Icon.Icon != null, "NotifyIcon 与代码绘制图标就绪");
            Assert(tray.Icon.ContextMenuStrip != null && tray.Icon.ContextMenuStrip.Items.Count >= 4,
                "托盘菜单项数量 ≥4（实际 " + tray.Icon.ContextMenuStrip.Items.Count + "）");
            tray.SetTooltip(new string('日', 100));
            Assert(tray.Icon.Text.Length <= 63, "tooltip ≤63 字符（实际 " + tray.Icon.Text.Length + "）");

            using (Bitmap ico = tray.Icon.Icon.ToBitmap())
            {
                Color c = ico.GetPixel(8, 8);
                Assert(c.ToArgb() != Color.Transparent.ToArgb(), "托盘图标非全透明");
                ico.Save(Path.Combine(_outDir, "tray-icon.png"), ImageFormat.Png);
                Console.WriteLine("PASS  " + Path.Combine(_outDir, "tray-icon.png"));
            }
            tray.Dispose();
        }

        private static Point ClientPoint(Form f, int x, int y)
        {
            return new Point(x, y);
        }

        private static void SendMouse(Control c, Point p, bool clickBody)
        {
            System.Reflection.MethodInfo mi = typeof(Control).GetMethod("OnMouseDown",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (mi == null) { _fail++; Console.WriteLine("FAIL  找不到 OnMouseDown"); return; }
            MouseEventArgs me = new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0);
            mi.Invoke(c, new object[] { me });
        }

        private static void Invoke(object target, string method)
        {
            System.Reflection.MethodInfo mi = target.GetType().GetMethod(method,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (mi == null)
            {
                _fail++;
                Console.WriteLine("FAIL  反射不到方法 " + method);
                return;
            }
            mi.Invoke(target, null);
        }

        private static void SetText(object target, string field, string value)
        {
            System.Reflection.FieldInfo fi = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (fi == null)
            {
                _fail++;
                Console.WriteLine("FAIL  反射不到字段 " + field);
                return;
            }
            TextBox tb = fi.GetValue(target) as TextBox;
            if (tb == null) { _fail++; Console.WriteLine("FAIL  字段不是 TextBox: " + field); return; }
            tb.Text = value;
        }

        private static void Assert(bool ok, string what)
        {
            if (ok)
            {
                Console.WriteLine("PASS  " + what);
            }
            else
            {
                _fail++;
                Console.WriteLine("FAIL  " + what);
            }
        }

        // ------------------------------------------------------------ 编辑器 v2 自检

        private static void Pulse(int rounds)
        {
            for (int i = 0; i < rounds; i++)
            {
                Application.DoEvents();
                Thread.Sleep(8);
            }
            Application.DoEvents();
        }

        private static Bitmap Shot(Form f, Point at)
        {
            f.StartPosition = FormStartPosition.Manual;
            f.Location = at;
            if (!f.Visible) f.Show();
            Pulse(24);
            Bitmap bmp = new Bitmap(f.Width, f.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Theme.Current.Bg);
            }
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
            return bmp;
        }

        private static object GetField(object target, string field)
        {
            System.Reflection.FieldInfo fi = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (fi == null)
            {
                _fail++;
                Console.WriteLine("FAIL  反射不到字段 " + field);
                return null;
            }
            return fi.GetValue(target);
        }

        private static void SetField(object target, string field, object value)
        {
            System.Reflection.FieldInfo fi = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (fi == null)
            {
                _fail++;
                Console.WriteLine("FAIL  反射不到字段 " + field);
                return;
            }
            fi.SetValue(target, value);
        }

        private static int GetInt(object target, string field)
        {
            object v = GetField(target, field);
            if (v == null) return -1;
            return Convert.ToInt32(v);
        }

        private static bool GetBool(object target, string field)
        {
            object v = GetField(target, field);
            if (v == null) return false;
            return Convert.ToBoolean(v);
        }

        private static Rectangle GetRect(object target, string field)
        {
            object v = GetField(target, field);
            if (v == null) return new Rectangle(0, 0, 0, 0);
            return (Rectangle)v;
        }

        private static Rectangle[] GetRects(object target, string field)
        {
            object v = GetField(target, field);
            Rectangle[] arr = v as Rectangle[];
            if (arr == null)
            {
                _fail++;
                Console.WriteLine("FAIL  字段不是 Rectangle[]: " + field);
                return new Rectangle[0];
            }
            return arr;
        }

        private static void InvokeArg(object target, string method, object arg)
        {
            System.Reflection.MethodInfo mi = target.GetType().GetMethod(method,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (mi == null)
            {
                _fail++;
                Console.WriteLine("FAIL  反射不到方法 " + method);
                return;
            }
            mi.Invoke(target, new object[] { arg });
        }

        private static string Hex(Color c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        private static bool Eq(Color a, Color b)
        {
            return a.ToArgb() == b.ToArgb();
        }

        /// <summary>四角必须不是卡片面（圆角切掉了）、正中必须是卡片面。</summary>
        private static void AssertRound(Form f, string tag)
        {
            Region rg = f.Region;
            int w = f.ClientSize.Width;
            int h = f.ClientSize.Height;
            bool ok = rg != null
                && !rg.IsVisible(new Point(1, 1))
                && !rg.IsVisible(new Point(w - 2, 1))
                && !rg.IsVisible(new Point(1, h - 2))
                && !rg.IsVisible(new Point(w - 2, h - 2))
                && rg.IsVisible(new Point(20, 20))
                && rg.IsVisible(new Point(w - 20, 20))
                && rg.IsVisible(new Point(20, h - 20))
                && rg.IsVisible(new Point(w - 20, h - 20));
            Assert(ok, tag + " 圆角 Region：四角被切、内部连通（" + w + "×" + h + "）");
        }

        private static void CheckEditorV2(Store store)
        {
            Console.WriteLine("--- 编辑器 v2：自绘下拉 / 星期行 / 高度自适应 ---");
            Theme.SetDark(false);
            Theme t = Theme.Current;
            Color surface = t.Surface;

            EventEditor ed = new EventEditor(store);
            ed.StartPosition = FormStartPosition.Manual;
            ed.Location = new Point(80, 80);
            ed.Show();
            Pulse(24);

            Rectangle boxRepeat = GetRect(ed, "_boxRepeat");
            Rectangle[] dots = GetRects(ed, "_dots");
            int dotsBottom = dots[dots.Length - 1].Bottom - 1;

            // ---- 单次态 460
            Assert(ed.ClientSize.Width == 420 && ed.ClientSize.Height == 460,
                "单次态客户区尺寸 " + ed.ClientSize.Width + "×" + ed.ClientSize.Height + " == 420×460");
            Assert(!GetBool(ed, "_showWeekRow"), "单次态 _showWeekRow=false（无「重复日」行）");
            AssertRound(ed, "460");
            Bitmap shotSingle = Shot(ed, new Point(80, 80));
            AssertRoundPixels(shotSingle, "editor-single", t);
            Save(shotSingle, "editor-single.png");

            // ---- 下拉展开态
            SendMouse(ed, new Point(boxRepeat.X + boxRepeat.Width / 2, boxRepeat.Y + boxRepeat.Height / 2), true);
            Pulse(8);
            Assert(GetBool(ed, "_dropOpen"), "点击「重复」字段后 _dropOpen=true（已无原生 ComboBox）");
            Assert(!((Control)GetField(ed, "_txtNotes")).Visible, "下拉展开期间备注 TextBox 隐藏（否则子窗口会盖住自绘列表）");

            Bitmap shotDrop = Shot(ed, new Point(80, 80));
            // 采样行走在面板首项的实心区域（y=247，已过 10px 圆角，且高于备注框 256）
            int midY = boxRepeat.Bottom + 6 + 12;

            int rightMost = -1;
            for (int x = boxRepeat.Right + 8; x >= 0; x--)
            {
                if (!Eq(shotDrop.GetPixel(x, midY), surface)) { rightMost = x; break; }
            }
            int leftMost = -1;
            for (int x = 2; x <= boxRepeat.Right; x++)
            {
                if (!Eq(shotDrop.GetPixel(x, midY), surface)) { leftMost = x; break; }
            }
            Assert(rightMost == boxRepeat.Right - 1,
                "下拉面板最右像素列 x=" + rightMost + " == _boxRepeat.Right-1 (" + (boxRepeat.Right - 1) + ")，未越界");
            Assert(leftMost == boxRepeat.X,
                "下拉面板最左像素列 x=" + leftMost + " == _boxRepeat.X (" + boxRepeat.X + ")");
            Assert(rightMost - leftMost + 1 == boxRepeat.Width,
                "下拉面板宽度 " + (rightMost - leftMost + 1) + " == _boxRepeat.Width (" + boxRepeat.Width + ")");
            Assert(Eq(shotDrop.GetPixel(boxRepeat.X - 1, midY), surface),
                "x=" + (boxRepeat.X - 1) + " 是卡片底色 " + Hex(shotDrop.GetPixel(boxRepeat.X - 1, midY)) + "（左侧同样没溢出）");

            Color edgePx = shotDrop.GetPixel(boxRepeat.Right - 1, midY);
            Color outPx = shotDrop.GetPixel(boxRepeat.Right, midY);
            Assert(!Eq(edgePx, surface) && Eq(outPx, surface),
                "x=" + (boxRepeat.Right - 1) + " 是面板描边 " + Hex(edgePx)
                + "；x=" + boxRepeat.Right + " 是卡片底色 " + Hex(outPx));

            // 面板右边界外侧整列都不得有面板像素：235..255 是卡片面，256..355 是备注框面
            bool colClean = true;
            int badY = -1;
            Color badPx = Color.Empty;
            for (int y = boxRepeat.Bottom + 6; y <= 255; y++)
            {
                Color c = shotDrop.GetPixel(boxRepeat.Right, y);
                if (!Eq(c, surface)) { colClean = false; badY = y; badPx = c; break; }
            }
            Assert(colClean, "x=" + boxRepeat.Right + " 在 y=" + (boxRepeat.Bottom + 6) + "..255 整列为卡片底色 "
                + Hex(surface) + (badY < 0 ? "" : "，例外 y=" + badY + "=" + Hex(badPx)));

            bool colClean2 = true;
            int badY2 = -1;
            for (int y = 265; y <= 350; y++)
            {
                if (!Eq(shotDrop.GetPixel(boxRepeat.Right, y), t.SurfaceAlt)) { colClean2 = false; badY2 = y; break; }
            }
            Assert(colClean2, "x=" + boxRepeat.Right + " 在 y=265..350 为备注框面 " + Hex(t.SurfaceAlt)
                + "（面板未越界覆盖）" + (badY2 < 0 ? "" : "，例外 y=" + badY2));

            int colX = boxRepeat.X + 20;
            int topY = -1;
            for (int y = boxRepeat.Bottom + 1; y < shotDrop.Height; y++)
            {
                if (!Eq(shotDrop.GetPixel(colX, y), surface)) { topY = y; break; }
            }
            int faceY = -1;
            for (int y = topY; y < shotDrop.Height; y++)
            {
                if (Eq(shotDrop.GetPixel(colX, y), surface)) { faceY = y; break; }
            }
            Assert(topY > dotsBottom,
                "下拉（含 5px 阴影）上沿 y=" + topY + " 在「颜色」行下沿 y=" + dotsBottom + " 之下，二者不重叠");
            Console.WriteLine("      像素取证：颜色行圆点下沿 (230," + dotsBottom + ")=" + Hex(shotDrop.GetPixel(230, dotsBottom))
                + "；下拉阴影首行 (" + colX + "," + topY + ")=" + Hex(shotDrop.GetPixel(colX, topY))
                + "；下拉实面首行 (" + colX + "," + faceY + ")=" + Hex(shotDrop.GetPixel(colX, faceY))
                + "；差额 y=" + dotsBottom + "→" + topY + " 共 " + (topY - dotsBottom - 1) + " 行空档");
            Console.WriteLine("      像素取证：x=198/199/200/201 @y=" + midY + " → "
                + Hex(shotDrop.GetPixel(198, midY)) + " " + Hex(shotDrop.GetPixel(199, midY)) + " "
                + Hex(shotDrop.GetPixel(200, midY)) + " " + Hex(shotDrop.GetPixel(201, midY))
                + "（卡片面 " + Hex(surface) + "，面板描边 " + Hex(t.Border) + "）");
            Save(shotDrop, "editor-dropdown.png");

            // ---- 点击外部收起
            SendMouse(ed, new Point(8, 300), true);
            Pulse(8);
            Assert(!GetBool(ed, "_dropOpen"), "点击外部（8,300）收起下拉");
            Assert(((Control)GetField(ed, "_txtNotes")).Visible, "收起后备注 TextBox 恢复显示");

            // ---- 每周 + 三天
            int mask = RepeatRule.MaskOf(DayOfWeek.Monday)
                | RepeatRule.MaskOf(DayOfWeek.Wednesday)
                | RepeatRule.MaskOf(DayOfWeek.Friday);
            SetField(ed, "_weekMask", mask);
            InvokeArg(ed, "SetRepeatIndex", 2);
            Pulse(8);
            Assert(GetInt(ed, "_repeatIndex") == 2, "切换到「每周」_repeatIndex=2");
            Assert(GetBool(ed, "_showWeekRow"), "每周出现「重复日」行");
            Assert(ed.ClientSize.Width == 420 && ed.ClientSize.Height == 502,
                "每周态客户区尺寸 " + ed.ClientSize.Width + "×" + ed.ClientSize.Height + " == 420×502");

            Rectangle[] chips = GetRects(ed, "_weekChips");
            Assert(chips.Length == 7, "星期 chip 数量 " + chips.Length + " == 7");
            Assert(chips[0].Width == 34 && chips[0].Height == 28,
                "chip 尺寸 " + chips[0].Width + "×" + chips[0].Height + " == 34×28");
            Assert(chips[1].X - chips[0].Right == 6, "chip 间距 " + (chips[1].X - chips[0].Right) + " == 6");
            Assert(ed.ClientSize.Width - chips[6].Right >= 14,
                "星期行右侧内边距 " + (ed.ClientSize.Width - chips[6].Right) + " ≥ 14");
            Rectangle boxNotes = GetRect(ed, "_boxNotes");
            Assert(boxNotes.Y == 256 + 42, "备注整体下移 42：_boxNotes.Y=" + boxNotes.Y);
            Assert(GetRect(ed, "_btnSave").Y == 396 + 42, "保存按钮下移 42：_btnSave.Y=" + GetRect(ed, "_btnSave").Y);
            AssertRound(ed, "502");
            Bitmap shotWeekly = Shot(ed, new Point(80, 80));
            AssertRoundPixels(shotWeekly, "editor-weekly", t);
            Color pxMon = shotWeekly.GetPixel(chips[0].X + 17, chips[0].Y + 14);
            Color pxTue = shotWeekly.GetPixel(chips[1].X + 17, chips[1].Y + 14);
            Assert(Eq(pxMon, t.Accent), "已选 chip「一」底色 " + Hex(pxMon) + " == Accent " + Hex(t.Accent));
            Assert(Eq(pxTue, t.SurfaceAlt), "未选 chip「二」底色 " + Hex(pxTue) + " == SurfaceAlt " + Hex(t.SurfaceAlt));
            Save(shotWeekly, "editor-weekly.png");

            // ---- 切回单次：高度回到 460
            InvokeArg(ed, "SetRepeatIndex", 0);
            Pulse(8);
            Assert(ed.ClientSize.Height == 460 && !GetBool(ed, "_showWeekRow"),
                "切回单次 → 客户区回到 " + ed.ClientSize.Width + "×" + ed.ClientSize.Height);
            AssertRound(ed, "回到 460");
            ed.Close();
            Pulse(4);

            // ---- 至少保 1 天 / FillFrom 默认星期 / 保存写 WeekDays
            ScheduleItem one = new ScheduleItem();
            one.Id = Guid.NewGuid().ToString("N");
            one.CreatedAt = DateTime.Now;
            one.Title = "只剩一天";
            one.Date = new DateTime(2026, 3, 4);      // 周三
            one.Hour = 9;
            one.Minute = 0;
            one.Repeat = RepeatKind.Weekly;
            one.WeekDays = RepeatRule.MaskOf(DayOfWeek.Wednesday);
            one.Enabled = true;
            one.Color = "#4C8DFF";

            EventEditor ed2 = new EventEditor(store, one);
            ed2.StartPosition = FormStartPosition.Manual;
            ed2.Location = new Point(520, 80);
            ed2.Show();
            Pulse(24);
            Assert(ed2.ClientSize.Height == 502,
                "编辑「每周」条目时窗口高 " + ed2.ClientSize.Height + " == 502");
            Assert(GetInt(ed2, "_weekMask") == RepeatRule.MaskOf(DayOfWeek.Wednesday),
                "FillFrom: Weekly 且 WeekDays!=0 → 保留原掩码 " + GetInt(ed2, "_weekMask"));

            Rectangle[] chips2 = GetRects(ed2, "_weekChips");
            int ci = RepeatRule.DayIndex(DayOfWeek.Wednesday);
            SendMouse(ed2, new Point(chips2[ci].X + 17, chips2[ci].Y + 14), true);
            Pulse(4);
            Assert(GetInt(ed2, "_weekMask") == RepeatRule.MaskOf(DayOfWeek.Wednesday),
                "点掉最后一天被忽略：_weekMask 仍为 " + GetInt(ed2, "_weekMask"));

            int ciFri = RepeatRule.DayIndex(DayOfWeek.Friday);
            SendMouse(ed2, new Point(chips2[ciFri].X + 17, chips2[ciFri].Y + 14), true);
            Pulse(4);
            int expect = RepeatRule.MaskOf(DayOfWeek.Wednesday) | RepeatRule.MaskOf(DayOfWeek.Friday);
            Assert(GetInt(ed2, "_weekMask") == expect, "点选周五后 _weekMask = " + GetInt(ed2, "_weekMask") + " == " + expect);
            SetText(ed2, "_txtTitle", "每周三与周五");
            Invoke(ed2, "DoSave");
            Pulse(4);
            Assert(ed2.Result != null && ed2.Result.WeekDays == expect,
                "保存：Repeat==Weekly → WeekDays = mask "
                + (ed2.Result == null ? "null" : ed2.Result.WeekDays.ToString()) + " == " + expect);

            // FillFrom: Weekly 且 WeekDays==0 → 默认勾选 Date 的星期
            ScheduleItem zero = new ScheduleItem();
            zero.Id = Guid.NewGuid().ToString("N");
            zero.CreatedAt = DateTime.Now;
            zero.Title = "未指定星期";
            zero.Date = new DateTime(2026, 3, 6);     // 周五
            zero.Hour = 8;
            zero.Minute = 30;
            zero.Repeat = RepeatKind.Weekly;
            zero.WeekDays = 0;
            zero.Enabled = true;
            zero.Color = "#4C8DFF";
            EventEditor ed4 = new EventEditor(store, zero);
            ed4.StartPosition = FormStartPosition.Manual;
            ed4.Location = new Point(80, 620);
            ed4.Show();
            Pulse(24);
            Assert(GetInt(ed4, "_weekMask") == RepeatRule.MaskOf(DayOfWeek.Friday),
                "FillFrom: Weekly 且 WeekDays==0 → 默认勾选 Date 的星期（周五，mask=" + GetInt(ed4, "_weekMask") + "）");
            ed4.Close();
            Pulse(4);

            // 非每周 → WeekDays 写 0
            EventEditor ed3 = new EventEditor(store);
            ed3.StartPosition = FormStartPosition.Manual;
            ed3.Location = new Point(520, 620);
            ed3.Show();
            Pulse(24);
            SetText(ed3, "_txtTitle", "每天一条");
            SetField(ed3, "_weekMask", 127);
            InvokeArg(ed3, "SetRepeatIndex", 1);
            Pulse(4);
            Invoke(ed3, "DoSave");
            Pulse(4);
            Assert(ed3.Result != null && ed3.Result.Repeat == RepeatKind.Daily && ed3.Result.WeekDays == 0,
                "保存：Repeat!=Weekly → WeekDays 写 0（实际 "
                + (ed3.Result == null ? "null" : ed3.Result.WeekDays.ToString()) + "）");
        }

        /// <summary>PNG 上四角不是卡片面、正中是卡片面 —— 圆角在真实位图上没有破。</summary>
        private static void AssertRoundPixels(Bitmap bmp, string name, Theme t)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            Color c1 = bmp.GetPixel(1, 1);
            Color c2 = bmp.GetPixel(w - 2, 1);
            Color c3 = bmp.GetPixel(1, h - 2);
            Color c4 = bmp.GetPixel(w - 2, h - 2);
            Color mid = bmp.GetPixel(w / 2, h / 2);
            bool ok = !Eq(c1, t.Surface) && !Eq(c2, t.Surface) && !Eq(c3, t.Surface) && !Eq(c4, t.Surface);
            Assert(ok, name + " " + w + "×" + h + " 四角非卡片面 ((1,1)=" + Hex(c1) + " (" + (w - 2) + ",1)=" + Hex(c2)
                + " (1," + (h - 2) + ")=" + Hex(c3) + " (" + (w - 2) + "," + (h - 2) + ")=" + Hex(c4)
                + "；卡片面=" + Hex(t.Surface) + " 窗后底=" + Hex(t.Bg) + ")");
            Assert(Eq(mid, t.Surface) || !Eq(mid, t.Bg),
                name + " 正中 (" + w / 2 + "," + h / 2 + ")=" + Hex(mid) + " 已绘制卡片面");
        }
    }
}
