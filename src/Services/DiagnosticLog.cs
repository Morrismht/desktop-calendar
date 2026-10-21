using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DesktopCalendar.Services
{
    /// <summary>
    /// 极简诊断日志：%APPDATA%\DesktopCalendar\diagnostics.log。
    /// 单文件 200KB 上限，超了先把旧文件改名 .1 再重开；全程吞异常——写日志绝不能把程序弄崩。
    /// </summary>
    public static class DiagnosticLog
    {
        public const long MaxBytes = 200 * 1024;
        public const string FileName = "diagnostics.log";
        public const string DirectoryName = "DesktopCalendar";

        private static readonly object Sync = new object();

        /// <summary>日志目录 %APPDATA%\DesktopCalendar。</summary>
        public static string LogDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    DirectoryName);
            }
        }

        /// <summary>日志文件绝对路径（PathOverride 非空时用它，供自检把日志导到临时文件）。</summary>
        public static string LogPath
        {
            get
            {
                if (!string.IsNullOrEmpty(_pathOverride))
                {
                    return _pathOverride;
                }
                if (!string.IsNullOrEmpty(_effectivePath))
                {
                    return _effectivePath;
                }
                return Path.Combine(LogDirectory, FileName);
            }
        }

        /// <summary>自检用：把日志改写到指定文件；设为 null 恢复正常路径。生产流程不设它。</summary>
        public static string PathOverride
        {
            get { return _pathOverride; }
            set
            {
                _pathOverride = value;
                _effectivePath = null;
            }
        }

        /// <summary>最近一次交给 Write 的完整行（含时间戳），写入失败时也能拿出来排查。</summary>
        public static string LastLine
        {
            get { return _lastLine; }
        }

        /// <summary>实际写入成功的位置（%APPDATA% 被拒时会退到 exe 同级 data 目录），供界面显示。</summary>
        public static string EffectivePath
        {
            get { return _effectivePath; }
        }

        private static string _pathOverride;
        private static string _lastLine;
        private static string _effectivePath;

        /// <summary>最近一次写入失败的原因（成功则清空），供自检与界面排查用。</summary>
        public static string LastError
        {
            get { return _lastError; }
        }

        private static string _lastError;

        /// <summary>追加一行（自动加时间戳）。任何失败都静默，只返回 false 并记下 LastError。</summary>
        public static bool Write(string line)
        {
            string composed = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                + " " + (line == null ? "" : line) + "\r\n";
            _lastLine = composed;
            try
            {
                lock (Sync)
                {
                    string path = LogPath;
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    RollOver(path);
                    File.AppendAllText(path, composed, Encoding.UTF8);
                    _lastError = null;
                    return true;
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.GetType().Name + " " + ex.Message;
                if (string.IsNullOrEmpty(_pathOverride))
                {
                    return WriteFallback(composed);
                }
                return false;
            }
        }

        /// <summary>首选目录写不进去（安全软件/策略拒绝）时，退到 exe 同级 data 目录再写一次。</summary>
        private static bool WriteFallback(string composed)
        {
            try
            {
                string exeDir = Path.GetDirectoryName(typeof(DiagnosticLog).Assembly.Location);
                if (string.IsNullOrEmpty(exeDir))
                {
                    return false;
                }
                string dir = Path.Combine(exeDir, "data");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string path = Path.Combine(dir, FileName);
                RollOver(path);
                File.AppendAllText(path, composed, Encoding.UTF8);
                _effectivePath = path;
                _lastError = null;
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.GetType().Name + " " + ex.Message;
                return false;
            }
        }

        /// <summary>超过上限就把当前文件挪成 .1（覆盖旧的 .1），下次追加自然重开新文件。</summary>
        private static void RollOver(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists || info.Length <= MaxBytes)
                {
                    return;
                }
                string bak = path + ".1";
                if (File.Exists(bak))
                {
                    File.Delete(bak);
                }
                File.Move(path, bak);
            }
            catch (Exception)
            {
            }
        }
    }
}
