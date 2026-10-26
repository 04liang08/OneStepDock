using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OneStepDock
{
    public sealed partial class DockForm : Form, IMessageFilter
    {
        private const int HotkeyId = 0x4F53;
        private const int ModeMaximize = 0;
        private const int ModeKeep = 1;
        private const int StaticRefreshInterval = 0;
        private const int StaticRefreshOnDemand = 1;

        private readonly AppSettings _settings;
        private readonly List<ThumbnailCard> _cards = new List<ThumbnailCard>();
        private readonly CardListPanel _list;
        private readonly ToolTip _toolTip = new ToolTip();
        private readonly System.Windows.Forms.Timer _scanTimer;
        private readonly System.Windows.Forms.Timer _quickRefreshTimer;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _showItem;
        private readonly ToolStripMenuItem _maxModeItem;
        private readonly ToolStripMenuItem _keepModeItem;
        private readonly ToolStripMenuItem _themeAutoItem;
        private readonly ToolStripMenuItem _themeLightItem;
        private readonly ToolStripMenuItem _themeDarkItem;
        private readonly ToolStripMenuItem _hotkeyItem;
        private readonly ToolStripMenuItem _autoStartItem;
        private readonly ToolStripMenuItem _restoreHiddenItem;
        private readonly ToolStripMenuItem _restartAdminItem;
        private readonly ToolStripMenuItem _restoreSizesItem;
        private readonly ToolStripMenuItem _monitorMenu;
        private readonly ToolStripMenuItem _staticRefreshIntervalItem;
        private readonly ToolStripMenuItem _staticRefreshOnDemandItem;
        private readonly ToolStripMenuItem _pinItem;
        private readonly ToolStripMenuItem _excludeItem;
        private readonly ToolStripSeparator _cardSeparator;
        private readonly ToolStripMenuItem _exitItem;
        private readonly Dictionary<IntPtr, Native.WINDOWPLACEMENT> _savedPlacements =
            new Dictionary<IntPtr, Native.WINDOWPLACEMENT>();

        private ThumbnailCard _menuCard;
        private IconOverlay _overlay;
        private bool _overlayHiddenForMenu;
        private NotifyIcon _tray;
        private Icon _trayIcon;
        private int _initialDpi;
        private bool _appBarAdded;
        private bool _hotkeyRegistered;
        private bool _initialized;
        private bool _layoutSuspended;
        private bool _resizingWidth;
        private bool _draggingWidth;
        private int _dragStartScreenX;
        private int _dragStartWidth;
        private int _scrollOffset;
        private int _contentHeight;
        private bool _scrollDragging;
        private bool _scrollbarHot;
        private int _scrollDragOffset;
        private IntPtr _lastTintForeground = IntPtr.Zero;
        private IntPtr _lastPreviewForeground = IntPtr.Zero;
        private DateTime _lastTintSampleUtc = DateTime.MinValue;
        private Color _lastSampledColor = Color.Empty;
        private bool _exiting;

        public DockForm()
        {
            _settings = SettingsStore.Load();
            DockTheme.ApplySample(Color.FromArgb(238, 241, 246), _settings.ThemeMode);

            Text = "一步";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = DockTheme.Backdrop;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            Font = CreateUiFont(9f, FontStyle.Regular);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            _list = new CardListPanel
            {
                Dock = DockStyle.Fill,
                BackColor = DockTheme.Backdrop
            };
            _list.Paint += ListPaint;
            _list.MouseDown += ChromeMouseDown;
            _list.MouseDown += ListMouseDown;
            _list.MouseMove += ListMouseMove;
            _list.MouseUp += ListMouseUp;
            _list.MouseLeave += ListMouseLeave;
            Controls.Add(_list);

            _toolTip.InitialDelay = 1200;
            _toolTip.ReshowDelay = 100;
            _toolTip.AutoPopDelay = 8000;
            _toolTip.ShowAlways = true;

            _menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                ShowCheckMargin = false,
                Font = CreateUiFont(9f, FontStyle.Regular)
            };
            _showItem = new ToolStripMenuItem("隐藏侧栏    Ctrl+Alt+Space");
            _showItem.Click += (s, e) => ToggleDock();
            _maxModeItem = new ToolStripMenuItem("切换时铺满主区");
            _maxModeItem.Click += (s, e) => SetMode(ModeMaximize);
            _keepModeItem = new ToolStripMenuItem("切换时保持原状");
            _keepModeItem.Click += (s, e) => SetMode(ModeKeep);
            var modeHintItem = new ToolStripMenuItem("Shift+单击 = 临时使用另一模式") { Enabled = false };

            var themeMenu = new ToolStripMenuItem("主题");
            _themeAutoItem = new ToolStripMenuItem("跟随主窗口");
            _themeAutoItem.Click += (s, e) => SetThemeMode(0);
            _themeLightItem = new ToolStripMenuItem("浅色");
            _themeLightItem.Click += (s, e) => SetThemeMode(1);
            _themeDarkItem = new ToolStripMenuItem("深色");
            _themeDarkItem.Click += (s, e) => SetThemeMode(2);
            themeMenu.DropDownItems.Add(_themeAutoItem);
            themeMenu.DropDownItems.Add(_themeLightItem);
            themeMenu.DropDownItems.Add(_themeDarkItem);

            var settingsMenu = new ToolStripMenuItem("设置");
            _hotkeyItem = new ToolStripMenuItem("设置快捷键…");
            _hotkeyItem.Click += (s, e) => BeginInvoke(new Action(ChangeHotkey));
            _autoStartItem = new ToolStripMenuItem("开机自启");
            _autoStartItem.Click += (s, e) => ToggleAutoStart();
            _restoreHiddenItem = new ToolStripMenuItem("恢复所有隐藏窗口");
            _restoreHiddenItem.Click += (s, e) => RestoreHiddenApps();
            _restartAdminItem = new ToolStripMenuItem("以管理员身份重启");
            _restartAdminItem.Click += (s, e) => BeginInvoke(new Action(RestartAsAdmin));
            _monitorMenu = new ToolStripMenuItem("停靠屏幕");
            var staticRefreshMenu = new ToolStripMenuItem("静态预览刷新");
            _staticRefreshIntervalItem = new ToolStripMenuItem("定时刷新（4 秒）");
            _staticRefreshIntervalItem.Click +=
                (s, e) => SetStaticPreviewRefreshMode(StaticRefreshInterval);
            _staticRefreshOnDemandItem = new ToolStripMenuItem("仅在切走或标题变化时");
            _staticRefreshOnDemandItem.Click +=
                (s, e) => SetStaticPreviewRefreshMode(StaticRefreshOnDemand);
            staticRefreshMenu.DropDownItems.Add(_staticRefreshIntervalItem);
            staticRefreshMenu.DropDownItems.Add(_staticRefreshOnDemandItem);
            staticRefreshMenu.DropDownItems.Add(new ToolStripSeparator());
            staticRefreshMenu.DropDownItems.Add(
                new ToolStripMenuItem("仅影响无法实时预览的窗口") { Enabled = false });
            _restoreSizesItem = new ToolStripMenuItem("还原被最大化的窗口");
            _restoreSizesItem.Click += (s, e) => RestoreWindowSizes();
            settingsMenu.DropDownItems.Add(_hotkeyItem);
            settingsMenu.DropDownItems.Add(_autoStartItem);
            settingsMenu.DropDownItems.Add(new ToolStripSeparator());
            settingsMenu.DropDownItems.Add(_monitorMenu);
            settingsMenu.DropDownItems.Add(staticRefreshMenu);
            settingsMenu.DropDownItems.Add(_restoreSizesItem);
            settingsMenu.DropDownItems.Add(_restoreHiddenItem);
            settingsMenu.DropDownItems.Add(new ToolStripSeparator());
            settingsMenu.DropDownItems.Add(_restartAdminItem);

            _pinItem = new ToolStripMenuItem("置顶该应用");
            _pinItem.Click += (s, e) => TogglePinnedApp();
            _excludeItem = new ToolStripMenuItem("隐藏该应用");
            _excludeItem.Click += (s, e) => ExcludeCurrentApp();
            _cardSeparator = new ToolStripSeparator();

            _exitItem = new ToolStripMenuItem("退出一步");
            _exitItem.Click += (s, e) =>
            {
                _exiting = true;
                Close();
            };

            _menu.Items.Add(_pinItem);
            _menu.Items.Add(_excludeItem);
            _menu.Items.Add(_cardSeparator);
            _menu.Items.Add(_showItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_maxModeItem);
            _menu.Items.Add(_keepModeItem);
            _menu.Items.Add(modeHintItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(themeMenu);
            _menu.Items.Add(settingsMenu);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_exitItem);
            _menu.Opening += (s, e) =>
            {
                if (!Bounds.Contains(Cursor.Position))
                {
                    _menuCard = null;
                }
                UpdateMenuState();
            };
            _menu.Opened += (s, e) =>
            {
                _toolTip.Active = false;
                if (_overlay == null)
                {
                    return;
                }

                if (Bounds.Contains(Cursor.Position))
                {
                    RefreshOverlay();
                }
                else
                {
                    _overlayHiddenForMenu = true;
                    _overlay.Hide();
                }
            };
            _menu.Closed += (s, e) =>
            {
                bool wasHidden = _overlayHiddenForMenu;
                _overlayHiddenForMenu = false;
                if (!_exiting && Visible && _overlay != null)
                {
                    if (wasHidden)
                    {
                        _overlay.Show();
                    }
                    RefreshOverlay();
                }
                _toolTip.Active = true;
            };

            BuildTray();
            UpdateMenuState();

            _scanTimer = new System.Windows.Forms.Timer { Interval = 700 };
            _scanTimer.Tick += (s, e) => RefreshWindows();

            _quickRefreshTimer = new System.Windows.Forms.Timer { Interval = 220 };
            _quickRefreshTimer.Tick += (s, e) =>
            {
                _quickRefreshTimer.Stop();
                RefreshWindows();
            };

            Application.AddMessageFilter(this);

            Rectangle screen = GetTargetScreen().Bounds;
            int width = Px(Math.Max(200, Math.Min(560, _settings.DockWidth)));
            Bounds = new Rectangle(screen.Right - width, screen.Top, width, screen.Height);
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _hotkeyRegistered = TryRegisterHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            LayoutChrome();
            ApplyAppBar();
            RefreshWindows();
            CreateOverlay();
            _scanTimer.Start();

            if (!_hotkeyRegistered && _tray != null)
            {
                _tray.ShowBalloonTip(
                    4000,
                    "一步",
                    "Ctrl+Alt+Space 注册失败，可能被其他程序占用。可从托盘图标显示或隐藏侧栏。",
                    ToolTipIcon.Warning);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_list == null)
            {
                return;
            }

            ClampWidth();
            if (!_draggingWidth)
            {
                LayoutCards();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                OpenMenu(this, e.Location);
                return;
            }

            if (e.Button == MouseButtons.Left && e.X <= Px(11))
            {
                _draggingWidth = true;
                _dragStartScreenX = Cursor.Position.X;
                _dragStartWidth = Width;
                Capture = true;
                Cursor = Cursors.SizeWE;
                _overlay?.Hide();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_draggingWidth)
            {
                int delta = Cursor.Position.X - _dragStartScreenX;
                int target = _dragStartWidth - delta;
                target = Math.Max(Px(200), Math.Min(Px(560), target));
                if (target != Width)
                {
                    Width = target;
                }
                return;
            }

            Cursor = e.X <= Px(11) ? Cursors.SizeWE : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_draggingWidth)
            {
                return;
            }

            _draggingWidth = false;
            Capture = false;
            Cursor = Cursors.Default;
            SaveWidth();
            ApplyAppBar();
            LayoutCards();
            if (_overlay != null)
            {
                _overlay.Show();
                RefreshOverlay();
            }
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (!_draggingWidth && _overlay != null && Visible)
            {
                RefreshOverlay();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_draggingWidth)
            {
                Cursor = Cursors.Default;
            }
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (!_initialized)
            {
                return;
            }

            LayoutChrome();
            ApplyAppBar();
            LayoutCards();
            RefreshOverlay();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                ToggleDock();
                return;
            }

            if (m.Msg == Native.WM_SETTINGCHANGE || m.Msg == Native.WM_DISPLAYCHANGE)
            {
                base.WndProc(ref m);
                if (!_exiting && Visible)
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (!_exiting && Visible)
                        {
                            ApplyAppBar();
                            RefreshWindows();
                        }
                    }));
                }
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _exiting = true;
            _scanTimer?.Stop();
            _quickRefreshTimer?.Stop();
            SaveWidth();
            RemoveAppBar();

            if (_hotkeyRegistered && IsHandleCreated)
            {
                Native.UnregisterHotKey(Handle, HotkeyId);
                _hotkeyRegistered = false;
            }

            Application.RemoveMessageFilter(this);
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }

            if (_trayIcon != null)
            {
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            if (_overlay != null)
            {
                _overlay.Hide();
                _overlay.Dispose();
                _overlay = null;
            }

            _scanTimer?.Dispose();
            _quickRefreshTimer?.Dispose();
            _menu?.Dispose();
            _toolTip?.Dispose();
            base.OnFormClosed(e);
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != Native.WM_MOUSEWHEEL || !Visible || _list == null)
            {
                return false;
            }

            if (!Bounds.Contains(Cursor.Position))
            {
                return false;
            }

            int delta = (short)((long)m.WParam >> 16);
            if (delta == 0)
            {
                return false;
            }

            ScrollDock(delta);
            return true;
        }

        private void ToggleDock()
        {
            if (_exiting)
            {
                return;
            }

            if (Visible)
            {
                _scanTimer.Stop();
                _overlay?.Hide();
                RemoveAppBar();
                Hide();
            }
            else
            {
                Show();
                LayoutChrome();
                ApplyAppBar();
                RefreshWindows();
                foreach (ThumbnailCard card in _cards)
                {
                    card.ReapplyThumbnail();
                }
                RefreshThumbnailPositions();
                if (_overlay == null)
                {
                    CreateOverlay();
                }
                else
                {
                    _overlay.Show();
                    RefreshOverlay();
                }
                _scanTimer.Start();
            }

            UpdateMenuState();
        }

        private void SetMode(int mode)
        {
            if (_settings.Mode == mode)
            {
                return;
            }

            _settings.Mode = mode;
            SettingsStore.Save(_settings);
            UpdateMenuState();
        }

        private void SetStaticPreviewRefreshMode(int mode)
        {
            int normalized = mode == StaticRefreshOnDemand
                ? StaticRefreshOnDemand
                : StaticRefreshInterval;
            if (_settings.StaticPreviewRefreshMode == normalized)
            {
                return;
            }

            _settings.StaticPreviewRefreshMode = normalized;
            SettingsStore.Save(_settings);
            UpdateMenuState();
            RefreshAllStaticPreviewsOnce();
        }

        private void RefreshAllStaticPreviewsOnce()
        {
            foreach (ThumbnailCard card in _cards)
            {
                card.RefreshStaticPreviewOnce();
            }
        }

        private void SetThemeMode(int mode)
        {
            if (_settings.ThemeMode == mode)
            {
                ApplyThemePreference();
                return;
            }

            _settings.ThemeMode = mode;
            SettingsStore.Save(_settings);
            ApplyThemePreference();
            UpdateMenuState();
        }

        private void ApplyThemePreference()
        {
            Color sample = _lastSampledColor.IsEmpty
                ? Color.FromArgb(238, 241, 246)
                : _lastSampledColor;
            DockTheme.ApplySample(sample, _settings.ThemeMode);
            ApplyTheme();
        }

        private bool TryRegisterHotkey(int modifiers, int key)
        {
            if (!IsHandleCreated)
            {
                return false;
            }

            _hotkeyRegistered = Native.RegisterHotKey(
                Handle,
                HotkeyId,
                modifiers | Native.MOD_NOREPEAT,
                key);
            return _hotkeyRegistered;
        }

        private void ChangeHotkey()
        {
            if (!IsHandleCreated || _exiting)
            {
                return;
            }

            int oldModifiers = _settings.HotkeyModifiers;
            int oldKey = _settings.HotkeyKey;

            if (_hotkeyRegistered)
            {
                Native.UnregisterHotKey(Handle, HotkeyId);
                _hotkeyRegistered = false;
            }

            using (var dialog = new HotkeyDialog(oldModifiers, oldKey))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (TryRegisterHotkey(dialog.Modifiers, dialog.Key))
                    {
                        _settings.HotkeyModifiers = dialog.Modifiers;
                        _settings.HotkeyKey = dialog.Key;
                        SettingsStore.Save(_settings);
                        UpdateMenuState();
                        return;
                    }

                    if (_tray != null)
                    {
                        _tray.ShowBalloonTip(
                            3000,
                            "一步",
                            "快捷键注册失败，可能已被其他程序占用。",
                            ToolTipIcon.Warning);
                    }
                }
            }

            TryRegisterHotkey(oldModifiers, oldKey);
            UpdateMenuState();
        }

        private void ToggleAutoStart()
        {
            bool enable = !StartupRegistration.IsEnabled;
            if (StartupRegistration.SetEnabled(enable))
            {
                UpdateMenuState();
                if (_tray != null)
                {
                    _tray.ShowBalloonTip(
                        1800,
                        "一步",
                        enable ? "已设置开机自启" : "已关闭开机自启",
                        ToolTipIcon.Info);
                }
            }
            else if (_tray != null)
            {
                _tray.ShowBalloonTip(
                    3000,
                    "一步",
                    "写入开机自启失败，详情见 error.log。",
                    ToolTipIcon.Warning);
            }
        }

        internal void ShowFromSecondInstance()
        {
            if (_exiting)
            {
                return;
            }

            if (!Visible)
            {
                ToggleDock();
                return;
            }

            ApplyAppBar();
            RefreshWindows();
            RefreshOverlay();
        }

        private void UpdateMenuState()
        {
            if (_menu == null)
            {
                return;
            }

            string hotkey = HotkeyHelper.Describe(_settings.HotkeyModifiers, _settings.HotkeyKey);
            _showItem.Text = Visible ? "隐藏侧栏    " + hotkey : "显示侧栏    " + hotkey;
            _maxModeItem.Text = (_settings.Mode == ModeMaximize ? "✓  " : "     ") + "切换时铺满主区";
            _keepModeItem.Text = (_settings.Mode == ModeKeep ? "✓  " : "     ") + "切换时保持原状";
            _themeAutoItem.Text = (_settings.ThemeMode == 0 ? "✓  " : "     ") + "跟随主窗口";
            _themeLightItem.Text = (_settings.ThemeMode == 1 ? "✓  " : "     ") + "浅色";
            _themeDarkItem.Text = (_settings.ThemeMode == 2 ? "✓  " : "     ") + "深色";
            _hotkeyItem.Text = "设置快捷键…    " + hotkey;
            _autoStartItem.Text = (StartupRegistration.IsEnabled ? "✓  " : "     ") + "开机自启";
            bool onDemand = _settings.StaticPreviewRefreshMode == StaticRefreshOnDemand;
            _staticRefreshIntervalItem.Text =
                (!onDemand ? "✓  " : "     ") + "定时刷新（4 秒）";
            _staticRefreshOnDemandItem.Text =
                (onDemand ? "✓  " : "     ") + "仅在切走或标题变化时";
            _restoreHiddenItem.Visible = _settings.ExcludedApps.Count > 0;
            _restartAdminItem.Visible = !Elevation.Current;
            _restoreSizesItem.Visible = HasRestorableSizes();
            RebuildMonitorMenu();

            bool hasCard = _menuCard != null;
            _pinItem.Visible = hasCard;
            _excludeItem.Visible = hasCard;
            _cardSeparator.Visible = hasCard;
            if (hasCard)
            {
                string process = _menuCard.ProcessName ?? "";
                bool pinned = ContainsApp(_settings.PinnedApps, process);
                _pinItem.Text = pinned ? "取消置顶该应用" : "置顶该应用";
                _pinItem.Enabled = process.Length > 0;
                _excludeItem.Enabled = process.Length > 0;
            }
        }

        private static bool ContainsApp(List<string> apps, string processName)
        {
            if (string.IsNullOrEmpty(processName))
            {
                return false;
            }

            foreach (string app in apps)
            {
                if (string.Equals(app, processName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private void TogglePinnedApp()
        {
            if (_menuCard == null)
            {
                return;
            }

            string process = _menuCard.ProcessName;
            if (string.IsNullOrEmpty(process))
            {
                return;
            }

            if (ContainsApp(_settings.PinnedApps, process))
            {
                _settings.PinnedApps.RemoveAll(
                    value => string.Equals(value, process, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                _settings.PinnedApps.Add(process.ToLowerInvariant());
            }

            SettingsStore.Save(_settings);
            RefreshWindows();
            _menuCard = null;
            UpdateMenuState();
        }

        private void ExcludeCurrentApp()
        {
            if (_menuCard == null)
            {
                return;
            }

            string process = _menuCard.ProcessName;
            if (string.IsNullOrEmpty(process))
            {
                return;
            }

            if (!ContainsApp(_settings.ExcludedApps, process))
            {
                _settings.ExcludedApps.Add(process.ToLowerInvariant());
            }

            SettingsStore.Save(_settings);
            RefreshWindows();
            _menuCard = null;
            UpdateMenuState();
        }

        private void RestoreHiddenApps()
        {
            if (_settings.ExcludedApps.Count == 0)
            {
                return;
            }

            _settings.ExcludedApps.Clear();
            SettingsStore.Save(_settings);
            UpdateMenuState();
            RefreshWindows();
        }

        private Screen GetTargetScreen()
        {
            if (!string.IsNullOrEmpty(_settings.DockMonitor))
            {
                foreach (Screen screen in Screen.AllScreens)
                {
                    if (string.Equals(screen.DeviceName, _settings.DockMonitor, StringComparison.OrdinalIgnoreCase))
                    {
                        return screen;
                    }
                }
            }

            return Screen.PrimaryScreen;
        }

        private void RebuildMonitorMenu()
        {
            if (_monitorMenu == null)
            {
                return;
            }

            _monitorMenu.DropDownItems.Clear();
            Screen selected = GetTargetScreen();
            foreach (Screen screen in Screen.AllScreens)
            {
                string label = screen.DeviceName.Replace(@"\\.\", "") +
                               "  " + screen.Bounds.Width + "×" + screen.Bounds.Height;
                if (screen.Primary)
                {
                    label += "（主屏）";
                }

                bool isSelected = string.Equals(
                    screen.DeviceName,
                    selected.DeviceName,
                    StringComparison.OrdinalIgnoreCase);
                var item = new ToolStripMenuItem((isSelected ? "✓  " : "     ") + label);
                Screen captured = screen;
                item.Click += (s, e) => SetDockMonitor(captured);
                _monitorMenu.DropDownItems.Add(item);
            }
        }

        private void SetDockMonitor(Screen screen)
        {
            if (screen == null)
            {
                return;
            }

            if (!string.Equals(_settings.DockMonitor, screen.DeviceName, StringComparison.OrdinalIgnoreCase))
            {
                _settings.DockMonitor = screen.DeviceName;
                SettingsStore.Save(_settings);
            }

            _overlay?.Hide();
            RemoveAppBar();
            Rectangle bounds = screen.Bounds;
            Bounds = new Rectangle(bounds.Right - Width, bounds.Top, Width, bounds.Height);
            ApplyAppBar();
            LayoutChrome();
            RefreshWindows();
            if (_overlay != null)
            {
                _overlay.Show();
                RefreshOverlay();
            }
            UpdateMenuState();
        }

        private bool HasRestorableSizes()
        {
            if (_savedPlacements.Count == 0)
            {
                return false;
            }

            var dead = new List<IntPtr>();
            bool any = false;
            foreach (IntPtr handle in _savedPlacements.Keys)
            {
                if (Native.IsWindow(handle))
                {
                    any = true;
                }
                else
                {
                    dead.Add(handle);
                }
            }
            foreach (IntPtr handle in dead)
            {
                _savedPlacements.Remove(handle);
            }
            return any;
        }

        private void RestoreWindowSizes()
        {
            if (_savedPlacements.Count == 0)
            {
                return;
            }

            var entries = new List<KeyValuePair<IntPtr, Native.WINDOWPLACEMENT>>(_savedPlacements);
            _savedPlacements.Clear();
            foreach (var entry in entries)
            {
                if (!Native.IsWindow(entry.Key))
                {
                    continue;
                }

                Native.WINDOWPLACEMENT placement = entry.Value;
                Native.SetWindowPlacement(entry.Key, ref placement);
            }
            UpdateMenuState();
        }

        private void RestartAsAdmin()
        {
            if (Elevation.Current || _exiting)
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = "--elevated",
                    UseShellExecute = true,
                    Verb = "runas"
                });
                _exiting = true;
                Close();
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                if (_tray != null)
                {
                    _tray.ShowBalloonTip(
                        3000,
                        "一步",
                        "未能以管理员身份启动。",
                        ToolTipIcon.Warning);
                }
            }
        }

        private void OpenMenu(Control anchor, Point location)
        {
            _menuCard = null;
            UpdateMenuState();
            _menu.Show(anchor, location);
        }

        private void OnCardContextMenuRequested(object sender, EventArgs e)
        {
            var card = sender as ThumbnailCard;
            if (card == null)
            {
                return;
            }

            _menuCard = card;
            _toolTip.Active = false;
            UpdateMenuState();
            _menu.Show(Cursor.Position);
        }

        private void BuildTray()
        {
            _trayIcon = CreateTrayIcon();
            _tray = new NotifyIcon
            {
                Icon = _trayIcon,
                Text = "一步 · 窗口侧栏",
                Visible = true,
                ContextMenuStrip = _menu
            };
            _tray.DoubleClick += (s, e) => ToggleDock();
            Icon = _trayIcon;
        }

        private void LayoutChrome()
        {
            Padding = new Padding(Px(10), 0, 0, 0);
            MinimumSize = new Size(Px(200), Px(300));
            MaximumSize = new Size(Px(560), 20000);
            BackColor = DockTheme.Backdrop;
            _list.BackColor = DockTheme.Backdrop;
            LayoutCards();
        }

        private void ClampWidth()
        {
            if (_resizingWidth)
            {
                return;
            }

            int min = Px(200);
            int max = Px(560);
            if (Width < min || Width > max)
            {
                _resizingWidth = true;
                Width = Math.Max(min, Math.Min(max, Width));
                _resizingWidth = false;
            }
        }

        private void RefreshThumbnailPositions()
        {
            foreach (ThumbnailCard card in _cards)
            {
                card.UpdateThumbnailDestination();
            }
        }

        private void SaveWidth()
        {
            if (Width <= 0)
            {
                return;
            }

            int logical = (int)Math.Round(Width * 96.0 / CurrentDpi);
            logical = Math.Max(200, Math.Min(560, logical));
            if (logical != _settings.DockWidth)
            {
                _settings.DockWidth = logical;
                SettingsStore.Save(_settings);
            }
        }

        private void ApplyAppBar()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            var monitorInfo = new Native.MONITORINFO
            {
                cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO))
            };
            IntPtr monitor = Native.MonitorFromWindow(Handle, Native.MONITOR_DEFAULTTONEAREST);
            if (!Native.GetMonitorInfo(monitor, ref monitorInfo))
            {
                return;
            }

            int width = Px(Math.Max(200, Math.Min(560, _settings.DockWidth)));
            var data = new Native.APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA)),
                hWnd = Handle,
                uEdge = Native.ABE_RIGHT
            };

            if (!_appBarAdded)
            {
                UIntPtr result = Native.SHAppBarMessage(Native.ABM_NEW, ref data);
                _appBarAdded = result != UIntPtr.Zero;
            }

            data.rc = new Native.RECT(
                monitorInfo.rcMonitor.Right - width,
                monitorInfo.rcMonitor.Top,
                monitorInfo.rcMonitor.Right,
                monitorInfo.rcMonitor.Bottom);

            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref data);
            data.rc.Left = data.rc.Right - width;
            Native.SHAppBarMessage(Native.ABM_SETPOS, ref data);

            Native.RECT r = data.rc;
            if (r.Width > 0 && r.Height > 0)
            {
                Native.SetWindowPos(
                    Handle,
                    Native.HWND_TOPMOST,
                    r.Left,
                    r.Top,
                    r.Width,
                    r.Height,
                    Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }
        }

        private void RemoveAppBar()
        {
            if (!_appBarAdded)
            {
                return;
            }

            var data = new Native.APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA)),
                hWnd = Handle
            };
            Native.SHAppBarMessage(Native.ABM_REMOVE, ref data);
            _appBarAdded = false;
        }

        private void ChromeMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                var control = sender as Control;
                OpenMenu(control ?? this, e.Location);
            }
        }

        private int CurrentDpi
        {
            get
            {
                if (IsHandleCreated)
                {
                    return DeviceDpi;
                }

                if (_initialDpi == 0)
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    {
                        _initialDpi = (int)Math.Round(g.DpiX);
                    }
                }

                return _initialDpi;
            }
        }

        private int Px(int logicalPixels)
        {
            return (int)Math.Round(logicalPixels * CurrentDpi / 96.0);
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

        private static Icon CreateTrayIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    using (var back = new SolidBrush(Color.FromArgb(22, 26, 33)))
                    using (GraphicsPath path = CreateRoundedPath(new Rectangle(1, 1, 30, 30), 8))
                    {
                        g.FillPath(back, path);
                    }

                    using (var accent = new SolidBrush(Color.FromArgb(76, 141, 255)))
                    using (GraphicsPath path = CreateRoundedPath(new Rectangle(17, 6, 10, 20), 3))
                    {
                        g.FillPath(accent, path);
                    }

                    using (var dot = new SolidBrush(Color.White))
                    {
                        g.FillRectangle(dot, 20, 9, 4, 3);
                        g.FillRectangle(dot, 20, 14, 4, 3);
                        g.FillRectangle(dot, 20, 19, 4, 3);
                    }

                    using (var accent = new SolidBrush(Color.FromArgb(76, 141, 255)))
                    {
                        using (GraphicsPath path = CreateRoundedPath(new Rectangle(6, 8, 7, 7), 2))
                        {
                            g.FillPath(accent, path);
                        }
                        using (GraphicsPath path = CreateRoundedPath(new Rectangle(6, 18, 7, 7), 2))
                        {
                            g.FillPath(accent, path);
                        }
                    }
                }

                IntPtr handle = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(handle))
                    {
                        return (Icon)temp.Clone();
                    }
                }
                finally
                {
                    Native.DestroyIcon(handle);
                }
            }
        }

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed class CardListPanel : Panel
        {
            public CardListPanel()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
                TabStop = false;
            }
        }
    }
}
