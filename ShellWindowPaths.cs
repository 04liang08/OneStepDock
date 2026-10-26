using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace OneStepDock
{
    /// <summary>
    /// 通过 ShellWindows 把资源管理器窗口解析成文件夹路径。
    /// 这是唯一走 COM 的地方；调用方每次扫描都会来问一遍，所以这里必须缓存，
    /// 只在窗口集合（句柄 / 标题）发生变化或缓存过期时才真正枚举。
    /// </summary>
    internal static class ShellWindowPaths
    {
        // 扫描间隔是 700ms，这里比它长一些：窗口集合没变时不该反复走 COM。
        // 新开的窗口会改变缓存键，所以不会因此延迟识别。
        private const int CacheLifetimeMs = 4000;

        private static string _cacheKey;
        private static Dictionary<IntPtr, string> _cache;
        private static int _cacheStamp;

        public static void Invalidate()
        {
            _cacheKey = null;
            _cache = null;
            _cacheStamp = 0;
        }

        public static Dictionary<IntPtr, string> Resolve(IList<WindowItem> items)
        {
            var empty = new Dictionary<IntPtr, string>();
            if (items == null || items.Count == 0)
            {
                return empty;
            }

            var handles = new HashSet<IntPtr>();
            var keyBuilder = new StringBuilder();
            foreach (WindowItem item in items)
            {
                if (!handles.Add(item.Handle))
                {
                    continue;
                }

                keyBuilder.Append(item.Handle.ToInt64().ToString("X"));
                keyBuilder.Append(':');
                keyBuilder.Append(item.Title ?? "");
                keyBuilder.Append('\n');
            }

            string key = keyBuilder.ToString();
            bool fresh = _cache != null &&
                         string.Equals(_cacheKey, key, StringComparison.Ordinal) &&
                         unchecked(Environment.TickCount - _cacheStamp) < CacheLifetimeMs;
            if (fresh)
            {
                return _cache;
            }

            Dictionary<IntPtr, string> resolved = Enumerate(handles);

            // 这一轮没枚举到的窗口沿用上一轮已知的路径：
            // 资源管理器刚打开、ShellWindows 还没登记时不会把结果抹成空。
            if (_cache != null)
            {
                foreach (KeyValuePair<IntPtr, string> pair in _cache)
                {
                    if (handles.Contains(pair.Key) && !resolved.ContainsKey(pair.Key))
                    {
                        resolved[pair.Key] = pair.Value;
                    }
                }
            }

            _cacheKey = key;
            _cache = resolved;
            _cacheStamp = Environment.TickCount;
            return resolved;
        }

        private static Dictionary<IntPtr, string> Enumerate(HashSet<IntPtr> handles)
        {
            var result = new Dictionary<IntPtr, string>();
            object shell = null;
            object windows = null;

            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                {
                    return result;
                }

                shell = Activator.CreateInstance(shellType);
                if (shell == null)
                {
                    return result;
                }

                windows = shellType.InvokeMember(
                    "Windows", BindingFlags.InvokeMethod, null, shell, null);
                if (windows == null)
                {
                    return result;
                }

                Type windowsType = windows.GetType();
                int count = Convert.ToInt32(
                    windowsType.InvokeMember("Count", BindingFlags.GetProperty, null, windows, null));

                for (int i = 0; i < count; i++)
                {
                    object window = null;
                    try
                    {
                        window = windowsType.InvokeMember(
                            "Item", BindingFlags.InvokeMethod, null, windows, new object[] { i });
                        if (window == null)
                        {
                            continue;
                        }

                        Type windowType = window.GetType();
                        long rawHandle = Convert.ToInt64(
                            windowType.InvokeMember("HWND", BindingFlags.GetProperty, null, window, null));
                        var handle = new IntPtr(rawHandle);
                        if (!handles.Contains(handle) || result.ContainsKey(handle))
                        {
                            continue;
                        }

                        string url = windowType.InvokeMember(
                            "LocationURL", BindingFlags.GetProperty, null, window, null) as string;
                        string path = UrlToPath(url);
                        if (!string.IsNullOrEmpty(path))
                        {
                            result[handle] = path;
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        if (window != null && Marshal.IsComObject(window))
                        {
                            Marshal.ReleaseComObject(window);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log(ex);
            }
            finally
            {
                if (windows != null && Marshal.IsComObject(windows))
                {
                    Marshal.ReleaseComObject(windows);
                }
                if (shell != null && Marshal.IsComObject(shell))
                {
                    Marshal.ReleaseComObject(shell);
                }
            }

            return result;
        }

        private static string UrlToPath(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            try
            {
                if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    return new Uri(url).LocalPath;
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
