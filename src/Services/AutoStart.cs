using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace DesktopCalendar.Services
{
    /// <summary>
    /// 开机自启。两条机制，互为兜底：
    /// 1) 注册表 HKCU\...\Run，值形如 "C:\path\app.exe" --silent（首选）；
    /// 2) 启动文件夹里的 DesktopCalendar.cmd（注册表被组策略禁用时兜底）。
    /// 只写当前用户，不碰 HKLM，不需要管理员。
    /// </summary>
    public static class AutoStart
    {
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string ValueName = "DesktopCalendar";
        public const string StartupCmdFileName = "DesktopCalendar.cmd";

        public const string MechanismRegistry = "registry";
        public const string MechanismStartup = "startup";
        public const string MechanismNone = "none";

        private static string _startupDirectoryOverride;

        /// <summary>测试钩子：null 表示用真实的「启动」文件夹。</summary>
        public static string StartupDirectoryOverride
        {
            get { return _startupDirectoryOverride; }
            set { _startupDirectoryOverride = value; }
        }

        /// <summary>启动文件夹路径（受 StartupDirectoryOverride 影响）。</summary>
        public static string StartupDirectory
        {
            get
            {
                string ov = _startupDirectoryOverride;
                if (ov != null && ov.Trim().Length > 0)
                {
                    return ov;
                }
                return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            }
        }

        /// <summary>&lt;Startup&gt;\DesktopCalendar.cmd</summary>
        public static string StartupCmdPath
        {
            get { return Path.Combine(StartupDirectory, StartupCmdFileName); }
        }

        /// <summary>当前生效的自启机制：registry / startup / none。</summary>
        public static string ActiveMechanism
        {
            get
            {
                if (IsRegistryEnabled)
                {
                    return MechanismRegistry;
                }
                if (IsStartupCmdEnabled)
                {
                    return MechanismStartup;
                }
                return MechanismNone;
            }
        }

        /// <summary>当前程序 exe 绝对路径（Assembly.GetEntryAssembly().Location）。</summary>
        public static string ExecutablePath
        {
            get
            {
                try
                {
                    Assembly asm = Assembly.GetEntryAssembly();
                    if (asm != null && !string.IsNullOrEmpty(asm.Location))
                    {
                        return asm.Location;
                    }
                }
                catch (Exception)
                {
                }
                try
                {
                    Process p = Process.GetCurrentProcess();
                    if (p.MainModule != null && !string.IsNullOrEmpty(p.MainModule.FileName))
                    {
                        return p.MainModule.FileName;
                    }
                }
                catch (Exception)
                {
                }
                string[] argv = Environment.GetCommandLineArgs();
                return argv.Length > 0 ? argv[0] : "";
            }
        }

        /// <summary>注册表里的启动项是否已指向当前 exe（路径非陈旧）。</summary>
        public static bool IsRegistryEnabled
        {
            get
            {
                string v = ReadRawValue();
                if (string.IsNullOrEmpty(v))
                {
                    return false;
                }
                string exe = ExecutablePath;
                if (string.IsNullOrEmpty(exe))
                {
                    return true;
                }
                return v.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        /// <summary>启动文件夹的 cmd 是否存在且指向当前 exe。</summary>
        public static bool IsStartupCmdEnabled
        {
            get
            {
                try
                {
                    string path = StartupCmdPath;
                    if (!File.Exists(path))
                    {
                        return false;
                    }
                    string content = ReadStartupCmdContent();
                    if (string.IsNullOrEmpty(content))
                    {
                        return false;
                    }
                    string exe = ExecutablePath;
                    if (string.IsNullOrEmpty(exe))
                    {
                        return true;
                    }
                    return content.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>任一机制生效即为已开启。</summary>
        public static bool IsEnabled
        {
            get { return IsRegistryEnabled || IsStartupCmdEnabled; }
        }

        /// <summary>启动项字符串："exe 路径" --silent。路径含空格时必须加引号。</summary>
        public static string BuildCommandValue(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
            {
                return null;
            }
            return string.Format("\"{0}\" --silent", exePath);
        }

        /// <summary>
        /// cmd 文件内容：CRLF 结尾的两行。cmd.exe 按系统 ANSI/OEM 代码页读取 .cmd，
        /// 写成 UTF-8 会让中文路径变乱码，故调用方用 Encoding.Default 落盘。
        /// </summary>
        public static string BuildStartupCmdContent(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
            {
                return null;
            }
            return "@echo off\r\n" + string.Format("start \"\" \"{0}\" --silent\r\n", exePath);
        }

        /// <summary>先写注册表，失败退到启动文件夹 cmd。</summary>
        public static bool TryEnable(out string error)
        {
            error = null;
            string regError;
            if (TryEnableRegistry(out regError))
            {
                return true;
            }
            string cmdError;
            if (TryEnableStartupCmd(out cmdError))
            {
                error = "已改用启动文件夹兜底（注册表写入失败：" + regError + "）";
                return true;
            }
            error = string.Format("注册表：{0}；启动文件夹：{1}", regError, cmdError);
            return false;
        }

        /// <summary>两种机制都清掉；任一清理失败返回 false 并把原因写进 error。</summary>
        public static bool TryDisable(out string error)
        {
            error = null;
            string regError;
            bool regOk = TryDisableRegistry(out regError);
            string cmdError;
            bool cmdOk = TryDisableStartupCmd(out cmdError);
            if (regOk && cmdOk)
            {
                return true;
            }
            if (!regOk && !cmdOk)
            {
                error = string.Format("注册表：{0}；启动文件夹：{1}", regError, cmdError);
            }
            else
            {
                error = !regOk ? regError : cmdError;
            }
            return false;
        }

        // ---------------- 机制一：注册表 ----------------

        /// <summary>写 "exe 路径" --silent 到 HKCU Run。</summary>
        public static bool TryEnableRegistry(out string error)
        {
            error = null;
            try
            {
                string exe = ExecutablePath;
                if (string.IsNullOrEmpty(exe))
                {
                    error = "无法确定程序自身路径";
                    return false;
                }
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null)
                    {
                        error = "无法打开注册表启动项：" + RunKey;
                        return false;
                    }
                    key.SetValue(ValueName, BuildCommandValue(exe), RegistryValueKind.String);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>删除注册表启动项。本来就没有值时视为成功（无需写权限）。</summary>
        public static bool TryDisableRegistry(out string error)
        {
            error = null;
            try
            {
                if (ReadRawValue() == null)
                {
                    return true;
                }
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null)
                    {
                        return true;
                    }
                    key.DeleteValue(ValueName, false);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ---------------- 机制二：启动文件夹 ----------------

        /// <summary>写 &lt;Startup&gt;\DesktopCalendar.cmd。</summary>
        public static bool TryEnableStartupCmd(out string error)
        {
            error = null;
            try
            {
                string exe = ExecutablePath;
                if (string.IsNullOrEmpty(exe))
                {
                    error = "无法确定程序自身路径";
                    return false;
                }
                string path = StartupCmdPath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(path, BuildStartupCmdContent(exe), Encoding.Default);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>删除启动文件夹 cmd。本来就没有文件时视为成功。</summary>
        public static bool TryDisableStartupCmd(out string error)
        {
            error = null;
            try
            {
                string path = StartupCmdPath;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>cmd 文件内容，读不到返回 null。</summary>
        public static string ReadStartupCmdContent()
        {
            try
            {
                string path = StartupCmdPath;
                if (!File.Exists(path))
                {
                    return null;
                }
                return File.ReadAllText(path, Encoding.Default);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------------- 原始值读写（自检记录/还原原状态） ----------------

        /// <summary>原始注册表值，不存在返回 null。</summary>
        public static string ReadRawValue()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null)
                    {
                        return null;
                    }
                    return key.GetValue(ValueName, null) as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>直接写入原始值（null = 删除），供自检还原注册表原状态。</summary>
        public static bool WriteRawValue(string value, out string error)
        {
            error = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null)
                    {
                        error = "无法打开注册表启动项：" + RunKey;
                        return false;
                    }
                    if (value == null)
                    {
                        key.DeleteValue(ValueName, false);
                    }
                    else
                    {
                        key.SetValue(ValueName, value, RegistryValueKind.String);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
