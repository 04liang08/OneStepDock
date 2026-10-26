using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OneStepDock
{
    public sealed partial class DockForm
    {
        private readonly List<IntPtr> _windowOrder = new List<IntPtr>();
        private bool _windowOrderInitialized;
        private Dictionary<IntPtr, WindowItem> _itemsByHandle =
            new Dictionary<IntPtr, WindowItem>();

        private void RefreshWindows()
        {
            if (!Visible || _exiting || _dragCard != null)
            {
                return;
            }

            try
            {
                // 兜底：预览已结束但遮罩还在，就收掉，避免整屏一直压暗。
                if (!_peekActive && _peekMask != null &&
                    !_peekMask.IsDisposed && _peekMask.Visible)
                {
                    _peekMask.HideOverlay();
                }

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

                items = OrderItems(items);
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

        private void UpdateCards(List<WindowItem> orderedItems)
        {
            EnsureDesktopCard();

            Dictionary<IntPtr, WindowLabelInfo> labels = orderedItems.Count > 0
                ? WindowLabels.Build(
                    orderedItems, _settings.CustomLabels, _settings.ShowAmbiguityLabels)
                : null;
            bool labelChanged = false;

            var existing = new Dictionary<IntPtr, ThumbnailCard>();
            foreach (ThumbnailCard card in _cards)
            {
                if (!card.IsDesktop)
                {
                    existing[card.WindowHandle] = card;
                }
            }

            var desired = new List<ThumbnailCard>(orderedItems.Count + 1);
            var added = new List<ThumbnailCard>();
            bool titleChanged = false;

            desired.Add(_desktopCard);
            if (!_cards.Contains(_desktopCard))
            {
                added.Add(_desktopCard);
            }

            foreach (WindowItem item in orderedItems)
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
                    card.HoverChanged += OnCardHoverChanged;
                    card.ContextMenuRequested += OnCardContextMenuRequested;
                    card.DragStarted += OnCardDragStarted;
                    card.DragMoved += OnCardDragMoved;
                    card.DragEnded += OnCardDragEnded;
                    card.PointerMoved += OnCardPointerMoved;
                    card.CloseHotChanged += OnCardCloseHotChanged;
                    card.CloseRequested += OnCardCloseRequested;
                    added.Add(card);
                }

                desired.Add(card);

                string previousLabel = card.Label;
                bool previousCustom = card.LabelIsCustom;
                WindowLabelInfo info;
                if (labels != null && labels.TryGetValue(item.Handle, out info))
                {
                    card.LabelKey = info.Key;
                    card.LabelIsCustom = info.IsCustom;
                    card.SetLabel(info.Text);
                }
                else
                {
                    card.LabelKey = null;
                    card.LabelIsCustom = false;
                    card.SetLabel(null);
                }
                if (!string.Equals(previousLabel, card.Label, StringComparison.Ordinal) ||
                    previousCustom != card.LabelIsCustom)
                {
                    labelChanged = true;
                }
            }

            bool orderChanged = desired.Count != _cards.Count;
            if (!orderChanged)
            {
                for (int i = 0; i < desired.Count; i++)
                {
                    if (desired[i] != _cards[i])
                    {
                        orderChanged = true;
                        break;
                    }
                }
            }

            int removedCount = existing.Count;
            foreach (ThumbnailCard stale in existing.Values)
            {
                if (_dragCard == stale)
                {
                    _dragCard = null;
                }
                _list.Controls.Remove(stale);
                stale.Dispose();
            }

            _cards.Clear();
            _cards.AddRange(desired);

            if (_peekCard != null && !_cards.Contains(_peekCard))
            {
                EndPeek();
            }

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
            else if (titleChanged || labelChanged)
            {
                RefreshOverlay();
            }

            if (_tray != null)
            {
                int windowCount = 0;
                foreach (ThumbnailCard card in _cards)
                {
                    if (!card.IsDesktop)
                    {
                        windowCount++;
                    }
                }
                _tray.Text = windowCount == 0
                    ? "一步 · 桌面"
                    : "一步 · " + windowCount + " 个窗口";
            }
        }

        private void EnsureDesktopCard()
        {
            if (_desktopCard != null)
            {
                return;
            }

            var info = new WindowItem
            {
                Handle = IntPtr.Zero,
                Title = "桌面",
                ProcessName = "",
                IsMinimized = false,
                IsElevated = false,
                IsNoRedirectionBitmap = false,
                IsForeground = false
            };
            _desktopCard = new ThumbnailCard(info, _toolTip, true);
            _desktopCard.Clicked += OnCardClicked;
            _desktopCard.HoverChanged += OnCardHoverChanged;
            _desktopCard.ContextMenuRequested += OnCardContextMenuRequested;
        }

        private List<WindowItem> OrderItems(List<WindowItem> items)
        {
            var byHandle = new Dictionary<IntPtr, WindowItem>();
            foreach (WindowItem item in items)
            {
                byHandle[item.Handle] = item;
            }

            _windowOrder.RemoveAll(handle => !byHandle.ContainsKey(handle));

            if (!_windowOrderInitialized)
            {
                _windowOrder.Clear();
                foreach (WindowItem item in items)
                {
                    _windowOrder.Add(item.Handle);
                }
                _windowOrderInitialized = true;
            }
            else
            {
                var known = new HashSet<IntPtr>(_windowOrder);
                foreach (WindowItem item in items)
                {
                    if (known.Add(item.Handle))
                    {
                        _windowOrder.Add(item.Handle);
                    }
                }
            }

            _itemsByHandle = byHandle;

            var ordered = new List<WindowItem>(_windowOrder.Count);
            foreach (IntPtr handle in _windowOrder)
            {
                WindowItem item;
                if (byHandle.TryGetValue(handle, out item))
                {
                    ordered.Add(item);
                }
            }
            return ordered;
        }

        private void MoveProcessToTop(string process)
        {
            var moved = new List<IntPtr>();
            foreach (IntPtr handle in _windowOrder)
            {
                WindowItem item;
                if (_itemsByHandle.TryGetValue(handle, out item) &&
                    string.Equals(item.ProcessName, process, StringComparison.OrdinalIgnoreCase))
                {
                    moved.Add(handle);
                }
            }

            foreach (IntPtr handle in moved)
            {
                _windowOrder.Remove(handle);
            }
            _windowOrder.InsertRange(0, moved);
        }

        private void OnCardDragStarted(object sender, EventArgs e)
        {
            _dragCard = sender as ThumbnailCard;
            if (_dragCard == null)
            {
                return;
            }

            EndPeek();
            _toolTip.Active = false;
            RefreshOverlay();
        }

        private void OnCardDragMoved(object sender, EventArgs e)
        {
            if (_dragCard == null || !_dragCard.IsDragging)
            {
                return;
            }

            var windows = new List<ThumbnailCard>();
            foreach (ThumbnailCard card in _cards)
            {
                if (!card.IsDesktop)
                {
                    windows.Add(card);
                }
            }

            var others = windows.FindAll(card => card != _dragCard);
            if (others.Count == 0)
            {
                return;
            }

            int pointY = _list.PointToClient(Cursor.Position).Y;
            int target = 0;
            foreach (ThumbnailCard card in others)
            {
                if (pointY > card.Top + card.Height / 2)
                {
                    target++;
                }
            }
            target = Math.Max(0, Math.Min(others.Count, target));

            int current = windows.IndexOf(_dragCard);
            if (current < 0 || current == target)
            {
                return;
            }

            var reordered = new List<ThumbnailCard>(others);
            reordered.Insert(target, _dragCard);

            var newCards = new List<ThumbnailCard>(_cards.Count);
            if (_desktopCard != null)
            {
                newCards.Add(_desktopCard);
            }
            newCards.AddRange(reordered);

            _cards.Clear();
            _cards.AddRange(newCards);
            RepositionCardsCore(Px(10), Px(8));
            _list.Invalidate();
            RefreshThumbnailPositions();
            RefreshOverlay();
        }

        private void OnCardDragEnded(object sender, EventArgs e)
        {
            if (_dragCard == null)
            {
                return;
            }

            CommitDragOrder();
            _dragCard = null;
            _toolTip.Active = true;
            RefreshWindows();
            RefreshOverlay();
        }

        private void CommitDragOrder()
        {
            _windowOrder.Clear();
            foreach (ThumbnailCard card in _cards)
            {
                if (!card.IsDesktop)
                {
                    _windowOrder.Add(card.WindowHandle);
                }
            }
        }

        private void OnCardClicked(object sender, EventArgs e)
        {
            var card = sender as ThumbnailCard;
            if (card == null)
            {
                return;
            }

            EndPeek();

            if (card.IsDesktop)
            {
                ShowDesktop();
                return;
            }

            if (card.IsMainWindow)
            {
                return;
            }

            bool shift = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
            bool forceMaximize = (_settings.Mode == ModeMaximize) ^ shift;
            ActivateWindow(card.WindowHandle, forceMaximize);

            _quickRefreshTimer.Stop();
            _quickRefreshTimer.Start();
        }

        private void ShowDesktop()
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                {
                    return;
                }

                object shell = Activator.CreateInstance(shellType);
                try
                {
                    shellType.InvokeMember(
                        "ToggleDesktop", BindingFlags.InvokeMethod, null, shell, null);
                }
                finally
                {
                    if (shell != null && Marshal.IsComObject(shell))
                    {
                        Marshal.ReleaseComObject(shell);
                    }
                }

                _quickRefreshTimer.Stop();
                _quickRefreshTimer.Start();
            }
            catch (Exception ex)
            {
                Program.Log(ex);
            }
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
