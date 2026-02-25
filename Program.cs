using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FlashscoreOverlay
{
    static class Program
    {
        [DllImport("kernel32.dll")] static extern bool AllocConsole();
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        [STAThread]
        static void Main()
        {
            // Attach to parent console (if launched from cmd/powershell)
            // or allocate a new one so Console.WriteLine logs are visible
            if (!AttachConsole(-1))
                AllocConsole();

            Console.WriteLine("═══ Furbo Flashscore Overlay ═══");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new OverlayForm());
        }
    }
}
