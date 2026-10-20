using System;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;

namespace DesktopCalendar.UI
{
    /// <summary>明/暗双主题：颜色 Token、字体、6 色分类板。全项目颜色只能从这里取。</summary>
    public class Theme
    {
        private static Theme _current;
        private static bool _dark;

        // 契约规定的 6 色分类板（明暗通用）
        private static readonly string[] PaletteHexValue = new string[]
        {
            "#4F7CFF", "#22C55E", "#F59E0B", "#EF4444", "#8B5CF6", "#06B6D4"
        };

        public Color Bg, Surface, SurfaceAlt, TextPrimary, TextSecondary, TextMuted,
                     Border, Accent, AccentSoft, TodayBg, Danger, Success;

        public Color[] Palette { get; private set; }

        public Font Title, Body, BodyBold, Small, SmallBold, Big;

        /// <summary>切换主题后触发，控件订阅它做重绘。</summary>
        public static event EventHandler Changed;

        public static string[] PaletteHex
        {
            get { return (string[])PaletteHexValue.Clone(); }
        }

        public static Theme Current
        {
            get { return EnsureInstance(); }
        }

        public static bool IsDark
        {
            get { return _dark; }
        }

        private Theme()
        {
            Palette = new Color[PaletteHexValue.Length];
            for (int i = 0; i < PaletteHexValue.Length; i++)
            {
                Palette[i] = ParseHex(PaletteHexValue[i], Color.Magenta);
            }

            string family = PickFamily();
            Title = new Font(family, 17f, FontStyle.Bold, GraphicsUnit.Point);
            Body = new Font(family, 10.5f, FontStyle.Regular, GraphicsUnit.Point);
            BodyBold = new Font(family, 10.5f, FontStyle.Bold, GraphicsUnit.Point);
            Small = new Font(family, 9f, FontStyle.Regular, GraphicsUnit.Point);
            SmallBold = new Font(family, 9f, FontStyle.Bold, GraphicsUnit.Point);
            Big = new Font(family, 22f, FontStyle.Bold, GraphicsUnit.Point);
        }

        private static Theme EnsureInstance()
        {
            if (_current == null)
            {
                _current = new Theme();
                _current.ApplyColors();
            }
            return _current;
        }

        public static void SetDark(bool dark)
        {
            bool changed = _current == null || _dark != dark;
            _dark = dark;
            EnsureInstance().ApplyColors();
            if (changed)
            {
                EventHandler h = Changed;
                if (h != null) h(null, EventArgs.Empty);
            }
        }

        public static void Toggle()
        {
            SetDark(!_dark);
        }

        private void ApplyColors()
        {
            if (IsDark)
            {
                Bg = ParseHex("#12141A", Color.Black);
                Surface = ParseHex("#1B1E26", Color.Black);
                SurfaceAlt = ParseHex("#23272F", Color.Black);
                TextPrimary = ParseHex("#E9EDF6", Color.White);
                TextSecondary = ParseHex("#9AA4B8", Color.Gray);
                TextMuted = ParseHex("#6B7488", Color.Gray);
                Border = ParseHex("#2C313B", Color.Gray);
                Accent = ParseHex("#6E92FF", Color.CornflowerBlue);
                AccentSoft = ParseHex("#232E4D", Color.Navy);
                TodayBg = ParseHex("#23304F", Color.Navy);
                Danger = ParseHex("#F87171", Color.Red);
                Success = ParseHex("#34D399", Color.Green);
            }
            else
            {
                Bg = ParseHex("#F4F6FB", Color.WhiteSmoke);
                Surface = ParseHex("#FFFFFF", Color.White);
                SurfaceAlt = ParseHex("#EEF2F9", Color.Gainsboro);
                TextPrimary = ParseHex("#1B2233", Color.Black);
                TextSecondary = ParseHex("#5B6478", Color.Gray);
                TextMuted = ParseHex("#98A2B3", Color.Gray);
                Border = ParseHex("#E3E8F2", Color.Gainsboro);
                Accent = ParseHex("#4F7CFF", Color.CornflowerBlue);
                AccentSoft = ParseHex("#E8EEFF", Color.AliceBlue);
                TodayBg = ParseHex("#E8EEFF", Color.AliceBlue);
                Danger = ParseHex("#EF4444", Color.Red);
                Success = ParseHex("#22C55E", Color.Green);
            }
        }

        /// <summary>日程的分类色：先精确命中分类板 6 色，其次任意合法 hex，最后回落到 Accent。</summary>
        public Color ResolveCategory(string colorHex)
        {
            if (!string.IsNullOrEmpty(colorHex))
            {
                string s = colorHex.Trim().ToUpperInvariant();
                for (int i = 0; i < PaletteHexValue.Length; i++)
                {
                    if (PaletteHexValue[i] == s) return Palette[i];
                }
                Color c = ParseHex(s, Color.Empty);
                if (!c.IsEmpty) return c;
            }
            return Palette[0];
        }

        public static Color ParseHex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            string s = hex.Trim();
            if (s.Length > 0 && s[0] == '#') s = s.Substring(1);
            if (s.Length != 6) return fallback;
            int v;
            if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return fallback;
            return Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        }

        private static string PickFamily()
        {
            string[] want = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" };
            try
            {
                using (InstalledFontCollection col = new InstalledFontCollection())
                {
                    FontFamily[] fams = col.Families;
                    for (int w = 0; w < want.Length; w++)
                    {
                        for (int i = 0; i < fams.Length; i++)
                        {
                            if (string.Equals(fams[i].Name, want[w], StringComparison.OrdinalIgnoreCase))
                            {
                                return want[w];
                            }
                        }
                    }
                }
            }
            catch
            {
                // 字体枚举失败也无所谓，回落即可
            }
            return "Segoe UI";
        }
    }
}
