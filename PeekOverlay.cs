using System;
using System.Drawing;
using System.Windows.Forms;

namespace OneStepDock
{
    internal sealed class PeekOverlay : Form
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private static readonly IntPtr HTTRANSPARENT = new IntPtr(-1);
        private static readonly IntPtr MA_NOACTIVATE = new IntPtr(3);

        private const byte DimAlpha = 150;

        public PeekOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.FromArgb(6, 8, 12);
            DoubleBuffered = true;
            ResizeRedraw = true;
            TabStop = false;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= (int)(Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT |
                                    Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = HTTRANSPARENT;
                return;
            }

            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = MA_NOACTIVATE;
                return;
            }

            base.WndProc(ref m);
        }

        public void ShowFor(Screen screen, Rectangle holeScreen)
        {
            if (screen == null)
            {
                return;
            }

            Rectangle bounds = screen.WorkingArea;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            try
            {
                if (Bounds != bounds)
                {
                    Bounds = bounds;
                }

                // 先建句柄并配好分层属性，再显示，避免闪一下不透明的黑屏。
                IntPtr handle = Handle;
                ApplyHole(handle, bounds, holeScreen);
                Native.SetLayeredWindowAttributes(handle, 0, DimAlpha, Native.LWA_ALPHA);

                if (!Visible)
                {
                    Show();
                }

                Native.SetWindowPos(
                    handle,
                    Native.HWND_TOPMOST,
                    bounds.Left,
                    bounds.Top,
                    bounds.Width,
                    bounds.Height,
                    Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }
            catch (Exception ex)
            {
                Program.Log(ex);
            }
        }

        public void HideOverlay()
        {
            if (!IsDisposed && Visible)
            {
                Hide();
            }
        }

        private static void ApplyHole(IntPtr handle, Rectangle bounds, Rectangle holeScreen)
        {
            IntPtr full = Native.CreateRectRgn(0, 0, bounds.Width, bounds.Height);
            if (full == IntPtr.Zero)
            {
                return;
            }

            if (holeScreen.Width > 0 && holeScreen.Height > 0)
            {
                IntPtr hole = Native.CreateRectRgn(
                    holeScreen.Left - bounds.Left,
                    holeScreen.Top - bounds.Top,
                    holeScreen.Right - bounds.Left,
                    holeScreen.Bottom - bounds.Top);

                if (hole != IntPtr.Zero)
                {
                    Native.CombineRgn(full, full, hole, Native.RGN_DIFF);
                    Native.DeleteObject(hole);
                }
            }

            // SetWindowRgn 接管区域所有权，不需要（也不能）再删除。
            if (Native.SetWindowRgn(handle, full, true) == 0)
            {
                Native.DeleteObject(full);
            }
        }
    }
}
