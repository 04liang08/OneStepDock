using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace OneStepDock
{
    internal static class WindowCapture
    {
        public static bool IsBlank(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                return true;
            }

            const int samplesX = 24;
            const int samplesY = 16;
            bool allLight = true;
            bool allDark = true;

            for (int iy = 0; iy < samplesY; iy++)
            {
                int y = Math.Min(bitmap.Height - 1, (iy * bitmap.Height + bitmap.Height / 2) / samplesY);
                for (int ix = 0; ix < samplesX; ix++)
                {
                    int x = Math.Min(bitmap.Width - 1, (ix * bitmap.Width + bitmap.Width / 2) / samplesX);
                    Color color = bitmap.GetPixel(x, y);
                    int luminance = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
                    if (luminance < 235)
                    {
                        allLight = false;
                    }
                    if (luminance > 20)
                    {
                        allDark = false;
                    }
                    if (!allLight && !allDark)
                    {
                        return false;
                    }
                }
            }

            return allLight || allDark;
        }

        public static ulong ComputeSignature(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                return 0;
            }

            const int columns = 24;
            const int rows = 16;
            ulong hash = 14695981039346656037UL;

            for (int iy = 0; iy < rows; iy++)
            {
                int y = Math.Min(
                    bitmap.Height - 1,
                    (iy * 2 + 1) * bitmap.Height / (rows * 2));
                for (int ix = 0; ix < columns; ix++)
                {
                    int x = Math.Min(
                        bitmap.Width - 1,
                        (ix * 2 + 1) * bitmap.Width / (columns * 2));
                    Color color = bitmap.GetPixel(x, y);
                    int quantized = ((color.R >> 4) << 8) | ((color.G >> 4) << 4) | (color.B >> 4);
                    hash ^= (ulong)(quantized + 1);
                    hash *= 1099511628211UL;
                }
            }

            hash ^= (ulong)bitmap.Width;
            hash *= 1099511628211UL;
            hash ^= (ulong)bitmap.Height;
            hash *= 1099511628211UL;
            return hash;
        }

        public static Bitmap CaptureClient(IntPtr hWnd)
        {
            Native.RECT window;
            Native.RECT client;
            if (!Native.GetWindowRect(hWnd, out window) ||
                !Native.GetClientRect(hWnd, out client))
            {
                return null;
            }

            int windowWidth = window.Width;
            int windowHeight = window.Height;
            int clientWidth = client.Width;
            int clientHeight = client.Height;
            if (windowWidth <= 0 || windowHeight <= 0 || clientWidth <= 0 || clientHeight <= 0)
            {
                return null;
            }

            var clientOrigin = new Native.POINT(0, 0);
            if (!Native.ClientToScreen(hWnd, ref clientOrigin))
            {
                return null;
            }

            int offsetX = clientOrigin.X - window.Left;
            int offsetY = clientOrigin.Y - window.Top;
            Rectangle crop = Rectangle.Intersect(
                new Rectangle(offsetX, offsetY, clientWidth, clientHeight),
                new Rectangle(0, 0, windowWidth, windowHeight));
            if (crop.Width <= 0 || crop.Height <= 0)
            {
                return null;
            }

            try
            {
                using (var full = new Bitmap(windowWidth, windowHeight, PixelFormat.Format32bppPArgb))
                {
                    using (var graphics = Graphics.FromImage(full))
                    {
                        IntPtr hdc = graphics.GetHdc();
                        bool ok;
                        try
                        {
                            ok = Native.PrintWindow(hWnd, hdc, Native.PW_RENDERFULLCONTENT);
                        }
                        finally
                        {
                            graphics.ReleaseHdc(hdc);
                        }

                        if (!ok)
                        {
                            return null;
                        }
                    }

                    if (crop.Width == windowWidth && crop.Height == windowHeight)
                    {
                        return new Bitmap(full);
                    }

                    return full.Clone(crop, PixelFormat.Format32bppPArgb);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
