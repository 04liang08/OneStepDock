using System;
using System.Drawing;
using System.Windows.Forms;

namespace OneStepDock
{
    internal sealed class HotkeyDialog : Form
    {
        private readonly Label _preview;
        private int _modifiers;
        private int _key;

        public int Modifiers
        {
            get { return _modifiers; }
        }

        public int Key
        {
            get { return _key; }
        }

        public HotkeyDialog(int modifiers, int key)
        {
            _modifiers = modifiers;
            _key = key;
            Text = "设置快捷键";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(360, 150);
            KeyPreview = true;
            Font = CreateUiFont(9f);

            var title = new Label
            {
                Text = "请按下新的快捷键组合",
                AutoSize = true,
                Location = new Point(24, 20),
                Font = CreateUiFont(10f)
            };

            _preview = new Label
            {
                Text = HotkeyHelper.Describe(_modifiers, _key),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(24, 52),
                Size = new Size(312, 36),
                Font = CreateUiFont(12f, FontStyle.Bold)
            };

            var hint = new Label
            {
                Text = "至少包含 Ctrl 或 Alt；Esc 取消。",
                AutoSize = true,
                ForeColor = Color.FromArgb(100, 106, 118),
                Location = new Point(24, 98)
            };

            var cancel = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(258, 115),
                Size = new Size(78, 26)
            };

            Controls.Add(title);
            Controls.Add(_preview);
            Controls.Add(hint);
            Controls.Add(cancel);
            CancelButton = cancel;
            KeyDown += OnHotkeyKeyDown;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            BringToFront();
        }

        private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            if (HotkeyHelper.IsModifierKey(e.KeyCode))
            {
                UpdatePreview(ControlModifiers(e), _key);
                return;
            }

            int modifiers = ControlModifiers(e);
            if ((modifiers & (HotkeyHelper.ModControl | HotkeyHelper.ModAlt | HotkeyHelper.ModWin)) == 0)
            {
                _preview.Text = "请包含 Ctrl 或 Alt";
                return;
            }

            _modifiers = modifiers;
            _key = (int)(e.KeyCode & Keys.KeyCode);
            e.SuppressKeyPress = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static int ControlModifiers(KeyEventArgs e)
        {
            int modifiers = 0;
            if (e.Control)
            {
                modifiers |= HotkeyHelper.ModControl;
            }
            if (e.Alt)
            {
                modifiers |= HotkeyHelper.ModAlt;
            }
            if (e.Shift)
            {
                modifiers |= HotkeyHelper.ModShift;
            }
            return modifiers;
        }

        private void UpdatePreview(int modifiers, int key)
        {
            _preview.Text = HotkeyHelper.Describe(modifiers, key);
        }

        private static Font CreateUiFont(float size)
        {
            return CreateUiFont(size, FontStyle.Regular);
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
    }
}
