using System;
using System.Drawing;

namespace OneStepDock
{
    internal static class DockTheme
    {
        public static bool IsLight { get; private set; }
        public static Color Backdrop { get; private set; }
        public static Color ThumbTop { get; private set; }
        public static Color ThumbBottom { get; private set; }
        public static Color Border { get; private set; }
        public static Color Indicator { get; private set; }
        public static Color Muted { get; private set; }
        public static Color Accent { get; } = Color.FromArgb(86, 148, 255);

        static DockTheme()
        {
            ApplySample(Color.FromArgb(238, 241, 246), 0);
        }

        public static void ApplySample(Color sample, int themeMode)
        {
            double hue;
            double saturation;
            double lightness;
            ToHsl(sample, out hue, out saturation, out lightness);

            bool light = themeMode == 1 || (themeMode != 2 && lightness >= 0.45);
            IsLight = light;

            if (saturation < 0.055)
            {
                hue = 214;
                saturation = light ? 0.10 : 0.30;
            }
            else if (light)
            {
                saturation = Math.Max(0.04, Math.Min(0.18, saturation * 0.5));
            }
            else
            {
                saturation = Math.Max(0.22, Math.Min(0.50, saturation * 0.8 + 0.05));
            }

            if (light)
            {
                Backdrop = FromHsl(hue, saturation * 0.65, 0.905);
                ThumbTop = FromHsl(hue, saturation * 0.45, 0.940);
                ThumbBottom = FromHsl(hue, saturation * 0.55, 0.885);
                Border = Color.FromArgb(28, 18, 28, 40);
                Indicator = Color.FromArgb(90, 55, 68, 85);
                Muted = Color.FromArgb(100, 105, 118);
            }
            else
            {
                Backdrop = FromHsl(hue, saturation * 0.60, 0.105);
                ThumbTop = FromHsl(hue, saturation * 0.45, 0.100);
                ThumbBottom = FromHsl(hue, saturation * 0.52, 0.130);
                Border = Color.FromArgb(24, 255, 255, 255);
                Indicator = Color.FromArgb(125, 190, 200, 215);
                Muted = Color.FromArgb(150, 158, 172);
            }
        }

        private static void ToHsl(Color color, out double hue, out double saturation, out double lightness)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            lightness = (max + min) / 2.0;

            if (Math.Abs(max - min) < 0.0001)
            {
                hue = 0;
                saturation = 0;
                return;
            }

            double delta = max - min;
            saturation = lightness > 0.5
                ? delta / (2.0 - max - min)
                : delta / (max + min);

            if (max == r)
            {
                hue = (g - b) / delta + (g < b ? 6 : 0);
            }
            else if (max == g)
            {
                hue = (b - r) / delta + 2;
            }
            else
            {
                hue = (r - g) / delta + 4;
            }

            hue *= 60;
        }

        private static Color FromHsl(double hue, double saturation, double lightness)
        {
            hue = ((hue % 360) + 360) % 360;
            saturation = Math.Max(0, Math.Min(1, saturation));
            lightness = Math.Max(0, Math.Min(1, lightness));

            double c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            double x = c * (1 - Math.Abs((hue / 60.0) % 2 - 1));
            double m = lightness - c / 2;
            double r = 0;
            double g = 0;
            double b = 0;

            if (hue < 60)
            {
                r = c;
                g = x;
            }
            else if (hue < 120)
            {
                r = x;
                g = c;
            }
            else if (hue < 180)
            {
                g = c;
                b = x;
            }
            else if (hue < 240)
            {
                g = x;
                b = c;
            }
            else if (hue < 300)
            {
                r = x;
                b = c;
            }
            else
            {
                r = c;
                b = x;
            }

            return Color.FromArgb(
                (int)Math.Round((r + m) * 255),
                (int)Math.Round((g + m) * 255),
                (int)Math.Round((b + m) * 255));
        }
    }
}
