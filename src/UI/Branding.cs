using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace DesktopCalendar.UI
{
    /// <summary>
    /// 品牌资产：exe 自身图标、托盘位图、纯几何品牌标记。
    /// 全部成员都自带 try/catch 兜底——调用方只会拿到 null，绝不会看到异常。
    /// </summary>
    public static class Branding
    {
        // 与 assets/make_icon.py 的 cand-d 完全同构，颜色是品牌固定色，不随主题变化。
        private static readonly Color BrandTop = Color.FromArgb(0x4A, 0x79, 0xF0);
        private static readonly Color BrandBottom = Color.FromArgb(0x24, 0x50, 0xC8);
        private static readonly Color MarkInk = Color.FromArgb(0x14, 0x1F, 0x36);
        private static readonly Color PageWhite = Color.White;

        // 小于该边长时省掉装订孔和页顶条带（与 cand-d 的 16/24/32 简化档一致）。
        private const int DetailMinSide = 48;

        // 数字视觉居中：下边距 = 上边距 × 该系数，而不是几何居中。
        private const float BottomMarginRatio = 0.62f;

        private static readonly object SyncRoot = new object();

        private static bool _iconLoaded;
        private static Icon _appIcon;

        private static bool _trayLoaded;
        private static Bitmap _trayBitmap;

        private static bool _markFamilyResolved;
        private static FontFamily _markFamily;
        private static int _markStyle;

        /// <summary>32×32 的应用图标；取自 exe 自身图标，取不到返回 null（调用方必须判空）。</summary>
        public static Icon AppIcon
        {
            get
            {
                lock (SyncRoot)
                {
                    if (!_iconLoaded)
                    {
                        _iconLoaded = true;
                        _appIcon = LoadAppIcon();
                    }
                    return _appIcon;
                }
            }
        }

        /// <summary>托盘用 16×16 位图，由 AppIcon 派生；取不到返回 null（调用方必须判空）。</summary>
        public static Bitmap TrayBitmap
        {
            get
            {
                lock (SyncRoot)
                {
                    if (!_trayLoaded)
                    {
                        _trayLoaded = true;
                        _trayBitmap = LoadTrayBitmap();
                    }
                    return _trayBitmap;
                }
            }
        }

        /// <summary>
        /// 纯 GDI+ 几何品牌标记（与 cand-d 同构）：蓝色渐变圆角方块 + 白色日历页（放大 9%）+
        /// 数字 31；边长 >= 48 才加装订孔与页顶条带，小尺寸只留底、页、数字。
        /// 随 box 尺寸自适应，内部异常静默。
        /// </summary>
        public static void DrawMark(Graphics g, Rectangle box)
        {
            if (g == null) return;
            try
            {
                int side = Math.Min(box.Width, box.Height);
                if (side < 6) return;

                Rectangle r = new Rectangle(box.X + (box.Width - side) / 2,
                                            box.Y + (box.Height - side) / 2,
                                            side, side);

                SmoothingMode oldSmoothing = g.SmoothingMode;
                InterpolationMode oldInterpolation = g.InterpolationMode;
                PixelOffsetMode oldOffset = g.PixelOffsetMode;
                try
                {
                    Draw.Smooth(g);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    // cand-d：白页按画面中心放大 +9%；边长 < 48 时省掉装订孔与页顶条带
                    bool detailed = side >= DetailMinSide;
                    float radius = side * 0.22f;
                    RectangleF tile = new RectangleF(r.X, r.Y, side, side);
                    using (GraphicsPath outer = Draw.RoundF(tile, radius))
                    using (LinearGradientBrush bg = new LinearGradientBrush(
                               new PointF(tile.X, tile.Y), new PointF(tile.Right, tile.Bottom),
                               BrandTop, BrandBottom))
                    {
                        g.FillPath(bg, outer);
                    }

                    float pageW = side * 0.796f;
                    float pageH = side * 0.763f;
                    float pageX = r.X + (side - pageW) / 2f;
                    float pageY = r.Y + (side - pageH) / 2f;
                    float pageR = side * 0.055f;

                    // 装订孔先画，下半截被白色页面盖住 => 读作两枚圆环（仅大尺寸）
                    if (detailed)
                    {
                        float holeW = side * 0.070f;
                        float holeH = side * 0.150f;
                        int holeX = (int)Math.Round(holeW);
                        int holeY = (int)Math.Round(holeH);
                        int holeR = (int)Math.Round(holeW / 2f);
                        int holeTop = (int)Math.Round(r.Y + side * 0.032f);
                        Draw.FillRound(g, new Rectangle((int)Math.Round(r.X + side * 0.335f - holeW / 2f),
                                                        holeTop, holeX, holeY), holeR, PageWhite);
                        Draw.FillRound(g, new Rectangle((int)Math.Round(r.X + side * 0.665f - holeW / 2f),
                                                        holeTop, holeX, holeY), holeR, PageWhite);
                    }

                    Rectangle page = new Rectangle((int)Math.Round(pageX), (int)Math.Round(pageY),
                                                   (int)Math.Round(pageW), (int)Math.Round(pageH));
                    Draw.FillRound(g, page, (int)Math.Round(pageR), PageWhite);

                    // 页顶条带（上两角圆、下两角方）+ 1px 分隔线（仅大尺寸）
                    float bodyTop = page.Y;
                    if (detailed)
                    {
                        int bandH = (int)Math.Round(page.Height * 0.175f);
                        if (bandH > 0)
                        {
                            Draw.FillRoundTop(g, new Rectangle(page.X, page.Y, page.Width, bandH),
                                              (int)Math.Round(pageR), Draw.Blend(PageWhite, BrandTop, 0.18f));
                            if (bandH + 1 < page.Height)
                            {
                                Draw.Fill(g, new Rectangle(page.X, page.Y + bandH, page.Width, 1),
                                          Draw.Blend(PageWhite, BrandTop, 0.35f));
                            }
                            bodyTop = page.Y + bandH;
                        }
                    }

                    DrawMarkNumber(g, page, pageX, pageW, bodyTop, detailed);
                }
                finally
                {
                    g.SmoothingMode = oldSmoothing;
                    g.InterpolationMode = oldInterpolation;
                    g.PixelOffsetMode = oldOffset;
                }
            }
            catch (Exception)
            {
                // 品牌标记画不出来不影响界面可用性
            }
        }

        // ---------------------------------------------------------------- 内部

        private static Icon LoadAppIcon()
        {
            Icon raw = null;
            try
            {
                string exe = Application.ExecutablePath;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return null;

                raw = Icon.ExtractAssociatedIcon(exe);
                if (raw == null) return null;

                Icon sized = new Icon(raw, 32, 32);
                if (sized == null) return null;
                if (!object.ReferenceEquals(sized, raw))
                {
                    // sized 自带独立句柄；raw 留着不释放（进程级单例，避免共享句柄被提前销毁）
                    return sized;
                }
                return sized;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Bitmap LoadTrayBitmap()
        {
            try
            {
                Icon src = AppIcon;
                if (src == null) return null;

                Bitmap bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawIcon(src, new Rectangle(0, 0, 16, 16));
                }
                return bmp;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>「31」用 Segoe UI Black（比 Bold 重一档），失败退到 Segoe UI Bold。</summary>
        private static FontFamily MarkFamily()
        {
            lock (SyncRoot)
            {
                if (_markFamilyResolved) return _markFamily;
                _markFamilyResolved = true;

                try
                {
                    // Segoe UI Black 的常规字重本身就是 Black，不需要再叠加 Bold
                    _markFamily = new FontFamily("Segoe UI Black");
                    _markStyle = (int)FontStyle.Regular;
                    return _markFamily;
                }
                catch (Exception) { _markFamily = null; }

                try
                {
                    _markFamily = new FontFamily("Segoe UI");
                    _markStyle = (int)FontStyle.Bold;
                    return _markFamily;
                }
                catch (Exception) { _markFamily = null; }

                try
                {
                    _markFamily = new FontFamily(FontFamily.GenericSansSerif.Name);
                    _markStyle = (int)FontStyle.Bold;
                }
                catch (Exception) { _markFamily = null; }
                return _markFamily;
            }
        }

        /// <summary>指定 em 尺寸下「31」字形轮廓的紧贴包围盒（世界坐标原点在左上）。</summary>
        private static RectangleF GlyphBounds(FontFamily family, float em)
        {
            if (family == null || em <= 0.5f) return RectangleF.Empty;

            using (GraphicsPath path = new GraphicsPath())
            using (StringFormat sf = StringFormat.GenericTypographic)
            {
                path.AddString("31", family, _markStyle, em, new PointF(0f, 0f), sf);
                return path.GetBounds();
            }
        }

        /// <summary>把「31」画进页面主体区：下边距 = 上边距 × BottomMarginRatio（视觉居中）。</summary>
        private static void DrawMarkNumber(Graphics g, Rectangle page, float pageX, float pageW,
                                           float bodyTop, bool detailed)
        {
            if (g == null || page.Width <= 2 || page.Height <= 2) return;

            FontFamily family = MarkFamily();
            if (family == null) return;

            float targetH = page.Height * (detailed ? 0.430f : 0.520f);
            if (targetH < 3f) return;

            // 先按估算 em 量一次紧贴字形高，再线性换算到目标高度（一次修正就够）
            float em = page.Height * 0.560f;
            RectangleF bounds = GlyphBounds(family, em);
            if (bounds.Height <= 0.5f) return;

            em *= targetH / bounds.Height;
            bounds = GlyphBounds(family, em);
            if (bounds.Height <= 0.5f) return;

            // 太宽就等比收回，绝不让数字溢出页面
            float maxWidth = page.Width * 0.90f;
            if (bounds.Width > maxWidth)
            {
                em *= maxWidth / bounds.Width;
                bounds = GlyphBounds(family, em);
                if (bounds.Width <= 0.5f) return;
            }

            float span = page.Bottom - bodyTop;
            float gap = span - bounds.Height;
            float topGap = gap > 0f ? gap / (1f + BottomMarginRatio) : 0f;

            float dx = pageX + pageW / 2f - (bounds.X + bounds.Width / 2f);
            float dy = bodyTop + topGap - bounds.Y;

            using (GraphicsPath path = new GraphicsPath())
            using (StringFormat sf = StringFormat.GenericTypographic)
            using (SolidBrush brush = new SolidBrush(MarkInk))
            using (Matrix move = new Matrix())
            {
                path.AddString("31", family, _markStyle, em, new PointF(0f, 0f), sf);
                move.Translate(dx, dy);
                path.Transform(move);
                g.FillPath(brush, path);
            }
        }
    }
}
