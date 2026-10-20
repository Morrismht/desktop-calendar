using System;
using System.Collections.Generic;
using System.Threading;
using DesktopCalendar.Data;
using DesktopCalendar.Models;

namespace DesktopCalendar.Services
{
    /// <summary>一条待弹出的提醒。</summary>
    public class DueReminder
    {
        public ScheduleItem Item;
        public DateTime Occurrence;
        public string Kind;   // "due" | "snooze" | "overdue"（逾期：occurrence 已过 30 分钟仍未完成）

        public override string ToString()
        {
            return string.Format("{0} {1} {2}", Kind, Occurrence.ToString("yyyy-MM-dd HH:mm"),
                Item == null ? "" : Item.Title);
        }
    }

    /// <summary>
    /// 30 秒轮询到期提醒。去重完全靠 Item.LastFiredAt / SnoozeUntil / DoneOccurrence，
    /// 不依赖内存集合，因此重启程序后不会重复提醒已处理过的当天项。
    /// </summary>
    public class Scheduler
    {
        /// <summary>逾期阈值：occurrence 过后超过这么多分钟仍未完成 → "overdue"。</summary>
        public const int OverdueMinutes = 30;

        private readonly Store _store;
        private Timer _timer;
        private DateTime _lastPollNow = DateTime.MinValue;

        public event Action<List<DueReminder>> RemindersDue;

        public Scheduler(Store store)
        {
            if (store == null)
            {
                throw new ArgumentNullException("store");
            }
            _store = store;
        }

        public Store Store
        {
            get { return _store; }
        }

        // ---------------- 轮询 ----------------

        public void Start()
        {
            if (_timer != null)
            {
                return;
            }
            _timer = new Timer(OnTick, null, 30000, 30000);
        }

        public void Stop()
        {
            if (_timer == null)
            {
                return;
            }
            try
            {
                _timer.Dispose();
            }
            catch (Exception)
            {
            }
            _timer = null;
        }

        private void OnTick(object state)
        {
            try
            {
                List<DueReminder> list = Poll(DateTime.Now);
                if (list.Count == 0)
                {
                    return;
                }
                Action<List<DueReminder>> handler = RemindersDue;
                if (handler != null)
                {
                    handler(list);
                }
            }
            catch (Exception)
            {
                // 轮询线程不允许抛异常
            }
        }

        /// <summary>
        /// 返回本轮应弹的提醒（只报"新"到期项）。可测试入口：now 由调用方给出。
        /// 顺序：今天到点 → due；逾期 30 分钟以上 → overdue；稍后提醒到点 → snooze。
        /// </summary>
        public List<DueReminder> Poll(DateTime now)
        {
            _lastPollNow = now;
            List<DueReminder> result = new List<DueReminder>();
            bool changed = false;
            List<ScheduleItem> snapshot = new List<ScheduleItem>(_store.Items);
            for (int i = 0; i < snapshot.Count; i++)
            {
                ScheduleItem it = snapshot[i];
                if (it == null || !it.Enabled)
                {
                    continue;
                }
                if (!RepeatRule.OccursOn(it, now))
                {
                    continue;
                }
                DateTime occ = RepeatRule.OccurrenceStart(it, now);
                if (it.IsDoneOn(occ))
                {
                    continue;
                }

                // 稍后提醒：到点即报 "snooze"，报告后清空，不会再报
                if (it.SnoozeUntil.HasValue)
                {
                    if (it.SnoozeUntil.Value > now)
                    {
                        continue;
                    }
                    result.Add(Make(it, occ, "snooze"));
                    it.SnoozeUntil = null;
                    it.LastFiredAt = occ;
                    changed = true;
                    continue;
                }

                // 已提醒过这个 occurrence
                if (it.LastFiredAt.HasValue && ScheduleItem.SameMinute(it.LastFiredAt.Value, occ))
                {
                    continue;
                }

                // 还没到点
                if (now < occ)
                {
                    continue;
                }

                string kind = (now - occ).TotalMinutes > OverdueMinutes ? "overdue" : "due";
                result.Add(Make(it, occ, kind));
                it.LastFiredAt = occ;
                changed = true;
            }
            if (changed)
            {
                _store.Save();
            }
            return result;
        }

        /// <summary>今天已过点且未完成（含已提醒过的），供状态栏/托盘提示。按时间升序。</summary>
        public List<DueReminder> OverdueToday(DateTime now)
        {
            List<DueReminder> result = new List<DueReminder>();
            List<ScheduleItem> day = _store.ForDay(now);
            for (int i = 0; i < day.Count; i++)
            {
                ScheduleItem it = day[i];
                DateTime occ = RepeatRule.OccurrenceStart(it, now);
                if (it.IsDoneOn(occ))
                {
                    continue;
                }
                if (now < occ)
                {
                    continue;
                }
                if ((now - occ).TotalMinutes > OverdueMinutes)
                {
                    result.Add(Make(it, occ, "overdue"));
                }
            }
            return result;
        }

        // ---------------- 用户操作 ----------------

        /// <summary>置 DoneOccurrence 并 Save。</summary>
        public void Complete(DueReminder d)
        {
            if (d == null || d.Item == null)
            {
                return;
            }
            d.Item.DoneOccurrence = d.Occurrence;
            d.Item.SnoozeUntil = null;
            _store.Update(d.Item);
        }

        /// <summary>
        /// 置 SnoozeUntil = 基准时刻 + minutes。基准取 max(occurrence, 最近一次 Poll 的 now)，
        /// 这样既保证测试用固定时间可复现，又保证"逾期很久的项点稍后"不会立刻再弹。
        /// </summary>
        public void Snooze(DueReminder d, int minutes)
        {
            if (d == null || d.Item == null)
            {
                return;
            }
            if (minutes <= 0)
            {
                minutes = 10;
            }
            DateTime baseTime = d.Occurrence;
            if (_lastPollNow > baseTime)
            {
                baseTime = _lastPollNow;
            }
            d.Item.SnoozeUntil = baseTime.AddMinutes(minutes);
            _store.Update(d.Item);
        }

        /// <summary>今天不再弹：置 LastFiredAt（不标记完成）。</summary>
        public void DismissToday(DueReminder d)
        {
            if (d == null || d.Item == null)
            {
                return;
            }
            d.Item.LastFiredAt = d.Occurrence;
            d.Item.SnoozeUntil = null;
            _store.Update(d.Item);
        }

        private static DueReminder Make(ScheduleItem it, DateTime occ, string kind)
        {
            DueReminder d = new DueReminder();
            d.Item = it;
            d.Occurrence = occ;
            d.Kind = kind;
            return d;
        }
    }
}
