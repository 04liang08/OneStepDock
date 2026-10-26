using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OneStepDock
{
    internal sealed class PeekThumbWindow : Form
    {
        private const int WM_MOUSEACTIVATE = 0x0021;
        private static readonly IntPtr MA_NOACTIVATE = new IntPtr(3);

        private IntPtr _thumbnailId;
        private Bitmap _staticImage;
        private bool _staticMode;

        public event EventHandler Clicked;

        public PeekThumbWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Black;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint, true);
            DoubleBuffered = true;
        }

        public bool IsShowingThumbnail
        {
            get { return _thumbnailId != IntPtr.Zero; }
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
                cp.ExStyle |= (int)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = MA_NOACTIVATE;
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            EventHandler handler = Clicked;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var brush = new SolidBrush(Color.FromArgb(8, 10, 14)))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }

            if (!_staticMode || _staticImage == null)
            {
                return;
            }

            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(_staticImage, ClientRectangle);
        }

        public void ShowFor(Rectangle frameScreen, IntPtr source)
        {
            if (frameScreen.Width <= 0 || frameScreen.Height <= 0)
            {
                return;
            }

            try
            {
                // 换目标时不能隐藏，否则每次切换都会闪一下黑底。
                DetachCore(false);

                Bounds = frameScreen;
                if (!Visible)
                {
                    Show();
                }

                IntPtr handle = Handle;
                Native.SetWindowPos(
                    handle,
                    Native.HWND_TOPMOST,
                    frameScreen.Left,
                    frameScreen.Top,
                    frameScreen.Width,
                    frameScreen.Height,
                    Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

                if (source == IntPtr.Zero || !Native.IsWindow(source))
                {
                    UseStaticImage(source);
                    return;
                }

                IntPtr thumbnailId;
                int hr = Native.DwmRegisterThumbnail(handle, source, out thumbnailId);
                if (hr != 0 || thumbnailId == IntPtr.Zero)
                {
                    UseStaticImage(source);
                    return;
                }

                _thumbnailId = thumbnailId;
                _staticMode = false;

                var properties = new Native.DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_VISIBLE |
                              Native.DWM_TNP_OPACITY | Native.DWM_TNP_SOURCECLIENTAREAONLY,
                    rcDestination = new Native.RECT(0, 0, ClientSize.Width, ClientSize.Height),
                    opacity = 255,
                    fVisible = true,
                    fSourceClientAreaOnly = false
                };
                Native.DwmUpdateThumbnailProperties(_thumbnailId, ref properties);
                Invalidate();
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                UseStaticImage(source);
            }
        }

        private void UseStaticImage(IntPtr source)
        {
            _staticMode = true;
            if (_staticImage != null)
            {
                _staticImage.Dispose();
                _staticImage = null;
            }

            if (source != IntPtr.Zero)
            {
                _staticImage = WindowCapture.CaptureClient(source);
            }

            Invalidate();
        }

        public void Detach()
        {
            DetachCore(true);
        }

        private void DetachCore(bool hide)
        {
            if (_thumbnailId != IntPtr.Zero)
            {
                try
                {
                    Native.DwmUnregisterThumbnail(_thumbnailId);
                }
                catch (Exception ex)
                {
                    Program.Log(ex);
                }
                _thumbnailId = IntPtr.Zero;
            }

            _staticMode = false;

            if (_staticImage != null)
            {
                _staticImage.Dispose();
                _staticImage = null;
            }

            if (hide && !IsDisposed && Visible)
            {
                Hide();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Detach();
            }

            base.Dispose(disposing);
        }
    }
}
