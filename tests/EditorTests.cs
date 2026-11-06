using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using DesktopCalendar.Data;
using DesktopCalendar.Models;
using DesktopCalendar.UI;

namespace DesktopCalendar.Tests
{
    /// <summary>
    /// 新建日程的默认日期：应当是被选中的那天，而不是永远今天；编辑已有日程时用它自己的日期。
    /// 单独编成 exe（不含 Program.cs），输出 x PASS / y FAIL，退出码非 0 即失败。
    /// </summary>
    internal static class EditorTests
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

            _root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_tmp", "editor-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Console.WriteLine("临时目录：" + _root);

            try
            {
                Case1();
                Case2();
                Case3();
                Case4();
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

        private static void Check(string name, bool ok, string detail)
        {
            if (ok)
            {
                _pass++;
                Console.WriteLine("PASS " + name);
                return;
            }
            _fail++;
            string line = name + (string.IsNullOrEmpty(detail) ? "" : " —— " + detail);
            _fails.Add(line);
            Console.WriteLine("FAIL " + line);
        }

        private static Store NewStore()
        {
            _seq++;
            string dir = Path.Combine(_root, "case" + _seq.ToString());
            Directory.CreateDirectory(dir);
            return new Store(dir);
        }

        private static string DateOf(int addDays)
        {
            return DateTime.Today.AddDays(addDays).ToString("yyyy-MM-dd");
        }

        private static void Case1()
        {
            Store store = NewStore();
            DateTime want = DateTime.Today.AddDays(3);
            using (EventEditor ed = new EventEditor(store, null, want))
            {
                string got = ed.DateText;
                Check("1 新建时默认日期=选中的那天（+3 天）", got == want.ToString("yyyy-MM-dd"),
                    "want=" + want.ToString("yyyy-MM-dd") + " got=" + got);
            }
        }

        private static void Case2()
        {
            Store store = NewStore();
            DateTime want = DateTime.Today.AddDays(-5);
            using (EventEditor ed = new EventEditor(store, null, want))
            {
                Check("2 选中过去的日子也照样初始为它（-5 天）", ed.DateText == want.ToString("yyyy-MM-dd"),
                    "got=" + ed.DateText);
            }
        }

        private static void Case3()
        {
            Store store = NewStore();
            using (EventEditor ed = new EventEditor(store, null))
            {
                Check("3 不传日期时仍退回今天（兼容旧调用）", ed.DateText == DateOf(0), "got=" + ed.DateText);
            }
        }

        private static void Case4()
        {
            Store store = NewStore();
            ScheduleItem it = new ScheduleItem();
            it.Id = "e1";
            it.Title = "病历复查";
            it.Date = DateTime.Today.AddDays(10);
            it.Hour = 15;
            it.Minute = 45;
            store.Items.Add(it);

            using (EventEditor ed = new EventEditor(store, it, DateTime.Today.AddDays(2)))
            {
                Check("4 编辑已有日程时用它自己的日期，不受选中日影响",
                    ed.DateText == it.Date.ToString("yyyy-MM-dd"), "got=" + ed.DateText);
            }
        }
    }
}
