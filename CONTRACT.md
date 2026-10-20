# desktop-calendar 契约（冻结，所有子代理必须遵守）

## 产品定义
Windows 桌面日历 / 日程提醒软件，中文界面，单文件 exe，免安装。
功能范围（用户明确"目前只做简单功能"）：月历浏览 → 写入日程（单次 / 每天 / 每周 / 每月重复）
→ 到点右下角提醒卡片 + 托盘闪烁 → 勾选完成、稍后提醒、按天统计完成率 → 开机静默自启。

## 硬约束（违反即返工）
1. **目标框架 .NET Framework 4.8**，只用系统自带编译器：
   `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
2. **语言级别只有 C# 5**。本机 csc 的 `/langversion` 仅支持 ISO-1/ISO-2/3/4/5。
   禁止：字符串插值 `$"..."`、`?.`、`?:` 空合并配合 ?. 、`nameof`、自动属性初始化器
   `public int X { get; set; } = 1;`、表达式体成员 `=>`、`using static`、`out var`、元组、`ThrowIfNull`。
   允许：`var`、LINQ、lambda、`async/await`、对象初始化器、`string.Format`、匿名类型、`dynamic`。
3. **零第三方依赖**，不联网、不用 NuGet。JSON 序列化自己实现（不要用 System.Text.Json）。
4. **源文件含中文**，编译时必须带 `/codepage:65001`，文件存 UTF-8。
5. 不执行任何 git 命令（发布由调度者统一做）。
6. 命名空间根 `DesktopCalendar`；四个子命名空间 `DesktopCalendar.Models|Data|Services|UI`。
7. 禁止留下 `_scratch/` 以外的临时代码；`_scratch/` 里的文件仅用于局部编译自检，绝不进入最终构建。

## 视觉规范（统一由 Theme 提供，不得各写一套颜色）
主题支持明/暗两套，运行时可切换，默认跟随明暗：默认浅色。
| Token | Light | Dark |
|---|---|---|
| Bg（窗口底） | #F4F6FB | #12141A |
| Surface（卡片） | #FFFFFF | #1B1E26 |
| SurfaceAlt（次级块） | #EEF2F9 | #23272F |
| TextPrimary | #1B2233 | #E9EDF6 |
| TextSecondary | #5B6478 | #9AA4B8 |
| TextMuted | #98A2B3 | #6B7488 |
| Border | #E3E8F2 | #2C313B |
| Accent | #4F7CFF | #6E92FF |
| AccentSoft | #E8EEFF | #232E4D |
| Danger | #EF4444 | #F87171 |
| Success | #22C55E | #34D399 |
| TodayBg | #E8EEFF | #23304F |
分类色板 6 色：`#4F7CFF #22C55E #F59E0B #EF4444 #8B5CF6 #06B6D4`
字体：Title = `Microsoft YaHei UI` 17pt Bold；Body 10.5pt；Small 9pt。全部 `TextRenderingHint.ClearTypeGridFit`。
圆角：卡片 12px，小控件 8px，按钮 10px。一律抗锯齿 + 半像素对齐。
DPI：进程启动即 `SetProcessDPIAware()`。

## 文件与类型契约（签名固定；可新增成员，不得改已有签名）
```
src/Models/Schedule.cs      namespace DesktopCalendar.Models
  public enum RepeatKind { None = 0, Daily = 1, Weekly = 2, Monthly = 3 }
  public class ScheduleItem {
      public string Id;                 // Guid.NewGuid().ToString("N")
      public string Title;              // 必填
      public string Notes;
      public DateTime Date;             // 起始日期，只用 .Date
      public int Hour; public int Minute;
      public RepeatKind Repeat;
      public DateTime? RepeatUntil;     // null = 永久重复
      public bool Enabled;
      public string Color;              // 分类色，来自 Theme.Palette
      public DateTime CreatedAt;
      public DateTime? LastFiredAt;     // 最近一次已提醒的 occurrence 起点
      public DateTime? SnoozeUntil;     // 稍后提醒目标时刻
      public DateTime? DoneOccurrence;  // 已完成的 occurrence 起点（单次即 Date）
      public bool IsDoneOn(DateTime occurrenceStart);
  }

src/Models/RepeatRule.cs    namespace DesktopCalendar.Models
  public static class RepeatRule {
      public static bool OccursOn(ScheduleItem it, DateTime day);                    // day 只看 .Date
      public static DateTime OccurrenceStart(ScheduleItem it, DateTime day);         // day.Date + Hour:Minute
      public static IEnumerable<DateTime> OccurrencesBetween(ScheduleItem it, DateTime fromInclusive, DateTime toInclusive);
      public static DateTime? NextOccurrence(ScheduleItem it, DateTime afterExclusive);
      public static string Describe(ScheduleItem it);   // "单次" / "每天" / "每周一" / "每月 15 日"
  }
  规则：Daily = Date 起每天；Weekly = 与 Date 同星期；Monthly = 每月同"日"，遇该月没有此日（如 31 号）取当月最后一天；RepeatUntil 含当天，超出则不再发生。

src/Data/Json.cs            namespace DesktopCalendar.Data
  public static class Json {
      public static string Write(object value);              // 支持 Dictionary<string,object>/List<object>/string/bool/int/long/double/DateTime?/null
      public static Dictionary<string,object> ReadObject(string text);   // 单对象
      public static List<Dictionary<string,object>> ReadObjectArray(string text);
      public static string Str(Dictionary<string,object> o, string k, string def = null);
      public static int Int(Dictionary<string,object> o, string k, int def = 0);
      public static bool Bool(Dictionary<string,object> o, string k, bool def = false);
      public static DateTime? Date(Dictionary<string,object> o, string k);
      public static string Esc(string s);
      // 日期统一用 ISO "yyyy-MM-ddTHH:mm:ss"；null 写成 null
  }

src/Data/Store.cs           namespace DesktopCalendar.Data
  public class Store {
      public Store(string dataDir = null);      // 默认 %APPDATA%\DesktopCalendar
      public string DataFilePath { get; }
      public List<ScheduleItem> Items { get; }
      public void Load();
      public void Save();                        // 原子写：临时文件 + File.Replace/覆盖，异常不外抛（记入 LastError）
      public string LastError { get; }
      public void Add(ScheduleItem it);
      public void Update(ScheduleItem it);       // 按 Id 替换并 Save
      public void Remove(string id);
      public ScheduleItem Find(string id);
      public List<ScheduleItem> ForDay(DateTime day);     // 当天所有 occurrence，按时间升序
      public int TotalForDay(DateTime day);
      public int DoneForDay(DateTime day);
  }

src/Services/AutoStart.cs   namespace DesktopCalendar.Services
  public static class AutoStart {
      public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
      public const string ValueName = "DesktopCalendar";
      public static bool IsEnabled { get; }
      public static bool TryEnable(out string error);    // 写 "exe路径" --silent
      public static bool TryDisable(out string error);
      public static string ExecutablePath { get; }       // Assembly.GetEntryAssembly().Location
  }

src/Services/Scheduler.cs   namespace DesktopCalendar.Services
  public class DueReminder {
      public ScheduleItem Item; public DateTime Occurrence;
      public string Kind;   // "due" | "snooze" | "overdue"（逾期：occurrence 已过 30 分钟仍未完成）
  }
  public class Scheduler {
      public Scheduler(Store store);
      public event Action<List<DueReminder>> RemindersDue;   // 每轮只报"新"到期项
      public void Start();  public void Stop();              // 30 秒轮询
      public List<DueReminder> Poll(DateTime now);           // 可测试入口：返回本轮应弹的提醒（内部去重靠 Item.LastFiredAt/SnoozeUntil）
      public List<DueReminder> OverdueToday(DateTime now);    // 今天已过点且未完成（供状态栏/托盘提示）
      public void Complete(DueReminder d);                   // 置 DoneOccurrence 并 Save
      public void Snooze(DueReminder d, int minutes);        // 置 SnoozeUntil
      public void DismissToday(DueReminder d);               // 置 LastFiredAt 且不再弹
  }

src/UI/Theme.cs             namespace DesktopCalendar.UI
  public class Theme {
      public static Theme Current { get; }
      public static bool IsDark { get; }
      public static void SetDark(bool dark);
      public static void Toggle();
      public Color Bg, Surface, SurfaceAlt, TextPrimary, TextSecondary, TextMuted, Border, Accent, AccentSoft, TodayBg, Danger, Success;
      public Color[] Palette { get; }
      public Font Title, Body, BodyBold, Small, SmallBold, Big;
  }

src/UI/Draw.cs              namespace DesktopCalendar.UI
  public static class Draw {
      public static void Smooth(Graphics g);
      public static GraphicsPath Round(Rectangle r, int radius);
      public static void FillRound(Graphics g, Rectangle r, int radius, Color c);
      public static void StrokeRound(Graphics g, Rectangle r, int radius, Color c, float w = 1f);
      public static void Text(Graphics g, string s, Font f, Color c, Rectangle r, StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Center);
      public static void Ellipsis(Graphics g, string s, Font f, Color c, Rectangle r);
      public static GraphicsPath RoundTop(Rectangle r, int radius);   // 顶部圆角（提醒卡片用）
  }

src/UI/Chrome.cs            namespace DesktopCalendar.UI
  public static class Chrome {
      public static void EnableRounded(Form f);        // Win11 DWM 圆角+阴影，失败静默忽略
      public static void EnableDrag(Control c, Form f); // 无边框窗口拖动（ReleaseCapture + WM_NCLBUTTONDOWN）
      public static void EnableResize(Form f);          // 右下角可拉伸
  }

src/UI/MonthView.cs         namespace DesktopCalendar.UI
  public class MonthView : Control {
      public DateTime Month { get; set; }        // 取 .Date 所在月
      public DateTime SelectedDate { get; set; }
      public Store Store { get; set; }           // 可为 null（设计期）
      public event Action<DateTime> DateSelected;
      public event Action<ScheduleItem> ItemActivated;   // 双击某天的日程
  }
  渲染要求：7 列 × 6 行；周一起始；非本月日期淡显；今天用 TodayBg 圆角块 + Accent 数字；
  选中日用 Surface + Accent 描边；日期下最多显示 2 条日程（色条 + 截断标题），多于 2 条显示 "+n"；
  鼠标悬停格用 SurfaceAlt；滚轮切月；点击选中，双击进入新建。

src/UI/MainForm.cs          namespace DesktopCalendar.UI
  public class MainForm : Form { public MainForm(Store store, Scheduler scheduler); }
  布局：无边框 1000×700，DWM 圆角。左侧 640 宽 MonthView + 月份标题栏（‹ › 今天）；
  右侧 320 宽侧栏：选中日期标题、"共 n 项 / 已完成 m"、日程列表（勾选圈 + 时间 + 标题 + 色条）、"+ 新建日程"按钮；
  底部 44 高状态条：今日进度、开机自启开关、明暗切换、最小化到托盘。

src/UI/ReminderCard.cs      namespace DesktopCalendar.UI
  public class ReminderCard : Form {
      public ReminderCard(DueReminder due, Scheduler scheduler);
      public event Action<ReminderCard> Closed;
      public void ShowAt(Point bottomRightAnchor);
  }
  外观：360×132 无边框置顶 TopMost、不进任务栏、淡入 200ms、圆角、左侧 6px 分类色条、
  标题（加粗）+ 时间 + "今天 / 已逾期" 标签；底部按钮 [完成]（Accent 实心）[+10 分钟] [今天忽略]（描边）。

src/UI/EventEditor.cs       namespace DesktopCalendar.UI
  public class EventEditor : Form {
      public EventEditor(Store store);
      public EventEditor(Store store, ScheduleItem existing);
      public ScheduleItem Result { get; }
  }
  字段：标题（TextBox）、日期（可输 yyyy-MM-dd + 前后日按钮）、时间（HH:mm）、重复（下拉：单次/每天/每周/每月）、
  颜色（6 色圆点选择）、备注（多行）；按钮 [取消][保存]。中文校验提示。

src/UI/TrayController.cs    namespace DesktopCalendar.UI
  public class TrayController : IDisposable {
      public TrayController(Store store);
      public void Bind(Form main);              // 关闭窗口=隐藏到托盘
      public NotifyIcon Icon { get; }
      public void ShowBalloon(string title, string text);
      public void SetTooltip(string text);
      public event Action ShowMainRequested;
      public event Action ExitRequested;
  }
  托盘菜单：打开日历 / 今日待办 / 开机自启（勾选）/ 退出。图标用代码绘制 16×16（不依赖外部 ico 文件）。

src/Program.cs              namespace DesktopCalendar
  单实例 Mutex；--silent 静默启动（不显示主窗口，仅托盘）；Application.EnableVisualStyles；
  Application.SetCompatibleTextRenderingDefault(false)；SetProcessDPIAware；全局未处理异常兜底。
```

## 构建方式（build.ps1 由整合者负责）
显式列出所有 .cs（不得用通配符扫 `_scratch`），命令形如：
```
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
  /out:build\desktop-calendar.exe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  /r:System.Core.dll <所有 src\*.cs 显式子路径>
```

## 验收证据要求
每个子代理必须给出：编译命令原文、编译退出码、自检程序的真实输出（含 PASS/FAIL 行）、
以及（UI 类任务）生成的 PNG 预览文件绝对路径。
