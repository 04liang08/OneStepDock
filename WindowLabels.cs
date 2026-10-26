using System;
using System.Collections.Generic;
using System.Text;

namespace OneStepDock
{
    internal sealed class WindowLabelInfo
    {
        /// <summary>要显示在卡片上的文字；null 表示不显示。</summary>
        public string Text { get; set; }

        /// <summary>持久化键，用户设置自定义标签时写进设置里的就是它。</summary>
        public string Key { get; set; }

        public bool IsCustom { get; set; }

        public bool HasText
        {
            get { return !string.IsNullOrEmpty(Text); }
        }
    }

    internal static class WindowLabels
    {
        public static Dictionary<IntPtr, WindowLabelInfo> Build(
            IList<WindowItem> items,
            IDictionary<string, string> customLabels,
            bool autoLabels)
        {
            var result = new Dictionary<IntPtr, WindowLabelInfo>();
            if (items == null || items.Count == 0)
            {
                return result;
            }

            // 整轮扫描只解析一次 ShellWindows：身份键和"重名才显示路径尾部"共用这份结果。
            Dictionary<IntPtr, string> paths = ShellWindowPaths.Resolve(
                WindowIdentity.CollectDirectoryCandidates(items));

            Dictionary<IntPtr, string> keys = WindowIdentity.Build(items, paths);

            // 自定义标签优先级最高：只要用户给这个身份起了名，就一直显示，与重名开关无关。
            var custom = new Dictionary<IntPtr, string>();
            foreach (WindowItem item in items)
            {
                string key;
                if (!keys.TryGetValue(item.Handle, out key) || string.IsNullOrEmpty(key))
                {
                    continue;
                }

                string text;
                if (customLabels != null &&
                    customLabels.TryGetValue(key, out text) &&
                    !string.IsNullOrEmpty(text))
                {
                    custom[item.Handle] = text;
                }
            }

            foreach (WindowItem item in items)
            {
                string key;
                keys.TryGetValue(item.Handle, out key);

                string text;
                bool isCustom = custom.TryGetValue(item.Handle, out text);

                result[item.Handle] = new WindowLabelInfo
                {
                    Text = isCustom ? text : null,
                    Key = key,
                    IsCustom = isCustom
                };
            }

            if (!autoLabels)
            {
                return result;
            }

            Dictionary<IntPtr, string> auto = BuildAutoLabels(items, paths);
            foreach (KeyValuePair<IntPtr, string> pair in auto)
            {
                WindowLabelInfo info;
                if (result.TryGetValue(pair.Key, out info) && !info.IsCustom)
                {
                    info.Text = pair.Value;
                }
            }

            return result;
        }

        private static Dictionary<IntPtr, string> BuildAutoLabels(
            IList<WindowItem> items,
            Dictionary<IntPtr, string> paths)
        {
            var labels = new Dictionary<IntPtr, string>();

            // 自定义过标签的窗口仍然参与分组计数：只是它的文字被替换掉，
            // 不影响同组其他窗口的判定，避免"给一个起了名，另一个的标签反而消失"。
            var byProcess = new Dictionary<string, List<WindowItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (WindowItem item in items)
            {
                if (string.IsNullOrEmpty(item.ProcessName))
                {
                    continue;
                }

                List<WindowItem> list;
                if (!byProcess.TryGetValue(item.ProcessName, out list))
                {
                    list = new List<WindowItem>();
                    byProcess[item.ProcessName] = list;
                }
                list.Add(item);
            }

            foreach (KeyValuePair<string, List<WindowItem>> group in byProcess)
            {
                List<WindowItem> list = group.Value;
                if (list.Count < 2)
                {
                    continue;
                }

                var shortTitles = new Dictionary<IntPtr, string>();
                ShortenTitles(list, shortTitles);

                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (WindowItem item in list)
                {
                    string text = shortTitles[item.Handle];
                    int count;
                    counts.TryGetValue(text, out count);
                    counts[text] = count + 1;
                }

                var conflicts = new List<WindowItem>();
                foreach (WindowItem item in list)
                {
                    if (counts[shortTitles[item.Handle]] > 1)
                    {
                        conflicts.Add(item);
                    }
                }

                var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (WindowItem item in list)
                {
                    string label = shortTitles[item.Handle];

                    string path;
                    if (counts[label] > 1 &&
                        paths != null && paths.TryGetValue(item.Handle, out path))
                    {
                        label = TailSegments(path, 2);
                    }

                    int seen;
                    used.TryGetValue(label, out seen);
                    seen++;
                    used[label] = seen;
                    if (seen > 1)
                    {
                        label = label + " (" + seen + ")";
                    }

                    labels[item.Handle] = label;
                }
            }

            return labels;
        }

        private static void ShortenTitles(
            List<WindowItem> list,
            Dictionary<IntPtr, string> output)
        {
            // 同一应用的所有窗口，如果标题都以相同的“ - 应用名”结尾，就把这个尾巴去掉，
            // 只留下真正用来区分窗口的那一段。
            string commonSuffix = null;
            bool strippable = true;

            foreach (WindowItem item in list)
            {
                string title = (item.Title ?? "").Trim();
                int index = title.LastIndexOf(" - ", StringComparison.Ordinal);
                if (index <= 0)
                {
                    strippable = false;
                    break;
                }

                string suffix = title.Substring(index + 3).Trim();
                if (suffix.Length == 0)
                {
                    strippable = false;
                    break;
                }

                if (commonSuffix == null)
                {
                    commonSuffix = suffix;
                }
                else if (!string.Equals(commonSuffix, suffix, StringComparison.OrdinalIgnoreCase))
                {
                    strippable = false;
                    break;
                }
            }

            foreach (WindowItem item in list)
            {
                string title = (item.Title ?? "").Trim();
                if (strippable)
                {
                    int index = title.LastIndexOf(" - ", StringComparison.Ordinal);
                    if (index > 0)
                    {
                        title = title.Substring(0, index).Trim();
                    }
                }
                output[item.Handle] = title;
            }
        }

        private static string TailSegments(string path, int count)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }

            string trimmed = path.TrimEnd('\\', '/');
            if (trimmed.Length == 0)
            {
                return "";
            }

            string[] parts = trimmed.Split(
                new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return trimmed;
            }

            if (parts.Length <= count)
            {
                return string.Join("\\", parts);
            }

            var builder = new StringBuilder("…\\");
            int start = parts.Length - count;
            for (int i = start; i < parts.Length; i++)
            {
                if (i > start)
                {
                    builder.Append('\\');
                }
                builder.Append(parts[i]);
            }
            return builder.ToString();
        }
    }
}
