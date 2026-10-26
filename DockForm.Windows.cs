using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OneStepDock
{
    public sealed partial class DockForm
    {
        private void RefreshWindows()
        {
            if (!Visible || _exiting)
            {
                return;
            }

            try
            {
                uint selfPid = (uint)Process.GetCurrentProcess().Id;
                IntPtr rawForeground = Native.GetForegroundWindow();
                bool foregroundIsOurs = WindowScanner.IsOwnWindow(rawForeground, selfPid);

                if (_settings.StaticPreviewRefreshMode == StaticRefreshOnDemand &&
                    _lastPreviewForeground != IntPtr.Zero &&
                    rawForeground != _lastPreviewForeground)
                {
                    RefreshStaticPreviewForWindow(_lastPreviewForeground);
                }
                _lastPreviewForeground = rawForeground;

                if (!foregroundIsOurs && rawForeground != IntPtr.Zero && !_menu.Visible)
                {
                    bool foregroundChanged = rawForeground != _lastTintForeground;
                    bool sampleDue = (DateTime.UtcNow - _lastTintSampleUtc).TotalSeconds >= 6;
                    if (foregroundChanged || sampleDue)
                    {
                        _lastTintForeground = rawForeground;
                        _lastTintSampleUtc = DateTime.UtcNow;
                        UpdateTintFromScreen();
                    }
                }

                IntPtr foreground = foregroundIsOurs ? IntPtr.Zero : rawForeground;
                List<WindowItem> items = WindowScanner.Scan(foreground, selfPid);

                if (_settings.ExcludedApps.Count > 0)
                {
                    items.RemoveAll(item => ContainsApp(_settings.ExcludedApps, item.ProcessName));
                }

                if (_settings.PinnedApps.Count > 0)
                {
                    items = SortPinnedFirst(items);
                }

                UpdateCards(items);

                if (_settings.StaticPreviewRefreshMode == StaticRefreshInterval)
                {
                    DateTime now = DateTime.UtcNow;
                    foreach (ThumbnailCard card in _cards)
                    {
                        card.RefreshStaticPreviewIfDue(now);
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log(ex);
            }
        }

        private void RefreshStaticPreviewForWindow(IntPtr hWnd)
        {
            foreach (ThumbnailCard card in _cards)
            {
                if (card.WindowHandle == hWnd)
                {
                    card.RefreshStaticPreviewOnce();
                    break;
                }
            }
        }

        private void UpdateCards(List<WindowItem> items)
        {
            var existing = new Dictionary<IntPtr, ThumbnailCard>();
            foreach (ThumbnailCard card in _cards)
            {
                existing[card.WindowHandle] = card;
            }

            var desired = new List<ThumbnailCard>(items.Count);
            var added = new List<ThumbnailCard>();
            bool titleChanged = false;

            foreach (WindowItem item in items)
            {
                ThumbnailCard card;
                if (existing.TryGetValue(item.Handle, out card))
                {
                    string previousTitle = card.Title;
                    card.UpdateInfo(item);
                    if (!string.Equals(previousTitle, card.Title, StringComparison.Ordinal))
                    {
                        titleChanged = true;
                        if (_settings.StaticPreviewRefreshMode == StaticRefreshOnDemand)
                        {
                            card.RefreshStaticPreviewOnce();
                        }
                    }
                    existing.Remove(item.Handle);
                }
                else
                {
                    card = new ThumbnailCard(item, _toolTip);
                    card.Clicked += OnCardClicked;
                    card.HoverChanged += (s, e) => RefreshOverlay();
                    card.ContextMenuRequested += OnCardContextMenuRequested;
                    added.Add(card);
                }
                desired.Add(card);
            }

            bool orderChanged = desired.Count != _cards.Count;
            if (!orderChanged)
            {
                for (int i = 0; i < desired.Count; i++)
                {
                    if (desired[i].WindowHandle != _cards[i].WindowHandle)
                    {
                        orderChanged = true;
                        break;
                    }
                }
            }

            int removedCount = existing.Count;
            foreach (ThumbnailCard stale in existing.Values)
            {
                _list.Controls.Remove(stale);
                stale.Dispose();
            }

            _cards.Clear();
            _cards.AddRange(desired);

            if (added.Count > 0)
            {
                _list.SuspendLayout();
                foreach (ThumbnailCard card in added)
                {
                    _list.Controls.Add(card);
                }
                _list.ResumeLayout(false);
            }

            bool layoutNeeded = added.Count > 0 || removedCount > 0 || orderChanged;
            if (layoutNeeded)
            {
                LayoutCards();
            }
            else if (titleChanged)
            {
                RefreshOverlay();
            }

            if (_tray != null)
            {
                _tray.Text = _cards.Count == 0
                    ? "一步 · 没有其他窗口"
                    : "一步 · " + _cards.Count + " 个窗口";
            }
        }

        private List<WindowItem> SortPinnedFirst(List<WindowItem> items)
        {
            var ordered = new List<WindowItem>(items.Count);
            foreach (WindowItem item in items)
            {
                if (ContainsApp(_settings.PinnedApps, item.ProcessName))
                {
                    ordered.Add(item);
                }
            }
            foreach (WindowItem item in items)
            {
                if (!ContainsApp(_settings.PinnedApps, item.ProcessName))
                {
                    ordered.Add(item);
                }
            }
            return ordered;
        }

        private void OnCardClicked(object sender, EventArgs e)
        {
            var card = sender as ThumbnailCard;
            if (card == null)
            {
                return;
            }

            bool shift = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
            bool forceMaximize = (_settings.Mode == ModeMaximize) ^ shift;
            ActivateWindow(card.WindowHandle, forceMaximize);

            _quickRefreshTimer.Stop();
            _quickRefreshTimer.Start();
        }

        private void ActivateWindow(IntPtr hWnd, bool maximize)
        {
            if (!Native.IsWindow(hWnd))
            {
                return;
            }

            if (Native.IsIconic(hWnd))
            {
                Native.ShowWindow(hWnd, Native.SW_RESTORE);
            }

            if (maximize && !Native.IsZoomed(hWnd))
            {
                var placement = new Native.WINDOWPLACEMENT
                {
                    length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT))
                };
                if (Native.GetWindowPlacement(hWnd, ref placement))
                {
                    _savedPlacements[hWnd] = placement;
                }
                Native.ShowWindow(hWnd, Native.SW_MAXIMIZE);
            }

            ForceForeground(hWnd);
        }

        private void ForceForeground(IntPtr hWnd)
        {
            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground == hWnd)
            {
                return;
            }

            uint ignored;
            uint foregroundThread = foreground != IntPtr.Zero
                ? Native.GetWindowThreadProcessId(foreground, out ignored)
                : 0;
            uint thisThread = Native.GetWindowThreadProcessId(Handle, out ignored);
            bool attached = false;

            try
            {
                if (foregroundThread != 0 && foregroundThread != thisThread)
                {
                    attached = Native.AttachThreadInput(thisThread, foregroundThread, true);
                }

                Native.SetWindowPos(
                    hWnd,
                    Native.HWND_TOP,
                    0,
                    0,
                    0,
                    0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_SHOWWINDOW);

                bool activated = Native.SetForegroundWindow(hWnd);
                Native.BringWindowToTop(hWnd);

                if (!activated && Native.GetForegroundWindow() != hWnd)
                {
                    Native.keybd_event((byte)Native.VK_MENU, 0, 0, UIntPtr.Zero);
                    Native.keybd_event((byte)Native.VK_MENU, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Native.SetForegroundWindow(hWnd);
                }
            }
            finally
            {
                if (attached)
                {
                    Native.AttachThreadInput(thisThread, foregroundThread, false);
                }
            }
        }
    }
}
