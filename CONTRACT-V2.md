# CONTRACT-V2（变更补丁，冻结于本轮；与 CONTRACT.md 冲突时以本文件为准）

本轮目标：6 项改进。**只追加，不破坏**已有公开面；已有 `data.json` 必须继续可读。
全项目约束不变：C# 5（禁 `?.` / `nameof` / 字符串插值 / 自动属性初始化器 / 表达式体成员）、
`/warn:4` 零警告零错误、颜色只能从 `Theme.Current` 取、不引入任何第三方 dll、不联网。

---

## 1. 逻辑层：每周可指定星期几（T1 拥有，T2/T5 消费）

`RepeatKind` **不新增枚举值**（保持 0..3，持久化格式不变）。改为：

```csharp
// src/Models/Schedule.cs
public class ScheduleItem
{
    ...
    /// <summary>每周重复时生效的星期掩码。bit0=周一 … bit6=周日；0 = 未指定（沿用 Date 的星期）。</summary>
    public int WeekDays;
}
```

- `Repeat == Weekly && WeekDays == 0` → **行为与现在完全一致**（`d.DayOfWeek == start.DayOfWeek`）。
- `Repeat == Weekly && WeekDays != 0` → 命中掩码中任一天就发生（仍受 `d >= start`、`RepeatUntil` 约束）。
- 其它 `RepeatKind` 一律忽略 `WeekDays`。
- 范围恒为 `0..127`，`Normalize()` 必须夹紧。

`RepeatRule` 新增（签名冻结，两边共用，禁止各自内联位运算）：

```csharp
/// <summary>周一=0 … 周日=6 的序号（与 WeekNames 一..日 的展示顺序一致）。</summary>
public static int DayIndex(DayOfWeek dow)          // (int)dow + 6) % 7
public static int MaskOf(DayOfWeek dow)            // 1 << DayIndex(dow)
public static bool MaskHas(int mask, DayOfWeek dow)
/// <summary>"一/三/五" 风格；mask==0 返回 ""；全选返回 "每天"？不——全选返回 "一二三四五六日"。</summary>
public static string MaskText(int mask)
```

`RepeatRule.Describe(ScheduleItem)` 在 `Weekly && WeekDays != 0` 时输出 `"每周" + MaskText(mask)`（例：`每周一、三、五`，用中文顿号「、」连接）。

持久化（`src/Data/Store.cs`）：`ToDict` 增 `o["WeekDays"] = it.WeekDays;`；`FromDict` 读
`it.WeekDays = Json.Int(o, "WeekDays", 0);` 并在 `Normalize()` 里夹到 `0..127`（缺失字段必须得 0，即旧数据语义不变）。
`Clone()` 必须复制 `WeekDays`。

## 2. 主题层：卡片/色块/提示/阴影 token（T3 拥有，T5 消费）

`Theme` 追加字段（名字冻结，明暗两套都必须赋值）：

```csharp
public Color Card;        // 卡片面：浅 #FFFFFF          深 #1A1D25
public Color CardLine;    // 卡片描边：浅 #E6EBF5        深 #262B35
public Color CellBg;      // 本月日期格色块：浅 #F5F7FC   深 #20242D
public Color CellBgFaint; // 非本月日期格色块：浅 #FAFBFE 深 #171A21
public Color HeaderBand;  // 星期行/卡头色带：浅 #EEF3FF  深 #1E2537
public Color TipBg;       // 每日提示卡：浅 #EEF4FF       深 #202A42
public Color TipLine;     // 每日提示卡描边：浅 #DCE7FF   深 #2E3B57
public Color Shadow;      // 阴影基色：浅 #1B2233         深 #000000
```

`AccentSoft` / `TodayBg` 语义不变（不要动，别处依赖）。

## 3. 绘制层：阴影与卡片（T3 拥有，T5 消费）

`Draw` 追加：

```csharp
/// <summary>向外扩散 spread 像素的分层柔和阴影，alpha 为最外层不透明度基准。</summary>
public static void Shadow(Graphics g, Rectangle r, int radius, Color baseColor, int spread, float alpha);
/// <summary>卡片 = 阴影 + 圆角卡片面 + 1px CardLine 描边。内部内容用返回值矩形排版。</summary>
public static Rectangle Card(Graphics g, Rectangle r, int radius, Color face, Color line, Color shadowColor);
/// <summary>纯色块填充（无描边），圆角 radius。</summary>
public static void Block(Graphics g, Rectangle r, int radius, Color c);
```

实现要求：阴影用「多层圆角矩形、由外向内 alpha 递增」叠加，**不得**用 `Bitmap` 模糊（避免每帧分配）；`spread<=0` 时退化为不画。1px 描边沿用现有结论：整数坐标、不内缩 0.5（内缩会发虚）。

## 4. 月历：去描边、改色块（T3 拥有）

`src/UI/MonthView.cs` 视觉变更，**公开 API 一个都不许改**（`SelectedDate` / `Month` / `MonthChanged` / `HoverDate` / `RenderTo` / 构造签名保持）：

- 删除所有 1px 发丝描边（`Blend(Border, Bg, …)` 那两处）。
- 日期格改为圆角色块：本月 `CellBg`、非本月 `CellBgFaint`、今天 `TodayBg`、选中 `Surface` + 2px `Accent` 环、悬停 `SurfaceAlt`。
- 星期行改为一条 `HeaderBand` 圆角色带（左右各内缩 0）承载「一…日」，文字仍用 `Small` 系列、周六周日降 `TextMuted`。
- MonthView **不再画外框/外卡**（外卡由 MainForm 的 `Draw.Card` 提供），自身背景画 `Theme.Card`。
- 3px 方角色条、`+n` 溢出、`EllipsisCharacter` 截断、10px 格圆角、格高均分与格宽上限 = 格高×1.5 全部保留。

## 5. 编辑器：自绘下拉 + 星期几多选（T2 拥有）

`EventEditor` 公开面保持：`EventEditor(Store)` / `EventEditor(Store, ScheduleItem)` / `ScheduleItem Result` / `ShowDialog()`。

- **删掉原生 `ComboBox`**（它就是「选择框超出边界」的根因：系统下拉宽于自绘字段、盖住右侧「颜色」行，已由 `build\verify-edit\editor-dropdown-open.png` 取证）。改为 `int _repeatIndex` + `bool _dropOpen` + `OnPaint` 自绘弹出列表 + `OnMouseDown` 命中，`Esc`/点外部收起。
- 下拉面板几何：`x = _boxRepeat.X`、`width = _boxRepeat.Width`（**严禁超出**）、`y = _boxRepeat.Bottom + 6`、`height = 4*30 + 8`、圆角 10、`Surface` 面 + 1px `Border` + `Draw.Shadow`。列表项 hover/选中：`Active` 用 `AccentSoft` 底 + `TextPrimary`，选中项左侧 3px `Accent` 竖条。
- 选择「每周」时，在下方新增一行 7 个星期 chip：`一 二 三 四 五 六 日`，各 34×28、间距 6、圆角 8、选中 = `Accent` 底白字、未选 = `SurfaceAlt` 底 `TextSecondary`；行标签「重复日」。至少保 1 天：取消最后一个时忽略该次点击。
- 窗口高度随星期行出现/隐藏切换：`H` 460 → 502（星期行 42px），`备注`/错误行/按钮整体下移，切换后必须重算 `Region`（`ApplyRoundedRegion`）并 `Invalidate()`。
- 保存：`Repeat == Weekly` 时 `it.WeekDays = mask`（mask 恰为 0 时按 `Date` 的星期补成 1 位），其它 kind 写 0。`FillFrom`：`Weekly && WeekDays == 0` → 默认勾选 `Date.DayOfWeek`。

## 6. 品牌（T4 拥有，T5 消费）

```csharp
// src/UI/Branding.cs（新增）
public static class Branding
{
    /// <summary>32×32 应用图标；取 exe 自身图标，失败返回 null（调用方必须判空）。</summary>
    public static Icon AppIcon { get; }
    /// <summary>16/24/32 托盘用位图；失败返回 null。</summary>
    public static Bitmap TrayBitmap { get; }
    /// <summary>纯几何的品牌标记（不依赖 ico 缩放），用于窗口头部 size×size。</summary>
    public static void DrawMark(Graphics g, Rectangle box);
}
```

`build.ps1` 增加：`assets\app.ico` 存在时给 csc 追加 `/win32icon:"assets\app.ico"`；缺失则跳过并打印一行提示（不得因此失败）。

## 7. 主窗栅格（T5 拥有，仅供参考数值，可微调但须满足硬约束）

`1180×780` 无边框（最小 `1040×700`；**原定 1000×700，落地后实测月历卡只剩 600 宽、格内标题被截成两个字，故上调**）。**硬约束**：两卡左右各距窗口 20px；两卡水平间距 ≥ 14px；月历卡内 padding ≥ 16px；底部状态条高 44。

- 头部 `y=0..64`：`Branding.DrawMark` 28×28 @ (24,18) → 月份标题 17pt Bold @ x=64 → 右端 `今天` 胶囊 → `—` / `×`。
- 月历卡：`(20, 68, 780, 656)`；日程卡：`(816, 68, 344, 656)`；间隙 16。窗口变窄时**先压侧栏**（344 → 300），月历卡不小于 700。
- 日程卡内部 vertical：日期头 56 → 统计行 24 → 日程列表（自适应，滚轮滚动）→ 每日提示面板（约侧栏 1/3，150–320 钳制，滚轮滚动）→ `+ 新建日程` 44，内 padding 16。
- 每日提示面板（v1.4 起）：头部「每日提示 / 编辑」，行区域把活动池（用户池非空则用户池，否则内置 26 条）逐条像日程行一样列出（无完成圆圈，两行正文，滚轮滚动，右侧细滚动条）；`TipText` 数据感知文案（有逾期/剩余/完成）保留为测试契约，不再上屏。
