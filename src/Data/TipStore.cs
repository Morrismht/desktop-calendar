using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace DesktopCalendar.Data
{
    /// <summary>提示条目的校验结果。编辑器把它翻成中文提示，测试拿它做断言。</summary>
    public enum TipCheck
    {
        Ok = 0,
        Empty,
        TooLong,
        Duplicate,
        Full,
        OutOfRange
    }

    /// <summary>
    /// 自定义「每日提示」池。独立文件 tips.json，绝不碰 data.json 的格式，
    /// 原子写（临时文件 + 替换，失败退回覆盖拷贝），UTF-8 无 BOM。
    /// 文件缺失 / JSON 坏 / 空数组一律回落空列表并把原因写进 LastError，绝不外抛。
    /// 内置 26 条默认提示住在这里（DefaultTipsValue），MainForm 不再自己维护一份。
    /// </summary>
    public class TipStore
    {
        /// <summary>条数上限。</summary>
        public const int MaxTips = 50;
        /// <summary>单条字符数上限。</summary>
        public const int MaxLength = 60;

        private const string FileName = "tips.json";
        private const string TipsKey = "tips";
        private const int FormatVersion = 1;

        /// <summary>内置提示池（26 条）：正向、具体、不说教。</summary>
        private static readonly string[] DefaultTipsValue = new string[]
        {
            "把一个待办拆成十分钟内能动手的第一步。",
            "日程之间留十五分钟空档，临时状况就不会连锁。",
            "先做最不想做的那件，剩下的都轻一些。",
            "把会议压成二十五分钟，留五分钟给自己收尾。",
            "今天只定三个重点，做完就算赢。",
            "提前十分钟到，比准点到更从容。",
            "写日程时顺手加上地点，出门前不用再翻记录。",
            "把「稍后再说」换成具体时间，事情才会真的发生。",
            "顺手能做完的事，别让它进入待办清单。",
            "留一段不排事的时间，用来处理意外和喘息。",
            "上午最清醒的两个小时，留给需要专注的事。",
            "重复的日程先设好，以后就不必再想它。",
            "完成一件就勾掉一件，进度看得见才有动力。",
            "把手机放远一点，专注的时间会更安静。",
            "出门前看一眼明天的安排，早晨会顺很多。",
            "给难任务配个好环境：干净的桌面和一杯水。",
            "日程别排满，七分满最好用。",
            "用颜色区分工作与个人，一眼看清一天的形状。",
            "十分钟的散步，常常比再刷一轮消息更提神。",
            "把大目标写成本周的一件事，胜过写十条愿望。",
            "会前先写一句话结论，讨论会快一半。",
            "今天的第一次专注，从关掉一个通知开始。",
            "傍晚回顾五分钟，明天的安排会清楚很多。",
            "困的时候先喝口水，再决定要不要硬撑。",
            "把明天要带的东西今晚放在门口。",
            "一次只做一件事，速度反而更快。"
        };

        private static readonly ReadOnlyCollection<string> DefaultTipsReadOnly =
            new ReadOnlyCollection<string>(DefaultTipsValue);

        private readonly List<string> _tips = new List<string>();
        private readonly string _dataDir;
        private readonly string _filePath;
        private string _lastError;

        public TipStore(string dataDir)
        {
            if (dataDir == null || dataDir.Trim().Length == 0)
            {
                dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DesktopCalendar");
            }
            _dataDir = dataDir;
            _filePath = Path.Combine(_dataDir, FileName);
            Load();
        }

        public string DataDir
        {
            get { return _dataDir; }
        }

        public string FilePath
        {
            get { return _filePath; }
        }

        /// <summary>当前自定义池；空表示用户没自定义过，调用方应回落 <see cref="Defaults"/>。</summary>
        public List<string> Tips
        {
            get { return _tips; }
        }

        public string LastError
        {
            get { return _lastError; }
        }

        /// <summary>内置 26 条默认提示（只读视图，实例上也取得到）。</summary>
        public IList<string> Defaults
        {
            get { return DefaultTipsReadOnly; }
        }

        /// <summary>同上，静态入口：MainForm 在 TipStore 缺失时也能拿到默认池。</summary>
        public static IList<string> BuiltinDefaults
        {
            get { return DefaultTipsReadOnly; }
        }

        // ---------------- 校验 ----------------

        /// <summary>去首尾空白；null 视作空串（不抛）。</summary>
        public static string Clean(string raw)
        {
            if (raw == null) return "";
            return raw.Trim();
        }

        /// <summary>
        /// 判断 raw 能不能进池子。<paramref name="replaceIndex"/> ≥ 0 时表示「替换第 N 条」，
        /// 此时不跟自己比重复、也不占名额（-1 表示新增）。
        /// </summary>
        public TipCheck Check(string raw, int replaceIndex)
        {
            string s = Clean(raw);
            if (s.Length == 0) return TipCheck.Empty;
            if (s.Length > MaxLength) return TipCheck.TooLong;
            for (int i = 0; i < _tips.Count; i++)
            {
                if (i == replaceIndex) continue;
                if (string.Equals(_tips[i], s, StringComparison.Ordinal)) return TipCheck.Duplicate;
            }
            if (replaceIndex < 0 && _tips.Count >= MaxTips) return TipCheck.Full;
            return TipCheck.Ok;
        }

        /// <summary>新增一条（已去空白）。返回 Ok 时池子已变更，但未落盘。</summary>
        public TipCheck Add(string raw)
        {
            TipCheck c = Check(raw, -1);
            if (c != TipCheck.Ok) return c;
            _tips.Add(Clean(raw));
            return TipCheck.Ok;
        }

        /// <summary>改写第 index 条。</summary>
        public TipCheck Set(int index, string raw)
        {
            if (index < 0 || index >= _tips.Count) return TipCheck.OutOfRange;
            TipCheck c = Check(raw, index);
            if (c != TipCheck.Ok) return c;
            _tips[index] = Clean(raw);
            return TipCheck.Ok;
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _tips.Count) return;
            _tips.RemoveAt(index);
        }

        /// <summary>把内存池换回内置 26 条（不落盘，由调用方 Save）。</summary>
        public void ResetToDefault()
        {
            _lastError = null;
            _tips.Clear();
            _tips.AddRange(DefaultTipsValue);
        }

        // ---------------- 读写 ----------------

        public void Load()
        {
            _lastError = null;
            _tips.Clear();
            try
            {
                if (!File.Exists(_filePath))
                {
                    _lastError = "提示文件不存在：" + _filePath;
                    return;
                }
                string text = File.ReadAllText(_filePath, Encoding.UTF8);
                List<string> rows = ParseTips(text);
                for (int i = 0; i < rows.Count; i++)
                {
                    string s = Clean(rows[i]);
                    if (s.Length == 0)
                    {
                        _lastError = string.Format("第 {0} 条提示为空，已跳过", i + 1);
                        continue;
                    }
                    if (s.Length > MaxLength)
                    {
                        s = s.Substring(0, MaxLength);
                        _lastError = string.Format("第 {0} 条超过 {1} 字，已截断", i + 1, MaxLength);
                    }
                    if (Contains(s)) continue;
                    if (_tips.Count >= MaxTips)
                    {
                        _lastError = string.Format("提示超过 {0} 条，多余部分已忽略", MaxTips);
                        break;
                    }
                    _tips.Add(s);
                }
                if (_tips.Count == 0 && _lastError == null)
                {
                    _lastError = "提示文件里没有可用条目，回落内置默认提示";
                }
            }
            catch (Exception ex)
            {
                _tips.Clear();
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

        public bool Contains(string tip)
        {
            string s = Clean(tip);
            if (s.Length == 0) return false;
            for (int i = 0; i < _tips.Count; i++)
            {
                if (string.Equals(_tips[i], s, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>{"version":1,"savedAt":"...","tips":["...","..."]}</summary>
        private string BuildText()
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["version"] = FormatVersion;
            root["savedAt"] = DateTime.Now;
            List<object> rows = new List<object>();
            for (int i = 0; i < _tips.Count; i++)
            {
                rows.Add(_tips[i]);
            }
            root[TipsKey] = rows;
            return Json.Write(root);
        }

        /// <summary>临时文件 → File.Replace（带 .bak）；不支持 Replace 时退回覆盖拷贝并清 tmp。</summary>
        private void WriteFile(string text)
        {
            if (!Directory.Exists(_dataDir))
            {
                Directory.CreateDirectory(_dataDir);
            }
            string tmp = _filePath + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(_filePath))
            {
                string bak = _filePath + ".bak";
                try
                {
                    File.Replace(tmp, _filePath, bak, true);
                }
                catch (Exception)
                {
                    File.Copy(tmp, _filePath, true);
                    TryDelete(tmp);
                }
            }
            else
            {
                File.Move(tmp, _filePath);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>读 tips.json：既认 {"tips":[...]}，也认裸的 ["...","..."]。格式坏则抛 FormatException。</summary>
        private static List<string> ParseTips(string text)
        {
            List<string> result = new List<string>();
            if (text == null) return result;
            string t = text.Trim().TrimStart('\uFEFF');
            if (t.Length == 0) return result;

            Dictionary<string, object> root;
            if (t[0] == '[')
            {
                root = Json.ReadObject("{\"" + TipsKey + "\":" + t + "}");
            }
            else
            {
                root = Json.ReadObject(t);
            }

            object boxed;
            if (!root.TryGetValue(TipsKey, out boxed)) return result;
            List<object> arr = boxed as List<object>;
            if (arr == null) return result;
            for (int i = 0; i < arr.Count; i++)
            {
                string s = arr[i] as string;
                if (s != null) result.Add(s);
            }
            return result;
        }
    }
}
