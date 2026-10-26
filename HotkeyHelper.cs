using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace OneStepDock
{
    internal static class HotkeyHelper
    {
        public const int ModAlt = 0x0001;
        public const int ModControl = 0x0002;
        public const int ModShift = 0x0004;
        public const int ModWin = 0x0008;

        public static string Describe(int modifiers, int key)
        {
            var parts = new List<string>();
            if ((modifiers & ModControl) != 0)
            {
                parts.Add("Ctrl");
            }
            if ((modifiers & ModAlt) != 0)
            {
                parts.Add("Alt");
            }
            if ((modifiers & ModShift) != 0)
            {
                parts.Add("Shift");
            }
            if ((modifiers & ModWin) != 0)
            {
                parts.Add("Win");
            }
            parts.Add(KeyName(key));
            return string.Join("+", parts);
        }

        public static string KeyName(int key)
        {
            if (key >= 0x41 && key <= 0x5A)
            {
                return ((char)key).ToString();
            }
            if (key >= 0x30 && key <= 0x39)
            {
                return ((char)key).ToString();
            }

            switch (key)
            {
                case 0x20:
                    return "Space";
                case 0x0D:
                    return "Enter";
                case 0x09:
                    return "Tab";
                case 0x1B:
                    return "Esc";
                case 0x08:
                    return "Backspace";
                case 0x2E:
                    return "Delete";
                case 0x2D:
                    return "Insert";
                case 0x24:
                    return "Home";
                case 0x23:
                    return "End";
                case 0x21:
                    return "PageUp";
                case 0x22:
                    return "PageDown";
                case 0x25:
                    return "Left";
                case 0x26:
                    return "Up";
                case 0x27:
                    return "Right";
                case 0x28:
                    return "Down";
                default:
                    string name = Enum.GetName(typeof(Keys), key);
                    return string.IsNullOrEmpty(name) ? ("0x" + key.ToString("X2")) : name;
            }
        }

        public static bool IsModifierKey(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey ||
                   key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu ||
                   key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey ||
                   key == Keys.LWin || key == Keys.RWin;
        }
    }
}
