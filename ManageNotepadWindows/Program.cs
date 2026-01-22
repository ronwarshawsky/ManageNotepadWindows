using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ManageNotepadWindows
{
    static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Use SetHighDpiMode for .NET Framework 4.7+ / Windows 10+
            // This is commented out because we're using app.manifest instead
            // Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new NotepadsManager());
        }
    }
}
