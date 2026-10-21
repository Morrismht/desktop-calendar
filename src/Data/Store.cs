using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DesktopCalendar.Models;

namespace DesktopCalendar.Data
{
    /// <summary>
    /// JSON 持久化。默认文件 %APPDATA%\DesktopCalendar\data.json，原子写（临时文件 + 替换），
    /// 任何 IO/解析异常都不外抛，记入 LastError。
    /// </summary>
    public class Store
    {
        private readonly List<ScheduleItem> _items = new List<ScheduleItem>();
        private readonly string _dataDir;
        private readonly string _dataFilePath;
        private string _lastError;

        public Store(string dataDir = null)
        {
            if (dataDir == null || dataDir.Trim().Length == 0)
            {
                dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DesktopCalendar");
            }
            string requested = dataDir;
            _dataDir = EnsureWritableDir(requested);
            DataDirFallback = !string.Equals(_dataDir, requested, StringComparison.OrdinalIgnoreCase);
            _dataFilePath = Path.Combine(_dataDir, "data.json");
            Load();
        }

        /// <summary>true 表示首选目录不可写、已退到 exe 同级 data 目录（UI 可据此提示一次）。</summary>
        public bool DataDirFallback { get; private set; }

        public string DataDir
        {
            get { return _dataDir; }
        }

        public string DataFilePath
        {
            get { return _dataFilePath; }
        }

        public List<ScheduleItem> Items
        {
            get { return _items; }
        }

        public string LastError
        {
            get { return _lastError; }
        }

        // ---------------- 数据目录解析 ----------------

        /// <summary>data.json 根节点里存提醒音开关的键名。</summary>
        public const string ReminderSoundKey = "reminderSound";

        private bool _reminderSound = true;

        /// <summary>提醒卡片弹出时是否播放提示音（默认开，跟日程一起存在 data.json 根节点）。</summary>
        public bool ReminderSound
        {
            get { return _reminderSound; }
            set { _reminderSound = value; }
        }

        public const string DataDirArg = "--datadir";
        public const string DataDirEnv = "DESKTOP_CALENDAR_DATA";

        /// <summary>
        /// 数据目录优先级：--datadir &lt;path&gt;（或 --datadir=&lt;path&gt;）&gt; 环境变量
        /// DESKTOP_CALENDAR_DATA &gt; %APPDATA%\DesktopCalendar。
        /// 结果经 Path.GetFullPath 规整；非法/为空的参数忽略，绝不抛异常。
        /// </summary>
        public static string ResolveDataDir(string[] args)
        {
            string fromArgs = DataDirFromArgs(args);
            if (fromArgs != null)
            {
                return fromArgs;
            }

            string fromEnv = null;
            try
            {
                fromEnv = Environment.GetEnvironmentVariable(DataDirEnv);
            }
            catch (Exception)
            {
            }
            string normalizedEnv = NormalizeDir(fromEnv);
            if (normalizedEnv != null)
            {
                return normalizedEnv;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DesktopCalendar");
        }

        /// <summary>
        /// 确认目录可写：能建目录并落一个探针文件就用它；否则退到 exe 同级的 data 目录。
        /// 用户机器上若 %APPDATA% 被安全软件/策略拒绝，程序仍能存下日程，而不是静默丢数据。
        /// </summary>
        private static string EnsureWritableDir(string preferred)
        {
            if (CanWriteDir(preferred))
            {
                return preferred;
            }
            string fallback = null;
            try
            {
                string exeDir = Path.GetDirectoryName(typeof(Store).Assembly.Location);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    fallback = Path.Combine(exeDir, "data");
                }
            }
            catch (Exception)
            {
                fallback = null;
            }
            if (fallback != null && !fallback.Equals(preferred, StringComparison.OrdinalIgnoreCase) && CanWriteDir(fallback))
            {
                return fallback;
            }
            return preferred;
        }

        private static bool CanWriteDir(string dir)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string probe = Path.Combine(dir, ".write-probe");
                File.WriteAllText(probe, "1", Encoding.UTF8);
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>扫描 --datadir / --datadir=；多个时后者生效；值形如 --xxx 视为缺值。</summary>
        private static string DataDirFromArgs(string[] args)
        {
            if (args == null)
            {
                return null;
            }
            string found = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == null)
                {
                    continue;
                }
                string trimmed = a.Trim();
                if (trimmed.Equals(DataDirArg, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length && !LooksLikeOption(args[i + 1]))
                    {
                        string ok = NormalizeDir(args[i + 1]);
                        if (ok != null)
                        {
                            found = ok;
                        }
                        i++;
                    }
                    continue;
                }
                if (trimmed.StartsWith(DataDirArg + "=", StringComparison.OrdinalIgnoreCase))
                {
                    string ok2 = NormalizeDir(trimmed.Substring(DataDirArg.Length + 1));
                    if (ok2 != null)
                    {
                        found = ok2;
                    }
                }
            }
            return found;
        }

        private static bool LooksLikeOption(string raw)
        {
            if (raw == null)
            {
                return false;
            }
            string s = raw.Trim();
            return s.Length > 1 && s[0] == '-' && s[1] == '-';
        }

        /// <summary>规整为绝对路径；空/非法返回 null（不抛）。</summary>
        private static string NormalizeDir(string raw)
        {
            if (raw == null)
            {
                return null;
            }
            string s = raw.Trim();
            if (s.Length == 0)
            {
                return null;
            }
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            {
                s = s.Substring(1, s.Length - 2).Trim();
                if (s.Length == 0)
                {
                    return null;
                }
            }
            try
            {
                return Path.GetFullPath(s);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------------- 读写 ----------------

        public void Load()
        {
            _lastError = null;
            _items.Clear();
            try
            {
                if (!File.Exists(_dataFilePath))
                {
                    return;
                }
                string text = File.ReadAllText(_dataFilePath, Encoding.UTF8);
                _reminderSound = ReadRootFlag(text, ReminderSoundKey, true);
                List<Dictionary<string, object>> rows = ParseRows(text);
                for (int i = 0; i < rows.Count; i++)
                {
                    try
                    {
                        _items.Add(FromDict(rows[i]));
                    }
                    catch (Exception ex)
                    {
                        _lastError = string.Format("第 {0} 条日程解析失败：{1}", i, ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _lastError = "读取失败：" + ex.Message;
            }
        }

        public void Save()
        {
            _lastError = null;
            try
            {
                WriteFile(BuildText());
            }
            catch (Exception ex)
            {
                _lastError = "保存失败：" + ex.Message;
            }
        }

        /// <summary>严格读取：出错直接抛出（供自检/诊断使用，UI 用 Load）。</summary>
        public void LoadStrict()
        {
            _lastError = null;
            _items.Clear();
            if (!File.Exists(_dataFilePath))
            {
                return;
            }
            string text = File.ReadAllText(_dataFilePath, Encoding.UTF8);
            _reminderSound = ReadRootFlag(text, ReminderSoundKey, true);
            List<Dictionary<string, object>> rows = ParseRows(text);
            for (int i = 0; i < rows.Count; i++)
            {
                _items.Add(FromDict(rows[i]));
            }
        }

        /// <summary>严格写入：出错直接抛出（供自检/诊断使用，UI 用 Save）。</summary>
        public void SaveStrict()
        {
            WriteFile(BuildText());
        }

        private string BuildText()        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["version"] = 1;
            root["savedAt"] = DateTime.Now;
            root[ReminderSoundKey] = _reminderSound;
            List<object> rows = new List<object>();
            for (int i = 0; i < _items.Count; i++)
            {
                rows.Add(ToDict(_items[i]));
            }
            root["items"] = rows;
            return Json.Write(root);
        }

        private void WriteFile(string text)
        {
            if (!Directory.Exists(_dataDir))
            {
                Directory.CreateDirectory(_dataDir);
            }
            string tmp = _dataFilePath + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(_dataFilePath))
            {
                string bak = _dataFilePath + ".bak";
                try
                {
                    File.Replace(tmp, _dataFilePath, bak, true);
                }
                catch (Exception)
                {
                    // 某些文件系统（网络盘/被占用）不支持 Replace，退回覆盖拷贝
                    File.Copy(tmp, _dataFilePath, true);
                    TryDelete(tmp);
                }
            }
            else
            {
                File.Move(tmp, _dataFilePath);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>读 data.json 根节点上的一个布尔开关；文件缺失/格式不对都退回默认值，绝不抛。</summary>
        private static bool ReadRootFlag(string text, string key, bool def)
        {
            if (text == null) return def;

            string t = text.Trim().TrimStart('\uFEFF');
            if (t.Length == 0 || t[0] != '{') return def;

            try
            {
                Dictionary<string, object> root = Json.ReadObject(t);
                object raw;
                if (!root.TryGetValue(key, out raw) || raw == null) return def;
                return Json.Bool(root, key, def);
            }
            catch (Exception)
            {
                return def;
            }
        }

        private static List<Dictionary<string, object>> ParseRows(string text)
        {
            if (text == null)
            {
                return new List<Dictionary<string, object>>();
            }
            string t = text.Trim().TrimStart('\uFEFF');
            if (t.Length == 0)
            {
                return new List<Dictionary<string, object>>();
            }
            if (t[0] == '[')
            {
                return Json.ReadObjectArray(t);
            }
            Dictionary<string, object> root = Json.ReadObject(t);
            object arr;
            if (root.TryGetValue("items", out arr))
            {
                return Json.AsObjectArray(arr);
            }
            return new List<Dictionary<string, object>>();
        }

        internal static Dictionary<string, object> ToDict(ScheduleItem it)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["Id"] = it.Id;
            o["Title"] = it.Title;
            o["Notes"] = it.Notes;
            o["Date"] = it.Date.Date;
            o["Hour"] = it.Hour;
            o["Minute"] = it.Minute;
            o["Repeat"] = (int)it.Repeat;
            o["WeekDays"] = it.WeekDays;
            o["RepeatUntil"] = it.RepeatUntil.HasValue ? (object)it.RepeatUntil.Value : null;
            o["Enabled"] = it.Enabled;
            o["Color"] = it.Color;
            o["CreatedAt"] = it.CreatedAt;
            o["LastFiredAt"] = it.LastFiredAt.HasValue ? (object)it.LastFiredAt.Value : null;
            o["SnoozeUntil"] = it.SnoozeUntil.HasValue ? (object)it.SnoozeUntil.Value : null;
            o["DoneOccurrence"] = it.DoneOccurrence.HasValue ? (object)it.DoneOccurrence.Value : null;
            return o;
        }

        internal static ScheduleItem FromDict(Dictionary<string, object> o)
        {
            ScheduleItem it = new ScheduleItem();
            it.Id = Json.Str(o, "Id");
            it.Title = Json.Str(o, "Title", "");
            it.Notes = Json.Str(o, "Notes", "");
            DateTime? date = Json.Date(o, "Date");
            it.Date = (date.HasValue ? date.Value : DateTime.Today).Date;
            it.Hour = Json.Int(o, "Hour", 9);
            it.Minute = Json.Int(o, "Minute", 0);
            int repeat = Json.Int(o, "Repeat", 0);
            if (repeat < 0 || repeat > 3)
            {
                repeat = 0;
            }
            it.Repeat = (RepeatKind)repeat;
            it.WeekDays = Json.Int(o, "WeekDays", 0);
            it.RepeatUntil = Json.Date(o, "RepeatUntil");
            it.Enabled = Json.Bool(o, "Enabled", true);
            it.Color = Json.Str(o, "Color", "#4F7CFF");
            DateTime? created = Json.Date(o, "CreatedAt");
            it.CreatedAt = created.HasValue ? created.Value : DateTime.Now;
            it.LastFiredAt = Json.Date(o, "LastFiredAt");
            it.SnoozeUntil = Json.Date(o, "SnoozeUntil");
            it.DoneOccurrence = Json.Date(o, "DoneOccurrence");
            it.Normalize();
            return it;
        }

        // ---------------- 增删改查 ----------------

        public void Add(ScheduleItem it)
        {
            if (it == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(it.Id))
            {
                it.Id = Guid.NewGuid().ToString("N");
            }
            it.Normalize();
            _items.Add(it);
            Save();
        }

        public void Update(ScheduleItem it)
        {
            if (it == null)
            {
                return;
            }
            int idx = IndexOf(it.Id);
            if (idx < 0)
            {
                Add(it);
                return;
            }
            _items[idx] = it;
            Save();
        }

        public void Remove(string id)
        {
            int idx = IndexOf(id);
            if (idx < 0)
            {
                return;
            }
            _items.RemoveAt(idx);
            Save();
        }

        public ScheduleItem Find(string id)
        {
            int idx = IndexOf(id);
            return idx < 0 ? null : _items[idx];
        }

        private int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return -1;
            }
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] != null && _items[i].Id == id)
                {
                    return i;
                }
            }
            return -1;
        }

        // ---------------- 按天查询 ----------------

        /// <summary>当天所有 occurrence（未停用），按时间升序。</summary>
        public List<ScheduleItem> ForDay(DateTime day)
        {
            List<ScheduleItem> list = new List<ScheduleItem>();
            DateTime d = day.Date;
            for (int i = 0; i < _items.Count; i++)
            {
                ScheduleItem it = _items[i];
                if (it == null || !it.Enabled)
                {
                    continue;
                }
                if (RepeatRule.OccursOn(it, d))
                {
                    list.Add(it);
                }
            }
            list.Sort(CompareForDay);
            return list;
        }

        private static int CompareForDay(ScheduleItem a, ScheduleItem b)
        {
            int ka = a.Hour * 60 + a.Minute;
            int kb = b.Hour * 60 + b.Minute;
            if (ka != kb)
            {
                return ka < kb ? -1 : 1;
            }
            return string.CompareOrdinal(a.Title == null ? "" : a.Title, b.Title == null ? "" : b.Title);
        }

        public int TotalForDay(DateTime day)
        {
            return ForDay(day).Count;
        }

        public int DoneForDay(DateTime day)
        {
            DateTime d = day.Date;
            List<ScheduleItem> list = ForDay(d);
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].IsDoneOn(RepeatRule.OccurrenceStart(list[i], d)))
                {
                    n++;
                }
            }
            return n;
        }
    }
}
