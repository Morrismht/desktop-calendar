using System;
using System.Collections.Generic;

namespace DesktopCalendar.Models
{
    /// <summary>
    /// 重复规则，纯函数，无状态、无 IO。
    /// Daily = Date 起每天；Weekly = 与 Date 同星期；Monthly = 每月同"日"，该月无此日时取当月最后一天。
    /// RepeatUntil 含当天，超出则不再发生。
    /// </summary>
    public static class RepeatRule
    {
        private static readonly string[] WeekNames = new string[] { "日", "一", "二", "三", "四", "五", "六" };

        // NextOccurrence 的扫描上限：约 11 年，足够覆盖任何真实用途，同时保证不会死循环。
        private const int MaxScanDays = 4000;

        /// <summary>day 只看 .Date。</summary>
        public static bool OccursOn(ScheduleItem it, DateTime day)
        {
            if (it == null)
            {
                return false;
            }
            DateTime d = day.Date;
            DateTime start = it.Date.Date;
            if (d < start)
            {
                return false;
            }
            if (it.RepeatUntil.HasValue && d > it.RepeatUntil.Value.Date)
            {
                return false;
            }
            switch (it.Repeat)
            {
                case RepeatKind.None:
                    return d == start;
                case RepeatKind.Daily:
                    return true;
                case RepeatKind.Weekly:
                    return d.DayOfWeek == start.DayOfWeek;
                case RepeatKind.Monthly:
                    int last = DateTime.DaysInMonth(d.Year, d.Month);
                    int target = start.Day;
                    if (target > last)
                    {
                        target = last;
                    }
                    return d.Day == target;
                default:
                    return false;
            }
        }

        /// <summary>day.Date + Hour:Minute。</summary>
        public static DateTime OccurrenceStart(ScheduleItem it, DateTime day)
        {
            if (it == null)
            {
                return day.Date;
            }
            return day.Date.AddHours(it.Hour).AddMinutes(it.Minute);
        }

        /// <summary>
        /// [fromInclusive, toInclusive] 内所有 occurrence 起点，升序。
        /// 端点按"日期"比较（与 OccursOn 的 .Date 语义一致），因此传当天 0 点也能取到当天 09:00 的项。
        /// </summary>
        public static IEnumerable<DateTime> OccurrencesBetween(ScheduleItem it, DateTime fromInclusive, DateTime toInclusive)
        {
            List<DateTime> list = new List<DateTime>();
            if (it == null)
            {
                return list;
            }
            DateTime from = fromInclusive.Date;
            DateTime to = toInclusive.Date;
            if (to < from)
            {
                return list;
            }
            DateTime start = it.Date.Date;
            if (start > from)
            {
                from = start;
            }
            if (it.RepeatUntil.HasValue && to > it.RepeatUntil.Value.Date)
            {
                to = it.RepeatUntil.Value.Date;
            }
            for (DateTime d = from; d <= to; d = d.AddDays(1))
            {
                if (OccursOn(it, d))
                {
                    list.Add(OccurrenceStart(it, d));
                }
            }
            return list;
        }

        /// <summary>严格晚于 afterExclusive 的下一个 occurrence；不存在（无重复已过 / 超出 RepeatUntil）返回 null。</summary>
        public static DateTime? NextOccurrence(ScheduleItem it, DateTime afterExclusive)
        {
            if (it == null)
            {
                return null;
            }
            DateTime start = it.Date.Date;
            DateTime? until = it.RepeatUntil.HasValue ? (DateTime?)it.RepeatUntil.Value.Date : null;
            if (until.HasValue && start > until.Value)
            {
                return null;
            }

            if (it.Repeat == RepeatKind.None)
            {
                DateTime only = OccurrenceStart(it, start);
                if (only > afterExclusive)
                {
                    return only;
                }
                return null;
            }

            DateTime cursor = afterExclusive.Date;
            if (cursor < start)
            {
                cursor = start;
            }
            for (int i = 0; i < MaxScanDays; i++)
            {
                DateTime day = cursor.AddDays(i);
                if (until.HasValue && day > until.Value)
                {
                    return null;
                }
                if (OccursOn(it, day))
                {
                    DateTime occ = OccurrenceStart(it, day);
                    if (occ > afterExclusive)
                    {
                        return occ;
                    }
                }
            }
            return null;
        }

        /// <summary>"单次" / "每天" / "每周一" / "每月 15 日"；有终止日时追加"（至 yyyy-MM-dd）"。</summary>
        public static string Describe(ScheduleItem it)
        {
            if (it == null)
            {
                return "";
            }
            string text;
            switch (it.Repeat)
            {
                case RepeatKind.Daily:
                    text = "每天";
                    break;
                case RepeatKind.Weekly:
                    text = "每周" + WeekNames[(int)it.Date.DayOfWeek];
                    break;
                case RepeatKind.Monthly:
                    text = string.Format("每月 {0} 日", it.Date.Day);
                    break;
                default:
                    text = "单次";
                    break;
            }
            if (it.RepeatUntil.HasValue)
            {
                text = text + string.Format("（至 {0}）", it.RepeatUntil.Value.ToString("yyyy-MM-dd"));
            }
            return text;
        }

        /// <summary>该条目在 day 这一天的 occurrence 起点；day 不发生则返回 null。</summary>
        public static DateTime? OccurrenceOn(ScheduleItem it, DateTime day)
        {
            if (!OccursOn(it, day))
            {
                return null;
            }
            return OccurrenceStart(it, day);
        }
    }
}
