using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace DesktopCalendar.Services
{
    /// <summary>
    /// 开机自启。三条机制，逐级兜底：
    /// 1) 注册表 HKCU\...\Run，值形如 "C:\path\app.exe" --silent（首选）；
    /// 2) 启动文件夹里的 DesktopCalendar.cmd（注册表被组策略禁用时兜底）；
    /// 3) 登录计划任务 DesktopCalendar（schtasks /SC ONLOGON，用户级 /RL LIMITED，前两条都被拒时兜底）。
    /// 只写当前用户，不碰 HKLM，不需要管理员。
    /// 每条失败都写成「机制名：动作 → 异常类型/退出码 + 路径或命令行」，便于定位。
    /// </summary>
    public static class AutoStart
    {
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string ValueName = "DesktopCalendar";
        public const string StartupCmdFileName = "DesktopCalendar.cmd";
        public const string TaskName = "DesktopCalendar";

        public const string MechanismRegistry = "registry";
        public const string MechanismStartup = "startup";
        public const string MechanismTask = "task";
        public const string MechanismNone = "none";

        private const string SchtasksExe = "schtasks.exe";
        private const int SchtasksTimeoutMs = 15000;

        /// <summary>本类自造的退出码：schtasks 超时。</summary>
        public const int SchtasksTimeoutExit = -2;
        /// <summary>本类自造的退出码：schtasks 进程根本没起来。</summary>
        public const int SchtasksLaunchFailedExit = -3;

        /// <summary>计划任务存在性缓存秒数：IsEnabled 会在重绘里被调用，不能每次都起进程。</summary>
        private const int TaskQueryCacheSeconds = 5;

        private static string _startupDirectoryOverride;

        private static readonly object TaskCacheLock = new object();
        private static bool _taskCacheValid;
        private static bool _taskCacheValue;
        private static DateTime _taskCacheAt = DateTime.MinValue;

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

        /// <summary>机制一的展示名（错误信息与 DescribeState 共用）。</summary>
        public static string RegistryLabel
        {
            get { return "注册表(HKCU\\" + RunKey + ")"; }
        }

        /// <summary>机制二的展示名，带当前生效的启动文件夹路径。</summary>
        public static string StartupLabel
        {
            get { return "启动文件夹(" + StartupDirectory + ")"; }
        }

        /// <summary>机制三的展示名，带计划任务名。</summary>
        public static string TaskLabel
        {
            get { return "计划任务(" + TaskName + ")"; }
        }

        /// <summary>当前生效的自启机制：registry / startup / task / none。</summary>
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
                if (IsScheduledTaskEnabled)
                {
                    return MechanismTask;
                }
                return MechanismNone;
            }
        }

        /// <summary>机制 key 的中文说法。</summary>
        public static string MechanismText(string mechanism)
        {
            if (mechanism == MechanismRegistry)
            {
                return "注册表 Run 值";
            }
            if (mechanism == MechanismStartup)
            {
                return "启动文件夹 cmd";
            }
            if (mechanism == MechanismTask)
            {
                return "登录计划任务";
            }
            return "未开启";
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

        /// <summary>登录计划任务是否存在（schtasks /Query 退出码 0 即存在），带 5 秒缓存。</summary>
        public static bool IsScheduledTaskEnabled
        {
            get
            {
                lock (TaskCacheLock)
                {
                    if (_taskCacheValid
                        && (DateTime.UtcNow - _taskCacheAt).TotalSeconds < TaskQueryCacheSeconds)
                    {
                        return _taskCacheValue;
                    }
                }
                bool exists = QueryScheduledTaskExists();
                ForceTaskCache(exists);
                return exists;
            }
        }

        /// <summary>任一机制生效即为已开启。</summary>
        public static bool IsEnabled
        {
            get { return IsRegistryEnabled || IsStartupCmdEnabled || IsScheduledTaskEnabled; }
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

        /// <summary>
        /// schtasks /Create 的参数串：
        /// /Create /TN "DesktopCalendar" /TR "\"&lt;exe&gt;\" --silent" /SC ONLOGON /RL LIMITED /F
        /// /TR 的值本身含引号，命令行里要再转义一层（\" ），否则 schtasks 会把 --silent 当作自己的参数。
        /// </summary>
        public static string BuildSchtasksCreateArgs(string exePath)
        {
            string run = BuildCommandValue(exePath);
            if (run == null)
            {
                return null;
            }
            string escaped = run.Replace("\"", "\\\"");
            return string.Format("/Create /TN \"{0}\" /TR \"{1}\" /SC ONLOGON /RL LIMITED /F", TaskName, escaped);
        }

        /// <summary>schtasks /Delete 的参数串。</summary>
        public static string BuildSchtasksDeleteArgs()
        {
            return string.Format("/Delete /TN \"{0}\" /F", TaskName);
        }

        /// <summary>级联里的一级机制：成功返回 true；失败返回 false 并把原因写进 error。</summary>
        public delegate bool AutoStartMechanism(out string error);

        /// <summary>
        /// 通用级联：按顺序跑 steps，第一个成功即停（成功下标写进 index）。
        /// 全失败时把每一步的原因按 labels 顺序汇总进 error；中途降级成功则把已失败的原因写进 error。
        /// 抽成公开方法是为了自检能注入假机制，验证「成功即停 / 失败继续 / 全失败汇总」，不必真去改注册表。
        /// </summary>
        public static bool EnableCascade(AutoStartMechanism[] steps, string[] labels, out int index, out string error)
        {
            error = null;
            index = -1;
            List<string> failures = new List<string>();
            for (int i = 0; i < steps.Length; i++)
            {
                string stepError = null;
                bool ok;
                try
                {
                    ok = steps[i](out stepError);
                }
                catch (Exception ex)
                {
                    ok = false;
                    stepError = ExceptionText(ex);
                }
                string label = labels != null && i < labels.Length ? labels[i] : ("第" + (i + 1) + "级");
                if (ok)
                {
                    index = i;
                    if (i > 0)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.Append("已降级到「").Append(label).Append("」方式（这是兜底机制），原机制失败原因：");
                        for (int k = 0; k < failures.Count; k++)
                        {
                            sb.Append(Environment.NewLine).Append(k + 1).Append(") ").Append(failures[k]);
                        }
                        error = sb.ToString();
                    }
                    return true;
                }
                failures.Add(label + " → " + (string.IsNullOrEmpty(stepError) ? "未知失败（该级未返回原因）" : stepError));
            }
            StringBuilder all = new StringBuilder("全部 ").Append(steps.Length).Append(" 种方式都失败：");
            for (int i = 0; i < failures.Count; i++)
            {
                all.Append(Environment.NewLine).Append(i + 1).Append(") ").Append(failures[i]);
            }
            error = all.ToString();
            return false;
        }

        /// <summary>给一级机制套上诊断日志：无论成败都落一行。</summary>
        private static AutoStartMechanism Logged(string action, string mechanism, AutoStartMechanism step)
        {
            return delegate(out string stepError)
            {
                string inner;
                bool ok = step(out inner);
                LogAttempt(action, mechanism, ok, inner);
                stepError = inner;
                return ok;
            };
        }

        /// <summary>先写注册表，失败退启动文件夹 cmd，再失败退登录计划任务。</summary>
        public static bool TryEnable(out string error)
        {
            AutoStartMechanism[] steps = new AutoStartMechanism[]
            {
                Logged("启用", MechanismRegistry, TryEnableRegistry),
                Logged("启用", MechanismStartup, TryEnableStartupCmd),
                Logged("启用", MechanismTask, TryEnableScheduledTask)
            };
            string[] labels = new string[] { RegistryLabel, StartupLabel, TaskLabel };
            int index;
            bool ok = EnableCascade(steps, labels, out index, out error);
            if (!ok)
            {
                error = error + Environment.NewLine
                    + "（已按「注册表 → 启动文件夹 → 登录计划任务」的兜底顺序逐条尝试，三条兜底通道都失败）";
            }
            return ok;
        }

        /// <summary>三种机制都清掉；任一清理失败返回 false 并把逐条原因写进 error。</summary>
        public static bool TryDisable(out string error)
        {
            error = null;

            string regError;
            bool regOk = TryDisableRegistry(out regError);
            LogAttempt("停用", MechanismRegistry, regOk, regError);

            string cmdError;
            bool cmdOk = TryDisableStartupCmd(out cmdError);
            LogAttempt("停用", MechanismStartup, cmdOk, cmdError);

            string taskError;
            bool taskOk = TryDisableScheduledTask(out taskError);
            LogAttempt("停用", MechanismTask, taskOk, taskError);

            if (regOk && cmdOk && taskOk)
            {
                return true;
            }

            StringBuilder sb = new StringBuilder("停用未完全成功：");
            if (!regOk)
            {
                sb.Append(Environment.NewLine).Append("1) ").Append(regError);
            }
            if (!cmdOk)
            {
                sb.Append(Environment.NewLine).Append("2) ").Append(cmdError);
            }
            if (!taskOk)
            {
                sb.Append(Environment.NewLine).Append("3) ").Append(taskError);
            }
            error = sb.ToString();
            return false;
        }

        /// <summary>逐机制的人类可读状态（多行），供界面悬浮提示与日志使用。</summary>
        public static string DescribeState()
        {
            string exe = SafeExePath();
            string active;
            try
            {
                active = ActiveMechanism;
            }
            catch (Exception)
            {
                active = MechanismNone;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("生效机制：").Append(MechanismText(active)).Append("（").Append(active).Append("）")
              .Append("；目标 exe=").Append(exe).Append(Environment.NewLine);

            bool reg = false;
            try
            {
                reg = IsRegistryEnabled;
            }
            catch (Exception)
            {
            }
            sb.Append(RegistryLabel).Append("：").Append(reg ? "生效" : "未生效")
              .Append("；当前值=").Append(ValueOrNone(ReadRawValue()))
              .Append("；目标 exe=").Append(exe).Append(Environment.NewLine);

            bool cmd = false;
            try
            {
                cmd = IsStartupCmdEnabled;
            }
            catch (Exception)
            {
            }
            string cmdPath = "";
            try
            {
                cmdPath = StartupCmdPath;
            }
            catch (Exception)
            {
            }
            sb.Append(StartupLabel).Append("：").Append(cmd ? "生效" : "未生效")
              .Append("；当前文件=").Append(cmd || File.Exists(cmdPath) ? cmdPath : "（无）")
              .Append("；目标 exe=").Append(exe).Append(Environment.NewLine);

            bool task = false;
            try
            {
                task = IsScheduledTaskEnabled;
            }
            catch (Exception)
            {
            }
            sb.Append(TaskLabel).Append("：").Append(task ? "生效" : "未生效")
              .Append("；状态=").Append(task ? "已注册（schtasks /Query 退出码 0）" : "不存在")
              .Append("；目标 exe=").Append(exe);
            return sb.ToString();
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
                    error = RegistryLabel + "：写入值 " + ValueName + " 失败 → 无法确定程序自身路径";
                    return false;
                }
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null)
                    {
                        error = RegistryLabel + "：打开子键失败 → 返回 null；目标 exe=" + exe;
                        return false;
                    }
                    key.SetValue(ValueName, BuildCommandValue(exe), RegistryValueKind.String);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = RegistryLabel + "：写入值 " + ValueName + " 失败 → " + ExceptionText(ex)
                    + "；目标 exe=" + SafeExePath();
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
                error = RegistryLabel + "：删除值 " + ValueName + " 失败 → " + ExceptionText(ex);
                return false;
            }
        }

        // ---------------- 机制二：启动文件夹 ----------------

        /// <summary>写 &lt;Startup&gt;\DesktopCalendar.cmd。</summary>
        public static bool TryEnableStartupCmd(out string error)
        {
            error = null;
            string path = null;
            try
            {
                string exe = ExecutablePath;
                if (string.IsNullOrEmpty(exe))
                {
                    error = StartupLabel + "：写入 " + StartupCmdPath + " 失败 → 无法确定程序自身路径";
                    return false;
                }
                path = StartupCmdPath;
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
                error = StartupLabel + "：写入 " + (path == null ? StartupCmdPath : path) + " 失败 → "
                    + ExceptionText(ex) + "；目标 exe=" + SafeExePath();
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
                error = StartupLabel + "：删除 " + StartupCmdPath + " 失败 → " + ExceptionText(ex);
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

        // ---------------- 机制三：登录计划任务 ----------------

        /// <summary>直接问 schtasks 任务是否存在（不走缓存）。退出码 0 = 存在。</summary>
        public static bool QueryScheduledTaskExists()
        {
            string output;
            int code = RunSchtasks(string.Format("/Query /TN \"{0}\"", TaskName), out output);
            return code == 0;
        }

        /// <summary>schtasks /Create /SC ONLOGON /RL LIMITED /F。</summary>
        public static bool TryEnableScheduledTask(out string error)
        {
            error = null;
            string exe = SafeExePath();
            string args = BuildSchtasksCreateArgs(exe);
            if (args == null)
            {
                error = TaskLabel + "：创建失败 → 无法确定程序自身路径";
                return false;
            }
            string output;
            int code = RunSchtasks(args, out output);
            if (code == 0)
            {
                ForceTaskCache(true);
                return true;
            }
            InvalidateTaskCache();
            error = TaskLabel + "：schtasks /Create 失败 → 退出码 " + code + "，" + output
                + "；命令行=" + SchtasksExe + " " + args + "；目标 exe=" + exe;
            return false;
        }

        /// <summary>schtasks /Delete /F。任务本来就不存在时视为成功。</summary>
        public static bool TryDisableScheduledTask(out string error)
        {
            error = null;
            if (!QueryScheduledTaskExists())
            {
                ForceTaskCache(false);
                return true;
            }
            string args = BuildSchtasksDeleteArgs();
            string output;
            int code = RunSchtasks(args, out output);
            if (code == 0)
            {
                ForceTaskCache(false);
                return true;
            }
            // 退出码非 0 但复查发现任务已不在，同样算清理干净。
            if (!QueryScheduledTaskExists())
            {
                ForceTaskCache(false);
                return true;
            }
            InvalidateTaskCache();
            error = TaskLabel + "：schtasks /Delete 失败 → 退出码 " + code + "，" + output
                + "；命令行=" + SchtasksExe + " " + args;
            return false;
        }

        /// <summary>
        /// 跑一次 schtasks：不弹黑窗、捕获 stdout/stderr、最多等 15 秒。
        /// 返回退出码；负数为本类自造（-2 超时、-3 进程没起来）。
        /// </summary>
        public static int RunSchtasks(string arguments, out string output)
        {
            output = "";
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = SchtasksExe;
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            // schtasks 的输出是系统 ANSI 代码页（中文机器上是 GBK），按 Encoding.Default 解才不是乱码。
            try
            {
                psi.StandardOutputEncoding = Encoding.Default;
                psi.StandardErrorEncoding = Encoding.Default;
            }
            catch (Exception)
            {
            }

            Process p = null;
            try
            {
                p = Process.Start(psi);
                if (p == null)
                {
                    output = "exit=" + SchtasksLaunchFailedExit + "（Process.Start 返回 null）";
                    return SchtasksLaunchFailedExit;
                }
                // stderr 异步读，避免两个管道互相堵死。
                Task<string> stderrTask = p.StandardError.ReadToEndAsync();
                string stdout = p.StandardOutput.ReadToEnd();
                bool stderrOk = stderrTask.Wait(SchtasksTimeoutMs);
                string stderr = stderrOk ? stderrTask.Result : "（读取超时）";
                bool exited = p.WaitForExit(SchtasksTimeoutMs);
                int code = exited ? p.ExitCode : SchtasksTimeoutExit;
                output = "exit=" + (exited ? code.ToString() : "超时(>" + SchtasksTimeoutMs + "ms)")
                    + "，stdout=" + Clip(stdout) + "，stderr=" + Clip(stderr);
                return code;
            }
            catch (Exception ex)
            {
                output = "exit=" + SchtasksLaunchFailedExit + "（启动 " + SchtasksExe + " 失败 → "
                    + ExceptionText(ex) + "）";
                return SchtasksLaunchFailedExit;
            }
            finally
            {
                if (p != null)
                {
                    try
                    {
                        p.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private static void ForceTaskCache(bool exists)
        {
            lock (TaskCacheLock)
            {
                _taskCacheValue = exists;
                _taskCacheValid = true;
                _taskCacheAt = DateTime.UtcNow;
            }
        }

        private static void InvalidateTaskCache()
        {
            lock (TaskCacheLock)
            {
                _taskCacheValid = false;
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
                        error = RegistryLabel + "：打开子键失败 → 返回 null";
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
                error = RegistryLabel + "：还原值 " + ValueName + " 失败 → " + ExceptionText(ex);
                return false;
            }
        }

        // ---------------- 小工具 ----------------

        /// <summary>异常类型 + 消息（带内部异常），写进错误串用。</summary>
        private static string ExceptionText(Exception ex)
        {
            if (ex == null)
            {
                return "未知错误";
            }
            string text = ex.GetType().Name + " " + ex.Message;
            if (ex.InnerException != null)
            {
                text = text + " / " + ex.InnerException.GetType().Name + " " + ex.InnerException.Message;
            }
            return text;
        }

        private static string SafeExePath()
        {
            try
            {
                string exe = ExecutablePath;
                return string.IsNullOrEmpty(exe) ? "（未知）" : exe;
            }
            catch (Exception)
            {
                return "（未知）";
            }
        }

        private static string ValueOrNone(string value)
        {
            return string.IsNullOrEmpty(value) ? "（无）" : value;
        }

        private static string Clip(string s)
        {
            if (s == null)
            {
                return "";
            }
            s = s.Trim().Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
            if (s.Length > 400)
            {
                s = s.Substring(0, 400) + "…";
            }
            return s;
        }

        private static void LogAttempt(string action, string mechanism, bool ok, string error)
        {
            string line = "AutoStart." + action + " 机制=" + mechanism + " 结果=" + (ok ? "成功" : "失败");
            if (!ok && !string.IsNullOrEmpty(error))
            {
                line = line + " 错误=" + error.Replace("\r\n", " / ").Replace("\n", " / ");
            }
            DiagnosticLog.Write(line);
        }
    }
}
