using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ForHonorQuickActions
{

internal static class ProcessActions
{
    private const int StopTimeoutMilliseconds = 6000;
    private const int QuietPeriodMilliseconds = 750;
    private const int GwOwner = 4;

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

    public static readonly string[] GameProcesses = { "forhonor" };

    // These cover the process names used by Ubisoft Connect and both generations of Easy Anti-Cheat.
    public static readonly string[] CleanRestartProcesses =
    {
        "forhonor", "EasyAntiCheat", "EasyAntiCheat_EOS",
        "upc", "UbisoftGameLauncher", "UbisoftGameLauncher64",
        "UbisoftConnect", "UbisoftConnectWebCore", "UplayWebCore"
    };

    public static bool Stop(IEnumerable<string> processNames)
    {
        var names = processNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var stopwatch = Stopwatch.StartNew();
        var lastTargetSeenAt = stopwatch.ElapsedMilliseconds;

        // Kill() is asynchronous, and launchers can briefly recreate a process
        // while they are shutting down. Keep sweeping until every requested name
        // has stayed absent for a short quiet period instead of trusting one
        // process snapshot.
        while (stopwatch.ElapsedMilliseconds < StopTimeoutMilliseconds)
        {
            var foundTarget = false;
            foreach (var name in names)
            {
                Process[] processes;
                try { processes = Process.GetProcessesByName(name); }
                catch (System.ComponentModel.Win32Exception) { continue; }

                if (processes.Length > 0)
                {
                    foundTarget = true;
                    lastTargetSeenAt = stopwatch.ElapsedMilliseconds;
                }

                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited) process.Kill();
                        process.WaitForExit(750);
                    }
                    catch (InvalidOperationException) { /* It exited before we could stop it. */ }
                    catch (System.ComponentModel.Win32Exception) { /* It is protected or already gone. */ }
                    finally { process.Dispose(); }
                }
            }

            if (!foundTarget && stopwatch.ElapsedMilliseconds - lastTargetSeenAt >= QuietPeriodMilliseconds)
                return true;

            Thread.Sleep(100);
        }

        return !AreAnyRunning(names);
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
        var processIds = new HashSet<int>();
        var mainWindowHandles = new List<IntPtr>();

        try
        {
            foreach (var process in Process.GetProcessesByName("forhonor"))
            {
                try
                {
                    process.Refresh();
                    processIds.Add(process.Id);
                    var window = process.MainWindowHandle;
                    if (window != IntPtr.Zero) mainWindowHandles.Add(window);
                }
                catch (InvalidOperationException) { /* The game exited while its window was being inspected. */ }
                catch (System.ComponentModel.Win32Exception) { /* The game window is not available yet. */ }
                finally { process.Dispose(); }
            }
        }
        catch (System.ComponentModel.Win32Exception) { /* Process enumeration can race with exits. */ }

        if (processIds.Count == 0) return null;

        // MainWindowHandle can point at a launcher/splash window (or be zero)
        // with some fullscreen modes. Enumerate all top-level windows owned by
        // every For Honor process and choose the largest visible game window.
        IntPtr bestWindow = IntPtr.Zero;
        long bestVisibleArea = 0;
        EnumWindows(delegate(IntPtr window, IntPtr parameter)
        {
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (!processIds.Contains((int)processId) || !IsWindowVisible(window) || IsIconic(window))
                return true;
            if (GetWindow(window, GwOwner) != IntPtr.Zero) return true;

            NativeRect nativeBounds;
            if (!GetWindowRect(window, out nativeBounds)) return true;
            var bounds = Rectangle.FromLTRB(nativeBounds.Left, nativeBounds.Top, nativeBounds.Right, nativeBounds.Bottom);
            if (bounds.Width <= 0 || bounds.Height <= 0) return true;

            var screen = Screen.FromRectangle(bounds);
            var visibleBounds = Rectangle.Intersect(bounds, screen.Bounds);
            var visibleArea = (long)visibleBounds.Width * visibleBounds.Height;
            if (visibleArea > bestVisibleArea)
            {
                bestVisibleArea = visibleArea;
                bestWindow = window;
            }
            return true;
        }, IntPtr.Zero);

        if (bestWindow != IntPtr.Zero) return Screen.FromHandle(bestWindow);

        // Retain the framework's process-level result as a fallback while the
        // native game window is still transitioning between display modes.
        foreach (var window in mainWindowHandles)
        {
            if (IsWindowVisible(window) && !IsIconic(window)) return Screen.FromHandle(window);
        }

        return null;
    }

    private static bool AreAnyRunning(IEnumerable<string> processNames)
    {
        foreach (var name in processNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                var anyRunning = processes.Length > 0;
                foreach (var process in processes) process.Dispose();
                if (anyRunning) return true;
            }
            catch (System.ComponentModel.Win32Exception) { /* Process enumeration can race with exits. */ }
        }

        return false;
    }
}
}
