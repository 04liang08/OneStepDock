using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OneStepDock
{
    public sealed partial class DockForm
    {
        private const int PeekDelayMs = 320;
        private const int PeekCloseIntervalMs = 120;

        // 指针停在侧栏空白处时允许的宽限时间。卡片之间的间隙很短，
        // 给一点缓冲就不会在横向穿行时把预览反复关掉又打开。
        private const int PeekIdleGraceMs = 240;

        private System.Windows.Forms.Timer _peekDelayTimer;
        private System.Windows.Forms.Timer _peekCloseTimer;
        private PeekOverlay _peekMask;
        private PeekThumbWindow _peekThumb;

        // 待触发的候选卡片（悬停中、等待延迟结束）。
        private ThumbnailCard _peekCard;

        // 当前正在显示预览的卡片。与 _peekCard 分开记录，否则切换目标时会把自己判成「已经在预览」。
        private ThumbnailCard _peekedCard;

        // 指针当前所在的卡片，供守护定时器交叉确认。
        private ThumbnailCard _hoverCard;

        private bool _peekActive;
        private Rectangle _peekHole;
        private IntPtr _peekRaisedHandle = IntPtr.Zero;
        private IntPtr _peekPriorForeground = IntPtr.Zero;

        // 指针开始悬在侧栏空白处的时间戳（TickCount），0 表示不在此状态。
        private int _peekIdleSince;

        private void InitializePeek()
        {
            _peekDelayTimer = new System.Windows.Forms.Timer { Interval = PeekDelayMs };
            _peekDelayTimer.Tick += (s, e) => StartPendingPeek();

            _peekCloseTimer = new System.Windows.Forms.Timer { Interval = PeekCloseIntervalMs };
            _peekCloseTimer.Tick += (s, e) => PollPeekClose();
        }

        private void OnCardHoverChanged(object sender, EventArgs e)
        {
            RefreshOverlay();

            var card = sender as ThumbnailCard;
            if (card == null)
            {
                return;
            }

            UpdatePeekHover(card, card.IsHovered);
        }

        private void UpdatePeekHover(ThumbnailCard card, bool hover)
        {
            if (card == null)
            {
                return;
            }

            if (_settings.HoverPreviewMode < 1 || _peekDelayTimer == null)
            {
                if (_peekActive)
                {
                    EndPeek();
                }
                return;
            }

            if (hover)
            {
                _hoverCard = card;

                if (!CanPeek(card))
                {
                    // 悬停到不能预览的卡片（桌面卡、主窗口卡、最小化窗口）时直接收起，
                    // 不再保留上一张卡的预览。
                    CancelPendingPeek();
                    if (_peekActive)
                    {
                        EndPeek();
                    }
                    return;
                }

                if (_peekActive && _peekedCard == card)
                {
                    _peekCard = card;
                    _peekDelayTimer.Stop();
                    return;
                }

                _peekCard = card;
                _peekDelayTimer.Stop();
                _peekDelayTimer.Start();
                return;
            }

            // MouseLeave 是异步投递的，从 A 移到 B 时 B 的 MouseEnter 可能先到。
            // 用指针位置再确认一次，避免把刚进入的卡片误判为离开。
            if (card.ClientRectangle.Contains(card.PointToClient(Cursor.Position)))
            {
                return;
            }

            if (_hoverCard == card)
            {
                _hoverCard = null;
            }

            if (_peekCard == card)
            {
                CancelPendingPeek();
            }
        }

        private void CancelPendingPeek()
        {
            _peekCard = null;
            if (_peekDelayTimer != null)
            {
                _peekDelayTimer.Stop();
            }
        }

        /// <summary>
        /// 预览显示期间的守护逻辑：不再依赖进入/离开事件的先后顺序，
        /// 统一由指针位置决定是保持、切换还是收起。
        /// </summary>
        private void PollPeekClose()
        {
            if (!_peekActive)
            {
                _peekCloseTimer.Stop();
                return;
            }

            Point cursor = Cursor.Position;

            // 指针进了预览区域：保持。
            if (_peekHole.Width > 0 && _peekHole.Contains(cursor))
            {
                _peekIdleSince = 0;
                return;
            }

            ThumbnailCard hover = _hoverCard;
            if (hover != null)
            {
                // 不完全信任 MouseLeave：万一事件漏发，用几何位置兜底，
                // 否则指针已经离开卡片，这里还会一直认为「仍停在原卡上」而不收起。
                if (hover.IsDisposed || !hover.IsHovered ||
                    !hover.RectangleToScreen(hover.ClientRectangle).Contains(cursor))
                {
                    hover = null;
                }
            }

            if (hover != null)
            {
                _peekIdleSince = 0;

                if (hover == _peekedCard)
                {
                    return;
                }

                if (CanPeek(hover))
                {
                    // 指针停在了另一张可预览的卡上，等延迟结束就切换。
                    // 若排队状态因为事件乱序丢了，这里补一次。
                    if (_peekCard != hover)
                    {
                        _peekCard = hover;
                        _peekDelayTimer.Stop();
                        _peekDelayTimer.Start();
                    }
                    return;
                }

                EndPeek();
                return;
            }

            // 不在任何卡片上：离开侧栏立刻收起，
            // 停在侧栏空白处则先等一小段，容忍卡片之间的间隙。
            if (!Bounds.Contains(cursor))
            {
                EndPeek();
                return;
            }

            if (_peekIdleSince == 0)
            {
                _peekIdleSince = Environment.TickCount;
                return;
            }

            if (unchecked(Environment.TickCount - _peekIdleSince) < PeekIdleGraceMs)
            {
                return;
            }

            EndPeek();
        }

        private bool CanPeek(ThumbnailCard card)
        {
            if (card == null || card.IsDisposed || card.IsDesktop || card.IsMainWindow)
            {
                return false;
            }

            if (_settings.HoverPreviewMode < 1)
            {
                return false;
            }

            if (_menu != null && _menu.Visible)
            {
                return false;
            }

            if (_dragCard != null || _scrollDragging || _draggingWidth)
            {
                return false;
            }

            if (!Visible || _exiting)
            {
                return false;
            }

            IntPtr handle = card.WindowHandle;
            if (handle == IntPtr.Zero || !Native.IsWindow(handle))
            {
                return false;
            }

            // 最小化窗口没有可用的真实位置和画面，不做悬停预览。
            if (Native.IsIconic(handle))
            {
                return false;
            }

            return true;
        }

        private void StartPendingPeek()
        {
            if (_peekDelayTimer != null)
            {
                _peekDelayTimer.Stop();
            }

            ThumbnailCard card = _peekCard;
            if (card == null || card.IsDisposed)
            {
                _peekCard = null;
                return;
            }

            // 已经在这张卡上显示预览，不需要重启。
            if (_peekActive && _peekedCard == card)
            {
                return;
            }

            // 延迟期间指针可能已经离开，用实时位置再确认一次，避免悬停过一串卡片后
            // 停在一张早已离开的卡上。
            if (!card.IsHovered || !CanPeek(card))
            {
                _peekCard = null;
                return;
            }

            StartPeek(card);
        }

        private void StartPeek(ThumbnailCard card)
        {
            IntPtr handle = card.WindowHandle;
            Rectangle frame = GetFrameBounds(handle);
            if (frame.Width <= 0 || frame.Height <= 0)
            {
                _peekCard = null;
                return;
            }

            Screen screen = Screen.FromHandle(handle) ?? Screen.PrimaryScreen;
            if (screen == null)
            {
                _peekCard = null;
                return;
            }

            Rectangle maskHole = Rectangle.Intersect(frame, screen.Bounds);
            if (maskHole.Width <= 0 || maskHole.Height <= 0)
            {
                _peekCard = null;
                return;
            }

            bool switching = _peekActive;
            int mode = _settings.HoverPreviewMode;

            try
            {
                if (_peekMask == null || _peekMask.IsDisposed)
                {
                    _peekMask = new PeekOverlay();
                }

                // 切换目标时原地改洞，不隐藏再显示，避免整屏遮罩闪一下。
                _peekMask.ShowFor(screen, maskHole);
                _peekHole = frame;

                if (mode == 2)
                {
                    // 切走时不能重新记录前台窗口，否则会丢掉最初的那个。
                    if (!switching)
                    {
                        _peekPriorForeground = Native.GetForegroundWindow();
                    }

                    if (_peekThumb != null && !_peekThumb.IsDisposed)
                    {
                        _peekThumb.Detach();
                    }

                    RaiseWithoutActivating(handle);
                    _peekRaisedHandle = handle;
                }
                else
                {
                    if (_peekRaisedHandle != IntPtr.Zero)
                    {
                        RestoreRaisedWindow(false);
                    }

                    if (_peekThumb == null || _peekThumb.IsDisposed)
                    {
                        _peekThumb = new PeekThumbWindow();
                        _peekThumb.Clicked += OnPeekThumbClicked;
                    }
                    _peekThumb.ShowFor(frame, handle);
                }

                _peekCard = card;
                _peekedCard = card;
                _peekActive = true;
                _peekIdleSince = 0;

                RaiseDockChrome();

                if (!switching)
                {
                    _peekCloseTimer.Stop();
                }
                _peekCloseTimer.Start();
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                EndPeek();
            }
        }

        private void RaiseWithoutActivating(IntPtr handle)
        {
            long exStyle = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64();
            bool topMost = (exStyle & Native.WS_EX_TOPMOST) != 0;

            IntPtr insertAfter = topMost && _peekMask != null && !_peekMask.IsDisposed
                ? _peekMask.Handle
                : Native.HWND_TOP;

            Native.SetWindowPos(
                handle,
                insertAfter,
                0,
                0,
                0,
                0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        private void OnPeekThumbClicked(object sender, EventArgs e)
        {
            ThumbnailCard card = _peekedCard;
            EndPeek();

            if (card == null || card.IsDesktop)
            {
                return;
            }

            ActivateWindow(card.WindowHandle, false);
            _quickRefreshTimer.Stop();
            _quickRefreshTimer.Start();
        }

        private void RaiseDockChrome()
        {
            if (IsHandleCreated && !_exiting)
            {
                Native.SetWindowPos(
                    Handle,
                    Native.HWND_TOPMOST,
                    0,
                    0,
                    0,
                    0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }

            if (_overlay != null && !_overlay.IsDisposed && _overlay.Visible &&
                _overlay.IsHandleCreated)
            {
                Native.SetWindowPos(
                    _overlay.Handle,
                    Native.HWND_TOPMOST,
                    0,
                    0,
                    0,
                    0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }

        private void RestoreRaisedWindow(bool requireSameForeground)
        {
            if (_peekRaisedHandle == IntPtr.Zero)
            {
                return;
            }

            IntPtr previous = _peekPriorForeground;
            _peekRaisedHandle = IntPtr.Zero;
            _peekPriorForeground = IntPtr.Zero;

            if (previous == IntPtr.Zero || !Native.IsWindow(previous))
            {
                return;
            }

            // 用户已经主动切换了前台窗口时不要再抢回来。
            if (requireSameForeground && Native.GetForegroundWindow() != previous)
            {
                return;
            }

            Native.SetWindowPos(
                previous,
                Native.HWND_TOP,
                0,
                0,
                0,
                0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        private void EndPeek()
        {
            if (_peekDelayTimer != null)
            {
                _peekDelayTimer.Stop();
            }
            if (_peekCloseTimer != null)
            {
                _peekCloseTimer.Stop();
            }

            if (!_peekActive)
            {
                _peekCard = null;
                _peekedCard = null;
                _peekIdleSince = 0;
                return;
            }

            _peekActive = false;
            _peekCard = null;
            _peekedCard = null;
            _peekHole = Rectangle.Empty;
            _peekIdleSince = 0;

            try
            {
                if (_peekThumb != null && !_peekThumb.IsDisposed)
                {
                    _peekThumb.Detach();
                }

                if (_peekMask != null && !_peekMask.IsDisposed)
                {
                    _peekMask.HideOverlay();
                }

                RestoreRaisedWindow(true);
            }
            catch (Exception ex)
            {
                Program.Log(ex);
            }
        }

        private void DisposePeek()
        {
            EndPeek();

            if (_peekDelayTimer != null)
            {
                _peekDelayTimer.Dispose();
                _peekDelayTimer = null;
            }

            if (_peekCloseTimer != null)
            {
                _peekCloseTimer.Dispose();
                _peekCloseTimer = null;
            }

            if (_peekThumb != null)
            {
                _peekThumb.Detach();
                _peekThumb.Dispose();
                _peekThumb = null;
            }

            if (_peekMask != null)
            {
                _peekMask.HideOverlay();
                _peekMask.Dispose();
                _peekMask = null;
            }
        }

        private static Rectangle GetFrameBounds(IntPtr handle)
        {
            Native.RECT frame;
            int size = Marshal.SizeOf(typeof(Native.RECT));
            if (Native.DwmGetWindowAttribute(
                    handle, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out frame, size) == 0)
            {
                var result = new Rectangle(frame.Left, frame.Top, frame.Width, frame.Height);
                if (result.Width > 0 && result.Height > 0)
                {
                    return result;
                }
            }

            Native.RECT window;
            if (Native.GetWindowRect(handle, out window))
            {
                return new Rectangle(window.Left, window.Top, window.Width, window.Height);
            }

            return Rectangle.Empty;
        }
    }
}
