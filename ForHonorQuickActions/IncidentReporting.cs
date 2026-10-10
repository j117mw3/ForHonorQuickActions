using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;

namespace ForHonorQuickActions
{
internal sealed class IncidentReport
{
    public int Schema = 1;
    public string IncidentId, AppVersion, Build, WindowsVersion, LaunchMethod, Action, Outcome;
    public List<IncidentEvent> Events;
}
internal sealed class IncidentEvent
{
    public string Time, Stage, Outcome;
    public int Pid, Error;
    public long Created, Milliseconds;
}
internal sealed class ReportReply { public string status { get; set; } public string issueUrl { get; set; } }
internal static class IncidentReporting
{
    // Set only after the deployed Worker has passed submission verification.
    public const string Endpoint = "https://for-honor-recovery-reports.mrassetto.workers.dev/reports";
    private static readonly Queue<IncidentEvent> timeline = new Queue<IncidentEvent>();
    private static readonly object sync = new object();
    private static readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForHonorQuickActions", "reports");
    public static bool Configured { get { return Endpoint.StartsWith("https://", StringComparison.Ordinal); } }
    public static void Record(string stage, string outcome, int pid = 0, int error = 0, long milliseconds = 0, long created = 0)
    {
        lock (sync)
        {
            timeline.Enqueue(new IncidentEvent { Time = DateTime.UtcNow.ToString("o"), Stage = stage, Outcome = outcome,
                Pid = pid, Error = error, Milliseconds = milliseconds, Created = created });
            while (timeline.Count > 200) timeline.Dequeue();
        }
    }
    public static IncidentReport Create(string action, string launch, string outcome)
    {
        string build = "unavailable";
        try
        {
            using (var file = File.OpenRead(System.Windows.Forms.Application.ExecutablePath))
            using (var sha = SHA256.Create()) build = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        catch (IOException) { }
        var report = new IncidentReport { IncidentId = Guid.NewGuid().ToString("D"), AppVersion = typeof(IncidentReporting).Assembly.GetName().Version.ToString(),
            Build = build, WindowsVersion = WindowsBuild(), LaunchMethod = launch, Action = action, Outcome = outcome };
        lock (sync) report.Events = timeline.ToList();
        while (Encoding.UTF8.GetByteCount(Serialize(report)) > 32768 && report.Events.Count > 0) report.Events.RemoveAt(0);
        Save(report); return report;
    }
    public static string Serialize(IncidentReport report) { return RecoveryHelper.Json.Serialize(report); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct VersionInfo
    {
        public int Size, Major, Minor, Build, Platform;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string ServicePack;
    }
    [DllImport("ntdll.dll", CharSet = CharSet.Unicode)] private static extern int RtlGetVersion(ref VersionInfo info);
    private static string WindowsBuild()
    {
        var info = new VersionInfo { Size = Marshal.SizeOf(typeof(VersionInfo)) };
        return RtlGetVersion(ref info) == 0 ? info.Major + "." + info.Minor + "." + info.Build : Environment.OSVersion.Version.ToString();
    }
    public static bool Save(IncidentReport report)
    {
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, report.IncidentId + ".json"), Serialize(report), new UTF8Encoding(false));
            foreach (var old in new DirectoryInfo(folder).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(20)) old.Delete();
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    public static async Task<ReportReply> SendAsync(IncidentReport report)
    {
        if (!Configured) throw new InvalidOperationException();
        using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
        using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) })
        using (var content = new StringContent(Serialize(report), Encoding.UTF8, "application/json"))
        using (var cancellation = new CancellationTokenSource(20000))
        using (var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content })
        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token))
        {
            response.EnsureSuccessStatusCode();
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var memory = new MemoryStream())
            {
                var buffer = new byte[1024]; int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation.Token)) > 0)
                { if (memory.Length + count > 4096) throw new IOException(); memory.Write(buffer, 0, count); }
                var reply = RecoveryHelper.Json.Deserialize<ReportReply>(Encoding.UTF8.GetString(memory.ToArray()));
                Uri uri;
                if (reply.status != "sent" || !Uri.TryCreate(reply.issueUrl, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.Host != "github.com" ||
                    !System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, "^/j117mw3/ForHonorQuickActions/issues/[0-9]+$")) throw new IOException();
                return reply;
            }
        }
    }
}
}
