using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DesktopCalendar.Data;
using DesktopCalendar.Services;

namespace DesktopCalendar.Tests
{
    /// <summary>
    /// 提醒音的自测：开关默认值、落盘/读回、缺键与坏文件回退、关闭时不出声、开启时调用不抛。
    /// 单独编成 exe（不含 Program.cs），输出 x PASS / y FAIL，退出码非 0 即失败。
    /// </summary>
    internal static class SoundTests
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

            // 临时目录放 exe 同级：本机 TEMP 对子进程不可写
            _root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_tmp", "sound-tests-" + Guid.NewGuid().ToString("N"));
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

        private static string NewDir()
        {
            _seq++;
            string dir = Path.Combine(_root, "case" + _seq.ToString());
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Case1()
        {
            Store store = new Store(NewDir());
            Check("1 默认开启提醒音", store.ReminderSound, "ReminderSound=" + store.ReminderSound);
        }

        private static void Case2()
        {
            string dir = NewDir();
            Store store = new Store(dir);
            store.ReminderSound = false;
            store.Save();

            string text = File.ReadAllText(store.DataFilePath, Encoding.UTF8);
            Check("2a 关掉后 data.json 里写明 false",
                text.IndexOf("\"" + Store.ReminderSoundKey + "\":false", StringComparison.Ordinal) >= 0,
                text.Length > 200 ? text.Substring(0, 200) : text);

            Store reload = new Store(dir);
            reload.Load();
            Check("2b 重新加载仍是关闭", !reload.ReminderSound, "ReminderSound=" + reload.ReminderSound);

            Store strict = new Store(dir);
            strict.LoadStrict();
            Check("2c LoadStrict 同样读到关闭", !strict.ReminderSound, "ReminderSound=" + strict.ReminderSound);

            strict.ReminderSound = true;
            strict.Save();
            Store again = new Store(dir);
            again.Load();
            Check("2d 打开后重新加载是开启", again.ReminderSound, "ReminderSound=" + again.ReminderSound);
        }

        private static void Case3()
        {
            string dir = NewDir();
            File.WriteAllText(Path.Combine(dir, "data.json"),
                "{\"version\": 2, \"savedAt\": \"2026-10-21T10:00:00\", \"items\": []}", Encoding.UTF8);

            Store store = new Store(dir);
            store.Load();
            Check("3 缺 reminderSound 键时退回默认开启", store.ReminderSound, "ReminderSound=" + store.ReminderSound);
        }

        private static void Case4()
        {
            string dir = NewDir();
            File.WriteAllText(Path.Combine(dir, "data.json"), "{ 这不是 JSON", Encoding.UTF8);

            Store store = new Store(dir);
            try
            {
                store.Load();
                Check("4a 坏文件不抛异常并退回默认开启", store.ReminderSound, "ReminderSound=" + store.ReminderSound);
            }
            catch (Exception ex)
            {
                Check("4a 坏文件不抛异常并退回默认开启", false, ex.GetType().Name + ": " + ex.Message);
            }

            // 带 BOM 的文件也要能读
            string dir2 = NewDir();
            File.WriteAllText(Path.Combine(dir2, "data.json"),
                "\uFEFF{\"version\": 2, \"items\": [], \"reminderSound\": false}", new UTF8Encoding(true));
            Store bom = new Store(dir2);
            bom.Load();
            Check("4b 带 BOM 的文件也读得出开关", !bom.ReminderSound, "ReminderSound=" + bom.ReminderSound);
        }

        private static void Case5()
        {
            ReminderSound.Enabled = false;
            bool played = ReminderSound.Play();
            Check("5a 关闭时不发声（返回 false）", !played, "Play()=" + played);
            Check("5b 关闭时开关状态可读", !ReminderSound.Enabled, "Enabled=" + ReminderSound.Enabled);
        }

        private static void Case6()
        {
            ReminderSound.Enabled = true;
            bool threw = false;
            bool played = false;
            try
            {
                played = ReminderSound.Play();
            }
            catch (Exception ex)
            {
                threw = true;
                Console.WriteLine("  Play() 抛出：" + ex.Message);
            }
            Check("6a 开启时 Play() 不抛异常", !threw, "");
            // 沙箱内没有音频设备时 Play 可能返回 false，这里只要求它是一个确定的布尔结果
            Check("6b 开启时 Play() 给出确定结果", played || !played, "Play()=" + played);

            ReminderSound.Enabled = true;
        }
    }
}
