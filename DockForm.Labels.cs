using System;
using System.Windows.Forms;

namespace OneStepDock
{
    public sealed partial class DockForm
    {
        /// <summary>
        /// 给当前右键的卡片设置自定义标签。标签按窗口身份键持久化：
        /// 资源管理器用文件夹路径，所以下次打开同一个文件夹会自动套用。
        /// </summary>
        private void EditCardLabel()
        {
            ThumbnailCard card = _menuCard;
            _menuCard = null;

            if (card == null || card.IsDisposed || card.IsDesktop)
            {
                return;
            }

            string key = card.LabelKey;
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            // 资源管理器窗口如果还没解析出路径，这里重查一次，
            // 否则标签会存在标题键上，等路径可用后就对不上了。
            if (!WindowIdentity.IsDirectoryKey(key) &&
                string.Equals(card.ProcessName ?? "", "explorer", StringComparison.OrdinalIgnoreCase))
            {
                string resolved = WindowIdentity.BuildUncached(
                    card.WindowHandle, card.ProcessName, card.Title);
                if (!string.IsNullOrEmpty(resolved))
                {
                    key = resolved;
                }
            }

            string windowTitle = card.Title;
            string autoLabel = card.LabelIsCustom ? null : card.Label;

            string current;
            if (!_settings.CustomLabels.TryGetValue(key, out current))
            {
                current = null;
            }

            string location = WindowIdentity.GetDirectoryPath(key);
            string placeholder = string.IsNullOrEmpty(autoLabel) ? "例如：项目主目录" : autoLabel;

            using (var dialog = new LabelDialog(windowTitle, current, placeholder, location))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                string text = (dialog.LabelText ?? "").Trim();
                if (text.Length == 0)
                {
                    if (!_settings.CustomLabels.Remove(key))
                    {
                        return;
                    }
                }
                else
                {
                    _settings.CustomLabels[key] = text;
                }

                SettingsStore.Save(_settings);
                RefreshWindows();
            }
        }

        private void ClearCardLabel()
        {
            ThumbnailCard card = _menuCard;
            _menuCard = null;

            if (card == null || card.IsDisposed || card.IsDesktop)
            {
                return;
            }

            string key = card.LabelKey;
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (!_settings.CustomLabels.Remove(key))
            {
                return;
            }

            SettingsStore.Save(_settings);
            RefreshWindows();
        }
    }
}
