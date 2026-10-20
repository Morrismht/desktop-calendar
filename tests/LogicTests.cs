using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DesktopCalendar.Data;
using DesktopCalendar.Models;
using DesktopCalendar.Services;
using Microsoft.Win32;

namespace DesktopCalendar.Tests
{
    /// <summary>纯逻辑层自检（/target:exe）。断言全部真实执行，打印 PASS/FAIL。</summary>
    internal static class LogicTests
    {
        private static int _pass;
        private static int _fail;
        private static int _skip;
        private static readonly List<string> _failed = new List<string>();
        private static readonly List<string> _skipped = new List<string>();

        public static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (Exception)
            {
            }

            Console.WriteLine("=== desktop-calendar 逻辑层自检 ===");

            Run("RepeatRule", TestRepeatRule);
            Run("WeekDays", TestWeekDays);
            Run("Json", TestJson);
            Run("Store", TestStore);
            Run("Store.ResolveDataDir", TestResolveDataDir);
            Run("Scheduler", TestScheduler);
            Run("AutoStart", TestAutoStart);
            Run("AutoStart 启动文件夹兜底", TestStartupCmdFallback);

            TryDeleteEmptyDir(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-tmp"));

            Console.WriteLine();
            Console.WriteLine(string.Format("通过 {0} 项 / 失败 {1} 项 / 跳过 {2} 项（共 {3} 项）",
                _pass, _fail, _skip, _pass + _fail + _skip));
            if (_fail > 0)
            {
                Console.WriteLine("失败清单：");
                for (int i = 0; i < _failed.Count; i++)
                {
                    Console.WriteLine("  - " + _failed[i]);
                }
            }
            if (_skip > 0)
            {
                Console.WriteLine("跳过清单（环境限制，非逻辑缺陷）：");
                for (int i = 0; i < _skipped.Count; i++)
                {
                    Console.WriteLine("  - " + _skipped[i]);
                }
            }
            Console.WriteLine(_fail == 0 ? "RESULT: ALL PASS" : "RESULT: FAILED");
            return _fail == 0 ? 0 : 1;
        }

        // ---------------- 断言助手 ----------------

        private static void Check(bool ok, string name)
        {
            if (ok)
            {
                _pass++;
                Console.WriteLine("PASS: " + name);
            }
            else
            {
                _fail++;
                _failed.Add(name);
                Console.WriteLine("FAIL: " + name);
            }
        }

        /// <summary>环境限制导致的未执行断言：不算 PASS 也不算 FAIL，单独计数并列出原因。</summary>
        private static void Skip(string name, string reason)
        {
            _skip++;
            _skipped.Add(name + "（" + reason + "）");
            Console.WriteLine("SKIP: " + name + " —— " + reason);
        }

        private static void Eq(object expected, object actual, string name)
        {
            bool ok = Equals(expected, actual);
            if (!ok)
            {
                Console.WriteLine(string.Format("      期望 [{0}] 实际 [{1}]", Fmt(expected), Fmt(actual)));
            }
            Check(ok, name);
        }

        private static string Fmt(object v)
        {
            if (v == null)
            {
                return "null";
            }
            if (v is DateTime)
            {
                return ((DateTime)v).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            if (v is bool)
            {
                return ((bool)v) ? "true" : "false";
            }
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static void Run(string title, Action body)
        {
            Console.WriteLine();
            Console.WriteLine("--- " + title + " ---");
            try
            {
                body();
            }
            catch (Exception ex)
            {
                _fail++;
                _failed.Add(title + " 抛出异常");
                Console.WriteLine("FAIL: " + title + " 抛出异常：" + ex);
            }
        }

        // ---------------- 1. RepeatRule ----------------

        private static ScheduleItem NewItem(string title, DateTime date, int hour, int minute, RepeatKind repeat)
        {
            ScheduleItem it = new ScheduleItem();
            it.Title = title;
            it.Date = date;
            it.Hour = hour;
            it.Minute = minute;
            it.Repeat = repeat;
            return it;
        }

        private static void TestRepeatRule()
        {
            ScheduleItem none = NewItem("单次会议", new DateTime(2024, 3, 10), 9, 30, RepeatKind.None);
            ScheduleItem daily = NewItem("每天 09:30", new DateTime(2024, 3, 10), 9, 30, RepeatKind.Daily);
            ScheduleItem weekly = NewItem("周一 14:05", new DateTime(2024, 3, 11), 14, 5, RepeatKind.Weekly);
            ScheduleItem monthly31 = NewItem("31 号 08:00", new DateTime(2024, 1, 31), 8, 0, RepeatKind.Monthly);
            ScheduleItem monthly15 = NewItem("15 号 08:00", new DateTime(2024, 1, 15), 8, 0, RepeatKind.Monthly);

            // Daily
            Check(!RepeatRule.OccursOn(daily, new DateTime(2024, 3, 9)), "Daily 起始日之前不命中");
            Check(RepeatRule.OccursOn(daily, new DateTime(2024, 3, 10)), "Daily 起始日命中");
            Check(RepeatRule.OccursOn(daily, new DateTime(2024, 3, 11, 23, 59, 59)), "Daily 后续每天命中（只看 .Date）");
            Check(RepeatRule.OccursOn(daily, new DateTime(2024, 4, 1)), "Daily 跨月仍命中");

            // 单次
            Check(RepeatRule.OccursOn(none, new DateTime(2024, 3, 10)), "单次 当天命中");
            Check(!RepeatRule.OccursOn(none, new DateTime(2024, 3, 11)), "单次 次日不命中");
            Check(!RepeatRule.OccursOn(none, new DateTime(2024, 3, 9)), "单次 前一天不命中");

            // OccurrenceStart
            Eq(new DateTime(2024, 3, 10, 9, 30, 0), RepeatRule.OccurrenceStart(daily, new DateTime(2024, 3, 10)),
                "OccurrenceStart = day.Date + Hour:Minute");
            Eq(new DateTime(2024, 3, 20, 14, 5, 0), RepeatRule.OccurrenceStart(weekly, new DateTime(2024, 3, 20, 18, 0, 0)),
                "OccurrenceStart 忽略传入的时分");

            // Weekly
            Eq(DayOfWeek.Monday, weekly.Date.DayOfWeek, "Weekly 用例起始日是周一");
            Check(RepeatRule.OccursOn(weekly, new DateTime(2024, 3, 18)), "Weekly 只命中同星期（下周一）");
            Check(RepeatRule.OccursOn(weekly, new DateTime(2024, 4, 1)), "Weekly 只命中同星期（三周后的周一）");
            Check(!RepeatRule.OccursOn(weekly, new DateTime(2024, 3, 19)), "Weekly 周二不命中");
            Check(!RepeatRule.OccursOn(weekly, new DateTime(2024, 3, 12)), "Weekly 起始周内其他天不命中");
            Check(!RepeatRule.OccursOn(weekly, new DateTime(2024, 3, 4)), "Weekly 起始日之前同星期也不命中");

            // Monthly：31 号遇到短月在当月最后一天
            Check(RepeatRule.OccursOn(monthly31, new DateTime(2024, 1, 31)), "Monthly 1 月 31 日命中");
            Check(RepeatRule.OccursOn(monthly31, new DateTime(2024, 2, 29)), "Monthly 31 号在 2 月（闰年）取 29 日");
            Check(RepeatRule.OccursOn(monthly31, new DateTime(2024, 4, 30)), "Monthly 31 号在 4 月取 30 日");
            Check(RepeatRule.OccursOn(monthly31, new DateTime(2024, 6, 30)), "Monthly 31 号在 6 月取 30 日");
            Check(!RepeatRule.OccursOn(monthly31, new DateTime(2024, 2, 28)), "Monthly 闰年 2 月 28 日不命中");
            Check(!RepeatRule.OccursOn(monthly31, new DateTime(2024, 4, 29)), "Monthly 4 月 29 日不命中");
            Check(RepeatRule.OccursOn(monthly31, new DateTime(2025, 2, 28)), "Monthly 31 号在平年 2 月取 28 日");
            Check(RepeatRule.OccursOn(monthly31, new DateTime(2024, 3, 31)), "Monthly 长月仍是 31 日");
            Check(RepeatRule.OccursOn(monthly15, new DateTime(2024, 2, 15)), "Monthly 15 号在 2 月命中 15 日");
            Check(!RepeatRule.OccursOn(monthly15, new DateTime(2024, 2, 29)), "Monthly 15 号不落到月末");
            Check(!RepeatRule.OccursOn(monthly31, new DateTime(2024, 1, 15)), "Monthly 起始日之前不命中");

            // RepeatUntil：含当天，越界不命中
            ScheduleItem until = daily.Clone();
            until.RepeatUntil = new DateTime(2024, 3, 12);
            Check(RepeatRule.OccursOn(until, new DateTime(2024, 3, 12)), "RepeatUntil 含当天命中");
            Check(!RepeatRule.OccursOn(until, new DateTime(2024, 3, 13)), "RepeatUntil 越界不命中");
            Check(RepeatRule.OccursOn(until, new DateTime(2024, 3, 10)), "RepeatUntil 窗口内命中");

            // OccurrencesBetween
            List<DateTime> three = new List<DateTime>(
                RepeatRule.OccurrencesBetween(daily, new DateTime(2024, 3, 10), new DateTime(2024, 3, 12)));
            Eq(3, three.Count, "OccurrencesBetween Daily 3 天 = 3 个端点");
            Eq(new DateTime(2024, 3, 10, 9, 30, 0), three[0], "OccurrencesBetween 首端点 = 起始日 09:30");
            Eq(new DateTime(2024, 3, 12, 9, 30, 0), three[2], "OccurrencesBetween 末端点 = 结束日 09:30");

            List<DateTime> weeks = new List<DateTime>(
                RepeatRule.OccurrencesBetween(weekly, new DateTime(2024, 3, 11), new DateTime(2024, 3, 25)));
            Eq(3, weeks.Count, "OccurrencesBetween Weekly 3 周 = 3 个端点");
            Eq(new DateTime(2024, 3, 25, 14, 5, 0), weeks[2], "OccurrencesBetween Weekly 末端点含结束日");

            List<DateTime> one = new List<DateTime>(
                RepeatRule.OccurrencesBetween(none, new DateTime(2024, 3, 1), new DateTime(2024, 3, 31)));
            Eq(1, one.Count, "OccurrencesBetween 单次窗口内 = 1 个");
            List<DateTime> zero = new List<DateTime>(
                RepeatRule.OccurrencesBetween(none, new DateTime(2024, 3, 11), new DateTime(2024, 3, 31)));
            Eq(0, zero.Count, "OccurrencesBetween 单次不命中窗口 = 0 个");

            List<DateTime> bounded = new List<DateTime>(
                RepeatRule.OccurrencesBetween(until, new DateTime(2024, 3, 1), new DateTime(2024, 3, 31)));
            Eq(3, bounded.Count, "OccurrencesBetween 受 RepeatUntil 截断 = 3 个");

            // NextOccurrence
            Eq(new DateTime(2024, 3, 11, 9, 30, 0),
                RepeatRule.NextOccurrence(daily, new DateTime(2024, 3, 10, 9, 30, 0)),
                "NextOccurrence Daily 严格晚于当次 = 次日同一时刻");
            Eq(new DateTime(2024, 3, 10, 9, 30, 0),
                RepeatRule.NextOccurrence(daily, new DateTime(2024, 3, 10, 9, 29, 0)),
                "NextOccurrence Daily 当天未到点 = 当天");
            Eq(new DateTime(2024, 3, 18, 14, 5, 0),
                RepeatRule.NextOccurrence(weekly, new DateTime(2024, 3, 11, 14, 5, 0)),
                "NextOccurrence Weekly = 下周一");
            Eq(new DateTime(2024, 3, 31, 8, 0, 0),
                RepeatRule.NextOccurrence(monthly31, new DateTime(2024, 2, 29, 8, 0, 0)),
                "NextOccurrence Monthly 31 号：2/29 之后 = 3/31");
            Eq(null, RepeatRule.NextOccurrence(until, new DateTime(2024, 3, 12, 9, 30, 0)),
                "NextOccurrence 超出 RepeatUntil = null");
            Eq(null, RepeatRule.NextOccurrence(none, new DateTime(2024, 3, 10, 9, 30, 0)),
                "NextOccurrence 单次已过 = null");
            Eq(new DateTime(2024, 3, 10, 9, 30, 0),
                RepeatRule.NextOccurrence(none, new DateTime(2024, 3, 1, 0, 0, 0)),
                "NextOccurrence 单次未到 = 起始时刻");

            // Describe（中文）
            Eq("单次", RepeatRule.Describe(none), "Describe 单次");
            Eq("每天", RepeatRule.Describe(daily), "Describe 每天");
            Eq("每周一", RepeatRule.Describe(weekly), "Describe 每周一");
            Eq("每月 15 日", RepeatRule.Describe(monthly15), "Describe 每月 15 日");
            Eq("每月 31 日", RepeatRule.Describe(monthly31), "Describe 每月 31 日");
            Check(RepeatRule.Describe(until).StartsWith("每天（至 2024-03-12）", StringComparison.Ordinal),
                "Describe 带 RepeatUntil 时追加至某日");
        }

        // ---------------- 1b. 每周指定星期几（WeekDays） ----------------

        /// <summary>一三五 = bit0|bit2|bit4。</summary>
        private const int MonWedFri = 1 + 4 + 16;

        private static ScheduleItem NewWeekly(string title, DateTime date, int weekDays)
        {
            ScheduleItem it = NewItem(title, date, 9, 0, RepeatKind.Weekly);
            it.WeekDays = weekDays;
            return it;
        }

        private static void TestWeekDays()
        {
            // DayIndex / MaskOf / MaskHas
            Eq(0, RepeatRule.DayIndex(DayOfWeek.Monday), "DayIndex 周一 = 0");
            Eq(6, RepeatRule.DayIndex(DayOfWeek.Sunday), "DayIndex 周日 = 6");
            Eq(1, RepeatRule.MaskOf(DayOfWeek.Monday), "MaskOf 周一 = 1");
            Eq(64, RepeatRule.MaskOf(DayOfWeek.Sunday), "MaskOf 周日 = 64");
            Check(RepeatRule.MaskHas(MonWedFri, DayOfWeek.Wednesday), "MaskHas 命中周三");
            Check(RepeatRule.MaskHas(MonWedFri, DayOfWeek.Monday), "MaskHas 命中周一");
            Check(!RepeatRule.MaskHas(MonWedFri, DayOfWeek.Tuesday), "MaskHas 不命中周二");
            Check(!RepeatRule.MaskHas(0, DayOfWeek.Monday), "MaskHas(0) 恒不命中");

            // MaskText
            Eq("", RepeatRule.MaskText(0), "MaskText(0) = 空串");
            Eq("一、三、五", RepeatRule.MaskText(MonWedFri), "MaskText 一三五用中文顿号");
            Eq("日", RepeatRule.MaskText(RepeatRule.MaskOf(DayOfWeek.Sunday)), "MaskText 单日 = 日");
            Eq("一、二、三、四、五、六、日", RepeatRule.MaskText(127), "MaskText 全选 = 一二三四五六日");

            // mask 命中 / 不命中（起始日 2024-03-11 是周一）
            ScheduleItem mwf = NewWeekly("一三五 09:00", new DateTime(2024, 3, 11), MonWedFri);
            Eq(DayOfWeek.Monday, mwf.Date.DayOfWeek, "WeekDays 用例起始日是周一");
            Check(RepeatRule.OccursOn(mwf, new DateTime(2024, 3, 11)), "mask 命中起始日周一");
            Check(RepeatRule.OccursOn(mwf, new DateTime(2024, 3, 13)), "mask 命中周三");
            Check(RepeatRule.OccursOn(mwf, new DateTime(2024, 3, 15)), "mask 命中周五");
            Check(!RepeatRule.OccursOn(mwf, new DateTime(2024, 3, 12)), "mask 不命中周二");
            Check(!RepeatRule.OccursOn(mwf, new DateTime(2024, 3, 16)), "mask 不命中周六");
            Check(!RepeatRule.OccursOn(mwf, new DateTime(2024, 3, 17)), "mask 不命中周日");
            Check(RepeatRule.OccursOn(mwf, new DateTime(2024, 3, 20)), "mask 跨周仍命中下周三");

            // start 日期下限（掩码命中但在起始日之前 → 不发生）
            Check(!RepeatRule.OccursOn(mwf, new DateTime(2024, 3, 6)), "mask 命中周三但早于起始日 = 不发生");
            Check(!RepeatRule.OccursOn(mwf, new DateTime(2024, 3, 8)), "mask 命中周五但早于起始日 = 不发生");

            // RepeatUntil 上限（含当天）
            ScheduleItem bounded = mwf.Clone();
            bounded.RepeatUntil = new DateTime(2024, 3, 15);
            Check(RepeatRule.OccursOn(bounded, new DateTime(2024, 3, 15)), "RepeatUntil 当天仍命中");
            Check(RepeatRule.OccursOn(bounded, new DateTime(2024, 3, 13)), "RepeatUntil 窗口内命中");
            Check(!RepeatRule.OccursOn(bounded, new DateTime(2024, 3, 18)), "RepeatUntil 之后不命中（下周一）");

            // mask = 0 时与旧 Weekly 完全等价
            ScheduleItem legacyWeekly = NewWeekly("旧周复", new DateTime(2024, 3, 11), 0);
            int mismatch = 0;
            DateTime cursor = new DateTime(2024, 3, 1);
            for (int i = 0; i < 90; i++)
            {
                DateTime day = cursor.AddDays(i);
                bool oldBehavior = day.Date >= legacyWeekly.Date.Date && day.DayOfWeek == legacyWeekly.Date.DayOfWeek;
                if (RepeatRule.OccursOn(legacyWeekly, day) != oldBehavior)
                {
                    mismatch++;
                }
            }
            Eq(0, mismatch, "WeekDays=0 时 90 天逐日与旧 Weekly 行为逐点一致");
            Eq("每周一", RepeatRule.Describe(legacyWeekly), "WeekDays=0 时 Describe 仍为 每周一");

            // 全选 127 = 从起始日起每天
            ScheduleItem everyday = NewWeekly("全选 127", new DateTime(2024, 3, 11), 127);
            int missing = 0;
            for (int i = 0; i < 30; i++)
            {
                if (!RepeatRule.OccursOn(everyday, new DateTime(2024, 3, 11).AddDays(i)))
                {
                    missing++;
                }
            }
            Eq(0, missing, "mask=127 时从起始日起 30 天每天都命中");

            // 单日 mask 的 NextOccurrence 跳到下一周同一天
            ScheduleItem onlyWed = NewWeekly("只周三 09:00", new DateTime(2024, 3, 11), RepeatRule.MaskOf(DayOfWeek.Wednesday));
            Eq(new DateTime(2024, 3, 13, 9, 0, 0),
                RepeatRule.NextOccurrence(onlyWed, new DateTime(2024, 3, 11, 9, 0, 0)),
                "单日 mask：周一起始之后 = 本周三");
            Eq(new DateTime(2024, 3, 20, 9, 0, 0),
                RepeatRule.NextOccurrence(onlyWed, new DateTime(2024, 3, 13, 9, 0, 0)),
                "单日 mask：本周三之后 = 下一周同一天");
            ScheduleItem onlyWedBounded = onlyWed.Clone();
            onlyWedBounded.RepeatUntil = new DateTime(2024, 3, 13);
            Eq(null,
                RepeatRule.NextOccurrence(onlyWedBounded, new DateTime(2024, 3, 13, 9, 0, 0)),
                "单日 mask 受 RepeatUntil 约束：本周三之后无下一个 = null");

            // 多日 mask 的 NextOccurrence 取最近一次
            Eq(new DateTime(2024, 3, 13, 9, 0, 0),
                RepeatRule.NextOccurrence(mwf, new DateTime(2024, 3, 11, 9, 0, 0)),
                "NextOccurrence mask：周一之后 = 周三");
            Eq(new DateTime(2024, 3, 15, 9, 0, 0),
                RepeatRule.NextOccurrence(mwf, new DateTime(2024, 3, 13, 9, 0, 0)),
                "NextOccurrence mask：周三之后 = 周五");
            Eq(new DateTime(2024, 3, 18, 9, 0, 0),
                RepeatRule.NextOccurrence(mwf, new DateTime(2024, 3, 15, 9, 0, 0)),
                "NextOccurrence mask：周五之后 = 下周一（跨周跳跃）");

            // OccurrencesBetween 跨周跳跃
            List<DateTime> week1 = new List<DateTime>(
                RepeatRule.OccurrencesBetween(mwf, new DateTime(2024, 3, 11), new DateTime(2024, 3, 17)));
            Eq(3, week1.Count, "OccurrencesBetween mask 一周 = 3 个端点");
            Eq(new DateTime(2024, 3, 11, 9, 0, 0), week1[0], "OccurrencesBetween mask 第 1 个 = 周一");
            Eq(new DateTime(2024, 3, 15, 9, 0, 0), week1[2], "OccurrencesBetween mask 第 3 个 = 周五");

            // Describe
            Eq("每周一、三、五", RepeatRule.Describe(mwf), "Describe mask = 每周一、三、五");
            Eq("每周一、三、五（至 2024-03-15）", RepeatRule.Describe(bounded), "Describe mask 带 RepeatUntil");

            // Clone / Normalize 夹紧
            Eq(MonWedFri, mwf.Clone().WeekDays, "Clone 复制 WeekDays");
            ScheduleItem clampedHigh = NewWeekly("夹紧上限", new DateTime(2024, 3, 11), 200);
            clampedHigh.Normalize();
            Eq(127, clampedHigh.WeekDays, "Normalize 把 WeekDays=200 夹到 127");
            ScheduleItem clampedLow = NewWeekly("夹紧下限", new DateTime(2024, 3, 11), -5);
            clampedLow.Normalize();
            Eq(0, clampedLow.WeekDays, "Normalize 把 WeekDays=-5 夹到 0");

            // 其它 RepeatKind 忽略 WeekDays
            ScheduleItem dailyIgnored = NewItem("每天但带掩码", new DateTime(2024, 3, 11), 9, 0, RepeatKind.Daily);
            dailyIgnored.WeekDays = RepeatRule.MaskOf(DayOfWeek.Monday);
            Check(RepeatRule.OccursOn(dailyIgnored, new DateTime(2024, 3, 12)),
                "Daily 忽略 WeekDays（周二也命中）");
            ScheduleItem noneIgnored = NewItem("单次带掩码", new DateTime(2024, 3, 11), 9, 0, RepeatKind.None);
            noneIgnored.WeekDays = 127;
            Check(!RepeatRule.OccursOn(noneIgnored, new DateTime(2024, 3, 12)),
                "单次忽略 WeekDays（次日不命中）");
            ScheduleItem monthlyIgnored = NewItem("每月 11 日带掩码", new DateTime(2024, 3, 11), 9, 0, RepeatKind.Monthly);
            monthlyIgnored.WeekDays = RepeatRule.MaskOf(DayOfWeek.Wednesday);
            Check(RepeatRule.OccursOn(monthlyIgnored, new DateTime(2024, 4, 11)),
                "Monthly 忽略 WeekDays（4/11 周四仍命中）");
            Check(!RepeatRule.OccursOn(monthlyIgnored, new DateTime(2024, 4, 10)),
                "Monthly 忽略 WeekDays（4/10 周三不因掩码命中）");

            // JSON 往返
            string dir = NewTempDir("dc-selftest-weekdays-");
            try
            {
                Store store = new Store(dir);
                ScheduleItem saved = NewWeekly("一三五保存", new DateTime(2024, 3, 11), MonWedFri);
                saved.Id = "wd-id";
                store.Add(saved);
                string text = File.ReadAllText(store.DataFilePath, Encoding.UTF8);
                Check(text.IndexOf("\"WeekDays\"", StringComparison.Ordinal) >= 0, "data.json 写出 WeekDays 键");
                Store reloaded = new Store(dir);
                reloaded.LoadStrict();
                Eq(MonWedFri, reloaded.Find("wd-id").WeekDays, "往返 WeekDays = 21");
                Check(RepeatRule.OccursOn(reloaded.Find("wd-id"), new DateTime(2024, 3, 13)),
                    "往返后仍命中周三");
                Check(!RepeatRule.OccursOn(reloaded.Find("wd-id"), new DateTime(2024, 3, 12)),
                    "往返后仍不命中周二");

                // 旧数据（无 WeekDays 键）：字段得 0，行为与改动前一致
                string legacyJson = "{\n" +
                    "  \"version\": 1,\n" +
                    "  \"savedAt\": \"2024-03-01T00:00:00\",\n" +
                    "  \"items\": [\n" +
                    "    {\n" +
                    "      \"Id\": \"legacy-weekly\",\n" +
                    "      \"Title\": \"旧数据周一\",\n" +
                    "      \"Notes\": \"\",\n" +
                    "      \"Date\": \"2024-03-11T00:00:00\",\n" +
                    "      \"Hour\": 9,\n" +
                    "      \"Minute\": 0,\n" +
                    "      \"Repeat\": 2,\n" +
                    "      \"RepeatUntil\": null,\n" +
                    "      \"Enabled\": true,\n" +
                    "      \"Color\": \"#4F7CFF\",\n" +
                    "      \"CreatedAt\": \"2024-03-01T00:00:00\",\n" +
                    "      \"LastFiredAt\": null,\n" +
                    "      \"SnoozeUntil\": null,\n" +
                    "      \"DoneOccurrence\": null\n" +
                    "    }\n" +
                    "  ]\n" +
                    "}";
                string legacyDir = NewTempDir("dc-selftest-legacy-");
                try
                {
                    File.WriteAllText(Path.Combine(legacyDir, "data.json"), legacyJson, new UTF8Encoding(false));
                    Store legacy = new Store(legacyDir);
                    legacy.LoadStrict();
                    Eq(1, legacy.Items.Count, "旧 data.json（无 WeekDays 键）读入 1 条");
                    ScheduleItem old = legacy.Find("legacy-weekly");
                    Check(old != null, "旧数据按 Id 找到条目");
                    Eq(0, old.WeekDays, "旧数据缺 WeekDays 键 → WeekDays = 0");
                    Eq(RepeatKind.Weekly, old.Repeat, "旧数据 Repeat = Weekly");
                    Check(RepeatRule.OccursOn(old, new DateTime(2024, 3, 11)), "旧数据 2024-03-11（周一）发生");
                    Check(RepeatRule.OccursOn(old, new DateTime(2024, 3, 18)), "旧数据 2024-03-18（下周一）发生");
                    Check(!RepeatRule.OccursOn(old, new DateTime(2024, 3, 12)), "旧数据 2024-03-12（周二）不发生");
                    Eq("每周一", RepeatRule.Describe(old), "旧数据 Describe 仍为 每周一");
                }
                finally
                {
                    TryDeleteDir(legacyDir);
                }
            }
            finally
            {
                TryDeleteDir(dir);
            }
        }

        // ---------------- 2. Json ----------------

        private static void TestJson()
        {
            string tricky = "中文\"引号\\反斜杠\n换行\t制表";
            string quoted = Json.Esc(tricky);
            Check(quoted.IndexOf("中文") >= 0, "Esc 中文原样输出（不转 \\u）");
            Check(quoted.IndexOf("\\\"") >= 0, "Esc 引号被转义");
            Check(quoted.IndexOf("\\\\") >= 0, "Esc 反斜杠被转义");
            Check(quoted.IndexOf("\\n") >= 0, "Esc 换行转义为 \\n");
            Check(quoted.IndexOf("\\t") >= 0, "Esc 制表转义为 \\t");
            Eq("\"" + "a" + "\\\"" + "b" + "\"", Json.Esc("a\"b"), "Esc 输出带引号的字面量");
            Eq("null", Json.Esc(null), "Esc(null) = null");

            Dictionary<string, object> o = new Dictionary<string, object>();
            o["s"] = tricky;
            o["n"] = null;
            o["b"] = true;
            o["bf"] = false;
            o["i"] = 42;
            o["neg"] = -7;
            o["l"] = 9000000000L;
            o["dbl"] = 1.5;
            DateTime dt = new DateTime(2024, 3, 10, 9, 30, 15);
            o["dt"] = dt;
            o["dtn"] = null;
            o["empty"] = "";

            string text = Json.Write(o);
            Check(text.IndexOf("2024-03-10T09:30:15") >= 0, "日期写成 ISO yyyy-MM-ddTHH:mm:ss");
            Check(text.IndexOf("中文") >= 0, "整份 JSON 保留中文");

            Dictionary<string, object> back = Json.ReadObject(text);
            Eq(tricky, Json.Str(back, "s"), "往返 string（中文/引号/反斜杠/换行/制表）");
            Eq("", Json.Str(back, "empty"), "往返空字符串");
            Eq(null, Json.Str(back, "n"), "往返 null（Str 取默认 null）");
            Check(back.ContainsKey("n") && back["n"] == null, "往返 null 键存在且值为 null");
            Eq(true, Json.Bool(back, "b"), "往返 true");
            Eq(false, Json.Bool(back, "bf"), "往返 false");
            Eq(42, Json.Int(back, "i"), "往返 int");
            Eq(-7, Json.Int(back, "neg"), "往返负 int");
            Eq(9000000000L, back["l"], "往返 long");
            Eq(1.5, back["dbl"], "往返 double");
            Eq(dt, Json.Date(back, "dt"), "往返 DateTime");
            Eq(null, Json.Date(back, "dtn"), "往返 null DateTime");
            Eq("缺省值", Json.Str(back, "missing", "缺省值"), "Str 缺键返回缺省值");
            Eq(9, Json.Int(back, "missing", 9), "Int 缺键返回缺省值");
            Eq(true, Json.Bool(back, "missing", true), "Bool 缺键返回缺省值");

            // 数组
            Dictionary<string, object> a1 = new Dictionary<string, object>();
            a1["Id"] = "a";
            a1["Hour"] = 1;
            a1["Title"] = "日程甲";
            Dictionary<string, object> a2 = new Dictionary<string, object>();
            a2["Id"] = "b";
            a2["Hour"] = 2;
            a2["Title"] = "日程乙";
            List<object> arr = new List<object>();
            arr.Add(a1);
            arr.Add(a2);

            List<Dictionary<string, object>> parsed = Json.ReadObjectArray(Json.Write(arr));
            Eq(2, parsed.Count, "ReadObjectArray 解析 2 个对象的数组");
            Eq("a", Json.Str(parsed[0], "Id"), "ReadObjectArray 第 1 项 Id");
            Eq("日程甲", Json.Str(parsed[0], "Title"), "ReadObjectArray 第 1 项中文 Title");
            Eq("b", Json.Str(parsed[1], "Id"), "ReadObjectArray 第 2 项 Id");
            Eq(2, Json.Int(parsed[1], "Hour"), "ReadObjectArray 第 2 项 Hour");

            // 嵌套
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["version"] = 1;
            root["items"] = arr;
            root["nested"] = a1;
            Dictionary<string, object> rootBack = Json.ReadObject(Json.Write(root));
            Eq(1, Json.Int(rootBack, "version"), "嵌套 root version 往返");
            Eq(2, Json.AsObjectArray(rootBack["items"]).Count, "嵌套 items 数组往返 2 项");
            Eq("a", Json.Str(Json.AsObjectArray(rootBack["items"])[0], "Id"), "嵌套 items 第 1 项 Id");
            Eq("a", Json.Str(Json.ReadObject(Json.Write(a1)), "Id"), "单对象往返");

            // 空数组 / 空对象 / 非法
            Eq(0, Json.ReadObjectArray("[]").Count, "ReadObjectArray 空数组 = 0 项");
            Eq(0, Json.ReadObject("{}").Count, "ReadObject 空对象 = 0 键");
            Eq(0, Json.ReadObjectArray("").Count, "ReadObjectArray 空文本 = 0 项");

            bool threw = false;
            try
            {
                Json.ReadObject("{oops}");
            }
            catch (Exception)
            {
                threw = true;
            }
            Check(threw, "非法 JSON 文本抛异常");
        }

        // ---------------- 3. Store ----------------

        private static void TestStore()
        {
            string dir = NewTempDir("dc-selftest-store-");
            try
            {
                Store store = new Store(dir);
                Eq(0, store.Items.Count, "Store 新建实例初始 0 条");
                Eq(Path.Combine(dir, "data.json"), store.DataFilePath, "DataFilePath = <dir>\\data.json");
                Eq(null, store.LastError, "新建 Store LastError 为 null");

                // 默认数据目录 = %APPDATA%\DesktopCalendar（只校验路径推导，不写入）
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                Eq(Path.Combine(Path.Combine(appData, "DesktopCalendar"), "data.json"),
                    new Store().DataFilePath, "默认 DataFilePath = %APPDATA%\\DesktopCalendar\\data.json");

                ScheduleItem a = NewItem("周会准备", new DateTime(2024, 3, 11), 9, 0, RepeatKind.None);
                a.Id = "id-a";
                a.Notes = "备注：中文\"引号\"\n换行\t制表";
                a.Color = "#22C55E";
                a.Enabled = true;

                ScheduleItem b = NewItem("晨会", new DateTime(2024, 3, 11), 8, 30, RepeatKind.Weekly);
                b.Id = "id-b";
                b.Notes = "";
                b.Color = "#F59E0B";

                ScheduleItem c = NewItem("晚间复盘", new DateTime(2024, 3, 11), 20, 0, RepeatKind.Daily);
                c.Id = "id-c";
                c.Notes = "每天复盘 10 分钟";
                c.Color = "#8B5CF6";
                c.RepeatUntil = new DateTime(2024, 4, 30);

                store.Add(a);
                store.Add(b);
                store.Add(c);
                Eq(3, store.Items.Count, "Store Add 3 条");
                Check(File.Exists(store.DataFilePath), "Add 后已落盘 data.json");

                // 新实例 + Load
                Store reloaded = new Store(dir);
                reloaded.Load();
                Eq(3, reloaded.Items.Count, "新建 Store 实例 Load 后条数一致 = 3");

                ScheduleItem ra = reloaded.Find("id-a");
                Check(ra != null, "Load 后按 Id 找到单次条目");
                Eq(a.Id, ra.Id, "往返 Id");
                Eq(a.Title, ra.Title, "往返 Title");
                Eq(a.Notes, ra.Notes, "往返 Notes（含引号/换行/制表）");
                Eq(a.Date, ra.Date, "往返 Date");
                Eq(a.Hour, ra.Hour, "往返 Hour");
                Eq(a.Minute, ra.Minute, "往返 Minute");
                Eq(a.Repeat, ra.Repeat, "往返 Repeat（None）");
                Eq(a.RepeatUntil, ra.RepeatUntil, "往返 RepeatUntil（null）");
                Eq(a.Enabled, ra.Enabled, "往返 Enabled");
                Eq(a.Color, ra.Color, "往返 Color");
                Eq(a.LastFiredAt, ra.LastFiredAt, "往返 LastFiredAt（null）");
                Eq(a.SnoozeUntil, ra.SnoozeUntil, "往返 SnoozeUntil（null）");
                Eq(a.DoneOccurrence, ra.DoneOccurrence, "往返 DoneOccurrence（null）");

                ScheduleItem rb = reloaded.Find("id-b");
                Eq(RepeatKind.Weekly, rb.Repeat, "往返 Repeat（Weekly）");
                Eq(8, rb.Hour, "往返 Weekly 条目 Hour");
                ScheduleItem rc = reloaded.Find("id-c");
                Eq(RepeatKind.Daily, rc.Repeat, "往返 Repeat（Daily）");
                Eq(new DateTime(2024, 4, 30), rc.RepeatUntil.Value.Date, "往返 RepeatUntil 日期");

                // ForDay：按时间升序
                List<ScheduleItem> day = reloaded.ForDay(new DateTime(2024, 3, 11));
                Eq(3, day.Count, "ForDay 2024-03-11 = 3 条");
                Eq("晨会", day[0].Title, "ForDay 升序第 1 条 = 08:30 的周重复条目");
                Eq("周会准备", day[1].Title, "ForDay 升序第 2 条 = 09:00");
                Eq("晚间复盘", day[2].Title, "ForDay 升序第 3 条 = 20:00");
                Eq(3, reloaded.TotalForDay(new DateTime(2024, 3, 11)), "TotalForDay = 3");
                Eq(0, reloaded.DoneForDay(new DateTime(2024, 3, 11)), "DoneForDay 初始 = 0");
                Eq(1, reloaded.ForDay(new DateTime(2024, 3, 12)).Count, "ForDay 次日只剩 Daily 条目 = 1");
                Eq(0, reloaded.ForDay(new DateTime(2024, 3, 10)).Count, "ForDay 起始日之前 = 0");

                // Update 生效 + DoneForDay
                ra.DoneOccurrence = RepeatRule.OccurrenceStart(ra, new DateTime(2024, 3, 11));
                reloaded.Update(ra);
                Eq(1, reloaded.DoneForDay(new DateTime(2024, 3, 11)), "标记完成 1 条后 DoneForDay = 1");
                Check(reloaded.Find("id-a").IsDoneOn(new DateTime(2024, 3, 11, 9, 0, 0)),
                    "IsDoneOn 命中已完成的 occurrence");
                Check(!reloaded.Find("id-a").IsDoneOn(new DateTime(2024, 3, 11, 10, 0, 0)),
                    "IsDoneOn 不命中其他时刻");
                Eq(3, reloaded.TotalForDay(new DateTime(2024, 3, 11)), "完成不改变 TotalForDay");

                rb.Title = "晨会（已改）";
                reloaded.Update(rb);
                Eq("晨会（已改）", reloaded.Find("id-b").Title, "Update 生效（标题被替换）");
                Eq(3, reloaded.Items.Count, "Update 不改变条数");

                // Remove
                reloaded.Remove("id-c");
                Eq(2, reloaded.Items.Count, "Remove 生效后 = 2 条");
                Check(reloaded.Find("id-c") == null, "Remove 后 Find 返回 null");

                // Save 后文件是合法 JSON
                reloaded.Save();
                Eq(null, reloaded.LastError, "Save 后 LastError 为 null");
                string fileText = File.ReadAllText(reloaded.DataFilePath, Encoding.UTF8);
                Dictionary<string, object> rootObj = Json.ReadObject(fileText);
                Check(rootObj != null, "Save 后文件是合法 JSON 对象");
                Check(fileText.IndexOf("\"items\"") >= 0, "JSON 含 items 键");
                List<Dictionary<string, object>> rows = Json.AsObjectArray(rootObj["items"]);
                Eq(2, rows.Count, "JSON items 数量 = 2");
                Check(!File.Exists(reloaded.DataFilePath + ".tmp"), "原子写不遗留 .tmp 临时文件");

                // 再 Load：Update/Remove 已落盘
                Store again = new Store(dir);
                again.Load();
                Eq(2, again.Items.Count, "Update/Remove 已落盘（再 Load = 2 条）");
                Eq("晨会（已改）", again.Find("id-b").Title, "改后的标题已落盘");
                Eq(1, again.DoneForDay(new DateTime(2024, 3, 11)), "完成状态已落盘");
                Eq(null, again.LastError, "再次 Load 无错误");

                // 损坏文件不应抛异常
                File.WriteAllText(again.DataFilePath, "{ 这不是 JSON ", new UTF8Encoding(false));
                Store broken = new Store(dir);
                Eq(0, broken.Items.Count, "损坏文件 Load 后 0 条（不抛异常）");
                Check(broken.LastError != null, "损坏文件记录到 LastError");
            }
            finally
            {
                TryDeleteDir(dir);
            }
        }

        // ---------------- 4. Scheduler ----------------

        private static void TestScheduler()
        {
            string dir = NewTempDir("dc-selftest-sched-");
            try
            {
                Store store = new Store(dir);
                ScheduleItem m = NewItem("体检", new DateTime(2024, 3, 11), 10, 0, RepeatKind.None);
                m.Id = "s-id-m";
                store.Add(m);
                Scheduler sch = new Scheduler(store);
                Check(store == sch.Store, "Scheduler.Store 暴露注入的 Store");

                List<DueReminder> r = sch.Poll(new DateTime(2024, 3, 11, 9, 59, 0));
                Eq(0, r.Count, "未到点不报");
                r = sch.Poll(new DateTime(2024, 3, 11, 10, 0, 0));
                Eq(1, r.Count, "正好到点报 1 条");
                Eq("due", r[0].Kind, "到点 Kind = due");
                Eq(new DateTime(2024, 3, 11, 10, 0, 0), r[0].Occurrence, "提醒 occurrence = 2024-03-11 10:00");
                Eq("体检", r[0].Item.Title, "提醒携带正确日程");
                r = sch.Poll(new DateTime(2024, 3, 11, 10, 0, 30));
                Eq(0, r.Count, "同一 occurrence 第二次 Poll 不重复报");
                r = sch.Poll(new DateTime(2024, 3, 11, 10, 20, 0));
                Eq(0, r.Count, "已提醒过的 occurrence 之后不再报 due");
                Eq(new DateTime(2024, 3, 11, 10, 0, 0), store.Find("s-id-m").LastFiredAt.Value,
                    "LastFiredAt 记录了已提醒的 occurrence");

                // 稍后提醒
                ScheduleItem s2 = NewItem("复查", new DateTime(2024, 3, 11), 11, 0, RepeatKind.None);
                s2.Id = "s-id-snooze";
                store.Add(s2);
                List<DueReminder> due2 = sch.Poll(new DateTime(2024, 3, 11, 11, 0, 0));
                Eq(1, due2.Count, "11:00 只报新到点的 1 条");
                Eq("复查", due2[0].Item.Title, "新报的是复查");

                sch.Snooze(due2[0], 10);
                Eq(new DateTime(2024, 3, 11, 11, 10, 0), store.Find("s-id-snooze").SnoozeUntil.Value,
                    "Snooze(10) 置 SnoozeUntil = 11:10");
                r = sch.Poll(new DateTime(2024, 3, 11, 11, 5, 0));
                Eq(0, r.Count, "Snooze 后 5 分钟不报");
                r = sch.Poll(new DateTime(2024, 3, 11, 11, 9, 59));
                Eq(0, r.Count, "Snooze 后 9 分 59 秒仍不报");
                r = sch.Poll(new DateTime(2024, 3, 11, 11, 10, 0));
                Eq(1, r.Count, "Snooze 到点报 1 条");
                Eq("snooze", r[0].Kind, "Snooze 到点 Kind = snooze");
                r = sch.Poll(new DateTime(2024, 3, 11, 11, 10, 1));
                Eq(0, r.Count, "snooze 报过之后不重复");

                // 完成
                ScheduleItem s3 = NewItem("取快递", new DateTime(2024, 3, 11), 12, 0, RepeatKind.None);
                s3.Id = "s-id-done";
                store.Add(s3);
                int doneBefore = store.DoneForDay(new DateTime(2024, 3, 11));
                List<DueReminder> due3 = sch.Poll(new DateTime(2024, 3, 11, 12, 0, 0));
                Eq(1, due3.Count, "12:00 报 1 条");
                sch.Complete(due3[0]);
                Eq(doneBefore + 1, store.DoneForDay(new DateTime(2024, 3, 11)), "Complete 后 DoneForDay +1");
                Eq(new DateTime(2024, 3, 11, 12, 0, 0), store.Find("s-id-done").DoneOccurrence.Value,
                    "Complete 写入 DoneOccurrence");
                r = sch.Poll(new DateTime(2024, 3, 11, 12, 1, 0));
                Eq(0, r.Count, "Complete 后当天不再报");
                r = sch.Poll(new DateTime(2024, 3, 11, 12, 40, 0));
                Eq(0, r.Count, "Complete 后逾期也不再报");

                // 逾期
                ScheduleItem s4 = NewItem("提交报告", new DateTime(2024, 3, 11), 14, 0, RepeatKind.None);
                s4.Id = "s-id-overdue";
                store.Add(s4);
                List<DueReminder> od = sch.Poll(new DateTime(2024, 3, 11, 14, 20, 0));
                Eq(1, od.Count, "14:20 只报未提醒过的逾期项 1 条");
                Eq("due", od[0].Kind, "逾期 20 分钟仍算 due（未超 30 分钟）");

                ScheduleItem s5 = NewItem("逾期报告", new DateTime(2024, 3, 11), 15, 0, RepeatKind.None);
                s5.Id = "s-id-overdue2";
                store.Add(s5);
                List<DueReminder> od2 = sch.Poll(new DateTime(2024, 3, 11, 15, 31, 0));
                Eq(1, od2.Count, "15:31 报 1 条逾期项");
                Eq("overdue", od2[0].Kind, "逾期 31 分钟 Kind = overdue");

                // OverdueToday（含已提醒过的，供状态栏提示）
                List<DueReminder> overdueList = sch.OverdueToday(new DateTime(2024, 3, 11, 15, 31, 0));
                Eq(4, overdueList.Count, "OverdueToday 15:31 = 4 条未完成且逾期超 30 分钟");
                Eq("overdue", overdueList[0].Kind, "OverdueToday 的 Kind = overdue");
                Eq(new DateTime(2024, 3, 11, 10, 0, 0), overdueList[0].Occurrence, "OverdueToday 按时间升序第 1 条 = 10:00 体检");
                Eq("体检", overdueList[0].Item.Title, "OverdueToday 第 1 条标题 = 体检");
                Eq(0, sch.OverdueToday(new DateTime(2024, 3, 11, 10, 0, 0)).Count,
                    "OverdueToday 10:00 = 0 条（刚好到点不算逾期）");

                // 停用项不报
                ScheduleItem s6 = NewItem("停用项", new DateTime(2024, 3, 11), 16, 0, RepeatKind.None);
                s6.Id = "s-id-disabled";
                s6.Enabled = false;
                store.Add(s6);
                r = sch.Poll(new DateTime(2024, 3, 11, 16, 0, 0));
                Eq(0, r.Count, "Enabled=false 的项不报");
                Eq(0, store.ForDay(new DateTime(2024, 3, 11)).FindAll(delegate(ScheduleItem x) { return x.Id == "s-id-disabled"; }).Count,
                    "停用项不出现在 ForDay");

                // 非当天
                r = sch.Poll(new DateTime(2024, 3, 12, 16, 0, 0));
                Eq(0, r.Count, "次日这些单次项都不再报");

                // 周重复项
                ScheduleItem w = NewItem("周会", new DateTime(2024, 4, 1), 9, 0, RepeatKind.Weekly);
                w.Id = "s-id-weekly";
                store.Add(w);
                r = sch.Poll(new DateTime(2024, 4, 1, 9, 0, 0));
                Eq(1, r.Count, "周一 09:00 报周重复项");
                Eq("周会", r[0].Item.Title, "报的是周重复项");
                Eq("due", r[0].Kind, "当周到点带 Kind = due");
                r = sch.Poll(new DateTime(2024, 4, 2, 9, 0, 0));
                Eq(0, r.Count, "周二 09:00 周重复项不报");
                r = sch.Poll(new DateTime(2024, 4, 8, 9, 0, 0));
                Eq(1, r.Count, "下周一 09:00 周重复项再次报");
                Eq(new DateTime(2024, 4, 8, 9, 0, 0), r[0].Occurrence, "下周一的 occurrence = 2024-04-08 09:00");

                // DismissToday
                ScheduleItem s7 = NewItem("今天忽略项", new DateTime(2024, 3, 26), 17, 0, RepeatKind.None);
                s7.Id = "s-id-dismiss";
                store.Add(s7);
                List<DueReminder> dd = sch.Poll(new DateTime(2024, 3, 26, 17, 0, 0));
                Eq(1, dd.Count, "17:00 报 1 条");
                sch.DismissToday(dd[0]);
                Eq(new DateTime(2024, 3, 26, 17, 0, 0), store.Find("s-id-dismiss").LastFiredAt.Value,
                    "DismissToday 置 LastFiredAt");
                Eq(false, store.Find("s-id-dismiss").DoneOccurrence.HasValue, "DismissToday 不标记完成");
                r = sch.Poll(new DateTime(2024, 3, 26, 17, 5, 0));
                Eq(0, r.Count, "DismissToday 后不再报");

                // Start/Stop 冒烟
                bool fired = false;
                sch.RemindersDue += delegate(List<DueReminder> list) { fired = fired || list.Count > 0; };
                sch.Start();
                sch.Stop();
                Check(!fired, "Start/Stop 不抛异常（30 秒轮询未在测试窗口内触发）");
            }
            finally
            {
                TryDeleteDir(dir);
            }
        }

        // ---------------- 5. AutoStart ----------------

        private static void TestAutoStart()
        {
            string original = AutoStart.ReadRawValue();
            bool originalEnabled = AutoStart.IsEnabled;

            string probeError;
            bool writable = CanWriteRunKey(out probeError);
            Console.WriteLine("  [环境] HKCU Run 可写 = " + writable +
                              (probeError == null ? "" : "（" + probeError + "）"));
            Console.WriteLine("  [环境] 原始启动项 = " + (original == null ? "<不存在>" : original));
            Console.WriteLine("  [环境] 原始 IsEnabled = " + originalEnabled);

            string expect = AutoStart.BuildCommandValue(AutoStart.ExecutablePath);
            Console.WriteLine("  [环境] 期望写入值 = " + expect);

            try
            {
                string error;
                Check(AutoStart.ExecutablePath.Length > 0, "ExecutablePath 非空");
                Check(AutoStart.ExecutablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),
                    "ExecutablePath 指向 .exe");

                // 值格式（生产代码与真实写入用的是同一个函数，可直接断言）
                Eq("\"" + AutoStart.ExecutablePath + "\" --silent", expect,
                    "BuildCommandValue = \"exe 路径\" --silent");
                Check(expect != null && expect.EndsWith(" --silent", StringComparison.Ordinal),
                    "BuildCommandValue 以 \" --silent\" 结尾");
                Eq("\"C:\\a b\\x.exe\" --silent", AutoStart.BuildCommandValue("C:\\a b\\x.exe"),
                    "BuildCommandValue 给含空格路径加引号");
                Eq(null, AutoStart.BuildCommandValue(null), "BuildCommandValue(null) = null");

                if (!writable)
                {
                    string reason = "本进程对 HKCU 的写操作被系统/沙箱拒绝：" +
                                    (probeError == null ? "未知原因" : probeError) +
                                    "；pwsh 侧同机可写，说明是进程级限制而非代码缺陷";

                    bool enabled = AutoStart.TryEnable(out error);
                    Eq(false, enabled, "无写权限时 TryEnable 返回 false（不抛异常）");
                    Check(!string.IsNullOrEmpty(error), "无写权限时 TryEnable 给出 error 文本");
                    Eq(original, AutoStart.ReadRawValue(), "TryEnable 失败后注册表未被改动（无污染）");
                    Eq(originalEnabled, AutoStart.IsEnabled, "TryEnable 失败后 IsEnabled 保持原值");

                    bool disabled = AutoStart.TryDisable(out error);
                    Check(disabled, "无写权限但本就无启动项时 TryDisable 返回 true（无需写权限，不抛异常）");
                    Check(string.IsNullOrEmpty(error), "无写权限且无需清理时 error 为空");
                    Eq(original, AutoStart.ReadRawValue(), "TryDisable 失败后注册表未被改动（无污染）");
                    Eq(originalEnabled, AutoStart.IsEnabled, "TryDisable 失败后 IsEnabled 保持原值");

                    Skip("TryEnable 后 IsEnabled==true 且注册表值 = 期望值", reason);
                    Skip("TryDisable 后注册表值被删除且恢复原状态", reason);
                    return;
                }

                bool enabledOk = AutoStart.TryEnable(out error);
                Check(enabledOk, "TryEnable 返回 true" + (error == null ? "" : "（error=" + error + "）"));

                string value = AutoStart.ReadRawValue();
                Check(value != null, "TryEnable 后注册表有值");
                Check(value != null && value.EndsWith("--silent", StringComparison.Ordinal),
                    "注册表值以 --silent 结尾");
                Check(value != null && value.IndexOf(AutoStart.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0,
                    "注册表值包含 exe 路径");
                Eq(expect, value, "注册表值 = \"exe 路径\" --silent");
                Eq(true, AutoStart.IsEnabled, "TryEnable 后 IsEnabled = true");

                bool disabledOk = AutoStart.TryDisable(out error);
                Check(disabledOk, "TryDisable 返回 true" + (error == null ? "" : "（error=" + error + "）"));
                Eq(null, AutoStart.ReadRawValue(), "TryDisable 后注册表值已删除");
                Eq(false, AutoStart.IsEnabled, "TryDisable 后 IsEnabled = false");

                bool disabledAgain = AutoStart.TryDisable(out error);
                Check(disabledAgain, "重复 TryDisable 不报错");
            }
            finally
            {
                string restoreError;
                AutoStart.WriteRawValue(original, out restoreError);
                Eq(original, AutoStart.ReadRawValue(),
                    original == null ? "原状态本无启动项，已保持无值" : "已还原原注册表值");
                Eq(originalEnabled, AutoStart.IsEnabled, "已还原原始 IsEnabled 状态");
            }
        }

        // ---------------- 3b. Store.ResolveDataDir ----------------

        private static void TestResolveDataDir()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string defaultDir = Path.Combine(appData, "DesktopCalendar");
            string envName = Store.DataDirEnv;
            string oldEnv = GetEnvSafe(envName);
            string dirA = Path.Combine(appData, "dc-test-a");
            string dirB = Path.Combine(appData, "dc-test-b");
            Console.WriteLine("  [环境] 默认数据目录 = " + defaultDir);
            Console.WriteLine("  [环境] 原 " + envName + " = " + (oldEnv == null ? "<未设置>" : oldEnv));

            try
            {
                SetEnv(envName, null);
                Eq(defaultDir, Store.ResolveDataDir(null), "无参数、无环境变量 = %APPDATA%\\DesktopCalendar");
                Eq(defaultDir, Store.ResolveDataDir(new string[0]), "空参数数组 = 默认目录");
                Eq(defaultDir, Store.ResolveDataDir(new string[] { "--unrelated", "x" }), "无关参数不影响默认目录");

                Eq(Path.GetFullPath(dirA), Store.ResolveDataDir(new string[] { "--datadir", dirA }),
                    "--datadir <path> 生效");
                Eq(Path.GetFullPath(dirB), Store.ResolveDataDir(new string[] { "--datadir=" + dirB }),
                    "--datadir=<path> 生效");
                Eq(Path.GetFullPath(dirA), Store.ResolveDataDir(new string[] { "--datadir", dirA, "--silent" }),
                    "--datadir 后面跟 --silent 也能取到");
                Eq(Path.GetFullPath(dirB), Store.ResolveDataDir(new string[] { "--datadir", dirA, "--datadir=" + dirB }),
                    "多个 --datadir 时后者生效");
                Eq(defaultDir, Store.ResolveDataDir(new string[] { "--datadir", "--silent" }),
                    "--datadir 后面直接跟另一个选项时视为缺值，回落默认目录");

                SetEnv(envName, dirA);
                Eq(Path.GetFullPath(dirA), Store.ResolveDataDir(null), "环境变量 " + envName + " 生效（无参数）");
                Eq(Path.GetFullPath(dirA), Store.ResolveDataDir(new string[0]), "环境变量生效（空参数）");
                Eq(Path.GetFullPath(dirB), Store.ResolveDataDir(new string[] { "--datadir", dirB }),
                    "优先级：--datadir 胜过环境变量");

                SetEnv(envName, "   ");
                Eq(defaultDir, Store.ResolveDataDir(null), "环境变量为空白时回落默认目录");

                string badEnv;
                SetEnv(envName, "C:\\bad|name\\x");
                badEnv = SafeResolve(null);
                Eq(defaultDir, badEnv, "环境变量含非法字符时不抛且回落默认目录");

                SetEnv(envName, null);
                Eq(defaultDir, Store.ResolveDataDir(new string[] { "--datadir" }),
                    "--datadir 缺少值时回落默认目录");
                Eq(defaultDir, Store.ResolveDataDir(new string[] { "--datadir=" }),
                    "--datadir= 空值时回落默认目录");
                Eq(defaultDir, Store.ResolveDataDir(new string[] { "--datadir", "" }),
                    "--datadir 值为空字符串时回落默认目录");
                Eq(defaultDir, SafeResolve(new string[] { "--datadir", "C:\\bad|name" }),
                    "--datadir 非法时不抛且回落默认目录");

                string rel = Store.ResolveDataDir(new string[] { "--datadir", "rel-dc-sub" });
                Check(Path.IsPathRooted(rel), "--datadir 相对路径被规整为绝对路径");
                Eq(Path.GetFullPath("rel-dc-sub"), rel, "相对路径规整结果 = Path.GetFullPath(相对路径)");

                Store s = new Store();
                Eq(defaultDir, s.DataDir, "new Store() 的 DataDir 仍为 %APPDATA%\\DesktopCalendar（契约未改）");
                Eq(Path.Combine(defaultDir, "data.json"), s.DataFilePath, "new Store() 的 DataFilePath = data.json");
            }
            finally
            {
                SetEnv(envName, oldEnv);
                Eq(oldEnv, GetEnvSafe(envName), "已还原 " + envName + " 环境变量");
            }
        }

        private static string SafeResolve(string[] args)
        {
            try
            {
                return Store.ResolveDataDir(args);
            }
            catch (Exception ex)
            {
                return "THROW:" + ex.GetType().Name;
            }
        }

        // ---------------- 5b. AutoStart 启动文件夹兜底 ----------------

        private static void TestStartupCmdFallback()
        {
            string exe = AutoStart.ExecutablePath;
            string originalOverride = AutoStart.StartupDirectoryOverride;
            string originalValue = AutoStart.ReadRawValue();

            string probeError;
            bool writable = CanWriteRunKey(out probeError);
            Console.WriteLine("  [环境] HKCU Run 可写 = " + writable +
                              (probeError == null ? "" : "（" + probeError + "）"));
            Console.WriteLine("  [环境] 当前 exe = " + exe);

            string tempDir = null;
            try
            {
                tempDir = NewTempDir("dc-selftest-autostart");
                Console.WriteLine("  [环境] 临时启动文件夹 = " + tempDir);
                AutoStart.StartupDirectoryOverride = tempDir;

                Eq(Path.Combine(tempDir, AutoStart.StartupCmdFileName), AutoStart.StartupCmdPath,
                    "StartupCmdPath = <Startup>\\DesktopCalendar.cmd");
                Eq("@echo off\r\nstart \"\" \"" + exe + "\" --silent\r\n",
                    AutoStart.BuildStartupCmdContent(exe),
                    "BuildStartupCmdContent = @echo off + start \"\" \"exe\" --silent（CRLF）");
                Eq(null, AutoStart.BuildStartupCmdContent(null), "BuildStartupCmdContent(null) = null");

                string ignore;
                AutoStart.TryDisableRegistry(out ignore);
                AutoStart.TryDisableStartupCmd(out ignore);
                Eq(false, File.Exists(AutoStart.StartupCmdPath), "起点：临时目录内无 cmd");
                Eq(false, AutoStart.IsStartupCmdEnabled, "起点：IsStartupCmdEnabled = false");

                string e1;
                Check(AutoStart.TryEnableStartupCmd(out e1),
                    "TryEnableStartupCmd 返回 true" + (e1 == null ? "" : "（error=" + e1 + "）"));
                Check(File.Exists(AutoStart.StartupCmdPath), "启动文件夹 cmd 已写出");
                string content = AutoStart.ReadStartupCmdContent();
                Check(content != null && content.StartsWith("@echo off\r\n", StringComparison.Ordinal),
                    "cmd 第一行 = @echo off");
                Check(content != null && content.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0,
                    "cmd 内容含当前 exe 绝对路径");
                Check(content != null && content.IndexOf("--silent", StringComparison.Ordinal) >= 0,
                    "cmd 内容含 --silent");
                Check(content != null && content.IndexOf("start \"\" \"", StringComparison.Ordinal) >= 0,
                    "cmd 用 start \"\" 拉起进程");
                Check(content != null && content.EndsWith("\r\n", StringComparison.Ordinal), "cmd 以 CRLF 结尾");
                Eq(true, AutoStart.IsStartupCmdEnabled, "IsStartupCmdEnabled = true");
                Eq(true, AutoStart.IsEnabled, "仅 cmd 存在时 IsEnabled = true");
                Eq("startup", AutoStart.ActiveMechanism, "仅 cmd 存在时 ActiveMechanism = startup");

                string e2;
                Check(AutoStart.TryDisableStartupCmd(out e2),
                    "TryDisableStartupCmd 返回 true" + (e2 == null ? "" : "（error=" + e2 + "）"));
                Eq(false, File.Exists(AutoStart.StartupCmdPath), "cmd 文件已删除");
                Eq(false, AutoStart.IsEnabled, "只删 cmd 后 IsEnabled = false");
                Eq("none", AutoStart.ActiveMechanism, "两种机制都没有时 ActiveMechanism = none");
                Check(AutoStart.TryDisableStartupCmd(out e2), "重复 TryDisableStartupCmd 不报错");

                string error;
                Check(AutoStart.TryEnable(out error),
                    "TryEnable 返回 true" + (error == null ? "" : "（提示=" + error + "）"));
                Eq(true, AutoStart.IsEnabled, "TryEnable 后 IsEnabled = true");

                if (writable)
                {
                    Eq("registry", AutoStart.ActiveMechanism, "注册表可写时 ActiveMechanism = registry");
                    string value = AutoStart.ReadRawValue();
                    Check(value != null, "TryEnable 后注册表有值");
                    Check(value != null && value.EndsWith("--silent", StringComparison.Ordinal),
                        "注册表值以 --silent 结尾");
                    Check(value != null && value.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0,
                        "注册表值包含 exe 路径");
                    Eq(AutoStart.BuildCommandValue(exe), value, "注册表值 = \"exe 路径\" --silent");
                    Eq(false, File.Exists(AutoStart.StartupCmdPath), "注册表成功时不写 cmd 兜底");
                }
                else
                {
                    Eq("startup", AutoStart.ActiveMechanism, "注册表被拒时兜底 ActiveMechanism = startup");
                    Check(File.Exists(AutoStart.StartupCmdPath), "注册表被拒后 cmd 兜底文件真的写出来了");
                    string c2 = AutoStart.ReadStartupCmdContent();
                    Check(c2 != null && c2.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0,
                        "兜底 cmd 含 exe 路径");
                    Check(c2 != null && c2.IndexOf("--silent", StringComparison.Ordinal) >= 0,
                        "兜底 cmd 含 --silent");
                    Eq(originalValue, AutoStart.ReadRawValue(), "兜底路径不污染注册表");
                    Check(!string.IsNullOrEmpty(error) && error.IndexOf("兜底", StringComparison.Ordinal) >= 0,
                        "兜底时 error 说明已改用启动文件夹");
                }

                string off;
                Check(AutoStart.TryDisable(out off),
                    "TryDisable 返回 true" + (off == null ? "" : "（error=" + off + "）"));
                Eq(false, AutoStart.IsEnabled, "TryDisable 后 IsEnabled = false");
                Eq("none", AutoStart.ActiveMechanism, "TryDisable 后 ActiveMechanism = none");
                Eq(null, AutoStart.ReadRawValue(), "TryDisable 后注册表值已删除");
                Eq(false, File.Exists(AutoStart.StartupCmdPath), "TryDisable 后 cmd 文件已删除");
                Check(AutoStart.TryDisable(out off), "重复 TryDisable 不报错");

                string fileAsDir = Path.Combine(tempDir, "not-a-dir");
                File.WriteAllText(fileAsDir, "x");
                AutoStart.StartupDirectoryOverride = fileAsDir;
                string e3;
                Eq(false, AutoStart.TryEnableStartupCmd(out e3),
                    "目标目录不可用时 TryEnableStartupCmd 返回 false（不抛）");
                Check(!string.IsNullOrEmpty(e3), "失败时给出 error 文本");
                Eq(false, AutoStart.IsStartupCmdEnabled, "目标不可用时 IsStartupCmdEnabled = false");
                AutoStart.StartupDirectoryOverride = tempDir;
            }
            finally
            {
                AutoStart.StartupDirectoryOverride = originalOverride;
                string restoreError;
                AutoStart.WriteRawValue(originalValue, out restoreError);
                Eq(originalOverride, AutoStart.StartupDirectoryOverride, "已还原 StartupDirectoryOverride");
                Eq(originalValue, AutoStart.ReadRawValue(),
                    originalValue == null ? "原状态本无启动项，已保持无值" : "已还原原注册表值");
                TryDeleteDir(tempDir);
            }
        }

        /// <summary>探测本进程能否写 HKCU Run：use 一个临时值名，随后删除。</summary>
        private static bool CanWriteRunKey(out string error)
        {
            error = null;
            const string probeName = "DesktopCalendarSelfTestProbe";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(AutoStart.RunKey))
                {
                    if (key == null)
                    {
                        error = "CreateSubKey 返回 null";
                        return false;
                    }
                    try
                    {
                        key.SetValue(probeName, "probe", RegistryValueKind.String);
                        key.DeleteValue(probeName, false);
                    }
                    catch (Exception ex)
                    {
                        error = ex.GetType().Name + ": " + ex.Message;
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        // ---------------- 工具 ----------------

        private static void SetEnv(string name, string value)
        {
            try
            {
                Environment.SetEnvironmentVariable(name, value);
            }
            catch (Exception)
            {
            }
        }

        private static string GetEnvSafe(string name)
        {
            try
            {
                return Environment.GetEnvironmentVariable(name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 依次尝试 %TEMP%、exe 目录、当前目录下的临时子目录：某些受限进程（如被沙箱约束的
        /// 会话）不允许写 %TEMP%，但允许写自己的目录。全部失败才抛异常。
        /// </summary>
        private static string NewTempDir(string prefix)
        {
            List<string> roots = new List<string>();
            roots.Add(Path.GetTempPath());
            roots.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-tmp"));
            roots.Add(Path.Combine(Directory.GetCurrentDirectory(), "_scratch"));

            List<string> errors = new List<string>();
            for (int i = 0; i < roots.Count; i++)
            {
                string dir = Path.Combine(roots[i], prefix + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(dir);
                    return dir;
                }
                catch (Exception ex)
                {
                    errors.Add(roots[i] + " -> " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            throw new IOException("无法创建临时目录：" + string.Join(" | ", errors.ToArray()));
        }

        private static void TryDeleteDir(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>自检结束后清掉空的 selftest-tmp 容器目录。</summary>
        private static void TryDeleteEmptyDir(string dir)
        {
            try
            {
                if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                {
                    Directory.Delete(dir, false);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
