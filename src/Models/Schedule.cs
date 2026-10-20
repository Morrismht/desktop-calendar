using System;

namespace DesktopCalendar.Models
{
    /// <summary>重复方式。数值与持久化格式一致，禁止改动。</summary>
    public enum RepeatKind
    {
        None = 0,
        Daily = 1,
        Weekly = 2,
        Monthly = 3
    }

    /// <summary>一条日程（纯数据，不含任何 UI 依赖）。</summary>
    public class ScheduleItem
    {
        public string Id;                 // Guid.NewGuid().ToString("N")
        public string Title;              // 必填
        public string Notes;
        public DateTime Date;             // 起始日期，只用 .Date
        public int Hour;
        public int Minute;
        public RepeatKind Repeat;
        /// <summary>每周重复时生效的星期掩码。bit0=周一 … bit6=周日；0 = 未指定（沿用 Date 的星期）。</summary>
        public int WeekDays;
        public DateTime? RepeatUntil;     // null = 永久重复
        public bool Enabled;
        public string Color;              // 分类色，来自 Theme.Palette
        public DateTime CreatedAt;
        public DateTime? LastFiredAt;     // 最近一次已提醒的 occurrence 起点
        public DateTime? SnoozeUntil;     // 稍后提醒目标时刻
        public DateTime? DoneOccurrence;  // 已完成的 occurrence 起点（单次即 Date）

        public ScheduleItem()
        {
            Id = Guid.NewGuid().ToString("N");
            Title = "";
            Notes = "";
            Date = DateTime.Today;
            Hour = 9;
            Minute = 0;
            Repeat = RepeatKind.None;
            WeekDays = 0;
            RepeatUntil = null;
            Enabled = true;
            Color = "#4F7CFF";
            CreatedAt = DateTime.Now;
        }

        /// <summary>该 occurrence 是否已完成。按分钟比较，忽略秒/毫秒差异。</summary>
        public bool IsDoneOn(DateTime occurrenceStart)
        {
            if (!DoneOccurrence.HasValue)
            {
                return false;
            }
            return SameMinute(DoneOccurrence.Value, occurrenceStart);
        }

        /// <summary>当天该条的触发时刻（Date 的 Hour:Minute）。</summary>
        public DateTime StartOf(DateTime day)
        {
            return day.Date.AddHours(Hour).AddMinutes(Minute);
        }

        /// <summary>补齐缺失字段（解析旧数据 / 手工构造时使用）。</summary>
        public void Normalize()
        {
            if (string.IsNullOrEmpty(Id))
            {
                Id = Guid.NewGuid().ToString("N");
            }
            if (Title == null)
            {
                Title = "";
            }
            if (Notes == null)
            {
                Notes = "";
            }
            if (string.IsNullOrEmpty(Color))
            {
                Color = "#4F7CFF";
            }
            if (Hour < 0 || Hour > 23)
            {
                Hour = 9;
            }
            if (Minute < 0 || Minute > 59)
            {
                Minute = 0;
            }
            if (WeekDays < 0)
            {
                WeekDays = 0;
            }
            if (WeekDays > 127)
            {
                WeekDays = 127;
            }
            Date = Date.Date;
            if (CreatedAt == default(DateTime))
            {
                CreatedAt = DateTime.Now;
            }
        }

        public ScheduleItem Clone()
        {
            ScheduleItem c = new ScheduleItem();
            c.Id = Id;
            c.Title = Title;
            c.Notes = Notes;
            c.Date = Date;
            c.Hour = Hour;
            c.Minute = Minute;
            c.Repeat = Repeat;
            c.WeekDays = WeekDays;
            c.RepeatUntil = RepeatUntil;
            c.Enabled = Enabled;
            c.Color = Color;
            c.CreatedAt = CreatedAt;
            c.LastFiredAt = LastFiredAt;
            c.SnoozeUntil = SnoozeUntil;
            c.DoneOccurrence = DoneOccurrence;
            return c;
        }

        public override string ToString()
        {
            return string.Format("{0} {1:00}:{2:00} {3}", Date.ToString("yyyy-MM-dd"), Hour, Minute, Title);
        }

        internal static bool SameMinute(DateTime a, DateTime b)
        {
            return a.Year == b.Year && a.Month == b.Month && a.Day == b.Day
                && a.Hour == b.Hour && a.Minute == b.Minute;
        }
    }
}
