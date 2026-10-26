using System;
using System.Drawing;
using System.Windows.Forms;

namespace OneStepDock
{
    internal sealed class LabelDialog : Form
    {
        private readonly TextBox _input;

        public string LabelText
        {
            get { return _input.Text; }
        }

        public LabelDialog(string windowTitle, string currentLabel, string placeholder, string target)
        {
            Text = "自定义标签";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(400, 186);
            KeyPreview = true;
            Font = CreateUiFont(9f);
            BackColor = Color.FromArgb(32, 34, 39);
            ForeColor = Color.FromArgb(232, 234, 237);

            var caption = new Label
            {
                Text = Truncate(windowTitle, 46),
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(24, 18),
                Size = new Size(352, 20),
                Font = CreateUiFont(10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 245, 248)
            };

            var hint = new Label
            {
                Text = target == null
                    ? "这个标签只跟随当前窗口标题。"
                    : "这个标签会跟随该位置，重新打开时自动套用。",
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(24, 42),
                Size = new Size(352, 18),
                ForeColor = Color.FromArgb(148, 154, 166)
            };

            _input = new TextBox
            {
                Location = new Point(24, 68),
                Size = new Size(352, 26),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(45, 48, 54),
                ForeColor = Color.FromArgb(240, 242, 246),
                MaxLength = 40,
                PlaceholderText = placeholder ?? ""
            };

            if (!string.IsNullOrEmpty(currentLabel))
            {
                _input.Text = currentLabel;
            }

            var pathHint = new Label
            {
                Text = target ?? "",
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(24, 100),
                Size = new Size(352, 18),
                ForeColor = Color.FromArgb(120, 126, 138)
            };

            var note = new Label
            {
                Text = "留空并确定 = 清除标签",
                AutoSize = false,
                Location = new Point(24, 120),
                Size = new Size(170, 18),
                ForeColor = Color.FromArgb(120, 126, 138)
            };

            var ok = new Button
            {
                Text = "确定",
                DialogResult = DialogResult.OK,
                Location = new Point(214, 146),
                Size = new Size(78, 26),
                FlatStyle = FlatStyle.System
            };

            var cancel = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(298, 146),
                Size = new Size(78, 26),
                FlatStyle = FlatStyle.System
            };

            Controls.Add(caption);
            Controls.Add(hint);
            Controls.Add(_input);
            Controls.Add(pathHint);
            Controls.Add(note);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            BringToFront();
            _input.Focus();
            _input.SelectAll();
        }

        private static string Truncate(string text, int max)
        {
            string value = (text ?? "").Trim();
            if (value.Length <= max)
            {
                return value;
            }

            return value.Substring(0, max - 1) + "…";
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
