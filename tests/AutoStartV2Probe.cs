using System;
using System.IO;
using System.Text;
using DesktopCalendar.Services;

namespace AutoStartV2Probe
{
    /// <summary>
    /// AutoStart v2 自检探针。build\desktop-calendar.exe 作为引用程序集，直接打公开 API。
    /// 本会话派生的子进程可能被运行环境限制（注册表/启动文件夹/计划任务写入被拒），
    /// 所以这里不断言「一定启用成功」，只断言：三级链路都被尝试、错误串格式正确、状态自洽。
    /// 成功路径由 EnableCascade 注入假机制覆盖，不依赖真实权限。
    /// </summary>
    internal static class Program
    {
        private static int _pass;
        private static int _fail;

        private static void Check(string id, bool ok, string detail)
        {
            if (ok)
            {
                _pass++;
                Console.WriteLine("[PASS] " + id + (string.IsNullOrEmpty(detail) ? "" : " : " + detail));
            }
            else
            {
                _fail++;
                Console.WriteLine("[FAIL] " + id + (string.IsNullOrEmpty(detail) ? "" : " : " + detail));
            }
        }

        private static void Info(string s)
        {
            Console.WriteLine("       " + s);
        }

        private static bool Has(string hay, string needle)
        {
            if (hay == null || needle == null)
            {
                return false;
            }
            return hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string OneLine(string s)
        {
            if (s == null)
            {
                return "(null)";
            }
            string one = s.Replace("\r\n", " / ").Replace("\n", " / ");
            return one.Length > 700 ? one.Substring(0, 700) + "…" : one;
        }

        private static bool SafeIsEnabled()
        {
            try
            {
                return AutoStart.IsEnabled;
            }
            catch (Exception ex)
            {
                Info("IsEnabled 抛异常：" + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string probeExe = AutoStart.ExecutablePath;
            Info("探针 exe=" + probeExe);
            Info("真实启动文件夹=" + Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            Info("DiagnosticLog.LogPath=" + DiagnosticLog.LogPath);

            // 日志样例改写到临时文件：本会话派生的子进程可能被拒写 %APPDATA%，不能因此让自检红。
            // 真实路径先探一次并如实打印，再切到临时路径跑后面的可断言用例。
            string logRealPath = DiagnosticLog.LogPath;
            string logRealResult = DiagnosticLog.Write("[selftest] 真实路径探测")
                ? "成功"
                : "失败（" + DiagnosticLog.LastError + "）";
            Info("真实日志路径试探：" + logRealPath + " → " + logRealResult);
            string probeRoot = AppDomain.CurrentDomain.BaseDirectory;
            string tempLogDir = Path.Combine(probeRoot, "logtest");
            try
            {
                if (Directory.Exists(tempLogDir))
                {
                    Directory.Delete(tempLogDir, true);
                }
            }
            catch (Exception)
            {
            }
            Directory.CreateDirectory(tempLogDir);
            DiagnosticLog.PathOverride = Path.Combine(tempLogDir, DiagnosticLog.FileName);
            Info("自检日志改写到 " + DiagnosticLog.PathOverride);

            // ---------------- A：命令行拼装 ----------------
            string cmd = AutoStart.BuildCommandValue(@"C:\a b\app.exe");
            Check("A1 BuildCommandValue 带空格路径", cmd == "\"C:\\a b\\app.exe\" --silent", cmd);
            Check("A2 BuildCommandValue(null) 返回 null", AutoStart.BuildCommandValue(null) == null, null);

            // ---------------- B：级联顺序（注入假机制，不碰真实权限） ----------------
            int idx;
            string err;
            bool ok;

            ok = AutoStart.EnableCascade(
                new AutoStart.AutoStartMechanism[]
                {
                    delegate(out string e) { e = "第一级失败示例"; return false; },
                    delegate(out string e) { e = null; return true; },
                    delegate(out string e) { e = "第三级不该被调用"; return true; }
                },
                new string[] { "一级", "二级", "三级" }, out idx, out err);
            Check("B1 成功即停：第二级成功 → true 且 index=1", ok && idx == 1, "ok=" + ok + " index=" + idx);
            Check("B2 降级原因写进 error", Has(err, "已降级到「二级」") && Has(err, "第一级失败示例"), OneLine(err));

            ok = AutoStart.EnableCascade(
                new AutoStart.AutoStartMechanism[]
                {
                    delegate(out string e) { e = null; return true; },
                    delegate(out string e) { e = "不该被调用"; return true; }
                },
                new string[] { "一级", "二级" }, out idx, out err);
            Check("B3 首级成功 → true、index=0、error 为空", ok && idx == 0 && err == null, "ok=" + ok + " index=" + idx + " err=" + OneLine(err));

            ok = AutoStart.EnableCascade(
                new AutoStart.AutoStartMechanism[]
                {
                    delegate(out string e) { e = "甲失败"; return false; },
                    delegate(out string e) { e = "乙失败"; return false; },
                    delegate(out string e) { e = "丙失败"; return false; }
                },
                new string[] { "甲", "乙", "丙" }, out idx, out err);
            Check("B4 全失败 → false 且 index=-1", !ok && idx == -1, "ok=" + ok + " index=" + idx);
            Check("B5 全失败汇总三条原因",
                Has(err, "全部 3 种方式都失败") && Has(err, "甲失败") && Has(err, "乙失败") && Has(err, "丙失败"), OneLine(err));

            ok = AutoStart.EnableCascade(
                new AutoStart.AutoStartMechanism[]
                {
                    delegate(out string e) { e = "先失败"; return false; },
                    delegate(out string e) { throw new InvalidOperationException("故意抛异常"); }
                },
                new string[] { "一级", "二级" }, out idx, out err);
            Check("B6 某一级抛异常被接住并记入 error", !ok && Has(err, "InvalidOperationException"), OneLine(err));

            // ---------------- C：实机 TryEnable（结果随环境，格式必须对） ----------------
            bool enableOk = AutoStart.TryEnable(out err);
            string mech = AutoStart.ActiveMechanism;
            bool enabled = SafeIsEnabled();
            Info("C TryEnable 实机结果：ok=" + enableOk + " mechanism=" + mech + " IsEnabled=" + enabled);
            if (err != null)
            {
                Info("C 逐级明细：" + OneLine(err));
            }
            if (enableOk)
            {
                Check("C1 启用成功 → IsEnabled=true", enabled, "IsEnabled=" + enabled);
                Check("C2 启用成功 → 机制 ∈ {registry,startup,task}",
                    mech == AutoStart.MechanismRegistry || mech == AutoStart.MechanismStartup || mech == AutoStart.MechanismTask, mech);
                if (mech != AutoStart.MechanismRegistry)
                {
                    Check("C3 降级成功 → error 说明「已降级到 …」", Has(err, "已降级到"), OneLine(err));
                }
            }
            else
            {
                Check("C1 启用失败 → IsEnabled=false（无半成功状态）", !enabled, "IsEnabled=" + enabled);
                Check("C2 失败串含三条机制名",
                    Has(err, "注册表(HKCU") && Has(err, "启动文件夹(") && Has(err, "计划任务("), null);
                Check("C3 失败串含异常类型或 schtasks 退出码",
                    Has(err, "Exception") || Has(err, "退出码"), null);
                Check("C4 失败串含相关路径或目标 exe",
                    Has(err, "目标 exe=") || Has(err, probeExe), null);
            }
            string state = AutoStart.DescribeState();
            string[] stateLines = state.Replace("\r\n", "\n").Split('\n');
            Info("DescribeState 共 " + stateLines.Length + " 行：");
            for (int i = 0; i < stateLines.Length; i++)
            {
                Info("  " + stateLines[i]);
            }
            Check("C5 DescribeState 至少 3 行", stateLines.Length >= 3, "行数=" + stateLines.Length);
            Check("C6 DescribeState 含当前 exe 路径", Has(state, probeExe), null);
            Check("C7 DescribeState 逐机制列出三种机制",
                Has(state, "注册表(HKCU") && Has(state, "启动文件夹(") && Has(state, "计划任务("), null);

            // ---------------- D：停用 ----------------
            bool disableOk = AutoStart.TryDisable(out err);
            bool stillEnabled = SafeIsEnabled();
            Info("D TryDisable 实机结果：ok=" + disableOk + " IsEnabled=" + stillEnabled);
            if (err != null)
            {
                Info("D 逐级明细：" + OneLine(err));
            }
            Check("D1 停用后 IsEnabled=false", !stillEnabled, "IsEnabled=" + stillEnabled);
            if (!disableOk)
            {
                Check("D2 停用失败 → error 非空且含机制名",
                    Has(err, "注册表(HKCU") || Has(err, "启动文件夹(") || Has(err, "计划任务("), OneLine(err));
            }

            // ---------------- E：StartupDirectoryOverride 临时目录 ----------------
            string realStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string realCmd = Path.Combine(realStartup, AutoStart.StartupCmdFileName);
            bool realCmdBefore = File.Exists(realCmd);
            string tempStartup = Path.Combine(probeRoot, "startup_override_" + Guid.NewGuid().ToString("N"));
            bool eOk = false;
            string eErr = null;
            try
            {
                AutoStart.StartupDirectoryOverride = tempStartup;
                string tempCmdPath = AutoStart.StartupCmdPath;
                Check("E1 StartupCmdPath 落在 override 目录",
                    tempCmdPath.StartsWith(tempStartup, StringComparison.OrdinalIgnoreCase), tempCmdPath);
                eOk = AutoStart.TryEnableStartupCmd(out eErr);
                Info("E 临时启动文件夹=" + tempStartup + " ok=" + eOk + (eErr == null ? "" : " err=" + OneLine(eErr)));
                if (eOk)
                {
                    Check("E2 临时目录里生成了 cmd", File.Exists(tempCmdPath), tempCmdPath);
                    string content = File.Exists(tempCmdPath) ? File.ReadAllText(tempCmdPath, Encoding.Default) : "";
                    Check("E3 cmd 内容含 exe 路径", Has(content, probeExe), OneLine(content));
                }
                else
                {
                    Check("E2 本环境写入被拒 → 错误串含机制名与异常类型",
                        Has(eErr, "启动文件夹(") && (Has(eErr, "Exception") || Has(eErr, "拒绝")), OneLine(eErr));
                }
                Check("E4 真实启动文件夹未被写入", File.Exists(realCmd) == realCmdBefore, realCmd);
            }
            finally
            {
                AutoStart.StartupDirectoryOverride = null;
                try
                {
                    if (Directory.Exists(tempStartup))
                    {
                        Directory.Delete(tempStartup, true);
                    }
                }
                catch (Exception ex)
                {
                    Info("E 清理临时目录失败：" + ex.GetType().Name + " " + ex.Message);
                }
            }
            Check("E5 override 已还原为 null", AutoStart.StartupDirectoryOverride == null, null);
            Check("E6 临时启动目录已清理", !Directory.Exists(tempStartup), tempStartup);

            // ---------------- F：不可写路径的错误格式 ----------------
            string savedOverride = AutoStart.StartupDirectoryOverride;
            string fErr;
            bool fOk;
            try
            {
                AutoStart.StartupDirectoryOverride = @"Z:\nope";
                fOk = AutoStart.TryEnableStartupCmd(out fErr);
            }
            finally
            {
                AutoStart.StartupDirectoryOverride = savedOverride;
            }
            Info("F 不可写路径 Z:\\nope → ok=" + fOk + " err=" + OneLine(fErr));
            Check("F1 不可写路径返回 false", !fOk, "ok=" + fOk);
            Check("F2 错误串含机制名（启动文件夹）", Has(fErr, "启动文件夹("), null);
            Check("F3 错误串含异常类型", Has(fErr, "Exception"), null);
            Check("F4 错误串含相关路径", Has(fErr, "Z:\\nope") || Has(fErr, "目标 exe="), null);

            // ---------------- G：DiagnosticLog ----------------
            bool anyWriteOk = false;
            bool threw = false;
            try
            {
                for (int i = 0; i < 300; i++)
                {
                    if (DiagnosticLog.Write("[selftest] 占位行 " + i))
                    {
                        anyWriteOk = true;
                    }
                }
            }
            catch (Exception ex)
            {
                threw = true;
                Info("G 抛异常：" + ex.GetType().Name + " " + ex.Message);
            }
            Check("G1 300 次 DiagnosticLog.Write 不抛异常", !threw, null);
            if (anyWriteOk)
            {
                string logText = "";
                try
                {
                    logText = File.ReadAllText(DiagnosticLog.LogPath, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    Info("G 读回失败：" + ex.GetType().Name + " " + ex.Message);
                }
                Check("G2 能写 → 能读回", Has(logText, "[selftest] 占位行"), DiagnosticLog.LogPath);
                Check("G3 写入成功时 LastError 为空", string.IsNullOrEmpty(DiagnosticLog.LastError), DiagnosticLog.LastError);
            }
            else
            {
                Check("G2 写不进 → Write 静默返回 false 且 LastError 有值",
                    !threw && !string.IsNullOrEmpty(DiagnosticLog.LastError), "LastError=" + DiagnosticLog.LastError);
                Info("G 说明：本会话派生子进程对 " + DiagnosticLog.LogDirectory + " 无写权限，日志落盘在真机普通权限下才有意义。");
            }

            // ---------------- H：计划任务命令行（只拼装，不真的创建） ----------------
            string createArgs = AutoStart.BuildSchtasksCreateArgs(@"C:\a b\app.exe");
            string delArgs = AutoStart.BuildSchtasksDeleteArgs();
            Info("H create args=" + createArgs);
            Info("H delete args=" + delArgs);
            Check("H1 任务名与机制常量", AutoStart.TaskName == "DesktopCalendar" && AutoStart.MechanismTask == "task", null);
            Check("H2 /Create /SC ONLOGON /RL LIMITED /F 齐全",
                Has(createArgs, "/Create") && Has(createArgs, "/SC ONLOGON") && Has(createArgs, "/RL LIMITED") && Has(createArgs, "/F"), null);
            Check("H3 /TN 用任务名", Has(createArgs, "/TN \"DesktopCalendar\""), null);
            string innerExpected = AutoStart.BuildCommandValue(@"C:\a b\app.exe").Replace("\"", "\\\"");
            Check("H4 /TR 内层引用被转义并带 --silent",
                Has(createArgs, "/TR \"" + innerExpected + "\""), createArgs);
            Check("H5 删除参数 /Delete /TN /F",
                Has(delArgs, "/Delete") && Has(delArgs, "/TN \"DesktopCalendar\"") && Has(delArgs, "/F"), null);
            Info("H 当前机器是否存在计划任务 DesktopCalendar = " + AutoStart.QueryScheduledTaskExists() + "（本自检不创建、不删除真实任务）");

            // ---------------- L：日志落盘、格式与 200KB 滚动 ----------------
            string logSampleText = "";
            try
            {
                logSampleText = File.ReadAllText(DiagnosticLog.LogPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Info("L 读取自检日志失败：" + ex.GetType().Name + " " + ex.Message);
            }
            string[] logLines = logSampleText.Replace("\r\n", "\n").Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int autoLines = 0;
            for (int i = 0; i < logLines.Length; i++)
            {
                if (Has(logLines[i], "AutoStart."))
                {
                    autoLines++;
                }
            }
            Check("L1 日志里有 AutoStart 尝试行", autoLines > 0, "AutoStart 行=" + autoLines + "，总行=" + logLines.Length);
            Check("L2 行首是 yyyy-MM-dd HH:mm:ss.fff 时间戳", logLines.Length > 0 && logLines[0].Length > 23 && logLines[0].Substring(4, 1) == "-" && logLines[0].Substring(10, 1) == " ", logLines.Length > 0 ? logLines[0] : "(空)");
            Info("L 日志样例（临时路径文件，前 3 行）：");
            for (int i = 0; i < logLines.Length && i < 3; i++)
            {
                Info("   " + logLines[i]);
            }
            for (int i = 0; i < 3000; i++)
            {
                DiagnosticLog.Write("[selftest] 填充行 " + i + " " + new string('x', 80));
            }
            bool bakExists = File.Exists(DiagnosticLog.LogPath + ".1");
            long finalBytes = 0;
            try
            {
                finalBytes = new FileInfo(DiagnosticLog.LogPath).Length;
            }
            catch (Exception)
            {
            }
            Check("L3 超过 200KB 自动滚动出 .1", bakExists, null);
            Check("L4 滚动后当前日志不超过上限", finalBytes <= DiagnosticLog.MaxBytes, "现=" + finalBytes + " 字节，上限=" + DiagnosticLog.MaxBytes);
            DiagnosticLog.PathOverride = null;
            Check("L5 PathOverride 已还原为真实路径", DiagnosticLog.LogPath == logRealPath, DiagnosticLog.LogPath);
            try
            {
                if (Directory.Exists(tempLogDir))
                {
                    Directory.Delete(tempLogDir, true);
                }
            }
            catch (Exception)
            {
            }
            Check("L6 临时日志目录已清理", !Directory.Exists(tempLogDir), tempLogDir);

            Console.WriteLine("SUMMARY PASS=" + _pass + " FAIL=" + _fail);
            return _fail == 0 ? 0 : 1;
        }
    }
}
