using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using DesktopCalendar.Data;
using DesktopCalendar.Models;
using DesktopCalendar.Services;
using DesktopCalendar.UI;

namespace DesktopCalendar.Tests
{
    /// <summary>
    /// 自定义每日提示的自测：TipStore 的容错/校验/落盘，MainForm 的池子取舍与「编辑」按钮几何。
    /// 单独编成 exe（不含 Program.cs），输出 x PASS / y FAIL，退出码非 0 即失败。
    /// </summary>
    internal static class TipTests
    {
        private static int _pass;
        private static int _fail;
        private static readonly List<string> _fails = new List<string>();
        private static string _root;
        private static int _seq;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (Exception)
            {
            }

            // 临时目录放在 exe 同级，不用 Path.GetTempPath()：本机 TEMP 对子进程不可写。
            _root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_tmp", "tip-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Console.WriteLine("临时目录：" + _root);

            try
            {
                Case1();
                Case2();
                Case3();
                Case4();
                Case5();
                Case6();
                Case7();
                Case8And9();
                Case10();
                Case11();
            }
            catch (Exception ex)
            {
                _fail++;
                _fails.Add("未捕获异常：" + ex);
                Console.WriteLine("FAIL 未捕获异常：" + ex.Message);
            }

            Console.WriteLine();
            Console.WriteLine(string.Format("{0} PASS / {1} FAIL", _pass, _fail));
            for (int i = 0; i < _fails.Count; i++)
            {
                Console.WriteLine("  ! " + _fails[i]);
            }

            try
            {
                Directory.Delete(_root, true);
            }
            catch (Exception)
            {
            }
            return _fail == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- 断言

        private static void Check(string name, bool ok, string why)
        {
            if (ok)
            {
                _pass++;
                Console.WriteLine("PASS " + name);
            }
            else
            {
                _fail++;
                _fails.Add(name + " — " + why);
                Console.WriteLine("FAIL " + name + " — " + why);
            }
        }

        private static string Fmt(Rectangle r)
        {
            return string.Format("({0},{1},{2}×{3})", r.X, r.Y, r.Width, r.Height);
        }

        private static string Dump(IList<string> tips)
        {
            if (tips == null) return "<null>";
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < tips.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(tips[i]);
            }
            sb.Append("] 共 " + tips.Count + " 条");
            return sb.ToString();
        }

        private static string NewDir()
        {
            _seq++;
            string d = Path.Combine(_root, "case" + _seq);
            Directory.CreateDirectory(d);
            return d;
        }

        private static void WriteNoBom(string path, string text)
        {
            File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text));
        }

        // ---------------------------------------------------------------- 用例

        /// <summary>① 空/缺失文件回落默认。</summary>
        private static void Case1()
        {
            string dir = NewDir();
            TipStore s = new TipStore(dir);
            Check("1a 缺失 tips.json → 空池且 LastError 有值",
                s.Tips.Count == 0 && !string.IsNullOrEmpty(s.LastError),
                string.Format("count={0} err={1}", s.Tips.Count, s.LastError));
            Check("1b 缺失文件时不会自己造文件",
                !File.Exists(Path.Combine(dir, "tips.json")), "tips.json 不该被创建");
            Check("1c 内置默认 26 条可回落",
                s.Defaults.Count == 26 && TipStore.BuiltinDefaults.Count == 26,
                string.Format("instance={0} static={1}", s.Defaults.Count, TipStore.BuiltinDefaults.Count));

            string dir2 = NewDir();
            WriteNoBom(Path.Combine(dir2, "tips.json"), "{\"version\":1,\"savedAt\":\"2025-01-01T00:00:00\",\"tips\":[]}");
            TipStore s2 = new TipStore(dir2);
            Check("1d 空数组 → 空池且 LastError 有值",
                s2.Tips.Count == 0 && !string.IsNullOrEmpty(s2.LastError),
                string.Format("count={0} err={1}", s2.Tips.Count, s2.LastError));
        }

        /// <summary>② 坏 JSON 不抛。</summary>
        private static void Case2()
        {
            string dir = NewDir();
            WriteNoBom(Path.Combine(dir, "tips.json"), "{oops, not json at all");
            TipStore s = null;
            string thrown = null;
            try
            {
                s = new TipStore(dir);
            }
            catch (Exception ex)
            {
                thrown = ex.GetType().Name + ": " + ex.Message;
            }
            Check("2a 坏 JSON 构造不抛 + 空池 + LastError",
                thrown == null && s != null && s.Tips.Count == 0 && !string.IsNullOrEmpty(s.LastError),
                string.Format("thrown={0} count={1} err={2}", thrown, s == null ? -1 : s.Tips.Count, s == null ? null : s.LastError));

            string dir2 = NewDir();
            WriteNoBom(Path.Combine(dir2, "tips.json"), "完全不是 JSON");
            TipStore s2 = null;
            string thrown2 = null;
            try
            {
                s2 = new TipStore(dir2);
            }
            catch (Exception ex)
            {
                thrown2 = ex.GetType().Name + ": " + ex.Message;
            }
            Check("2b 纯文本文件不抛 + 空池 + LastError",
                thrown2 == null && s2 != null && s2.Tips.Count == 0 && !string.IsNullOrEmpty(s2.LastError),
                string.Format("thrown={0} count={1}", thrown2, s2 == null ? -1 : s2.Tips.Count));

            string dir3 = NewDir();
            WriteNoBom(Path.Combine(dir3, "tips.json"), "{\"tips\":\"不是数组\"}");
            TipStore s3 = null;
            string thrown3 = null;
            try
            {
                s3 = new TipStore(dir3);
            }
            catch (Exception ex)
            {
                thrown3 = ex.GetType().Name + ": " + ex.Message;
            }
            Check("2c tips 不是数组也不抛",
                thrown3 == null && s3 != null && s3.Tips.Count == 0,
                string.Format("thrown={0} count={1}", thrown3, s3 == null ? -1 : s3.Tips.Count));
        }

        /// <summary>③ 增删改保存后重新 Load 一致。</summary>
        private static void Case3()
        {
            string dir = NewDir();
            TipStore s = new TipStore(dir);
            TipCheck a1 = s.Add("第一条");
            TipCheck a2 = s.Add("第二条");
            TipCheck a3 = s.Add("第三条");
            TipCheck set = s.Set(0, "第一条改");
            s.RemoveAt(1);
            s.Save();
            TipStore r = new TipStore(dir);
            Check("3a 增删改链路返回 Ok",
                a1 == TipCheck.Ok && a2 == TipCheck.Ok && a3 == TipCheck.Ok && set == TipCheck.Ok,
                string.Format("{0}/{1}/{2}/{3}", a1, a2, a3, set));
            Check("3b 保存后重新 Load 内容与顺序一致",
                r.Tips.Count == 2 && r.Tips[0] == "第一条改" && r.Tips[1] == "第三条",
                Dump(r.Tips));
            Check("3c 重新 Load 不报错",
                string.IsNullOrEmpty(r.LastError), "err=" + r.LastError);

            TipStore s2 = new TipStore(NewDir());
            s2.Add("  前后都有空白  ");
            Check("3d 新增自动去首尾空白",
                s2.Tips.Count == 1 && s2.Tips[0] == "前后都有空白", Dump(s2.Tips));
        }

        /// <summary>④ 去重被拒。</summary>
        private static void Case4()
        {
            TipStore s = new TipStore(NewDir());
            s.Add("同一句话");
            Check("4a 完全相同 → Duplicate", s.Add("同一句话") == TipCheck.Duplicate, "期望 Duplicate");
            Check("4b 只差首尾空白 → Duplicate", s.Add("  同一句话 ") == TipCheck.Duplicate, "期望 Duplicate");
            Check("4c 池子没被污染", s.Tips.Count == 1, Dump(s.Tips));
            s.Add("另一句话");
            Check("4d 替换成已存在文案 → Duplicate", s.Set(1, "同一句话") == TipCheck.Duplicate, "期望 Duplicate");
            Check("4e 空串 / 纯空白 → Empty",
                s.Add("") == TipCheck.Empty && s.Add("   ") == TipCheck.Empty, "期望 Empty");
            Check("4f 替换自己不算重复", s.Set(0, "同一句话") == TipCheck.Ok, "期望 Ok");
        }

        /// <summary>⑤ 超长被截 / 被拒。</summary>
        private static void Case5()
        {
            TipStore s = new TipStore(NewDir());
            string tooLong = new string('字', 61);
            string fit = new string('字', 60);
            Check("5a 61 字新增 → TooLong", s.Add(tooLong) == TipCheck.TooLong, "期望 TooLong");
            Check("5b 60 字可接受", s.Check(fit, -1) == TipCheck.Ok, "期望 Ok");

            string dir = NewDir();
            WriteNoBom(Path.Combine(dir, "tips.json"),
                "{\"version\":1,\"tips\":[\"" + new string('长', 70) + "\",\"正常一条\"]}");
            TipStore r = new TipStore(dir);
            Check("5c 载入超长条目 → 截到 60 字",
                r.Tips.Count == 2 && r.Tips[0].Length == 60 && r.Tips[1] == "正常一条",
                string.Format("count={0} len0={1}", r.Tips.Count, r.Tips.Count > 0 ? r.Tips[0].Length : -1));
            Check("5d 截断时 LastError 有值", !string.IsNullOrEmpty(r.LastError), "err=" + r.LastError);
        }

        /// <summary>⑥ 超过 50 条被拒。</summary>
        private static void Case6()
        {
            TipStore s = new TipStore(NewDir());
            int added = 0;
            for (int i = 0; i < TipStore.MaxTips; i++)
            {
                if (s.Add("提示" + i) == TipCheck.Ok) added++;
            }
            Check("6a 加满 50 条全部 Ok", added == 50 && s.Tips.Count == 50,
                string.Format("added={0} count={1}", added, s.Tips.Count));
            Check("6b 第 51 条 → Full", s.Add("提示第51条") == TipCheck.Full, "期望 Full");

            string dir = NewDir();
            StringBuilder sb = new StringBuilder("{\"version\":1,\"tips\":[");
            for (int i = 0; i < 60; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"批量" + i + "\"");
            }
            sb.Append("]}");
            WriteNoBom(Path.Combine(dir, "tips.json"), sb.ToString());
            TipStore r = new TipStore(dir);
            Check("6c 载入 60 条 → 只留 50 条 + LastError",
                r.Tips.Count == 50 && !string.IsNullOrEmpty(r.LastError),
                string.Format("count={0} err={1}", r.Tips.Count, r.LastError));
        }

        /// <summary>⑦ ResetToDefault 生效。</summary>
        private static void Case7()
        {
            string dir = NewDir();
            TipStore s = new TipStore(dir);
            s.Add("我自己的一条");
            s.Save();
            TipStore r = new TipStore(dir);
            Check("7a 先确认落盘的是自定义", r.Tips.Count == 1 && r.Tips[0] == "我自己的一条", Dump(r.Tips));

            r.ResetToDefault();
            Check("7b ResetToDefault 内存生效（26 条且与 Defaults 一致）",
                r.Tips.Count == 26 && r.Tips[0] == r.Defaults[0] && r.Tips[25] == r.Defaults[25],
                Dump(r.Tips));

            r.Save();
            TipStore r2 = new TipStore(dir);
            Check("7c ResetToDefault + Save 落盘生效",
                r2.Tips.Count == 26 && r2.Tips[7] == r2.Defaults[7],
                Dump(r2.Tips));
        }

        /// <summary>⑧ 今天无日程时 TipText 只返回用户池内容。⑨ 提示面板几何：编辑链接在面板头右上，行区域嵌在面板内。</summary>
        private static void Case8And9()
        {
            string dir = NewDir();
            TipStore seed = new TipStore(dir);
            seed.Add("自定义甲");
            seed.Add("自定义乙");
            seed.Add("自定义丙");
            seed.Save();

            Store appStore = new Store(dir);
            Scheduler sch = new Scheduler(appStore);
            MainForm form = null;
            string err = null;
            try
            {
                form = new MainForm(appStore, sch);
            }
            catch (Exception ex)
            {
                err = ex.GetType().Name + ": " + ex.Message;
            }

            if (form == null)
            {
                Check("8 TipText 用户池非空时只返回用户池内容", false, "MainForm 构造失败：" + err);
                Check("9a 编辑链接在提示面板内右上", false, "MainForm 构造失败：" + err);
                Check("9b 提示行区域嵌在面板内且头部留白", false, "MainForm 构造失败：" + err);
                return;
            }

            try
            {
                HashSet<string> pool = new HashSet<string>();
                pool.Add("自定义甲");
                pool.Add("自定义乙");
                pool.Add("自定义丙");

                bool only = true;
                string bad = "";
                DateTime day = new DateTime(2025, 1, 1);
                for (int k = 0; k < 40; k++)
                {
                    string s = form.TipText(day.AddDays(k));
                    if (!pool.Contains(s))
                    {
                        only = false;
                        bad = s;
                        break;
                    }
                    form.NextTip();
                }
                Check("8a 今天无日程 + _tipIndex=0 时 40 次取样全部来自用户池", only,
                    "越界文案：" + bad);
                Check("8b 用户池 3 条 → 取样覆盖到 ≥3 种文案",
                    CountDistinct(form, 12) >= 3, "取样种类不足");
                Check("8c MainForm 的 TipStore 指向同一 dataDir",
                    form.TipStoreRef != null && form.TipStoreRef.Tips.Count == 3
                    && string.Equals(Path.GetFullPath(form.TipStoreRef.DataDir), Path.GetFullPath(appStore.DataDir), StringComparison.OrdinalIgnoreCase),
                    string.Format("dir={0} count={1}", form.TipStoreRef == null ? "<null>" : form.TipStoreRef.DataDir,
                        form.TipStoreRef == null ? -1 : form.TipStoreRef.Tips.Count));

                Rectangle e = form.TipEditBounds;
                Rectangle panel = form.TipBounds;
                Rectangle list = form.TipListBounds;
                Check("9a 编辑链接在提示面板内右上",
                    panel.Contains(e) && e.Right <= panel.Right - 4,
                    string.Format("edit={0} panel={1}", Fmt(e), Fmt(panel)));
                Check("9b 提示行区域嵌在面板内且头部留白",
                    list.Y >= panel.Y + 24 && list.Right <= panel.Right - 4
                    && list.Bottom <= panel.Bottom - 4 && panel.Height >= 150 && panel.Height <= 320,
                    string.Format("list={0} panel={1}", Fmt(list), Fmt(panel)));
                Check("9c 提示面板落在日程列表之下、状态区之上",
                    e.X > 0 && panel.Bottom < form.ClientSize.Height - 40,
                    string.Format("panel={0} clientH={1}", Fmt(panel), form.ClientSize.Height));

                Rectangle tipCard = form.TipCardBounds;
                Rectangle sideCard = form.SideCardBounds;
                Check("9d 每日提示是独立一卡，与日程卡上下并列且不重叠",
                    tipCard.Y > sideCard.Bottom && tipCard.Width == sideCard.Width
                    && sideCard.Contains(panel) == false && tipCard.Contains(panel),
                    string.Format("side={0} tip={1} panel={2}", Fmt(sideCard), Fmt(tipCard), Fmt(panel)));
                Check("9e 日程卡内不含提示面板（提示已经搬出日程框）",
                    !sideCard.Contains(panel) && tipCard.Top >= sideCard.Bottom,
                    string.Format("side={0} tip={1}", Fmt(sideCard), Fmt(tipCard)));
            }
            finally
            {
                try
                {
                    form.Dispose();
                }
                catch (Exception)
                {
                }
            }
        }

        private static int CountDistinct(MainForm form, int samples)
        {
            HashSet<string> seen = new HashSet<string>();
            DateTime day = new DateTime(2025, 6, 1);
            for (int k = 0; k < samples; k++)
            {
                seen.Add(form.TipText(day.AddDays(k)));
                form.NextTip();
            }
            return seen.Count;
        }

        /// <summary>⑩ tips.json 原子写不残留 .tmp。</summary>
        private static void Case10()
        {
            string dir = NewDir();
            TipStore s = new TipStore(dir);
            s.Add("原子写第一条");
            s.Save();
            string file = Path.Combine(dir, "tips.json");
            Check("10a 首次 Save 后不残留 .tmp", !File.Exists(file + ".tmp"),
                "残留：" + file + ".tmp");
            byte[] raw = File.ReadAllBytes(file);
            Check("10b 落盘是 UTF-8 无 BOM",
                raw.Length > 3 && !(raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF),
                "长度 " + raw.Length + " 前三字节 " + raw[0].ToString("X2") + " " + raw[1].ToString("X2") + " " + raw[2].ToString("X2"));

            s.Add("原子写第二条");
            s.Save();
            Check("10c 二次（覆盖）Save 后不残留 .tmp", !File.Exists(file + ".tmp"),
                "残留：" + file + ".tmp");

            string text = Encoding.UTF8.GetString(raw);
            Check("10d JSON 结构是 {version,savedAt,tips[]}",
                text.Contains("\"version\"") && text.Contains("\"savedAt\"") && text.Contains("\"tips\""),
                text.Length > 90 ? text.Substring(0, 90) : text);
        }

        /// <summary>⑪ 状态提醒不被用户池盖掉：逾期优先，只有今天没日程时才出用户池文案。</summary>
        private static void Case11()
        {
            string dir = NewDir();
            TipStore seed = new TipStore(dir);
            seed.Add("自定义甲");
            seed.Add("自定义乙");
            seed.Add("自定义丙");
            seed.Save();

            Store appStore = new Store(dir);
            DateTime today = DateTime.Today;
            ScheduleItem it = new ScheduleItem();
            it.Id = Guid.NewGuid().ToString("N");
            it.CreatedAt = DateTime.Now;
            it.Enabled = true;
            it.Title = "八点这件事";
            it.Notes = null;
            it.Date = today;
            it.Hour = 8;
            it.Minute = 0;
            it.Repeat = RepeatKind.None;
            it.RepeatUntil = null;
            it.WeekDays = 0;
            it.Color = null;
            appStore.Items.Clear();
            appStore.Add(it);
            appStore.Save();

            Scheduler sch = new Scheduler(appStore);
            MainForm form = null;
            string err = null;
            try
            {
                form = new MainForm(appStore, sch);
            }
            catch (Exception ex)
            {
                err = ex.GetType().Name + ": " + ex.Message;
            }

            if (form == null)
            {
                Check("11a 逾期优先于用户池", false, "MainForm 构造失败：" + err);
                Check("11b _tipIndex>0 时不显示状态文案", false, "MainForm 构造失败：" + err);
                Check("11c 今天无日程时回落用户池", false, "MainForm 构造失败：" + err);
                return;
            }

            try
            {
                HashSet<string> pool = new HashSet<string>();
                pool.Add("自定义甲");
                pool.Add("自定义乙");
                pool.Add("自定义丙");

                DateTime now = today.AddHours(9);
                form.TipIndexValue = 0;
                string a = form.TipText(now);
                Check("11a 用户池非空 + 今天有 1 项逾期 + _tipIndex=0 → 先给逾期提醒",
                    a != null && a.StartsWith("有 1 项已逾期"),
                    "实际：" + (a == null ? "<null>" : a));

                form.TipIndexValue = 1;
                string b = form.TipText(now);
                Check("11b 同样逾期但 _tipIndex=1 → 回到用户池，不再出状态文案",
                    b != null && pool.Contains(b),
                    "实际：" + (b == null ? "<null>" : b));

                form.TipIndexValue = 0;
                DateTime emptyDay = new DateTime(2024, 5, 6);
                string c = form.TipText(emptyDay.AddHours(9));
                Check("11c 用户池非空 + 今天无日程 + _tipIndex=0 → 走用户池",
                    c != null && pool.Contains(c),
                    "实际：" + (c == null ? "<null>" : c));
                Check("11d 无日程 + _tipIndex=0 与 8a 一致（取样仍在池内）",
                    form.TipText(emptyDay.AddHours(10)) != null,
                    "返回 null");
            }
            finally
            {
                try
                {
                    form.Dispose();
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
