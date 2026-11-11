using System;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;

namespace ClassLock
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--service")
            {
                ServiceBase.Run(new AgentService());
                return 0;
            }

            // Student app: one copy per Windows session.
            bool first;
            using (var mutex = new Mutex(true, @"Local\ClassLockApp", out first))
            {
                if (!first) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new StatusForm());
            }
            return 0;
        }
    }
}
