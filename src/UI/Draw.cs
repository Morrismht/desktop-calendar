using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace DesktopCalendar.UI
{
    /// <summary>绘图基础：抗锯齿、圆角路径、圆角填充/描边、文本对齐、省略号、顶部圆角。</summary>
    public static class Draw
    {
        public static void Smooth(Graphics g)
        {
            if (g == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            return RoundF(new RectangleF(r.X, r.Y, r.Width, r.Height), radius);
        }

        public static GraphicsPath RoundF(RectangleF rf, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2f;
            float max = Math.Min(rf.Width, rf.Height);
            if (d > max) d = max;
            if (d <= 0.1f)
            {
                p.AddRectangle(rf);
                p.CloseFigure();
                return p;
            }
            p.AddArc(rf.X, rf.Y, d, d, 180f, 90f);
            p.AddArc(rf.Right - d, rf.Y, d, d, 270f, 90f);
            p.AddArc(rf.Right - d, rf.Bottom - d, d, d, 0f, 90f);
            p.AddArc(rf.X, rf.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure();
            return p;
        }

        /// <summary>顶部两角圆、底部方（提醒卡片用）。</summary>
        public static GraphicsPath RoundTop(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2f;
            float max = Math.Min(r.Width, r.Height);
            if (d > max) d = max;
            if (d <= 0.1f)
            {
                p.AddRectangle(r);
                p.CloseFigure();
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180f, 90f);
            p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            p.AddLine(r.Right, r.Y + d, r.Right, r.Bottom);
            p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            p.AddLine(r.X, r.Bottom, r.X, r.Y + d);
            p.CloseFigure();
            return p;
        }

        public static void FillPath(Graphics g, GraphicsPath path, Color c)
        {
            if (g == null || path == null) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(c))
            {
                g.FillPath(b, path);
            }
            g.SmoothingMode = old;
        }

        public static void Fill(Graphics g, Rectangle r, Color c)
        {
            if (g == null || r.Width <= 0 || r.Height <= 0) return;
            using (SolidBrush b = new SolidBrush(c))
            {
                g.FillRectangle(b, r);
            }
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color c)
        {
            if (g == null || r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = RoundF(new RectangleF(r.X, r.Y, r.Width, r.Height), radius))
            {
                FillPath(g, p, c);
            }
        }

        public static void FillRoundTop(Graphics g, Rectangle r, int radius, Color c)
        {
            if (g == null || r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = RoundTop(r, radius))
            {
                FillPath(g, p, c);
            }
        }

        /// <summary>半像素对齐描边：内缩 w/2，所以 1px 落在整像素上不发虚，1.5px 自然抗锯齿。</summary>
        public static void StrokeRound(Graphics g, Rectangle r, int radius, Color c, float w = 1f)
        {
            if (g == null || r.Width <= 0 || r.Height <= 0 || w <= 0f) return;
            float inset = w / 2f;
            RectangleF rf = new RectangleF(r.X + inset, r.Y + inset, r.Width - w, r.Height - w);
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = RoundF(rf, radius))
            using (Pen pen = new Pen(c, w))
            {
                pen.Alignment = PenAlignment.Center;
                pen.LineJoin = LineJoin.Round;
                g.DrawPath(pen, p);
            }
            g.SmoothingMode = old;
        }

        // ---------------- CONTRACT-V2 §3：阴影 / 卡片 / 色块 ----------------

        /// <summary>
        /// 向外扩散 spread 像素的分层柔和阴影，alpha 为最内圈（贴边处）的不透明度基准。
        /// 由外向内叠 spread 圈同形圆角矩形环，外圈最淡、内圈最浓。每圈用「外框 + 内框」的
        /// Alternate 路径只填一次，所以任一像素恰好被涂一次、合成后就是设计值 alpha*r
        /// （若逐圈叠加覆盖，深色主题下 1%/圈 会全部被 8 位取整吃掉，阴影彻底消失）。
        /// 全程只有 FillPath，不分配 Bitmap（Bitmap 模糊每帧分配会拖帧）。
        /// spread &lt;= 0 或 alpha &lt;= 0 时直接返回。
        /// </summary>
        public static void Shadow(Graphics g, Rectangle r, int radius, Color baseColor, int spread, float alpha)
        {
            if (g == null || spread <= 0) return;
            if (alpha <= 0f || baseColor.A == 0) return;
            if (r.Width <= 0 || r.Height <= 0) return;
            if (alpha > 1f) alpha = 1f;

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int k = 0; k < spread; k++)
            {
                int d = spread - k;                                        // 本圈外扩像素数：spread → 1
                int a255 = (int)Math.Round(alpha * (k + 1) / (float)spread * 255f);
                if (a255 <= 0) continue;
                if (a255 > 255) a255 = 255;

                RectangleF outer = new RectangleF(r.X - d, r.Y - d, r.Width + d * 2, r.Height + d * 2);
                RectangleF inner = new RectangleF(outer.X + 1, outer.Y + 1, outer.Width - 2, outer.Height - 2);
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.FillMode = FillMode.Alternate;
                    using (GraphicsPath po = RoundF(outer, radius + d)) p.AddPath(po, false);
                    using (GraphicsPath pi = RoundF(inner, radius + d - 1)) p.AddPath(pi, false);
                    FillPath(g, p, Color.FromArgb(a255, baseColor));
                }
            }
            g.SmoothingMode = old;
        }

        /// <summary>
        /// 卡片 = 阴影（spread 10 / alpha 0.10，暗面补偿）+ 圆角卡片面 + 1px line 描边。
        /// alpha 以 0.10 为基准；面色越暗，同比例黑影能分辨的灰阶越少（深色主题底色 #12141A
        /// 只有 18/20/26，10% 阴影只够 1–2 级，8px 外直接被 8 位取整吃掉），所以按面色亮度
        /// 向上补偿，让两套主题的观感深度一致：浅色面 0.10（契约值），深色面约 0.21。
        /// 返回值：内容矩形 = r 四周向内收 1px（让开那 1px 描边），调用方按它排版内容；
        /// r 无效时原样返回 r。
        /// </summary>
        public static Rectangle Card(Graphics g, Rectangle r, int radius, Color face, Color line, Color shadowColor)
        {
            if (g == null || r.Width <= 0 || r.Height <= 0) return r;
            int lum = (face.R * 299 + face.G * 587 + face.B * 114) / 1000;
            float alpha = 0.10f + 0.12f * (1f - lum / 255f);
            Shadow(g, r, radius, shadowColor, 10, alpha);
            FillRound(g, r, radius, face);
            HairLine(g, r, radius, line);
            return new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
        }

        /// <summary>
        /// 1px 整数坐标描边：笔心落在 +0.5（与 StrokeRound 相同的整数结论），但关掉抗锯齿，
        /// 这样直边像素精确等于 line（抗锯齿下 1px 线只有 15/16 覆盖率，永远混出一点面色）。
        /// 圆角只有 1px 的阶梯，在这种低对比描边上肉眼不可见。
        /// </summary>
        private static void HairLine(Graphics g, Rectangle r, int radius, Color c)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            RectangleF rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
            using (GraphicsPath p = RoundF(rf, radius))
            using (Pen pen = new Pen(c, 1f))
            {
                pen.Alignment = PenAlignment.Center;
                pen.LineJoin = LineJoin.Round;
                g.DrawPath(pen, p);
            }
            g.SmoothingMode = old;
        }

        /// <summary>纯色块填充（无描边），圆角 radius。</summary>
        public static void Block(Graphics g, Rectangle r, int radius, Color c)
        {
            FillRound(g, r, radius, c);
        }

        public static void Line(Graphics g, float x1, float y1, float x2, float y2, Color c, float w = 1f)
        {
            if (g == null || w <= 0f) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(c, w))
            {
                g.DrawLine(pen, x1, y1, x2, y2);
            }
            g.SmoothingMode = old;
        }

        public static void Text(Graphics g, string s, Font f, Color c, Rectangle r,
                                StringAlignment h = StringAlignment.Near,
                                StringAlignment v = StringAlignment.Center)
        {
            if (g == null || f == null || string.IsNullOrEmpty(s)) return;
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            {
                sf.Alignment = h;
                sf.LineAlignment = v;
                sf.Trimming = StringTrimming.None;
                using (SolidBrush b = new SolidBrush(c))
                {
                    g.DrawString(s, f, b, r, sf);
                }
            }
        }

        public static void Ellipsis(Graphics g, string s, Font f, Color c, Rectangle r)
        {
            if (g == null || f == null || string.IsNullOrEmpty(s) || r.Width <= 2) return;
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            {
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                using (SolidBrush b = new SolidBrush(c))
                {
                    g.DrawString(s, f, b, r, sf);
                }
            }
        }

        /// <summary>两色线性混合，t=0 取 a，t=1 取 b（都带 alpha）。</summary>
        public static Color Blend(Color a, Color b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            int r = (int)Math.Round(a.R + (b.R - a.R) * t);
            int g = (int)Math.Round(a.G + (b.G - a.G) * t);
            int bl = (int)Math.Round(a.B + (b.B - a.B) * t);
            int al = (int)Math.Round(a.A + (b.A - a.A) * t);
            return Color.FromArgb(al, r, g, bl);
        }

        /// <summary>按 DPI 缩放一个像素值（96dpi 下原样返回）。</summary>
        public static int Scale(Graphics g, int px)
        {
            if (g == null) return px;
            float d = g.DpiY / 96f;
            if (d <= 0f) d = 1f;
            return (int)Math.Round(px * d);
        }

        public static float ScaleF(Graphics g, float px)
        {
            if (g == null) return px;
            float d = g.DpiY / 96f;
            if (d <= 0f) d = 1f;
            return px * d;
        }
    }
}
