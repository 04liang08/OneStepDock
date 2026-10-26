using System;
using System.Drawing;
using System.Windows.Forms;

namespace OneStepDock
{
    public sealed partial class DockForm
    {
        private void OnCardPointerMoved(object sender, EventArgs e)
        {
            var card = sender as ThumbnailCard;
            if (card == null || card.IsDisposed || card.IsDesktop)
            {
                return;
            }

            // 只有指针还在卡片范围内、且落在右上角角标上，才算命中关闭按钮。
            bool inside = card.RectangleToScreen(card.ClientRectangle).Contains(Cursor.Position);
            bool hot = inside &&
                       card.IsHovered &&
                       BadgeScreenRect(card).Contains(Cursor.Position);
            card.SetCloseHot(hot);
        }

        private void OnCardCloseHotChanged(object sender, EventArgs e)
        {
            RefreshOverlay();
        }

        private void OnCardCloseRequested(object sender, EventArgs e)
        {
            var card = sender as ThumbnailCard;
            if (card == null || card.IsDisposed || card.IsDesktop)
            {
                return;
            }

            IntPtr handle = card.WindowHandle;
            if (handle == IntPtr.Zero || !Native.IsWindow(handle))
            {
                return;
            }

            EndPeek();
            card.SetCloseHot(false);

            // 只投递 WM_CLOSE：让程序自己走正常关闭流程（可弹保存提示），
            // 也避免我们直接 DestroyWindow 把别的进程的窗口强行拆掉。
            Native.PostMessage(handle, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

            _quickRefreshTimer.Stop();
            _quickRefreshTimer.Start();
        }
    }
}
