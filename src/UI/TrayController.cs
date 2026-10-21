using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using DesktopCalendar.Data;
using DesktopCalendar.Services;

namespace DesktopCalendar.UI
{
    /// <summary>
    /// 托盘图标 + 右键菜单（v3：自己拥有 Shell_NotifyIcon，失败可观测）。
    ///
    /// 关键变化：不再相信 NotifyIcon.Visible 的静默失败。
    /// 1) 构造后先问壳层「这个槽位的图标真的在托盘里吗」——Shell_NotifyIconGetRect，
    ///    只有它说在，才算注册成功（RegistrationOk = true）。
    /// 2) 不在时自己 NIM_ADD 一个（guidItem + uID=1 + 自建隐藏窗口做回调），
    ///    每次调用都检查返回值与 GetLastWin32Error，并写诊断日志。
    /// 3) 自注册成功后回调消息进自建窗口：左键显示主窗口，右键在光标处弹菜单。
    /// 4) TaskbarCreated：explorer 重启后重新注册并复核。
    /// 5) 气泡走 NIF_INFO；tooltip 走 NIF_TIP。
    ///
    /// 图标来源仍然是 Branding.AppIcon（取不到才自绘；自绘的 HICON 由本对象持有到 Dispose）。
    /// </summary>
    public class TrayController : IDisposable
    {
        // tooltip 截断上限：szTip 容量 128（含结尾 0），历史行为是 63，保持不变
        private const int TOOLTIP_MAX = 63;
        private const int INFO_MAX = 255;
        private const int INFO_TITLE_MAX = 63;

        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;
        private const int NIM_SETVERSION = 0x00000004;

        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;
        private const int NIF_INFO = 0x00000010;

        private const int NIIF_INFO = 0x00000001;
        private const int NOTIFYICON_VERSION_4 = 4;

        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_CONTEXTMENU = 0x007B;

        /// <summary>自注册图标的回调消息（送到自建隐藏窗口）。NOTIFYICON_VERSION_4 下 LOWORD(lParam) 是鼠标消息。</summary>
        private const int WM_TRAYICON = 0x8000 + 500;

        /// <summary>自注册图标的稳定标识。进程重启后同一 guid 复用同一个托盘槽位。</summary>
        private static readonly Guid TrayGuid = new Guid("8f3c2a51-6d4b-4f0e-9a27-3c5d81b7e402");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATAW
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONIDENTIFIER
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public Guid guidItem;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATAW data);

        [DllImport("shell32.dll", SetLastError = false)]
        private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT location);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string name);

        /// <summary>explorer 重建任务栏时广播的消息 id（0 表示注册失败）。</summary>
        internal static readonly int WmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");

        // ------------------------------------------------ 壳层通道（可注入：测试用假实现，不依赖真实托盘）

        /// <summary>把 shell32 的调用全挡在这个接口后面。生产实现是 Win32TrayShell，测试可注入假实现。</summary>
        internal interface ITrayShellChannel
        {
            bool AddIcon(IntPtr hwnd, int id, Guid guid, IntPtr hicon, string tip, int callbackMessage, out int errorCode);
            bool SetVersion(IntPtr hwnd, int id, Guid guid, int version, out int errorCode);
            bool SetTip(IntPtr hwnd, int id, Guid guid, string tip, out int errorCode);
            bool ShowInfo(IntPtr hwnd, int id, Guid guid, string title, string body, int infoFlags, out int errorCode);
            bool DeleteIcon(IntPtr hwnd, int id, Guid guid, out int errorCode);
            bool IsPresent(IntPtr hwnd, int id, Guid guid);
        }

        private static ITrayShellChannel _shell = new Win32TrayShell();

        /// <summary>当前壳层通道；测试把它替换成假实现即可在无托盘环境里跑完整流程。</summary>
        internal static ITrayShellChannel Shell
        {
            get { return _shell; }
            set
            {
                if (value == null) _shell = new Win32TrayShell();
                else _shell = value;
            }
        }

        /// <summary>真实 shell32 通道：每次都检查返回值，并把 Win32 错误码带回来（错误码 0 也可能是假成功）。</summary>
        private sealed class Win32TrayShell : ITrayShellChannel
        {
            public bool AddIcon(IntPtr hwnd, int id, Guid guid, IntPtr hicon, string tip, int callbackMessage, out int errorCode)
            {
                NOTIFYICONDATAW d = BuildData(hwnd, id, guid, NIF_MESSAGE | NIF_ICON | NIF_TIP, hicon, tip, callbackMessage);
                return Call(NIM_ADD, ref d, out errorCode);
            }

            public bool SetVersion(IntPtr hwnd, int id, Guid guid, int version, out int errorCode)
            {
                NOTIFYICONDATAW d = BuildData(hwnd, id, guid, 0, IntPtr.Zero, "", 0);
                d.uVersion = version;
                return Call(NIM_SETVERSION, ref d, out errorCode);
            }

            public bool SetTip(IntPtr hwnd, int id, Guid guid, string tip, out int errorCode)
            {
                NOTIFYICONDATAW d = BuildData(hwnd, id, guid, NIF_TIP, IntPtr.Zero, tip, 0);
                return Call(NIM_MODIFY, ref d, out errorCode);
            }

            public bool ShowInfo(IntPtr hwnd, int id, Guid guid, string title, string body, int infoFlags, out int errorCode)
            {
                NOTIFYICONDATAW d = BuildData(hwnd, id, guid, NIF_INFO, IntPtr.Zero, "", 0);
                d.szInfo = Truncate(body, INFO_MAX);
                d.szInfoTitle = Truncate(title == null ? "桌面日历" : title, INFO_TITLE_MAX);
                d.dwInfoFlags = infoFlags;
                return Call(NIM_MODIFY, ref d, out errorCode);
            }

            public bool DeleteIcon(IntPtr hwnd, int id, Guid guid, out int errorCode)
            {
                NOTIFYICONDATAW d = BuildData(hwnd, id, guid, 0, IntPtr.Zero, "", 0);
                return Call(NIM_DELETE, ref d, out errorCode);
            }

            public bool IsPresent(IntPtr hwnd, int id, Guid guid)
            {
                try
                {
                    NOTIFYICONIDENTIFIER q = new NOTIFYICONIDENTIFIER();
                    q.cbSize = Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER));
                    if (guid == Guid.Empty)
                    {
                        q.hWnd = hwnd;
                        q.uID = id;
                        q.guidItem = Guid.Empty;
                    }
                    else
                    {
                        q.hWnd = IntPtr.Zero;
                        q.uID = 0;
                        q.guidItem = guid;
                    }
                    RECT rc;
                    int hr = Shell_NotifyIconGetRect(ref q, out rc);
                    return hr == 0;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            private static bool Call(int message, ref NOTIFYICONDATAW d, out int errorCode)
            {
                errorCode = 0;
                try
                {
                    bool ok = Shell_NotifyIcon(message, ref d);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        errorCode = err == 0 ? -1 : err;
                    }
                    return ok;
                }
                catch (Exception)
                {
                    errorCode = -2;
                    return false;
                }
            }
        }

        /// <summary>测试钩子：置 true 时跳过 Branding，强制走自绘图标分支。</summary>
        internal static bool ForceSelfDrawnIcon = false;

        /// <summary>测试钩子：置 true 时不走 NotifyIcon 的登记，只用本类自己的 NIM_ADD 通道。</summary>
        internal static bool ForceOwnIconRegistration = false;

        /// <summary>当前的托盘控制器（MainForm 判断「注册失败就不要隐藏窗口」用）。</summary>
        internal static TrayController Current;

        private readonly Store _store;

        private NotifyIcon _notify;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _miOpen;
        private ToolStripMenuItem _miToday;
        private ToolStripMenuItem _miAuto;
        private ToolStripMenuItem _miExit;

        private Form _main;
        private bool _exiting;
        private bool _disposed;
        private Icon _appIcon;

        /// <summary>自绘图标的 HICON：只有这个句柄归我们所有，Dispose 时才销毁。</summary>
        private IntPtr _ownHicon = IntPtr.Zero;
        private bool _iconFromBranding;
        private TrayMessageWindow _msgWindow;

        private int _taskbarRecoveries;
        private int _visibleEnsures;
        private int _destroyedHicons;

        private string _tooltip = "桌面日历";
        private string _lastBalloon = "";

        // ------------------------------------------------ 注册状态（本轮排障的核心）

        /// <summary>none / notify（NotifyIcon 的槽位确实在托盘里）/ owned（本类自己 NIM_ADD 的槽位）。</summary>
        private string _owner = "none";
        private bool _registrationOk;
        private int _addFailures;
        private int _lastWin32Error;
        private bool _versionSet;
        private IntPtr _iconHwnd = IntPtr.Zero;
        private int _iconId;
        private Guid _iconGuid = Guid.Empty;
        private int _shellAdds;
        private int _shellModifies;
        private int _shellDeletes;
        private int _shellFailures;
        private int _presenceChecks;
        private string _lastFlow = "init";

        // ------------------------------------------------ Win11 托盘图标提升（IsPromoted）

        private const string NotifyIconSettingsKey = @"Control Panel\NotifyIconSettings";

        /// <summary>最近一次提升尝试的结果：already / set:&lt;key&gt; / no-entry / no-key / err:…</summary>
        private string _promoteNote = "none";
        private bool _promoteApplied;
        private bool _promoteReAdding;
        private Timer _promoteTimer;

        private static bool _fallbackNoticeShown;
        private static int _tryAddCalls;
        private static int _noticeCount;
        private static string _lastNoticeText = "";

        /// <summary>测试注入点：非 null 时用它代替 MessageBox（测试里不能弹模态框）。</summary>
        internal static Action<string> NoticeSink = null;

        public event Action ShowMainRequested;
        public event Action ExitRequested;

        public TrayController(Store store)
        {
            _store = store;

            _appIcon = BuildAppIcon();

            _menu = new ContextMenuStrip();
            _menu.Font = Theme.Current.Small != null ? Theme.Current.Small : SystemFonts.MenuFont;
            _menu.ShowImageMargin = false;

            _miOpen = new ToolStripMenuItem("打开日历");
            _miOpen.Font = BoldOf(_menu.Font);
            _miOpen.Click += delegate { RaiseShowMain(); };

            _miToday = new ToolStripMenuItem("今日待办(0)");
            _miToday.Click += delegate { RaiseShowMain(); };

            _miAuto = new ToolStripMenuItem("开机自启");
            _miAuto.CheckOnClick = false;
            _miAuto.Click += AutoStartClicked;

            _miExit = new ToolStripMenuItem("退出");
            _miExit.Click += delegate { RequestExit(); };

            _menu.Items.Add(_miOpen);
            _menu.Items.Add(_miToday);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miAuto);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miExit);

            _menu.Opening += delegate { RefreshMenuState(); };

            // 隐藏窗口必须先建：它是自注册图标的消息接收窗口
            _msgWindow = CreateMessageWindow();

            _notify = new NotifyIcon();
            _notify.Icon = _appIcon;
            _notify.Text = _tooltip;
            _notify.ContextMenuStrip = _menu;
            if (!ForceOwnIconRegistration)
            {
                // 历史通道：NotifyIcon 自己登记一次（成不成功由下面的 RefreshRegistration 复核）
                _notify.Visible = true;
            }
            _notify.DoubleClick += delegate { RaiseShowMain(); };
            _notify.MouseDoubleClick += delegate { RaiseShowMain(); };

            RefreshRegistration("ctor");
            RefreshMenuState();

            Current = this;
            DiagnosticLog.Write("tray: ctor " + DiagnosticLine());
        }

        // ---------------------------------------------------------------- 公开成员

        public NotifyIcon Icon { get { return _notify; } }

        /// <summary>最近一次注册复核的结果：true 表示壳层里确实有这个图标（GetRect 确认过）。</summary>
        public bool RegistrationOk { get { return _registrationOk; } }

        /// <summary>NIM_ADD 的失败次数（成功时不会增长）。</summary>
        public int AddFailures { get { return _addFailures; } }

        /// <summary>最近一次壳层操作带回的 Win32 错误码（0 = 成功）。</summary>
        public int LastWin32Error { get { return _lastWin32Error; } }

        /// <summary>
        /// 确保图标已登记，返回「壳层里确实有这个图标」。幂等：已登记成功时只复核一次，不会重复 NIM_ADD。
        /// MainForm 用它决定能不能隐藏窗口——返回 false 就绝不能 Hide()。
        /// </summary>
        public bool TryAdd()
        {
            _tryAddCalls++;
            RefreshRegistration("TryAdd");
            return _registrationOk;
        }

        /// <summary>一行可直接写日志的诊断：注册是否成功、错误码、exe 路径、explorer 是否在跑。</summary>
        public string DiagnosticLine()
        {
            StringBuilder sb = new StringBuilder(320);
            sb.Append("ok=").Append(_registrationOk ? "True" : "False");
            sb.Append(" owner=").Append(_owner);
            sb.Append(" flow=").Append(_lastFlow);
            sb.Append(" addFailures=").Append(_addFailures);
            sb.Append(" lastErr=").Append(_lastWin32Error);
            sb.Append(" presenceChecks=").Append(_presenceChecks);
            sb.Append(" shellAdd=").Append(_shellAdds);
            sb.Append(" shellMod=").Append(_shellModifies);
            sb.Append(" shellDel=").Append(_shellDeletes);
            sb.Append(" shellFail=").Append(_shellFailures);
            sb.Append(" version=").Append(_versionSet ? NOTIFYICON_VERSION_4 : 0);
            sb.Append(" hwnd=0x").Append(_iconHwnd.ToInt64().ToString("X"));
            sb.Append(" uID=").Append(_iconId);
            sb.Append(" exe=").Append(ExecutablePath());

            string stamp = "";
            try
            {
                DateTime w = System.IO.File.GetLastWriteTime(ExecutablePath());
                stamp = w.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch (Exception) { }
            if (stamp.Length != 0) sb.Append(" exeStamp=").Append(stamp);

            sb.Append(" promote=").Append(_promoteNote);
            sb.Append(" session=").Append(CurrentSessionId());
            sb.Append(" explorer=").Append(DescribeExplorer());
            return sb.ToString();
        }

        /// <summary>主窗口关闭按钮 = 收起到托盘；真正退出时才放行。</summary>
        public void Bind(Form main)
        {
            if (_main != null)
            {
                try { _main.FormClosing -= MainFormClosing; } catch (Exception) { }
                try { _main.VisibleChanged -= MainVisibleChanged; } catch (Exception) { }
                _main = null;
            }

            _main = main;
            if (_main == null) return;
            _main.FormClosing += MainFormClosing;
            _main.VisibleChanged += MainVisibleChanged;
        }

        /// <summary>标记为「真正退出」，之后主窗口的关闭不再被拦截。</summary>
        public void PrepareExit()
        {
            _exiting = true;
        }

        /// <summary>把托盘图标重设一遍（幂等）：可见性掉了或图标句柄丢了都靠它救回来。</summary>
        public void EnsureVisible()
        {
            if (_disposed || _notify == null) return;
            try
            {
                if (_appIcon != null && (_notify.Icon == null || _notify.Icon.Handle != _appIcon.Handle))
                {
                    _notify.Icon = _appIcon;
                }
                if (_notify.Text != _tooltip) _notify.Text = _tooltip;
                if (!ForceOwnIconRegistration && !_notify.Visible) _notify.Visible = true;
                RefreshRegistration("EnsureVisible");
                _visibleEnsures++;
            }
            catch (Exception) { }
        }

        public void ShowBalloon(string title, string text)
        {
            if (_notify == null) return;
            string body = text == null ? "" : text;
            if (body.Length > 250) body = body.Substring(0, 250);
            _lastBalloon = body;

            if (!_registrationOk)
            {
                DiagnosticLog.Write("tray: 气泡跳过（托盘未注册）title=" + SafeText(title));
                return;
            }

            bool ok = false;
            int err = 0;
            _shellModifies++;
            try
            {
                ok = Shell.ShowInfo(_iconHwnd, _iconId, _iconGuid, title == null ? "桌面日历" : title, body, NIIF_INFO, out err);
            }
            catch (Exception)
            {
                ok = false;
                err = -3;
            }

            if (!ok)
            {
                _shellFailures++;
                _lastWin32Error = err;
                DiagnosticLog.Write("tray: NIM_MODIFY(NIF_INFO) 失败 err=" + err);
            }
        }

        public void SetTooltip(string text)
        {
            string t = text == null ? "" : text.Trim();
            if (t.Length == 0) t = "桌面日历";
            if (t.Length > TOOLTIP_MAX) t = t.Substring(0, TOOLTIP_MAX);
            _tooltip = t;
            if (_notify != null) _notify.Text = t;
            PushTooltipToShell();
        }

        /// <summary>注册失败时给用户的唯一一次提示：先试气泡，气泡发不出去才弹一次对话框。</summary>
        internal void NoticeFallbackToTaskbar()
        {
            string brief = "托盘图标注册失败（错误码 " + _lastWin32Error + "），已改为最小化到任务栏。";
            if (_registrationOk)
            {
                // 图标其实还在（只是复核慢了一步）：用气泡说一声，不打扰
                ShowBalloon("桌面日历", brief);
                return;
            }
            if (_fallbackNoticeShown) return;
            _fallbackNoticeShown = true;
            _noticeCount++;
            string msg = brief + "\r\n\r\n请把下面这行信息反馈给开发者：\r\n" + DiagnosticLine();
            _lastNoticeText = msg;
            DiagnosticLog.Write("tray: 兜底提示（整进程只弹一次）" + brief + " | " + DiagnosticLine());
            if (NoticeSink != null)
            {
                try { NoticeSink(msg); }
                catch (Exception) { }
                return;
            }
            try
            {
                MessageBox.Show(msg, "桌面日历", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception) { }
        }

        // ---------------------------------------------------------------- 测试钩子

        internal static int TaskbarCreatedMessageId { get { return WmTaskbarCreated; } }
        internal IntPtr OwnedHicon { get { return _ownHicon; } }
        internal bool IconFromBranding { get { return _iconFromBranding; } }
        internal int TaskbarRecoveries { get { return _taskbarRecoveries; } }
        internal int VisibleEnsures { get { return _visibleEnsures; } }
        internal int DestroyedHicons { get { return _destroyedHicons; } }
        internal IntPtr MessageWindowHandle { get { return _msgWindow == null ? IntPtr.Zero : _msgWindow.Handle; } }
        internal string RegistrationOwner { get { return _owner; } }
        internal int ShellAdds { get { return _shellAdds; } }
        internal int PresenceChecks { get { return _presenceChecks; } }
        internal bool VersionSet { get { return _versionSet; } }
        internal IntPtr IconSlotHwnd { get { return _iconHwnd; } }
        internal int IconSlotId { get { return _iconId; } }
        internal Guid IconSlotGuid { get { return _iconGuid; } }
        internal static Guid OwnSlotGuid { get { return TrayGuid; } }
        internal static int TryAddCalls { get { return _tryAddCalls; } }
        internal static int NoticeCount { get { return _noticeCount; } }
        internal static string LastNoticeText { get { return _lastNoticeText; } }

        /// <summary>测试钩子：把「只弹一次」的静态状态清干净，让每个用例互不影响。</summary>
        internal static void ResetNoticeForTests()
        {
            _fallbackNoticeShown = false;
            _noticeCount = 0;
            _lastNoticeText = "";
            _tryAddCalls = 0;
        }

        /// <summary>测试钩子：直接问壳层某个槽位是否真的在托盘里。</summary>
        internal bool QueryIconSlot(IntPtr hwnd, int id, Guid guid)
        {
            return IconSlotPresent(hwnd, id, guid);
        }

        // ---------------------------------------------------------------- 注册与复核

        /// <summary>
        /// 复核托盘注册状态：先问壳层 NotifyIcon 的槽位在不在，不在就自己 NIM_ADD 一个。
        /// 所有壳层调用的返回值都检查（不再有静默失败）。
        /// </summary>
        private void RefreshRegistration(string flow)
        {
            if (_disposed || _notify == null) return;
            _lastFlow = flow == null ? "" : flow;

            IntPtr notifyHwnd;
            int notifyId;
            bool haveNotifyId = TryReadNotifyIconIdentity(out notifyHwnd, out notifyId);
            bool notifyPresent = haveNotifyId && IconSlotPresent(notifyHwnd, notifyId, Guid.Empty);

            if (!ForceOwnIconRegistration && notifyPresent)
            {
                if (_owner == "owned") RemoveOwnIcon("notify-recovered");
                _owner = "notify";
                _iconHwnd = notifyHwnd;
                _iconId = notifyId;
                _iconGuid = Guid.Empty;
                _registrationOk = true;
                PromoteNotifyIcon();
                return;
            }

            if (_owner == "owned" && _msgWindow != null &&
                IconSlotPresent(_msgWindow.Handle, 1, TrayGuid))
            {
                _iconHwnd = _msgWindow.Handle;
                _iconId = 1;
                _iconGuid = TrayGuid;
                _registrationOk = true;
                return;
            }

            if (_owner == "owned")
            {
                // 上一轮自注册的槽位没了（explorer 重启 / 被清理），重新登记
                AddOwnIcon(flow);
                return;
            }

            AddOwnIcon(flow);
        }

        /// <summary>
        /// NotifyIcon 的槽位在托盘里，但 Win11 会把它丢进「隐藏的图标」溢出区——用户看不见。
        /// 把 IsPromoted 置 1 后让 NotifyIcon 重登记一次，壳层才会按新设置重建图标。
        /// </summary>
        private void PromoteNotifyIcon()
        {
            if (!TryPromoteTrayIcon() || _notify == null) return;
            try
            {
                _notify.Visible = false;
                _notify.Visible = true;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write("tray: 提升后重登记失败 " + SafeText(ex.Message));
            }
        }

        /// <summary>自己登记托盘图标：NIM_ADD + NIM_SETVERSION + GetRect 复核，全部检查返回值。</summary>
        private bool AddOwnIcon(string flow)
        {
            if (_msgWindow == null || _msgWindow.Handle == IntPtr.Zero)
            {
                _registrationOk = false;
                _owner = "none";
                DiagnosticLog.Write("tray: 自注册放弃——没有消息窗口 flow=" + flow);
                return false;
            }

            IntPtr hwnd = _msgWindow.Handle;
            IntPtr hicon = _appIcon == null ? IntPtr.Zero : _appIcon.Handle;

            bool added = false;
            int err = 0;
            _shellAdds++;
            try
            {
                added = Shell.AddIcon(hwnd, 1, TrayGuid, hicon, _tooltip, WM_TRAYICON, out err);
            }
            catch (Exception)
            {
                added = false;
                err = -3;
            }

            if (!added)
            {
                _addFailures++;
                _shellFailures++;
                _lastWin32Error = err;
                _owner = "none";
                _registrationOk = false;
                DiagnosticLog.Write(string.Format(
                    "tray: NIM_ADD 失败 flow={0} err={1} {2}", flow, _lastWin32Error, DiagnosticLine()));
                return false;
            }

            bool versioned = false;
            int verr = 0;
            try
            {
                versioned = Shell.SetVersion(hwnd, 1, TrayGuid, NOTIFYICON_VERSION_4, out verr);
            }
            catch (Exception)
            {
                versioned = false;
                verr = -3;
            }
            _versionSet = versioned;
            if (!versioned)
            {
                _shellFailures++;
                _lastWin32Error = verr;
            }

            // NIM_ADD 说成功不等于壳层真收下了，再用 GetRect 问一次
            bool present = IconSlotPresent(hwnd, 1, TrayGuid);
            _owner = present ? "owned" : "none";
            _registrationOk = present;

            DiagnosticLog.Write(string.Format(
                "tray: NIM_ADD 成功 flow={0} add=True setver={1} present={2} err={3} {4}",
                flow, versioned, present, _lastWin32Error, DiagnosticLine()));

            // Win11 会把这个新图标丢进「隐藏的图标」溢出区：改完 IsPromoted 要重登记一次才生效
            if (present && TryPromoteTrayIcon())
            {
                return ReaddOwnIcon(flow);
            }
            if (present) SchedulePromoteRetry();
            return present;
        }

        /// <summary>
        /// 把本程序的托盘图标提到任务栏可见区。Windows 11 把新注册的通知图标默认放进溢出区（IsPromoted 为空），
        /// 用户点最小化后根本找不到图标。条目在 HKCU\Control Panel\NotifyIconSettings\&lt;hash&gt;，
        /// 按 ExecutablePath 的文件名匹配自己的那条，把 IsPromoted 置 1。
        /// 返回 true 表示这次真改了值——壳层只在 NIM_ADD 时读它，所以调用方需要删掉再登记。
        /// </summary>
        private bool TryPromoteTrayIcon()
        {
            if (_promoteApplied) return false;

            string exeName = "";
            try { exeName = System.IO.Path.GetFileName(ExecutablePath()); }
            catch (Exception) { }
            if (exeName.Length == 0)
            {
                _promoteNote = "no-exe";
                return false;
            }

            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(NotifyIconSettingsKey, true))
                {
                    if (root == null)
                    {
                        _promoteNote = "no-key";
                        return false;
                    }

                    string[] subs = root.GetSubKeyNames();
                    int matched = 0;
                    for (int i = 0; i < subs.Length; i++)
                    {
                        using (RegistryKey k = root.OpenSubKey(subs[i], true))
                        {
                            if (k == null) continue;
                            string path = k.GetValue("ExecutablePath") as string;
                            if (path == null || path.Length == 0) continue;

                            string name = "";
                            try { name = System.IO.Path.GetFileName(path); }
                            catch (Exception) { }
                            if (!string.Equals(name, exeName, StringComparison.OrdinalIgnoreCase)) continue;

                            matched++;
                            if (ReadDword(k, "IsPromoted") == 1)
                            {
                                _promoteNote = "already";
                                continue;
                            }

                            k.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                            _promoteApplied = true;
                            _promoteNote = "set:" + subs[i];
                        }
                    }

                    if (matched == 0 && !_promoteApplied) _promoteNote = "no-entry";
                }
            }
            catch (Exception ex)
            {
                _promoteNote = "err:" + SafeText(ex.Message);
            }

            if (_promoteApplied)
            {
                DiagnosticLog.Write("tray: 托盘图标提升 note=" + _promoteNote);
            }
            return _promoteApplied;
        }

        private static int ReadDword(RegistryKey k, string name)
        {
            object v = k.GetValue(name);
            if (v is int) return (int)v;
            if (v is long) return (int)(long)v;
            return 0;
        }

        /// <summary>改完 IsPromoted 后重登记一次：NIM_DELETE + NIM_ADD，让壳层按新设置重建图标。</summary>
        private bool ReaddOwnIcon(string flow)
        {
            if (_promoteReAdding) return _registrationOk;
            _promoteReAdding = true;
            try
            {
                RemoveOwnIcon(flow + "+promote");
                return AddOwnIcon(flow + "+promote");
            }
            finally
            {
                _promoteReAdding = false;
            }
        }

        /// <summary>
        /// 首次启动时 explorer 要过一会儿才会把这条 NotifyIconSettings 落盘，2.5 秒后补一次。
        /// 只补一次；失败只写日志，不再纠缠。
        /// </summary>
        private void SchedulePromoteRetry()
        {
            if (_promoteApplied || _promoteTimer != null || _disposed) return;
            try
            {
                _promoteTimer = new Timer();
                _promoteTimer.Interval = 2500;
                _promoteTimer.Tick += delegate
                {
                    StopPromoteTimer();
                    if (_disposed) return;
                    if (TryPromoteTrayIcon() && _registrationOk)
                    {
                        ReaddOwnIcon("promote-retry");
                    }
                    else
                    {
                        DiagnosticLog.Write("tray: 图标提升未生效 note=" + _promoteNote);
                    }
                };
                _promoteTimer.Start();
            }
            catch (Exception)
            {
                _promoteTimer = null;
            }
        }

        private void StopPromoteTimer()
        {
            if (_promoteTimer == null) return;
            try
            {
                _promoteTimer.Stop();
                _promoteTimer.Dispose();
            }
            catch (Exception) { }
            _promoteTimer = null;
        }

        private void RemoveOwnIcon(string flow)
        {
            if (_msgWindow == null || _msgWindow.Handle == IntPtr.Zero) return;
            try
            {
                int derr = 0;
                _shellDeletes++;
                bool ok = Shell.DeleteIcon(_msgWindow.Handle, 1, TrayGuid, out derr);
                if (!ok)
                {
                    _shellFailures++;
                    _lastWin32Error = derr;
                    DiagnosticLog.Write("tray: NIM_DELETE 失败 flow=" + flow + " err=" + derr);
                }
            }
            catch (Exception) { }
            _owner = "none";
        }

        /// <summary>问壳层：这个槽位的图标真的在托盘里吗。hr == S_OK 才返回 true。</summary>
        private bool IconSlotPresent(IntPtr hwnd, int id, Guid guid)
        {
            if (hwnd == IntPtr.Zero && guid == Guid.Empty) return false;
            _presenceChecks++;
            try
            {
                return Shell.IsPresent(hwnd, id, guid);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>读 NotifyIcon 自己的隐藏窗口句柄与 uID（GetRect 需要用它问「图标在不在」）。</summary>
        private bool TryReadNotifyIconIdentity(out IntPtr hwnd, out int id)
        {
            hwnd = IntPtr.Zero;
            id = 0;
            if (_notify == null) return false;
            try
            {
                Type t = typeof(NotifyIcon);
                FieldInfo fw = t.GetField("window", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo fi = t.GetField("id", BindingFlags.Instance | BindingFlags.NonPublic);
                if (fw == null || fi == null) return false;

                NativeWindow w = fw.GetValue(_notify) as NativeWindow;
                if (w == null) return false;
                IntPtr h = w.Handle;
                if (h == IntPtr.Zero) return false;

                object o = fi.GetValue(_notify);
                if (!(o is int)) return false;

                hwnd = h;
                id = (int)o;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static NOTIFYICONDATAW BuildData(IntPtr hwnd, int id, Guid guid, int flags, IntPtr hicon, string tip, int callbackMessage)
        {
            NOTIFYICONDATAW d = new NOTIFYICONDATAW();
            d.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATAW));
            d.hWnd = hwnd;
            d.uID = id;
            d.uFlags = flags;
            d.uCallbackMessage = callbackMessage;
            d.hIcon = hicon;
            d.szTip = Truncate(tip, TOOLTIP_MAX);
            d.guidItem = guid;
            d.uVersion = NOTIFYICON_VERSION_4;
            return d;
        }

        private void PushTooltipToShell()
        {
            if (_disposed || !_registrationOk) return;
            try
            {
                int terr = 0;
                _shellModifies++;
                if (!Shell.SetTip(_iconHwnd, _iconId, _iconGuid, _tooltip, out terr))
                {
                    _shellFailures++;
                    _lastWin32Error = terr;
                    DiagnosticLog.Write("tray: NIM_MODIFY(NIF_TIP) 失败 err=" + terr);
                }
            }
            catch (Exception) { }
        }

        // ---------------------------------------------------------------- 内部

        private static string Truncate(string s, int max)
        {
            if (s == null) return "";
            if (max <= 0) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max);
        }

        private static string SafeText(string s)
        {
            return s == null ? "" : s;
        }

        private static string ExecutablePath()
        {
            try
            {
                string p = Application.ExecutablePath;
                return p == null ? "" : p;
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static int CurrentSessionId()
        {
            try
            {
                using (Process p = Process.GetCurrentProcess())
                {
                    return p.SessionId;
                }
            }
            catch (Exception)
            {
                return -1;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string className, string windowName);

        private static string DescribeExplorer()
        {
            try
            {
                Process[] ps = Process.GetProcessesByName("explorer");
                if (ps == null || ps.Length == 0)
                {
                    // 进程列表拿不到（受限环境）时，任务栏窗口还在就说明 explorer 活着
                    IntPtr tray = FindWindow("Shell_TrayWnd", null);
                    if (tray != IntPtr.Zero)
                    {
                        return "tray-window=0x" + tray.ToInt64().ToString("X") + "(pid-list-empty)";
                    }
                    return "not-running";
                }
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < ps.Length; i++)
                {
                    if (i > 0) sb.Append('+');
                    sb.Append("pid=").Append(ps[i].Id).Append("/s").Append(ps[i].SessionId);
                    try { ps[i].Dispose(); }
                    catch (Exception) { }
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "unknown(" + ex.GetType().Name + ")";
            }
        }

        private static Font BoldOf(Font f)
        {
            if (f == null) return null;
            try { return new Font(f, FontStyle.Bold); }
            catch (Exception) { return f; }
        }

        private void RaiseShowMain()
        {
            Action handler = ShowMainRequested;
            if (handler != null)
            {
                try { handler(); }
                catch (Exception) { }
            }
            else if (_main != null)
            {
                _main.Show();
                _main.WindowState = FormWindowState.Normal;
                _main.Activate();
            }
        }

        private void RaiseExit()
        {
            Action handler = ExitRequested;
            if (handler != null)
            {
                try { handler(); }
                catch (Exception) { }
                return;
            }
            Application.Exit();
        }

        private void RequestExit()
        {
            _exiting = true;
            RaiseExit();
        }

        private void AutoStartClicked(object sender, EventArgs e)
        {
            string error = null;
            bool ok;
            if (AutoStart.IsEnabled)
            {
                ok = AutoStart.TryDisable(out error);
            }
            else
            {
                ok = AutoStart.TryEnable(out error);
            }

            if (!ok)
            {
                string msg = string.IsNullOrEmpty(error) ? "无法修改开机自启设置" : error;
                ShowBalloon("开机自启", msg);
            }
            else
            {
                ShowBalloon("开机自启", AutoStart.IsEnabled ? "已开启，登录 Windows 后静默启动" : "已关闭");
            }
            RefreshMenuState();
        }

        private void RefreshMenuState()
        {
            if (_miAuto != null)
            {
                bool on = false;
                try { on = AutoStart.IsEnabled; }
                catch (Exception) { }
                _miAuto.Checked = on;
            }

            int total = 0;
            int done = 0;
            if (_store != null)
            {
                try
                {
                    total = _store.TotalForDay(DateTime.Today);
                    done = _store.DoneForDay(DateTime.Today);
                }
                catch (Exception) { }
            }

            if (_miToday != null)
            {
                _miToday.Text = string.Format("今日待办({0})", total);
            }

            SetTooltip(string.Format("桌面日历 · 今日 {0}/{1} 已完成", done, total));
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_exiting) return;
            if (e.CloseReason == CloseReason.WindowsShutDown) return;
            if (e.CloseReason == CloseReason.TaskManagerClosing) return;
            if (e.CloseReason == CloseReason.ApplicationExitCall) return;

            e.Cancel = true;
            if (_main != null) _main.Hide();
        }

        /// <summary>主窗口被隐藏（收起到托盘）时，把托盘图标重新确认一遍。</summary>
        private void MainVisibleChanged(object sender, EventArgs e)
        {
            if (_disposed) return;
            Form f = _main;
            if (f == null) return;
            if (f.Visible) return;
            EnsureVisible();
        }

        /// <summary>explorer 重建了任务栏：壳层那边的图标已经没了，重新登记并复核。</summary>
        private void OnTaskbarCreated()
        {
            if (_disposed || _notify == null) return;
            try
            {
                if (!ForceOwnIconRegistration)
                {
                    // NotifyIcon.Visible 的 setter 在值没变时是空操作，所以必须先置 false 再置 true
                    if (_notify.Visible) _notify.Visible = false;
                    _notify.Icon = _appIcon;
                    _notify.Text = _tooltip;
                    _notify.ContextMenuStrip = _menu;
                    _notify.Visible = true;
                }
                _owner = "none";
                RefreshRegistration("TaskbarCreated");
                _taskbarRecoveries++;
                DiagnosticLog.Write("tray: TaskbarCreated 恢复 " + DiagnosticLine());
            }
            catch (Exception) { }
        }

        /// <summary>自注册图标被点击（NOTIFYICON_VERSION_4：LOWORD(lParam) 是鼠标消息）。</summary>
        private void OnTrayMessage(int code)
        {
            if (_disposed) return;
            if (code == WM_LBUTTONUP || code == WM_LBUTTONDBLCLK)
            {
                RaiseShowMain();
                return;
            }
            if (code == WM_RBUTTONUP || code == WM_CONTEXTMENU)
            {
                ShowMenuAtCursor();
            }
        }

        private void ShowMenuAtCursor()
        {
            if (_menu == null) return;
            try
            {
                RefreshMenuState();
                _menu.Show(Cursor.Position);
            }
            catch (Exception) { }
        }

        private TrayMessageWindow CreateMessageWindow()
        {
            if (WmTaskbarCreated == 0) return null;
            try { return new TrayMessageWindow(this); }
            catch (Exception) { return null; }
        }

        // ---------------------------------------------------------------- 图标

        /// <summary>
        /// 优先用 exe 自带图标（Branding.AppIcon，32×32、进程级单例、句柄不归我们销毁）；
        /// 拿不到才回落到 Branding.TrayBitmap，最后才用代码绘制。
        /// </summary>
        private Icon BuildAppIcon()
        {
            if (!ForceSelfDrawnIcon)
            {
                try
                {
                    Icon brand = Branding.AppIcon;
                    if (brand != null && brand.Handle != IntPtr.Zero)
                    {
                        _iconFromBranding = true;
                        return brand;
                    }
                }
                catch (Exception) { }

                try
                {
                    Bitmap tray = Branding.TrayBitmap;
                    if (tray != null)
                    {
                        Icon fromBrand = IconFromBitmapOwned(tray);
                        if (fromBrand != null) return fromBrand;
                    }
                }
                catch (Exception) { }
            }

            return BuildIcon();
        }

        /// <summary>
        /// 位图转 Icon：HICON 由本对象持有（存进 _ownHicon），只到 Dispose 才 DestroyIcon。
        /// 以前的写法是 clone 之后立刻 DestroyIcon(h)，托盘手里那份句柄就悬空了。
        /// </summary>
        private Icon IconFromBitmapOwned(Bitmap bmp)
        {
            if (bmp == null) return null;
            IntPtr h = IntPtr.Zero;
            try
            {
                h = bmp.GetHicon();
                // Icon.FromHandle 得到的 Icon 不拥有句柄，句柄生命期由我们管
                // （成员属性 `Icon` 遮蔽了类型名，所以这里必须写全 System.Drawing.Icon）
                System.Drawing.Icon icon = System.Drawing.Icon.FromHandle(h);
                _ownHicon = h;
                return icon;
            }
            catch (Exception)
            {
                if (h != IntPtr.Zero)
                {
                    try { DestroyIcon(h); }
                    catch (Exception) { }
                }
                return null;
            }
        }

        private Icon BuildIcon()
        {
            Color baseColor = Theme.Current.Accent;
            using (Bitmap bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                    Draw.FillRound(g, new Rectangle(0, 0, 32, 32), 8, baseColor);

                    // 白色日历轮廓：表头 + 两枚挂环
                    Draw.FillRound(g, new Rectangle(6, 10, 20, 18), 4, Color.White);
                    using (SolidBrush b = new SolidBrush(baseColor))
                    {
                        g.FillRectangle(b, 8, 14, 16, 2);
                    }
                    Draw.FillRound(g, new Rectangle(10, 6, 4, 6), 2, Color.White);
                    Draw.FillRound(g, new Rectangle(18, 6, 4, 6), 2, Color.White);
                }

                Icon drawn = IconFromBitmapOwned(bmp);
                if (drawn != null) return drawn;
                return SystemIcons.Application;
            }
        }

        // ---------------------------------------------------------------- 隐藏窗口

        /// <summary>
        /// 隐藏顶层窗口：接 TaskbarCreated（广播消息不会送到 message-only 窗口），
        /// 同时作为本类自注册托盘图标的回调窗口。
        /// </summary>
        private sealed class TrayMessageWindow : NativeWindow, IDisposable
        {
            private readonly TrayController _owner;
            private bool _disposed;

            public TrayMessageWindow(TrayController owner)
            {
                _owner = owner;
                CreateParams cp = new CreateParams();
                cp.Caption = "DesktopCalendar.TrayController";
                cp.Style = 0;
                cp.ExStyle = 0;
                cp.ClassStyle = 0;
                CreateHandle(cp);
            }

            protected override void WndProc(ref Message m)
            {
                TrayController owner = _owner;
                if (owner != null)
                {
                    if (WmTaskbarCreated != 0 && m.Msg == WmTaskbarCreated)
                    {
                        owner.OnTaskbarCreated();
                    }
                    else if (m.Msg == WM_TRAYICON)
                    {
                        int raw = m.LParam.ToInt32();
                        owner.OnTrayMessage((int)(raw & 0xFFFF));
                    }
                }
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try { DestroyHandle(); }
                catch (Exception) { }
            }
        }

        // ---------------------------------------------------------------- 释放

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (_main != null)
            {
                try { _main.FormClosing -= MainFormClosing; }
                catch (Exception) { }
                try { _main.VisibleChanged -= MainVisibleChanged; }
                catch (Exception) { }
                _main = null;
            }

            StopPromoteTimer();

            // 自注册的图标必须先 NIM_DELETE 掉，再销毁它的回调窗口
            if (_owner == "owned") RemoveOwnIcon("dispose");

            if (_msgWindow != null)
            {
                try { _msgWindow.Dispose(); }
                catch (Exception) { }
                _msgWindow = null;
            }

            if (_notify != null)
            {
                try
                {
                    _notify.Visible = false;
                    _notify.ContextMenuStrip = null;
                    _notify.Dispose();
                }
                catch (Exception) { }
                _notify = null;
            }

            if (_menu != null)
            {
                try { _menu.Dispose(); }
                catch (Exception) { }
                _menu = null;
            }

            // 自绘图标的句柄在这里才销毁（而且只销毁一次）；
            // Branding.AppIcon / SystemIcons.Application 都不归我们所有，不能 Dispose
            if (_ownHicon != IntPtr.Zero && _appIcon != null)
            {
                try { _appIcon.Dispose(); }
                catch (Exception) { }
            }
            _appIcon = null;

            if (_ownHicon != IntPtr.Zero)
            {
                IntPtr h = _ownHicon;
                _ownHicon = IntPtr.Zero;
                try
                {
                    DestroyIcon(h);
                    _destroyedHicons++;
                }
                catch (Exception) { }
            }

            if (Current == this) Current = null;
            _registrationOk = false;
            _owner = "none";
        }
    }
}
