using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace OneStepDock
{
    internal sealed class ThumbnailCard : Panel
    {
        private const int StaticPreviewIntervalMs = 4000;

        private readonly ThumbSurface _thumb;
        private readonly ToolTip _toolTip;
        private readonly Font _placeholderFont;
        private readonly Font _mainMessageFont;
        private WindowItem _info;
        private IntPtr _thumbnailId;
        private Bitmap _icon;
        private bool _isDesktop;
        private bool _hover;
        private bool _live;
        private bool _staticMode;
        private bool _thumbVisible = true;
        private bool _thumbFullyVisible = true;
        private bool _dragCandidate;
        private bool _dragging;
        private Point _dragStartScreen;
        private Control _dragSource;
        private DateTime _lastStaticCaptureUtc;
        private ulong _staticSignature;
        private Bitmap _staticPreview;
        private Bitmap _staticScaled;
        private Bitmap _desktopWallpaper;
        private Bitmap _desktopWallpaperScaled;
        private int _dpi = 96;
        private string _label;

        public event EventHandler Clicked;
        public event EventHandler HoverChanged;
        public event EventHandler ContextMenuRequested;
        public event EventHandler DragStarted;
        public event EventHandler DragMoved;
        public event EventHandler DragEnded;

        public bool IsDesktop
        {
            get { return _isDesktop; }
        }

        public bool IsDragging
        {
            get { return _dragging; }
        }

        public bool IsMainWindow
        {
            get { return !_isDesktop && _info.IsForeground; }
        }

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

        public string Label
        {
            get { return _label; }
        }

        public bool HasLabel
        {
            get { return !string.IsNullOrEmpty(_label); }
        }

        /// <summary>窗口身份键，用于持久化自定义标签。</summary>
        public string LabelKey { get; set; }

        /// <summary>当前标签是不是用户自定义的（决定画法）。</summary>
        public bool LabelIsCustom { get; set; }

        public void SetLabel(string label)
        {
            string value = string.IsNullOrEmpty(label) ? null : label;
            if (string.Equals(_label, value, StringComparison.Ordinal))
            {
                return;
            }

            _label = value;
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

        public ThumbnailCard(WindowItem info, ToolTip toolTip, bool desktop = false)
        {
            _info = info;
            _toolTip = toolTip;
            _isDesktop = desktop;
            _placeholderFont = CreateUiFont(8f, FontStyle.Regular);
            _mainMessageFont = CreateUiFont(9.5f, FontStyle.Regular);

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
            MouseDown += (s, e) => OnCardMouseDown(e, this);
            MouseMove += (s, e) => OnCardMouseMove(e);
            MouseUp += (s, e) => OnCardMouseUp(e);
            _thumb.MouseEnter += (s, e) => SetHover(true);
            _thumb.MouseLeave += (s, e) => OnAreaLeave();
            _thumb.MouseDown += (s, e) => OnCardMouseDown(e, _thumb);
            _thumb.MouseMove += (s, e) => OnCardMouseMove(e);
            _thumb.MouseUp += (s, e) => OnCardMouseUp(e);
            _thumb.Resize += (s, e) => UpdateThumbnailDestination();
            LocationChanged += (s, e) => UpdateThumbnailDestination();
            _thumb.LocationChanged += (s, e) => UpdateThumbnailDestination();

            if (_isDesktop)
            {
                _icon = CreateDesktopIcon();
                ReloadDesktopWallpaper();
            }
            else
            {
                LoadWindowIcon();
            }
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
                                     _info.IsNoRedirectionBitmap != info.IsNoRedirectionBitmap ||
                                     _info.IsForeground != info.IsForeground;
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
                if (_dragSource != null)
                {
                    if (!_dragSource.IsDisposed)
                    {
                        _dragSource.Capture = false;
                    }
                    _dragSource = null;
                }
                ReleaseThumbnail();
                DisposeStaticPreview();
                DisposeDesktopWallpaper();
                if (_icon != null)
                {
                    _icon.Dispose();
                    _icon = null;
                }
                _placeholderFont.Dispose();
                _mainMessageFont.Dispose();
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

            if (_isDesktop)
            {
                PaintDesktopPreview(g, area);
                return;
            }

            if (_info.IsForeground)
            {
                PaintMainWindow(g, area);
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

        private void PaintMainWindow(Graphics g, Rectangle area)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new LinearGradientBrush(
                area, DockTheme.ThumbTop, DockTheme.ThumbBottom, 90f))
            {
                g.FillRectangle(brush, area);
            }

            var textRect = new Rectangle(
                area.Left + Px(16),
                area.Top + Px(10),
                area.Width - Px(32),
                area.Height - Px(20));
            TextRenderer.DrawText(
                g,
                "当前窗口已在主窗口中打开",
                _mainMessageFont,
                textRect,
                DockTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
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

        public void ReloadDesktopWallpaper()
        {
            if (!_isDesktop)
            {
                return;
            }

            DisposeDesktopWallpaper();
            try
            {
                var buffer = new StringBuilder(1024);
                if (Native.SystemParametersInfo(
                    Native.SPI_GETDESKWALLPAPER,
                    (uint)buffer.Capacity,
                    buffer,
                    0))
                {
                    string path = buffer.ToString();
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        using (var stream = new FileStream(
                            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var image = Image.FromStream(stream))
                        {
                            _desktopWallpaper = new Bitmap(image);
                        }
                    }
                }
            }
            catch
            {
                DisposeDesktopWallpaper();
            }

            if (_thumb != null)
            {
                _thumb.HasContent = _desktopWallpaper != null;
                _thumb.Invalidate();
            }
            Invalidate();
        }

        private void PaintDesktopPreview(Graphics g, Rectangle area)
        {
            if (_desktopWallpaper != null)
            {
                if (_desktopWallpaperScaled == null ||
                    _desktopWallpaperScaled.Width != area.Width ||
                    _desktopWallpaperScaled.Height != area.Height)
                {
                    DisposeDesktopScaled();
                    try
                    {
                        _desktopWallpaperScaled = new Bitmap(
                            area.Width, area.Height, PixelFormat.Format32bppPArgb);
                        using (var scaled = Graphics.FromImage(_desktopWallpaperScaled))
                        {
                            scaled.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            scaled.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            scaled.DrawImage(
                                _desktopWallpaper,
                                new Rectangle(0, 0, area.Width, area.Height));
                        }
                    }
                    catch
                    {
                        DisposeDesktopScaled();
                    }
                }

                if (_desktopWallpaperScaled != null)
                {
                    g.DrawImageUnscaled(_desktopWallpaperScaled, area.Left, area.Top);
                    return;
                }
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new LinearGradientBrush(
                area, DockTheme.ThumbTop, DockTheme.ThumbBottom, 90f))
            {
                g.FillRectangle(brush, area);
            }

            int glyphWidth = Math.Min(area.Width - Px(40), Px(92));
            int glyphHeight = Math.Max(Px(30), glyphWidth * 3 / 5);
            var screen = new Rectangle(
                area.Left + (area.Width - glyphWidth) / 2,
                area.Top + (area.Height - glyphHeight) / 2 - Px(8),
                glyphWidth,
                glyphHeight);
            if (glyphWidth > Px(20) && glyphHeight > Px(16))
            {
                using (var pen = new Pen(
                    Color.FromArgb(150, DockTheme.Muted), Math.Max(1f, Px(1))))
                {
                    g.DrawRectangle(pen, screen);
                    g.DrawLine(
                        pen,
                        screen.Left + screen.Width / 2,
                        screen.Bottom,
                        screen.Left + screen.Width / 2,
                        screen.Bottom + Px(6));
                    g.DrawLine(
                        pen,
                        screen.Left + screen.Width / 2 - Px(10),
                        screen.Bottom + Px(6),
                        screen.Left + screen.Width / 2 + Px(10),
                        screen.Bottom + Px(6));
                }
            }

            TextRenderer.DrawText(
                g,
                "桌面",
                _placeholderFont,
                new Rectangle(
                    area.Left,
                    area.Top + area.Height / 2 + Px(18),
                    area.Width,
                    Px(20)),
                DockTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        private void DisposeDesktopScaled()
        {
            if (_desktopWallpaperScaled != null)
            {
                _desktopWallpaperScaled.Dispose();
                _desktopWallpaperScaled = null;
            }
        }

        private void DisposeDesktopWallpaper()
        {
            DisposeDesktopScaled();
            if (_desktopWallpaper != null)
            {
                _desktopWallpaper.Dispose();
                _desktopWallpaper = null;
            }
        }

        private static Bitmap CreateDesktopIcon()
        {
            var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var screen = new Rectangle(7, 12, 50, 33);
                using (var fill = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                using (GraphicsPath path = CreateRoundedPath(screen, 5))
                {
                    g.FillPath(fill, path);
                }
                using (var pen = new Pen(Color.White, 3.2f))
                using (GraphicsPath path = CreateRoundedPath(screen, 5))
                {
                    g.DrawPath(pen, path);
                }
                using (var pen = new Pen(Color.White, 3.2f))
                {
                    g.DrawLine(pen, 32, 45, 32, 52);
                    g.DrawLine(pen, 21, 53, 43, 53);
                }
            }
            return bitmap;
        }

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int diameter = Math.Max(1, radius * 2);
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
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
            if (_isDesktop)
            {
                _thumb.Live = false;
                _thumb.HasContent = _desktopWallpaper != null;
                _thumb.Invalidate();
                return;
            }

            ReleaseThumbnail();
            DisposeStaticPreview();
            _staticMode = false;

            if (_info.IsForeground)
            {
                _thumb.Live = false;
                _thumb.HasContent = false;
                _thumb.Invalidate();
                return;
            }

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
            if (_isDesktop)
            {
                const string desktopText = "桌面\n单击显示桌面，再次单击恢复窗口";
                _toolTip.SetToolTip(this, desktopText);
                _toolTip.SetToolTip(_thumb, desktopText);
                return;
            }

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

        private void OnCardMouseDown(MouseEventArgs e, Control source)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (!_dragging)
                {
                    EventHandler contextHandler = ContextMenuRequested;
                    if (contextHandler != null)
                    {
                        contextHandler(this, EventArgs.Empty);
                    }
                }
                return;
            }

            if (e.Button != MouseButtons.Left || _dragging)
            {
                return;
            }

            if (_isDesktop)
            {
                EventHandler desktopClick = Clicked;
                if (desktopClick != null)
                {
                    desktopClick(this, EventArgs.Empty);
                }
                return;
            }

            _dragCandidate = true;
            _dragStartScreen = Cursor.Position;
            _dragSource = source;
            source.Capture = true;
        }

        private void OnCardMouseMove(MouseEventArgs e)
        {
            if (!_dragCandidate && !_dragging)
            {
                return;
            }

            Point screen = Cursor.Position;
            if (!_dragging)
            {
                if (Math.Abs(screen.Y - _dragStartScreen.Y) < Px(4))
                {
                    return;
                }

                _dragging = true;
                Cursor = Cursors.SizeNS;
                _thumb.Cursor = Cursors.SizeNS;
                SetHover(true);

                EventHandler started = DragStarted;
                if (started != null)
                {
                    started(this, EventArgs.Empty);
                }
            }

            EventHandler moved = DragMoved;
            if (moved != null)
            {
                moved(this, EventArgs.Empty);
            }
        }

        private void OnCardMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (_dragging)
            {
                _dragging = false;
                _dragCandidate = false;
                ReleaseDragCapture();
                Cursor = Cursors.Hand;
                _thumb.Cursor = Cursors.Hand;

                EventHandler ended = DragEnded;
                if (ended != null)
                {
                    ended(this, EventArgs.Empty);
                }
                return;
            }

            if (_dragCandidate)
            {
                _dragCandidate = false;
                ReleaseDragCapture();

                EventHandler clicked = Clicked;
                if (clicked != null)
                {
                    clicked(this, EventArgs.Empty);
                }
            }
        }

        private void ReleaseDragCapture()
        {
            if (_dragSource != null)
            {
                if (!_dragSource.IsDisposed)
                {
                    _dragSource.Capture = false;
                }
                _dragSource = null;
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
            if (_dragging || _dragCandidate)
            {
                return;
            }

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
