using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace OneStepDock
{
    internal sealed class WindowItem
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; }
        public string ProcessName { get; set; }
        public bool IsMinimized { get; set; }
        public bool IsElevated { get; set; }
        public bool IsNoRedirectionBitmap { get; set; }
    }

    internal static class WindowScanner
    {
        private static readonly HashSet<string> SkipClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "Progman",
            "WorkerW",
            "Shell_TrayWnd",
            "Shell_SecondaryTrayWnd",
            "Windows.UI.Core.CoreWindow",
            "MultitaskingViewFrame",
            "ForegroundStaging",
            "XamlExplorerHostIslandWindow"
        };

        public static List<WindowItem> Scan(IntPtr foreground, uint selfProcessId)
        {
            var items = new List<WindowItem>();
            IntPtr shell = Native.GetShellWindow();
            var processNames = new Dictionary<uint, string>();

            Native.EnumWindows((hWnd, lParam) =>
            {
                try
                {
                    if (!Native.IsWindowVisible(hWnd) || hWnd == shell || hWnd == foreground)
                    {
                        return true;
                    }

                    int length = Native.GetWindowTextLength(hWnd);
                    if (length == 0 || Native.GetAncestor(hWnd, Native.GA_ROOT) != hWnd)
                    {
                        return true;
                    }

                    long exStyle = Native.GetWindowLongPtr(hWnd, Native.GWL_EXSTYLE).ToInt64();
                    if ((exStyle & Native.WS_EX_TOOLWINDOW) != 0)
                    {
                        return true;
                    }
                    bool noRedirectionBitmap = (exStyle & Native.WS_EX_NOREDIRECTIONBITMAP) != 0;

                    int cloaked;
                    if (Native.DwmGetWindowAttribute(hWnd, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0)
                    {
                        return true;
                    }

                    var className = new StringBuilder(256);
                    Native.GetClassName(hWnd, className, className.Capacity);
                    if (SkipClasses.Contains(className.ToString()))
                    {
                        return true;
                    }

                    uint pid;
                    Native.GetWindowThreadProcessId(hWnd, out pid);
                    if (pid == selfProcessId)
                    {
                        return true;
                    }

                    var titleBuilder = new StringBuilder(length + 2);
                    Native.GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
                    string title = titleBuilder.ToString().Replace('\r', ' ').Replace('\n', ' ').Trim();
                    if (title.Length == 0)
                    {
                        return true;
                    }

                    string processName;
                    if (!processNames.TryGetValue(pid, out processName))
                    {
                        try
                        {
                            processName = Process.GetProcessById((int)pid).ProcessName;
                        }
                        catch
                        {
                            processName = "";
                        }
                        processNames[pid] = processName;
                    }

                    items.Add(new WindowItem
                    {
                        Handle = hWnd,
                        Title = title,
                        ProcessName = processName,
                        IsMinimized = Native.IsIconic(hWnd),
                        IsElevated = Elevation.IsProcessElevated(pid),
                        IsNoRedirectionBitmap = noRedirectionBitmap
                    });
                }
                catch
                {
                }

                return true;
            }, IntPtr.Zero);

            return items;
        }

        public static bool IsOwnWindow(IntPtr hWnd, uint selfProcessId)
        {
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }

            uint pid;
            Native.GetWindowThreadProcessId(hWnd, out pid);
            return pid == selfProcessId;
        }
    }
}
