using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ForHonorQuickActions
{
internal class ProcessTarget : IDisposable
{
    public int Pid;
    public long Created;
    public string Name;
    public string Path;
    public int Error;
    public bool Exited;
    internal Process Process;
    private IntPtr identityHandle;
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(IntPtr handle, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr handle, uint flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr handle, uint exitCode);

    public static ProcessTarget Capture(Process process)
    {
        var target = new ProcessTarget { Pid = process.Id, Name = process.ProcessName, Process = process };
        target.identityHandle = OpenProcess(0x101000, false, target.Pid); // SYNCHRONIZE | QUERY_LIMITED_INFORMATION
        if (target.identityHandle != IntPtr.Zero)
        {
            long created, exited, kernel, user;
            if (GetProcessTimes(target.identityHandle, out created, out exited, out kernel, out user))
                target.Created = DateTime.FromFileTimeUtc(created).Ticks;
            var path = new StringBuilder(32768); int length = path.Capacity;
            if (QueryFullProcessImageName(target.identityHandle, 0, path, ref length)) target.Path = path.ToString();
            return target;
        }
        try { target.Created = process.StartTime.ToUniversalTime().Ticks; } catch (Win32Exception) { }
        catch (InvalidOperationException) { target.Exited = true; }
        try { target.Path = process.MainModule.FileName; } catch (Win32Exception) { }
        catch (InvalidOperationException) { target.Exited = true; }
        // Opening and retaining this handle anchors exit checks to this object, not a later PID reuse.
        try { var handle = process.Handle; } catch (Win32Exception ex) { target.Error = ex.NativeErrorCode; }
        catch (InvalidOperationException) { target.Exited = true; }
        return target;
    }

    public virtual bool IsAlive()
    {
        if (Exited) return false;
        if (identityHandle != IntPtr.Zero)
        {
            uint wait = WaitForSingleObject(identityHandle, 0);
            if (wait == 0) { Exited = true; return false; }
            if (wait == 0xFFFFFFFF) Error = Marshal.GetLastWin32Error();
            return true;
        }
        try
        {
            if (Process.HasExited) { Exited = true; return false; }
            return true;
        }
        catch (InvalidOperationException) { Exited = true; return false; }
        catch (Win32Exception ex) { Error = ex.NativeErrorCode; return true; }
    }

    public virtual void TryKill()
    {
        if (!IsAlive()) return;
        IntPtr terminationHandle = OpenProcess(0x101001, false, Pid);
        if (terminationHandle == IntPtr.Zero) { Error = Marshal.GetLastWin32Error(); IsAlive(); return; }
        try
        {
            long created, exited, kernel, user;
            if (Created == 0 || !GetProcessTimes(terminationHandle, out created, out exited, out kernel, out user))
            { Error = Created == 0 ? 13 : Marshal.GetLastWin32Error(); return; }
            if (DateTime.FromFileTimeUtc(created).Ticks != Created) { Exited = true; return; }
            Error = TerminateProcess(terminationHandle, 1) ? 0 : Marshal.GetLastWin32Error();
        }
        finally { CloseHandle(terminationHandle); }
    }

    public void Dispose() { if (identityHandle != IntPtr.Zero) { CloseHandle(identityHandle); identityHandle = IntPtr.Zero; } if (Process != null) Process.Dispose(); }
}

internal sealed class StopResult : IDisposable
{
    public readonly List<ProcessTarget> Targets = new List<ProcessTarget>();
    public readonly List<int> EnumerationErrors = new List<int>();
    public long ElapsedMilliseconds;
    public bool Succeeded { get { return EnumerationErrors.Count == 0 && Targets.All(t => !t.IsAlive()); } }
    public void Dispose() { foreach (var target in Targets) target.Dispose(); }
}

internal static class ProcessStopper
{
    public static StopResult Stop(IEnumerable<string> names, int budget, int quiet)
    {
        var result = new StopResult();
        return StopCore(delegate
        {
            var captured = new List<ProcessTarget>();
            foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Process[] processes;
                try { processes = Process.GetProcessesByName(name); }
                catch (Win32Exception ex) { result.EnumerationErrors.Add(ex.NativeErrorCode); continue; }
                foreach (var process in processes)
                {
                    try { captured.Add(ProcessTarget.Capture(process)); }
                    catch (InvalidOperationException) { process.Dispose(); }
                }
            }
            return captured;
        }, budget, quiet, result);
    }

    internal static StopResult StopCore(Func<IEnumerable<ProcessTarget>> scan, int budget, int quiet, StopResult result = null)
    {
        result = result ?? new StopResult();
        var clock = Stopwatch.StartNew();
        long lastSeen = 0;
        while (clock.ElapsedMilliseconds < budget)
        {
            bool found = false;
            foreach (var candidate in scan())
            {
                found = true;
                var existing = result.Targets.FirstOrDefault(t => t.Pid == candidate.Pid && t.Created == candidate.Created && t.IsAlive());
                if (existing != null) { if (!ReferenceEquals(candidate, existing)) candidate.Dispose(); }
                else { existing = candidate; result.Targets.Add(existing); }
                if (clock.ElapsedMilliseconds < budget) existing.TryKill();
            }
            foreach (var target in result.Targets) target.IsAlive();
            if (found) lastSeen = clock.ElapsedMilliseconds;
            if (!found && clock.ElapsedMilliseconds - lastSeen >= quiet) break;
            Thread.Sleep((int)Math.Min(50, Math.Max(0, budget - clock.ElapsedMilliseconds)));
        }
        result.ElapsedMilliseconds = clock.ElapsedMilliseconds;
        return result;
    }
}

internal sealed class RecoveryRequest
{
    public string Nonce;
    public RecoveryIdentity[] Targets;
}
internal sealed class RecoveryIdentity
{
    public int Pid;
    public long Created;
    public string Path;
}
internal sealed class RecoveryResponse
{
    public string Outcome;
    public List<RecoveryStep> Steps = new List<RecoveryStep>();
}
internal sealed class RecoveryStep
{
    public string Stage;
    public string Outcome;
    public int Pid;
    public int Error;
    public long Milliseconds;
}

internal static class RecoveryHelper
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint pid);

    internal static JavaScriptSerializer Json { get { return new JavaScriptSerializer { MaxJsonLength = 65536 }; } }

    // Read a bounded frame without allowing an untrusted peer to allocate an unlimited line.
    internal static string ReadFrame(Stream stream)
    {
        var bytes = new List<byte>();
        for (int i = 0; i <= 32768; i++)
        {
            int b = stream.ReadByte();
            if (b == 10) return Encoding.UTF8.GetString(bytes.ToArray());
            if (b < 0) throw new IOException();
            bytes.Add((byte)b);
        }
        throw new IOException();
    }
    internal static void WriteFrame(Stream stream, object value)
    {
        var bytes = Encoding.UTF8.GetBytes(Json.Serialize(value) + "\n");
        if (bytes.Length > 32769) throw new IOException();
        stream.Write(bytes, 0, bytes.Length); stream.Flush();
    }

    public static async Task<RecoveryResponse> RunAsync(IEnumerable<ProcessTarget> targets)
    {
        string name = "FHQA-" + Guid.NewGuid().ToString("N");
        string nonce = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        using (var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 4096, 4096, security))
        {
            var self = Process.GetCurrentProcess();
            using (self)
            {
                Process helper;
                try
                {
                    helper = Process.Start(new ProcessStartInfo(self.MainModule.FileName,
                        "--recovery-helper " + name + " " + nonce + " " + self.Id + " " + self.StartTime.ToUniversalTime().Ticks)
                        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
                }
                catch (Win32Exception ex)
                {
                    return new RecoveryResponse { Outcome = ex.NativeErrorCode == 1223 ? "cancelled" : "helper-failed" };
                }
                using (helper)
                {
                    try
                    {
                        var connection = pipe.WaitForConnectionAsync();
                        if (await Task.WhenAny(connection, Task.Delay(15000)) != connection)
                            return new RecoveryResponse { Outcome = "helper-timeout" };
                        await connection;
                        uint peer;
                        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out peer) || peer != helper.Id)
                            return new RecoveryResponse { Outcome = "helper-failed" };
                        var request = new RecoveryRequest { Nonce = nonce, Targets = targets.Select(t => new RecoveryIdentity
                            { Pid = t.Pid, Created = t.Created, Path = t.Path }).ToArray() };
                        WriteFrame(pipe, request);
                        var response = Task.Run(delegate { return Json.Deserialize<RecoveryResponse>(ReadFrame(pipe)); });
                        if (await Task.WhenAny(response, Task.Delay(35000)) != response)
                            return new RecoveryResponse { Outcome = "helper-timeout" };
                        return await response;
                    }
                    catch (Exception) { return new RecoveryResponse { Outcome = "helper-failed" }; }
                }
            }
        }
    }

    public static int RunCommand(string[] args)
    {
        if (args.Length != 5 || !args[1].StartsWith("FHQA-", StringComparison.Ordinal) || args[1].Length != 37 || args[2].Length != 64) return 2;
        int parentId; long created;
        if (!int.TryParse(args[3], out parentId) || !long.TryParse(args[4], out created)) return 2;
        try
        {
            using (var parent = Process.GetProcessById(parentId))
            using (var self = Process.GetCurrentProcess())
            {
                if (parent.StartTime.ToUniversalTime().Ticks != created ||
                    !string.Equals(parent.MainModule.FileName, self.MainModule.FileName, StringComparison.OrdinalIgnoreCase)) return 2;
                using (var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    pipe.Connect(10000);
                    uint peer;
                    if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out peer) || peer != parentId) return 2;
                    var read = Task.Run(delegate { return Json.Deserialize<RecoveryRequest>(ReadFrame(pipe)); });
                    if (!read.Wait(10000)) return 2;
                    var request = read.Result;
                    if (request.Nonce != args[2] || request.Targets == null || request.Targets.Length == 0 || request.Targets.Length > 16) return 2;
                    WriteFrame(pipe, Recover(request));
                    return 0;
                }
            }
        }
        catch (Exception) { return 2; }
    }

    internal static RecoveryResponse Recover(RecoveryRequest request)
    {
        var response = new RecoveryResponse { Outcome = "failed" };
        var captured = new List<ProcessTarget>();
        try
        {
            // Validate every target before any service or process mutation.
            foreach (var identity in request.Targets)
            {
                Process process;
                try { process = Process.GetProcessById(identity.Pid); }
                catch (ArgumentException) { continue; }
                var target = ProcessTarget.Capture(process); captured.Add(target);
                if (!target.IsAlive()) continue;
                if (identity.Created == 0 || identity.Path == null || target.Created != identity.Created ||
                    !string.Equals(target.Path, identity.Path, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(System.IO.Path.GetFileName(target.Path), "forhonor.exe", StringComparison.OrdinalIgnoreCase))
                {
                    response.Steps.Add(new RecoveryStep { Stage = "validation", Outcome = "identity-unavailable", Pid = identity.Pid });
                    return response;
                }
            }
            if (captured.All(t => !t.IsAlive())) { response.Outcome = "success"; return response; }
            var serviceSteps = new RecoveryResponse();
            var serviceTask = Task.Run(delegate { StopEacServices(serviceSteps); });
            if (serviceTask.Wait(20000)) response.Steps.AddRange(serviceSteps.Steps);
            else response.Steps.Add(new RecoveryStep { Stage = "service-discovery", Outcome = "timeout", Milliseconds = 20000 });
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 10000 && captured.Any(t => t.IsAlive()))
            {
                foreach (var target in captured) target.TryKill();
                Thread.Sleep(50);
            }
            foreach (var target in captured) response.Steps.Add(new RecoveryStep { Stage = "termination",
                Outcome = target.IsAlive() ? "survived" : "exited", Pid = target.Pid, Error = target.Error, Milliseconds = clock.ElapsedMilliseconds });
            if (captured.All(t => !t.IsAlive())) response.Outcome = "success";
            return response;
        }
        catch (Exception) { response.Steps.Add(new RecoveryStep { Stage = "helper", Outcome = "unavailable" }); return response; }
        finally { foreach (var target in captured) target.Dispose(); }
    }

    private static void StopEacServices(RecoveryResponse response)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var options = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(3), ReturnImmediately = false };
            using (var search = new ManagementObjectSearcher("root\\cimv2",
                "SELECT Name, PathName, ServiceType FROM Win32_Service WHERE Name='EasyAntiCheat' OR Name='EasyAntiCheat_EOS'", options))
            using (var services = search.Get())
            foreach (ManagementObject service in services)
            {
                using (service)
                {
                    if (clock.ElapsedMilliseconds >= 20000) break;
                    string name = (string)service["Name"];
                    var step = new RecoveryStep { Stage = name == "EasyAntiCheat_EOS" ? "eac-eos-service" : "eac-service" };
                    response.Steps.Add(step);
                    string command = (string)service["PathName"];
                    string path = null;
                    if (!string.IsNullOrEmpty(command))
                    {
                        if (command.StartsWith("\"")) { int end = command.IndexOf('"', 1); if (end > 1) path = command.Substring(1, end - 1); }
                        else { int end = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); if (end >= 0) path = command.Substring(0, end + 4); }
                    }
                    string type = Convert.ToString(service["ServiceType"]);
                    if (path == null || !System.IO.Path.IsPathRooted(path) ||
                        !string.Equals(System.IO.Path.GetFileName(path), name + ".exe", StringComparison.OrdinalIgnoreCase) ||
                        (type != "Own Process" && type != "Share Process") || !TrustedEacBinary(path))
                    { step.Outcome = "identity-unavailable"; continue; }
                    if (clock.ElapsedMilliseconds >= 20000) { step.Outcome = "timeout"; break; }
                    try
                    {
                        using (var controller = new ServiceController(name))
                        {
                            if (controller.Status == ServiceControllerStatus.Stopped) step.Outcome = "already-stopped";
                            else if (!controller.CanStop && controller.Status != ServiceControllerStatus.StopPending) step.Outcome = "refused";
                            else
                            {
                                if (clock.ElapsedMilliseconds >= 20000) { step.Outcome = "timeout"; break; }
                                if (controller.Status != ServiceControllerStatus.StopPending) controller.Stop();
                                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMilliseconds(Math.Max(1, 20000 - clock.ElapsedMilliseconds)));
                                step.Outcome = "stopped";
                            }
                        }
                    }
                    catch (System.ServiceProcess.TimeoutException) { step.Outcome = "timeout"; }
                    catch (Win32Exception ex) { step.Outcome = "refused"; step.Error = ex.NativeErrorCode; }
                    catch (InvalidOperationException ex) { step.Outcome = "refused"; var native = ex.InnerException as Win32Exception; if (native != null) step.Error = native.NativeErrorCode; }
                    step.Milliseconds = clock.ElapsedMilliseconds;
                }
            }
        }
        catch (Exception) { response.Steps.Add(new RecoveryStep { Stage = "service-discovery", Outcome = "unavailable" }); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile { public uint Size; public IntPtr Path; public IntPtr File; public IntPtr Subject; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size; public IntPtr Callback; public IntPtr Sip; public uint Ui; public uint Revocation;
        public uint Choice; public IntPtr File; public uint StateAction; public IntPtr State; public IntPtr Url;
        public uint Flags; public uint Context;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    private static bool TrustedEacBinary(string path)
    {
        IntPtr filePath = Marshal.StringToCoTaskMemUni(path);
        IntPtr file = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile)));
        try
        {
            Marshal.StructureToPtr(new TrustFile { Size = (uint)Marshal.SizeOf(typeof(TrustFile)), Path = filePath }, file, false);
            var data = new TrustData { Size = (uint)Marshal.SizeOf(typeof(TrustData)), Ui = 2, Choice = 1, File = file, Flags = 0x1000 };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) return false;
            using (var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
            {
                string publisher = cert.GetNameInfo(X509NameType.SimpleName, false);
                return publisher == "Epic Games, Inc." || publisher == "EasyAntiCheat Oy" || publisher == "Epic Games Inc.";
            }
        }
        catch (Exception) { return false; }
        finally { Marshal.FreeHGlobal(file); Marshal.FreeCoTaskMem(filePath); }
    }
}
}
