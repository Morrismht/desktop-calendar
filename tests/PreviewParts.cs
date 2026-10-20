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
    }
}
