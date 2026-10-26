using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OneStepDock
{
    internal static class StartupRegistration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "OneStepDock";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    {
                        return key != null && key.GetValue(ValueName) as string != null;
                    }
                }
                catch (Exception ex)
                {
                    Program.Log(ex);
                    return false;
                }
            }
        }

        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    if (enabled)
                    {
                        key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                return false;
            }
        }
    }
}
