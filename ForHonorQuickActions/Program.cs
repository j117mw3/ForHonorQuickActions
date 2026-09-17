using System;
using System.Windows.Forms;

namespace ForHonorQuickActions
{

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var startedWithWindows = Array.Exists(args,
            delegate(string argument) { return string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase); });
        Application.Run(new MainForm(startedWithWindows));
    }
}
}
