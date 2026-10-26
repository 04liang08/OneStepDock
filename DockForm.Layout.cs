using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace OneStepDock
{
    public sealed partial class DockForm
    {
        private void LayoutCards()
        {
            if (_list == null || _layoutSuspended)
            {
                return;
            }

            _layoutSuspended = true;
            _list.SuspendLayout();
            try
            {
                int pad = Px(10);
                int gap = Px(8);
                int cardWidth = Math.Max(Px(120), _list.ClientSize.Width - pad * 2 - Px(10));
                int total = Px(10);
                foreach (ThumbnailCard card in _cards)
                {
                    card.Fit(cardWidth, CurrentDpi);
                    total += card.Height + gap;
                }
                if (_cards.Count > 0)
                {
                    total -= gap;
                }
                total += Px(10);

                _contentHeight = _cards.Count == 0 ? 0 : total;
                int maxScroll = Math.Max(0, _contentHeight - _list.ClientSize.Height);
                _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, maxScroll));
                RepositionCardsCore(pad, gap);
            }
            finally
            {
                _list.ResumeLayout();
                _layoutSuspended = false;
            }

            _list.Invalidate();
            RefreshThumbnailPositions();
            RefreshOverlay();
        }

        private void RepositionCardsCore(int pad, int gap)
        {
            int y = Px(10) - _scrollOffset;
            foreach (ThumbnailCard card in _cards)
            {
                card.Location = new Point(pad, y);
                y += card.Height + gap;
            }
        }

        private void ScrollDock(int wheelDelta)
        {
            int max = Math.Max(0, _contentHeight - _list.ClientSize.Height);
            if (max <= 0)
            {
                return;
            }

            EndPeek();

            int target = _scrollOffset - Math.Sign(wheelDelta) * Px(90);
            target = Math.Max(0, Math.Min(max, target));
            if (target == _scrollOffset)
            {
                return;
            }

            _scrollOffset = target;
            RepositionCardsCore(Px(10), Px(8));
            _list.Invalidate();
            RefreshThumbnailPositions();
            RefreshOverlay();
        }

        private void ListPaint(object sender, PaintEventArgs e)
        {
            Rectangle client = _list.ClientRectangle;
            if (client.Width <= 0 || client.Height <= 0)
            {
                return;
            }

            using (var brush = new SolidBrush(DockTheme.Backdrop))
            {
                e.Graphics.FillRectangle(brush, client);
            }

            bool hasWindowCards = false;
            foreach (ThumbnailCard card in _cards)
            {
                if (!card.IsDesktop)
                {
                    hasWindowCards = true;
                    break;
                }
            }

            if (!hasWindowCards)
            {
                int hintTop = Px(40);
                if (_cards.Count > 0)
                {
                    hintTop = _cards[0].Bottom + Px(12);
                }
                var hintRect = new Rectangle(Px(16), hintTop, client.Width - Px(32), Px(40));
                TextRenderer.DrawText(
                    e.Graphics,
                    "暂无其他窗口",
                    Font,
                    hintRect,
                    DockTheme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
                return;
            }

            int trackTop;
            int trackHeight;
            int thumbHeight;
            int maxScroll;
            if (!TryGetScrollMetrics(out trackTop, out trackHeight, out thumbHeight, out maxScroll))
            {
                return;
            }

            int scrolled = Math.Max(0, Math.Min(maxScroll, _scrollOffset));
            int thumbTop = trackTop + (int)Math.Round(
                (trackHeight - thumbHeight) * (double)scrolled / maxScroll);
            bool active = _scrollbarHot || _scrollDragging;
            int thumbWidth = active ? Px(5) : Px(3);
            var indicator = new Rectangle(
                client.Width - Px(7) - (thumbWidth - Px(3)) / 2,
                thumbTop,
                thumbWidth,
                thumbHeight);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = CreateRoundedPath(indicator, Px(2)))
            using (var brush = new SolidBrush(
                Color.FromArgb(active ? 170 : 115, DockTheme.Indicator)))
            {
                e.Graphics.FillPath(brush, path);
            }
        }

        private bool TryGetScrollMetrics(
            out int trackTop,
            out int trackHeight,
            out int thumbHeight,
            out int maxScroll)
        {
            trackTop = Px(10);
            trackHeight = 0;
            thumbHeight = 0;
            maxScroll = 0;

            if (_list == null)
            {
                return false;
            }

            int viewport = _list.ClientSize.Height;
            int content = _contentHeight;
            if (content <= viewport + 1)
            {
                return false;
            }

            maxScroll = content - viewport;
            trackHeight = viewport - Px(20);
            if (trackHeight <= Px(20))
            {
                return false;
            }

            thumbHeight = Math.Max(
                Px(26),
                (int)Math.Round(trackHeight * (double)viewport / content));
            thumbHeight = Math.Min(thumbHeight, trackHeight);
            return true;
        }

        private bool IsInScrollbarStrip(Point location)
        {
            return _list != null &&
                   location.X >= _list.ClientSize.Width - Px(18);
        }

        private void ListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !IsInScrollbarStrip(e.Location))
            {
                return;
            }

            int trackTop;
            int trackHeight;
            int thumbHeight;
            int maxScroll;
            if (!TryGetScrollMetrics(out trackTop, out trackHeight, out thumbHeight, out maxScroll))
            {
                return;
            }

            int thumbTop = trackTop + (int)Math.Round(
                (trackHeight - thumbHeight) * (double)_scrollOffset / maxScroll);
            _scrollDragOffset = e.Y >= thumbTop && e.Y <= thumbTop + thumbHeight
                ? e.Y - thumbTop
                : thumbHeight / 2;
            _scrollDragging = true;
            _list.Capture = true;
            _list.Cursor = Cursors.SizeNS;
            SetScrollFromMouse(e.Y, trackTop, trackHeight, thumbHeight, maxScroll);
            _list.Invalidate();
        }

        private void ListMouseMove(object sender, MouseEventArgs e)
        {
            if (_scrollDragging)
            {
                int trackTop;
                int trackHeight;
                int thumbHeight;
                int maxScroll;
                if (TryGetScrollMetrics(out trackTop, out trackHeight, out thumbHeight, out maxScroll))
                {
                    SetScrollFromMouse(e.Y, trackTop, trackHeight, thumbHeight, maxScroll);
                }
                return;
            }

            bool hot = IsInScrollbarStrip(e.Location);
            if (hot)
            {
                int trackTop;
                int trackHeight;
                int thumbHeight;
                int maxScroll;
                hot = TryGetScrollMetrics(out trackTop, out trackHeight, out thumbHeight, out maxScroll);
            }

            if (hot != _scrollbarHot)
            {
                _scrollbarHot = hot;
                _list.Cursor = hot ? Cursors.SizeNS : Cursors.Default;
                _list.Invalidate();
            }
        }

        private void ListMouseUp(object sender, MouseEventArgs e)
        {
            if (!_scrollDragging)
            {
                return;
            }

            _scrollDragging = false;
            _list.Capture = false;
            _list.Cursor = IsInScrollbarStrip(e.Location) ? Cursors.SizeNS : Cursors.Default;
            _list.Invalidate();
        }

        private void ListMouseLeave(object sender, EventArgs e)
        {
            if (_scrollDragging)
            {
                return;
            }

            if (_scrollbarHot)
            {
                _scrollbarHot = false;
                _list.Cursor = Cursors.Default;
                _list.Invalidate();
            }
        }

        private void SetScrollFromMouse(
            int y,
            int trackTop,
            int trackHeight,
            int thumbHeight,
            int maxScroll)
        {
            int travel = trackHeight - thumbHeight;
            if (travel <= 0 || maxScroll <= 0)
            {
                return;
            }

            int targetThumbTop = y - _scrollDragOffset;
            double ratio = (double)(targetThumbTop - trackTop) / travel;
            ratio = Math.Max(0, Math.Min(1, ratio));
            int newOffset = (int)Math.Round(maxScroll * ratio);
            if (newOffset == _scrollOffset)
            {
                return;
            }

            _scrollOffset = newOffset;
            RepositionCardsCore(Px(10), Px(8));
            _list.Invalidate();
            RefreshThumbnailPositions();
            RefreshOverlay();
        }

        private void ApplyTheme()
        {
            BackColor = DockTheme.Backdrop;
            _list.BackColor = DockTheme.Backdrop;
            foreach (ThumbnailCard card in _cards)
            {
                card.ApplyTheme();
            }
            _list.Invalidate();
            RefreshOverlay();
        }

        private void UpdateTintFromScreen()
        {
            if (!Visible || !IsHandleCreated || _list == null)
            {
                return;
            }

            try
            {
                int sampleWidth = Px(140);
                int gap = Px(6);
                int sampleHeight = Math.Min(Bounds.Height - Px(120), Px(480));
                int x = Bounds.Left - gap - sampleWidth;
                int y = Bounds.Top + (Bounds.Height - sampleHeight) / 2;
                if (x < 0 || sampleHeight < Px(60))
                {
                    return;
                }

                using (var bitmap = new Bitmap(sampleWidth, sampleHeight))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(
                        new Point(x, y),
                        Point.Empty,
                        new Size(sampleWidth, sampleHeight),
                        CopyPixelOperation.SourceCopy);
                    Color sampled = AverageColor(bitmap);
                    int difference =
                        Math.Abs(sampled.R - _lastSampledColor.R) +
                        Math.Abs(sampled.G - _lastSampledColor.G) +
                        Math.Abs(sampled.B - _lastSampledColor.B);
                    if (!_lastSampledColor.IsEmpty && difference < 12)
                    {
                        return;
                    }

                    _lastSampledColor = sampled;
                    DockTheme.ApplySample(sampled, _settings.ThemeMode);
                    ApplyTheme();
                }
            }
            catch (Exception ex)
            {
                Program.Log(ex);
            }
        }

        private void CreateOverlay()
        {
            if (_overlay != null || !Visible || !IsHandleCreated)
            {
                return;
            }

            _overlay = new IconOverlay(this);
            int width = ClientSize.Width;
            int height = ClientSize.Height;
            if (width > 0 && height > 0)
            {
                using (var empty = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                {
                    _overlay.UpdateBitmap(empty, RectangleToScreen(ClientRectangle).Location);
                }
            }
            _overlay.Show();
            RefreshOverlay();
        }

        private void RefreshOverlay()
        {
            if (_overlay == null || _overlay.IsDisposed || !_overlay.Visible || !Visible)
            {
                return;
            }

            int width = ClientSize.Width;
            int height = ClientSize.Height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            Point origin = RectangleToScreen(ClientRectangle).Location;
            int badgeSize = Px(28);
            int margin = Px(10);
            int iconSize = Px(19);

            try
            {
                using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);

                    foreach (ThumbnailCard card in _cards)
                    {
                        if (!card.PreviewVisible)
                        {
                            continue;
                        }

                        Rectangle preview = card.PreviewScreenBounds;
                        var badgeRect = new Rectangle(
                            preview.Right - margin - badgeSize - origin.X,
                            preview.Top + margin - origin.Y,
                            badgeSize,
                            badgeSize);

                        if (badgeRect.Right < 0 || badgeRect.Bottom < 0 ||
                            badgeRect.Left > width || badgeRect.Top > height)
                        {
                            continue;
                        }

                        using (GraphicsPath path = CreateRoundedPath(badgeRect, Px(7)))
                        using (var fill = new SolidBrush(Color.FromArgb(125, 10, 15, 22)))
                        {
                            g.FillPath(fill, path);
                        }

                        using (GraphicsPath path = CreateRoundedPath(badgeRect, Px(7)))
                        using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255), 1f))
                        {
                            g.DrawPath(pen, path);
                        }

                        Bitmap icon = card.AppIcon;
                        var iconRect = new Rectangle(
                            badgeRect.X + (badgeSize - iconSize) / 2,
                            badgeRect.Y + (badgeSize - iconSize) / 2,
                            iconSize,
                            iconSize);

                        if (icon != null)
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(icon, iconRect);
                        }
                        else
                        {
                            var glyph = iconRect;
                            glyph.Inflate(-Px(2), -Px(3));
                            if (glyph.Width > Px(4) && glyph.Height > Px(4))
                            {
                                using (var pen = new Pen(Color.FromArgb(190, 255, 255, 255), 1f))
                                {
                                    g.DrawRectangle(pen, glyph);
                                    g.DrawLine(
                                        pen,
                                        glyph.Left,
                                        glyph.Top + glyph.Height / 4,
                                        glyph.Right,
                                        glyph.Top + glyph.Height / 4);
                                }
                            }
                        }

                        string title = card.Title;
                        if (card.IsHovered && !string.IsNullOrEmpty(title))
                        {
                            int titleFontPx = Math.Max(Px(12), LabelFontPixel());
                            int titleHeight = titleFontPx + Px(5);
                            int bandHeight = titleFontPx + Px(18);

                            var scrimRect = new Rectangle(
                                preview.Left - origin.X,
                                preview.Bottom - origin.Y - bandHeight,
                                preview.Width,
                                bandHeight);

                            if (scrimRect.Width > 0 && scrimRect.Height > 0)
                            {
                                using (var scrim = new LinearGradientBrush(
                                    scrimRect,
                                    Color.Transparent,
                                    Color.FromArgb(175, 7, 10, 15),
                                    90f))
                                {
                                    g.FillRectangle(scrim, scrimRect);
                                }

                                using (Font titleFont = CreateOverlayFont(titleFontPx))
                                using (var titleBrush = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
                                using (var titleFormat = new StringFormat
                                {
                                    Trimming = StringTrimming.EllipsisCharacter,
                                    FormatFlags = StringFormatFlags.NoWrap,
                                    Alignment = StringAlignment.Near,
                                    LineAlignment = StringAlignment.Center
                                })
                                {
                                    var titleRect = new RectangleF(
                                        preview.Left + Px(12) - origin.X,
                                        preview.Bottom - origin.Y - Px(9) - titleHeight,
                                        preview.Width - Px(24),
                                        titleHeight);
                                    g.DrawString(title, titleFont, titleBrush, titleRect, titleFormat);
                                }
                            }
                        }

                        if (card.RequiresElevation)
                        {
                            int chipSize = Px(24);
                            var chip = new Rectangle(
                                preview.Left + Px(10) - origin.X,
                                preview.Top + Px(10) - origin.Y,
                                chipSize,
                                chipSize);

                            using (GraphicsPath chipPath = CreateRoundedPath(chip, Px(7)))
                            using (var chipBrush = new SolidBrush(Color.FromArgb(120, 10, 15, 22)))
                            {
                                g.FillPath(chipBrush, chipPath);
                            }

                            using (var lockPen = new Pen(Color.FromArgb(215, 255, 255, 255), 1.4f))
                            {
                                g.DrawArc(
                                    lockPen,
                                    chip.X + chipSize / 2 - Px(4),
                                    chip.Y + Px(7),
                                    Px(8),
                                    Px(8),
                                    180,
                                    180);
                                g.DrawRectangle(
                                    lockPen,
                                    chip.X + Px(7),
                                    chip.Y + Px(12),
                                    Px(10),
                                    Px(7));
                            }
                        }

                        if (card.HasLabel && !card.IsHovered)
                        {
                            DrawCardLabel(g, card.Label, preview, origin, card.LabelIsCustom);
                        }

                        var previewLocal = new Rectangle(
                            preview.Left - origin.X,
                            preview.Top - origin.Y,
                            preview.Width,
                            preview.Height);
                        int cornerRadius = Px(9);
                        using (var maskPath = new GraphicsPath())
                        {
                            maskPath.FillMode = FillMode.Alternate;
                            maskPath.AddRectangle(previewLocal);
                            using (GraphicsPath rounded = CreateRoundedPath(previewLocal, cornerRadius))
                            {
                                maskPath.AddPath(rounded, false);
                            }
                            using (var maskBrush = new SolidBrush(DockTheme.Backdrop))
                            {
                                g.FillPath(maskBrush, maskPath);
                            }
                        }

                        using (GraphicsPath borderPath = CreateRoundedPath(previewLocal, cornerRadius))
                        using (var borderPen = new Pen(
                            card.IsHovered || card.IsDragging
                                ? Color.FromArgb(210, DockTheme.Accent)
                                : DockTheme.Border,
                            1f))
                        {
                            g.DrawPath(borderPen, borderPath);
                        }
                    }

                    _overlay.UpdateBitmap(bitmap, origin);
                }
            }
            catch (Exception ex)
            {
                Program.Log(ex);
            }
        }

        private void DrawCardLabel(Graphics g, string text, Rectangle preview, Point origin, bool isCustom)
        {
            if (string.IsNullOrEmpty(text) || preview.Width <= 0)
            {
                return;
            }

            int fontSize = LabelFontPixel();
            int padX = Math.Max(Px(6), (int)Math.Round(fontSize * 0.70f));
            int padY = Math.Max(Px(2), (int)Math.Round(fontSize * 0.24f));
            int radius = Math.Max(Px(5), (int)Math.Round(fontSize * 0.42f));
            int margin = Math.Max(Px(8), (int)Math.Round(fontSize * 0.55f));
            int maxWidth = Math.Max(Px(40), preview.Width - margin * 2);

            using (Font font = CreateOverlayFont(fontSize, isCustom ? FontStyle.Bold : FontStyle.Regular))
            using (var format = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center
            })
            {
                int maxTextWidth = Math.Max(Px(20), maxWidth - padX * 2);
                SizeF measured = g.MeasureString(
                    text, font, new SizeF(maxTextWidth, fontSize * 2.2f), format);

                int width = Math.Min(
                    maxWidth,
                    Math.Max(Px(26), (int)Math.Ceiling(measured.Width) + padX * 2));
                int height = Math.Max(
                    fontSize + padY * 2,
                    (int)Math.Ceiling(measured.Height) + padY);

                var rect = new Rectangle(
                    preview.Left - origin.X + margin,
                    preview.Bottom - origin.Y - margin - height,
                    width,
                    height);

                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = CreateRoundedPath(rect, radius))
                using (var fill = new SolidBrush(isCustom
                    ? Color.FromArgb(196, 20, 40, 76)
                    : Color.FromArgb(158, 8, 12, 18)))
                {
                    g.FillPath(fill, path);
                }

                using (GraphicsPath path = CreateRoundedPath(rect, radius))
                using (var pen = new Pen(isCustom
                    ? Color.FromArgb(180, DockTheme.Accent)
                    : Color.FromArgb(60, 255, 255, 255), isCustom ? 1.4f : 1f))
                {
                    g.DrawPath(pen, path);
                }

                var textRect = new Rectangle(
                    rect.Left + padX,
                    rect.Top,
                    Math.Max(Px(10), rect.Width - padX * 2),
                    rect.Height);
                using (var brush = new SolidBrush(Color.FromArgb(252, 255, 255, 255)))
                {
                    g.DrawString(text, font, brush, textRect, format);
                }
            }
        }

        /// <summary>标签字号换算成设备像素，绘制标签与悬停标题共用。</summary>
        private int LabelFontPixel()
        {
            int size = _settings.LabelFontSize;
            if (size < AppSettings.MinLabelFontSize)
            {
                size = AppSettings.MinLabelFontSize;
            }
            else if (size > AppSettings.MaxLabelFontSize)
            {
                size = AppSettings.MaxLabelFontSize;
            }

            return Math.Max(6, Px(size));
        }

        private static Font CreateOverlayFont(int pixelHeight)
        {
            return CreateOverlayFont(pixelHeight, FontStyle.Regular);
        }

        private static Font CreateOverlayFont(int pixelHeight, FontStyle style)
        {
            try
            {
                return new Font("Microsoft YaHei UI", pixelHeight, style, GraphicsUnit.Pixel);
            }
            catch
            {
                try
                {
                    return new Font(FontFamily.GenericSansSerif, pixelHeight, style, GraphicsUnit.Pixel);
                }
                catch
                {
                    return new Font(
                        FontFamily.GenericSansSerif,
                        pixelHeight,
                        FontStyle.Regular,
                        GraphicsUnit.Pixel);
                }
            }
        }

        private static Color AverageColor(Bitmap bitmap)
        {
            long red = 0;
            long green = 0;
            long blue = 0;
            long count = 0;

            for (int y = 0; y < bitmap.Height; y += 5)
            {
                for (int x = 0; x < bitmap.Width; x += 5)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    red += pixel.R;
                    green += pixel.G;
                    blue += pixel.B;
                    count++;
                }
            }

            if (count == 0)
            {
                return Color.FromArgb(238, 241, 246);
            }

            return Color.FromArgb(
                (int)(red / count),
                (int)(green / count),
                (int)(blue / count));
        }
    }
}
