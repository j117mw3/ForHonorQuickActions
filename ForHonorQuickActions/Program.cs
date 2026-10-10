using System;
using System.Windows.Forms;

namespace ForHonorQuickActions
{

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--recovery-helper")
        { Environment.ExitCode = RecoveryHelper.RunCommand(args); return; }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var startedWithWindows = Array.Exists(args,
            delegate(string argument) { return string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase); });
        Application.Run(new MainForm(startedWithWindows));
    }
}
}
