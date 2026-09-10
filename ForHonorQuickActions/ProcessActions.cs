using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace ForHonorQuickActions
{

internal static class ProcessActions
{
    public static readonly string[] GameProcesses = { "forhonor" };

    // These cover the process names used by Ubisoft Connect and both generations of Easy Anti-Cheat.
    public static readonly string[] CleanRestartProcesses =
    {
        "forhonor", "EasyAntiCheat", "EasyAntiCheat_EOS",
        "upc", "UbisoftGameLauncher", "UbisoftGameLauncher64",
        "UbisoftConnect", "UbisoftConnectWebCore", "UplayWebCore"
    };

    public static void Stop(IEnumerable<string> processNames)
    {
        foreach (var name in processNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try { process.Kill(); }
                    catch (InvalidOperationException) { /* It exited before we could stop it. */ }
                    catch (System.ComponentModel.Win32Exception) { /* It is already protected or gone. */ }
                    finally { process.Dispose(); }
                }
            }
            catch (System.ComponentModel.Win32Exception) { /* Process enumeration can race with exits. */ }
        }
    }

    public static bool IsForHonorRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("forhonor");
            foreach (var process in processes) process.Dispose();
            return processes.Length > 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static Screen FindForHonorScreen()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("forhonor"))
            {
                try
                {
                    process.Refresh();
                    var window = process.MainWindowHandle;
                    if (window != IntPtr.Zero) return Screen.FromHandle(window);
                }
                catch (InvalidOperationException) { /* The game exited while its window was being inspected. */ }
                catch (System.ComponentModel.Win32Exception) { /* The game window is not available yet. */ }
                finally { process.Dispose(); }
            }
        }
        catch (System.ComponentModel.Win32Exception) { /* Process enumeration can race with exits. */ }

        return null;
    }
}
}
