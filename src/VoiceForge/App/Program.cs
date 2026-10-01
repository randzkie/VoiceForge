using System;
using System.Threading;
using System.Windows.Forms;

namespace VoiceForge
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Catch UI-thread exceptions with a friendly dialog instead of crashing silently.
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            Application.Run(new UI.MainForm());
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            try
            {
                MessageBox.Show(
                    "An unexpected error occurred:" + Environment.NewLine + e.Exception.Message,
                    "VoiceForge",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
                // Last resort - never crash the crash handler.
            }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            if (ex != null)
            {
                try
                {
                    MessageBox.Show(
                        "A fatal error occurred:" + Environment.NewLine + ex.Message,
                        "VoiceForge",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                catch
                {
                }
            }
        }
    }
}
