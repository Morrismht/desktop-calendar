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
