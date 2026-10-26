using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace OneStepDock
{
    internal sealed class ThumbnailCard : Panel
    {
        private const int StaticPreviewIntervalMs = 4000;

        private readonly ThumbSurface _thumb;
        private readonly ToolTip _toolTip;
        private readonly Font _placeholderFont;
        private WindowItem _info;
        private IntPtr _thumbnailId;
        private Bitmap _icon;
        private bool _hover;
        private bool _live;
        private bool _staticMode;
        private bool _thumbVisible = true;
        private bool _thumbFullyVisible = true;
        private DateTime _lastStaticCaptureUtc;
        private ulong _staticSignature;
        private Bitmap _staticPreview;
        private Bitmap _staticScaled;
        private int _dpi = 96;

        public event EventHandler Clicked;
        public event EventHandler HoverChanged;
        public event EventHandler ContextMenuRequested;

        public IntPtr WindowHandle
        {
            get { return _info.Handle; }
        }

        public Bitmap AppIcon
        {
            get { return _icon; }
        }

        public Rectangle PreviewScreenBounds
        {
            get { return _thumb.RectangleToScreen(_thumb.ClientRectangle); }
        }

        public bool PreviewVisible
        {
            get { return _thumbVisible; }
        }

        public string Title
        {
            get { return _info.Title; }
        }

        public bool IsHovered
        {
            get { return _hover; }
        }

        public string ProcessName
        {
            get { return _info.ProcessName; }
        }

        public bool RequiresElevation
        {
            get { return _info.IsElevated && !Elevation.Current; }
        }

        public ThumbnailCard(WindowItem info, ToolTip toolTip)
        {
            _info = info;
            _toolTip = toolTip;
            _placeholderFont = CreateUiFont(8f, FontStyle.Regular);

            BackColor = DockTheme.Backdrop;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            TabStop = false;

            _thumb = new ThumbSurface
            {
                BackColor = DockTheme.ThumbTop,
                Cursor = Cursors.Hand
            };
            _thumb.Paint += ThumbPaint;
            Controls.Add(_thumb);

            MouseEnter += (s, e) => SetHover(true);
            MouseLeave += (s, e) => OnAreaLeave();
            MouseDown += (s, e) => OnCardMouseDown(e);
            _thumb.MouseEnter += (s, e) => SetHover(true);
            _thumb.MouseLeave += (s, e) => OnAreaLeave();
            _thumb.MouseDown += (s, e) => OnCardMouseDown(e);
            _thumb.Resize += (s, e) => UpdateThumbnailDestination();
            LocationChanged += (s, e) => UpdateThumbnailDestination();
            _thumb.LocationChanged += (s, e) => UpdateThumbnailDestination();

            LoadWindowIcon();
            UpdateTooltip();
        }

        public void Fit(int width, int dpi)
        {
            _dpi = dpi;
            int previewHeight = (int)Math.Round(width * 9.0 / 16.0);
            SuspendLayout();
            SetBounds(Left, Top, width, previewHeight);
            _thumb.SetBounds(0, 0, width, previewHeight);
            ResumeLayout(false);

            UpdateThumbnailDestination();
            Invalidate();
        }

        public void UpdateInfo(WindowItem info)
        {
            bool needsThumbRefresh = _info.IsMinimized != info.IsMinimized ||
                                     _info.IsNoRedirectionBitmap != info.IsNoRedirectionBitmap;
            _info = info;
            UpdateTooltip();
            if (needsThumbRefresh && IsHandleCreated)
            {
                ApplyThumbnail();
            }
        }

        public void ReapplyThumbnail()
        {
            if (IsHandleCreated)
            {
                ApplyThumbnail();
            }
        }

        public void RefreshStaticPreviewIfDue(DateTime utcNow)
        {
            if (!_staticMode || !_thumbFullyVisible || !Visible)
            {
                return;
            }

            if ((utcNow - _lastStaticCaptureUtc).TotalMilliseconds < StaticPreviewIntervalMs)
            {
                return;
            }

            _lastStaticCaptureUtc = utcNow;
            CaptureAndApplyStaticPreview();
        }

        public void RefreshStaticPreviewOnce()
        {
            if (!_staticMode || !Visible)
            {
                return;
            }

            _lastStaticCaptureUtc = DateTime.UtcNow;
            CaptureAndApplyStaticPreview();
        }

        private void CaptureAndApplyStaticPreview()
        {
            if (_info.IsMinimized || !Native.IsWindow(_info.Handle))
            {
                return;
            }

            Bitmap capture = WindowCapture.CaptureClient(_info.Handle);
            if (capture == null || WindowCapture.IsBlank(capture))
            {
                if (capture != null)
                {
                    capture.Dispose();
                }
                return;
            }

            ulong signature = WindowCapture.ComputeSignature(capture);
            if (_staticPreview != null && signature == _staticSignature)
            {
                capture.Dispose();
                return;
            }

            DisposeScaledPreview();
            Bitmap previous = _staticPreview;
            _staticPreview = capture;
            _staticSignature = signature;
            if (previous != null)
            {
                previous.Dispose();
            }
            _thumb.HasContent = true;
            _thumb.Invalidate();
        }

        public void UpdateThumbnailDestination()
        {
            if (!IsHandleCreated || !_thumb.IsHandleCreated ||
                _thumb.Width <= 0 || _thumb.Height <= 0)
            {
                return;
            }

            Control top = TopLevelControl;
            if (top == null || !top.IsHandleCreated)
            {
                return;
            }

            Rectangle thumbScreen = _thumb.RectangleToScreen(_thumb.ClientRectangle);
            Control parent = Parent;
            Rectangle viewport = parent != null && parent.IsHandleCreated
                ? parent.RectangleToScreen(parent.ClientRectangle)
                : top.RectangleToScreen(top.ClientRectangle);
            Rectangle visible = Rectangle.Intersect(viewport, thumbScreen);
            bool anyVisible = visible.Width > 0 && visible.Height > 0;
            _thumbFullyVisible = visible == thumbScreen;
            if (_thumbVisible != anyVisible)
            {
                _thumbVisible = anyVisible;
                _thumb.Invalidate();
            }

            if (_thumbnailId == IntPtr.Zero)
            {
                return;
            }

            Point formOrigin = top.PointToScreen(Point.Empty);
            if (!anyVisible)
            {
                var hidden = new Native.DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = Native.DWM_TNP_VISIBLE,
                    fVisible = false
                };
                Native.DwmUpdateThumbnailProperties(_thumbnailId, ref hidden);
                return;
            }

            int x = visible.X - formOrigin.X;
            int y = visible.Y - formOrigin.Y;
            var properties = new Native.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_VISIBLE |
                          Native.DWM_TNP_OPACITY | Native.DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = new Native.RECT(x, y, x + visible.Width, y + visible.Height),
                opacity = 255,
                fVisible = true,
                fSourceClientAreaOnly = true
            };

            if (visible.Width < thumbScreen.Width || visible.Height < thumbScreen.Height)
            {
                Native.RECT client;
                if (!Native.GetClientRect(_info.Handle, out client) ||
                    client.Width <= 0 || client.Height <= 0)
                {
                    properties.fVisible = false;
                    Native.DwmUpdateThumbnailProperties(_thumbnailId, ref properties);
                    return;
                }

                double left = (visible.Left - thumbScreen.Left) / (double)thumbScreen.Width;
                double topFraction = (visible.Top - thumbScreen.Top) / (double)thumbScreen.Height;
                double right = left + visible.Width / (double)thumbScreen.Width;
                double bottom = topFraction + visible.Height / (double)thumbScreen.Height;

                int sourceLeft = (int)Math.Round(left * client.Width);
                int sourceTop = (int)Math.Round(topFraction * client.Height);
                int sourceRight = (int)Math.Round(right * client.Width);
                int sourceBottom = (int)Math.Round(bottom * client.Height);
                sourceLeft = Math.Max(0, Math.Min(client.Width - 1, sourceLeft));
                sourceTop = Math.Max(0, Math.Min(client.Height - 1, sourceTop));
                sourceRight = Math.Max(sourceLeft + 1, Math.Min(client.Width, sourceRight));
                sourceBottom = Math.Max(sourceTop + 1, Math.Min(client.Height, sourceBottom));

                properties.rcSource = new Native.RECT(
                    sourceLeft, sourceTop, sourceRight, sourceBottom);
                properties.dwFlags |= Native.DWM_TNP_SOURCERECT;
            }

            Native.DwmUpdateThumbnailProperties(_thumbnailId, ref properties);
        }

        public void ApplyTheme()
        {
            BackColor = DockTheme.Backdrop;
            _thumb.BackColor = DockTheme.ThumbTop;
            Invalidate();
            _thumb.Invalidate();
        }


        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyThumbnail();
            UpdateThumbnailDestination();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReleaseThumbnail();
                DisposeStaticPreview();
                if (_icon != null)
                {
                    _icon.Dispose();
                    _icon = null;
                }
                _placeholderFont.Dispose();
            }

            base.Dispose(disposing);
        }

        private void ThumbPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle area = _thumb.ClientRectangle;
            if (area.Width <= 0 || area.Height <= 0)
            {
                return;
            }

            if (_staticPreview != null)
            {
                PaintStaticPreview(g, area);
                return;
            }

            if (_live && _thumbVisible)
            {
                return;
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new LinearGradientBrush(
                area, DockTheme.ThumbTop, DockTheme.ThumbBottom, 90f))
            {
                g.FillRectangle(brush, area);
            }

            string label = _info.IsMinimized
                ? "已最小化"
                : (_staticMode ? "该窗口不支持实时预览" : (!_live ? "预览暂不可用" : null));
            if (label != null)
            {
                TextRenderer.DrawText(
                    g,
                    label,
                    _placeholderFont,
                    area,
                    DockTheme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }

        private void PaintStaticPreview(Graphics g, Rectangle area)
        {
            if (_staticScaled == null ||
                _staticScaled.Width != area.Width ||
                _staticScaled.Height != area.Height)
            {
                DisposeScaledPreview();
                try
                {
                    _staticScaled = new Bitmap(
                        area.Width, area.Height, PixelFormat.Format32bppPArgb);
                    using (var scaledGraphics = Graphics.FromImage(_staticScaled))
                    {
                        scaledGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        scaledGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        scaledGraphics.DrawImage(
                            _staticPreview,
                            new Rectangle(0, 0, area.Width, area.Height));
                    }
                }
                catch
                {
                    DisposeScaledPreview();
                    return;
                }
            }

            g.DrawImageUnscaled(_staticScaled, area.Left, area.Top);
        }

        private void UseStaticPreview()
        {
            _staticMode = true;
            Bitmap capture = WindowCapture.CaptureClient(_info.Handle);
            if (capture != null && WindowCapture.IsBlank(capture))
            {
                capture.Dispose();
                capture = null;
            }
            _staticPreview = capture;
            _staticSignature = capture != null ? WindowCapture.ComputeSignature(capture) : 0;
            _thumb.HasContent = capture != null;
            _lastStaticCaptureUtc = DateTime.UtcNow;
            _live = false;
            _thumb.Live = false;
            UpdateThumbnailDestination();
            _thumb.Invalidate();
        }

        private void DisposeScaledPreview()
        {
            if (_staticScaled != null)
            {
                _staticScaled.Dispose();
                _staticScaled = null;
            }
        }

        private void DisposeStaticPreview()
        {
            DisposeScaledPreview();
            if (_staticPreview != null)
            {
                _staticPreview.Dispose();
                _staticPreview = null;
            }
            _staticSignature = 0;
            _thumb.HasContent = false;
        }

        private void ApplyThumbnail()
        {
            ReleaseThumbnail();
            DisposeStaticPreview();
            _staticMode = false;

            Control top = TopLevelControl;
            if (_info.IsMinimized || !Native.IsWindow(_info.Handle) ||
                top == null || !top.IsHandleCreated)
            {
                _thumb.Live = false;
                _thumb.Invalidate();
                return;
            }

            if (_info.IsNoRedirectionBitmap)
            {
                UseStaticPreview();
                return;
            }

            try
            {
                IntPtr destination = top.Handle;
                IntPtr id;
                int hr = Native.DwmRegisterThumbnail(destination, _info.Handle, out id);
                if (hr == 0 && id != IntPtr.Zero)
                {
                    _thumbnailId = id;
                    _live = true;
                    UpdateThumbnailDestination();
                }
                else
                {
                    Program.LogText(
                        "DwmRegisterThumbnail failed hr=0x" + hr.ToString("X8") +
                        " src=0x" + _info.Handle.ToInt64().ToString("X") +
                        " dst=0x" + destination.ToInt64().ToString("X"));
                    UseStaticPreview();
                    return;
                }
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                UseStaticPreview();
                return;
            }

            _thumb.Live = _live;
            _thumb.Invalidate();
        }

        private void ReleaseThumbnail()
        {
            if (_thumbnailId != IntPtr.Zero)
            {
                Native.DwmUnregisterThumbnail(_thumbnailId);
                _thumbnailId = IntPtr.Zero;
            }
            _live = false;
            _thumb.Live = false;
        }

        private void LoadWindowIcon()
        {
            IntPtr handle = Native.SendMessage(_info.Handle, Native.WM_GETICON, (IntPtr)Native.ICON_BIG, IntPtr.Zero);
            if (handle == IntPtr.Zero)
            {
                handle = Native.SendMessage(_info.Handle, Native.WM_GETICON, (IntPtr)Native.ICON_SMALL2, IntPtr.Zero);
            }
            if (handle == IntPtr.Zero)
            {
                handle = Native.GetClassLongPtr(_info.Handle, Native.GCLP_HICONSM);
            }
            if (handle == IntPtr.Zero)
            {
                handle = Native.GetClassLongPtr(_info.Handle, Native.GCLP_HICON);
            }
            if (handle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                using (Icon icon = Icon.FromHandle(handle))
                {
                    _icon = icon.ToBitmap();
                }
            }
            catch
            {
            }
        }

        private void UpdateTooltip()
        {
            string text = _info.Title;
            if (!string.IsNullOrEmpty(_info.ProcessName))
            {
                text += Environment.NewLine + _info.ProcessName;
            }
            if (_info.IsMinimized)
            {
                text += Environment.NewLine + "窗口已最小化";
            }
            if (RequiresElevation)
            {
                text += Environment.NewLine + "需要以管理员身份运行才能切换";
            }
            _toolTip.SetToolTip(this, text);
            _toolTip.SetToolTip(_thumb, text);
        }

        private void OnCardMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                EventHandler contextHandler = ContextMenuRequested;
                if (contextHandler != null)
                {
                    contextHandler(this, EventArgs.Empty);
                }
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                EventHandler handler = Clicked;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        private void SetHover(bool value)
        {
            if (_hover == value)
            {
                return;
            }
            _hover = value;
            Invalidate();

            EventHandler handler = HoverChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void OnAreaLeave()
        {
            if (!ClientRectangle.Contains(PointToClient(Cursor.Position)))
            {
                SetHover(false);
            }
        }

        private int Px(int logicalPixels)
        {
            return (int)Math.Round(logicalPixels * _dpi / 96.0);
        }

        private static Font CreateUiFont(float size, FontStyle style)
        {
            try
            {
                return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
            }
        }

        private sealed class ThumbSurface : Panel
        {
            public ThumbSurface()
            {
                DoubleBuffered = true;
            }

            public bool Live { get; set; }
            public bool HasContent { get; set; }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                if (!Live && !HasContent)
                {
                    base.OnPaintBackground(e);
                }
            }
        }
    }
}
