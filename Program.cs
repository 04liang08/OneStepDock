using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace OneStepDock
{
    internal static class Program
    {
        private const string MutexName = "OneStepDock.SingleInstance";
        private const string ShowEventName = "OneStepDock.Show";

        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && Array.IndexOf(args, "--elevated") >= 0)
            {
                Thread.Sleep(700);
            }

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    SignalExistingInstance();
                    return;
                }

                using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.ThreadException += (s, e) => Log(e.Exception);
                    AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);

                    var form = new DockForm();
                    var waiter = new Thread(() =>
                    {
                        while (true)
                        {
                            try
                            {
                                showEvent.WaitOne();
                            }
                            catch
                            {
                                return;
                            }

                            if (form.IsDisposed)
                            {
                                return;
                            }

                            try
                            {
                                form.BeginInvoke(new Action(form.ShowFromSecondInstance));
                            }
                            catch
                            {
                                return;
                            }
                        }
                    })
                    {
                        IsBackground = true,
                        Name = "OneStepDock.ShowWaiter"
                    };
                    waiter.Start();

                    try
                    {
                        Application.Run(form);
                    }
                    catch (Exception ex)
                    {
                        Log(ex);
                        throw;
                    }
                    finally
                    {
                        showEvent.Set();
                    }
                }
            }
        }

        private static void SignalExistingInstance()
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    using (EventWaitHandle showEvent = EventWaitHandle.OpenExisting(ShowEventName))
                    {
                        showEvent.Set();
                        return;
                    }
                }
                catch
                {
                    Thread.Sleep(150);
                }
            }
        }

        internal static void Log(Exception ex)
        {
            if (ex == null)
            {
                return;
            }

            LogText(ex.ToString());
        }

        internal static void LogText(string message)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OneStepDock");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
