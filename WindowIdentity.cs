using System;
using System.Collections.Generic;
using System.Text;

namespace OneStepDock
{
    /// <summary>
    /// 给窗口算一个稳定的身份键，用来持久化自定义标签：
    /// 同一个键 = 同一个"位置"，即使窗口句柄换了也能对上。
    ///
    /// - 资源管理器：用文件夹路径（dir:c:\...），所以重新打开同一个文件夹会复用标签。
    /// - 其他应用：用 进程名 + 窗口标题（win:chrome|github）。
    /// </summary>
    internal static class WindowIdentity
    {
        private const string DirectoryPrefix = "dir:";
        private const string WindowPrefix = "win:";

        public static string DirectoryPrefixValue
        {
            get { return DirectoryPrefix; }
        }

        public static Dictionary<IntPtr, string> Build(IList<WindowItem> items)
        {
            return Build(items, null);
        }

        /// <summary>挑出需要用路径做身份的窗口（目前只有资源管理器）。</summary>
        public static List<WindowItem> CollectDirectoryCandidates(IList<WindowItem> items)
        {
            var result = new List<WindowItem>();
            if (items == null)
            {
                return result;
            }

            foreach (WindowItem item in items)
            {
                if (IsExplorer(item))
                {
                    result.Add(item);
                }
            }

            return result;
        }

        /// <summary>
        /// paths 传入 <see cref="ShellWindowPaths"/> 的结果，避免内部重复枚举 ShellWindows。
        /// 不传则自己解析一次。
        /// </summary>
        public static Dictionary<IntPtr, string> Build(
            IList<WindowItem> items,
            Dictionary<IntPtr, string> paths)
        {
            var result = new Dictionary<IntPtr, string>();
            if (items == null || items.Count == 0)
            {
                return result;
            }

            if (paths == null)
            {
                List<WindowItem> candidates = CollectDirectoryCandidates(items);
                paths = candidates.Count > 0
                    ? ShellWindowPaths.Resolve(candidates)
                    : null;
            }

            foreach (WindowItem item in items)
            {
                string path;
                if (paths != null &&
                    paths.TryGetValue(item.Handle, out path) &&
                    !string.IsNullOrEmpty(path))
                {
                    result[item.Handle] = DirectoryPrefix + NormalizePath(path);
                    continue;
                }

                result[item.Handle] = WindowPrefix +
                                      (item.ProcessName ?? "").Trim().ToLowerInvariant() +
                                      "|" +
                                      NormalizeTitle(item.Title);
            }

            return result;
        }

        /// <summary>
        /// 强制重算一个窗口的身份键（绕过路径缓存）。
        /// 用户点"设置自定义标签"时用：那一刻如果 ShellWindows 还没登记这个窗口，
        /// 会退化成标题键，等路径可用后键就变了、标签会对不上，所以这里重查一次。
        /// </summary>
        public static string BuildUncached(IntPtr handle, string processName, string title)
        {
            var item = new WindowItem
            {
                Handle = handle,
                ProcessName = processName,
                Title = title
            };

            if (IsExplorer(item))
            {
                ShellWindowPaths.Invalidate();
                Dictionary<IntPtr, string> paths =
                    ShellWindowPaths.Resolve(new List<WindowItem> { item });

                string path;
                if (paths.TryGetValue(handle, out path) && !string.IsNullOrEmpty(path))
                {
                    return DirectoryPrefix + NormalizePath(path);
                }
            }

            return WindowPrefix +
                   (processName ?? "").Trim().ToLowerInvariant() +
                   "|" +
                   NormalizeTitle(title);
        }

        public static bool IsDirectoryKey(string key)
        {
            return !string.IsNullOrEmpty(key) &&
                   key.StartsWith(DirectoryPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// 只有 dir: 键能还原成人类可读的位置；win: 键返回 null。
        /// </summary>
        public static string GetDirectoryPath(string key)
        {
            if (!IsDirectoryKey(key))
            {
                return null;
            }

            return key.Substring(DirectoryPrefix.Length);
        }

        private static bool IsExplorer(WindowItem item)
        {
            return item != null &&
                   string.Equals(
                       (item.ProcessName ?? "").Trim(),
                       "explorer",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }

            string trimmed = path.TrimEnd('\\', '/');
            if (trimmed.Length == 0)
            {
                return path.ToLowerInvariant();
            }

            // 盘符根目录（C:）要补回反斜杠，避免和"C:当前目录"混淆。
            if (trimmed.Length == 2 && trimmed[1] == ':')
            {
                trimmed += "\\";
            }

            return trimmed.ToLowerInvariant();
        }

        private static string NormalizeTitle(string title)
        {
            string text = (title ?? "").Trim();
            if (text.Length == 0)
            {
                return "";
            }

            var builder = new StringBuilder(text.Length);
            bool lastSpace = false;
            foreach (char ch in text)
            {
                bool space = char.IsWhiteSpace(ch);
                if (space)
                {
                    if (!lastSpace)
                    {
                        builder.Append(' ');
                    }
                }
                else
                {
                    builder.Append(ch);
                }
                lastSpace = space;
            }

            return builder.ToString().ToLowerInvariant();
        }
    }
}
