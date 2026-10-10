using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using ForHonorQuickActions;

internal static class RecoveryTests
{
    private sealed class FakeTarget : ProcessTarget
    {
        public bool Alive = true;
        public bool Denied;
        public override bool IsAlive() { return Alive; }
        public override void TryKill() { Error = Denied ? 5 : 0; }
    }
    private static int passed;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    [STAThread]
    private static int Main(string[] args)
    {
        var fixture = args[0]; var output = args[1]; Directory.CreateDirectory(output);
        try
        {
            var denied = new FakeTarget { Pid = 1, Created = 1, Name = "forhonor", Denied = true };
            using (var simulated = ProcessStopper.StopCore(() => new[] { denied }, 50, 5))
                Check(!simulated.Succeeded && simulated.Targets[0].Error == 5 && simulated.ElapsedMilliseconds < 150, "simulated access denied is bounded and retained");
            var pending = new FakeTarget { Pid = 2, Created = 1, Name = "forhonor" };
            using (var simulated = ProcessStopper.StopCore(() => new[] { pending }, 50, 5))
                Check(!simulated.Succeeded && simulated.Targets[0].Error == 0, "accepted termination without exit remains failure");
            using (var first = Process.Start(new ProcessStartInfo(fixture) { WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false }))
            using (var second = Process.Start(new ProcessStartInfo(fixture) { WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false }))
            {
                Thread.Sleep(200);
                using (var result = ProcessStopper.Stop(new[] { "fhqa-test-fixture" }, 6000, 750))
                {
                    Check(result.Succeeded && result.Targets.Count == 2, "two exact targets terminate");
                    Check(result.Targets.All(t => t.Created > 0 && t.Path != null), "identity and executable captured");
                    Check(result.ElapsedMilliseconds < 6200, "global termination budget");
                }
            }
            using (var live = Process.Start(new ProcessStartInfo(fixture) { WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false }))
            {
                try
                {
                    Thread.Sleep(100);
                    var identity = ProcessTarget.Capture(Process.GetProcessById(live.Id));
                    using (identity)
                    {
                        var request = new RecoveryRequest { Targets = new[] { new RecoveryIdentity { Pid = identity.Pid, Created = identity.Created + 1, Path = identity.Path } } };
                        var response = RecoveryHelper.Recover(request);
                        Check(response.Outcome == "failed" && response.Steps[0].Stage == "validation", "PID reuse or creation mismatch fails closed");
                        live.Refresh(); Check(!live.HasExited, "validation failure leaves process alive");
                        request.Targets[0].Created = identity.Created;
                        response = RecoveryHelper.Recover(request);
                        Check(response.Outcome == "failed", "non-ForHonor executable rejected before EAC mutation");
                        request.Targets[0].Path = null;
                        Check(RecoveryHelper.Recover(request).Outcome == "failed", "unavailable identity fails closed");
                        live.Kill(); live.WaitForExit();
                        Check(!identity.IsAlive(), "retained handle observes spontaneous exit");
                    }
                }
                finally { if (!live.HasExited) live.Kill(); }
            }
            Check(RecoveryHelper.RunCommand(new[] { "--recovery-helper" }) == 2, "malformed helper arguments rejected");
            for (int i = 0; i < 250; i++) IncidentReporting.Record("termination", "survived", i, 5, 6000);
            var incident = IncidentReporting.Create("Restart", "Steam", "failed");
            var json = IncidentReporting.Serialize(incident);
            Check(incident.Events.Count <= 200 && System.Text.Encoding.UTF8.GetByteCount(json) <= 32768, "bounded timeline and report");
            Check(!json.Contains(Environment.UserName) && !json.Contains(Environment.MachineName) && !json.Contains("Path") && !json.Contains("CommandLine"), "report has no identifying paths or machine fields");
            Check(IncidentReporting.Configured, "verified production endpoint is configured");
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new MainForm())
            {
                form.Show(); Application.DoEvents();
                Render(form, Path.Combine(output, "quick-actions.png"));
                Invoke(form, "ShowRecovery", MainForm.ActionKind.Restart, false); Application.DoEvents();
                var panel = (Panel)Field(form, "recoveryPanel");
                Check(form.ClientSize == new Size(430, 345) && panel.Visible, "recovery uses same window size");
                Check(panel.Controls.OfType<Button>().Select(b => b.Text).SequenceEqual(new[] { "Try as administrator", "Cancel" }), "consent controls and order");
                Render(form, Path.Combine(output, "recovery-consent.png"));
                Invoke(form, "LeaveRecovery"); Check(!(bool)Field(form, "recoveryVisible"), "cancel returns to Quick Actions");
                var leftover = new FakeTarget { Pid = 999, Created = 1, Name = "forhonor" };
                var unresolved = new StopResult(); unresolved.Targets.Add(leftover);
                form.GetType().GetField("unresolved", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, unresolved);
                form.RecoveryRunner = targets => System.Threading.Tasks.Task.FromResult(new RecoveryResponse { Outcome = "cancelled" });
                Invoke(form, "ShowRecovery", MainForm.ActionKind.Restart, false);
                ((Panel)Field(form, "recoveryPanel")).Controls.OfType<Button>().First().PerformClick(); Application.DoEvents();
                Check(!(bool)Field(form, "recoveryVisible") && (bool)InvokeResult(form, "HasUnresolvedGame"), "UAC cancellation preserves unresolved target without relaunch");
                form.RecoveryRunner = targets => System.Threading.Tasks.Task.FromResult(new RecoveryResponse { Outcome = "helper-timeout" });
                Invoke(form, "ShowRecovery", MainForm.ActionKind.Restart, false);
                ((Panel)Field(form, "recoveryPanel")).Controls.OfType<Button>().First().PerformClick(); Application.DoEvents();
                Check((bool)Field(form, "recoveryVisible") && ((Panel)Field(form, "recoveryPanel")).Controls.OfType<Button>().First().Text == "Send report", "helper timeout enters failure view without relaunch");
                Invoke(form, "LeaveRecovery");
                form.RecoveryRunner = targets => { leftover.Alive = false; return System.Threading.Tasks.Task.FromResult(new RecoveryResponse { Outcome = "success", Steps = new List<RecoveryStep> { new RecoveryStep { Stage = "eac-service", Outcome = "refused", Error = 5 } } }); };
                Invoke(form, "ShowRecovery", MainForm.ActionKind.Close, false);
                ((Panel)Field(form, "recoveryPanel")).Controls.OfType<Button>().First().PerformClick(); Application.DoEvents();
                Check(!(bool)Field(form, "recoveryVisible") && Field(form, "unresolved") == null, "successful Close completes despite simulated service refusal and never relaunches");
                leftover.Alive = true; unresolved = new StopResult(); unresolved.Targets.Add(leftover);
                form.GetType().GetField("unresolved", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, unresolved);
                Invoke(form, "RunActionAsync", MainForm.ActionKind.Restart); Application.DoEvents();
                Check((bool)Field(form, "recoveryVisible") && (MainForm.ActionKind)Field(form, "recoveryAction") == MainForm.ActionKind.Restart, "main restart routes retained target to recovery");
                Invoke(form, "LeaveRecovery");
                form.GetType().GetField("gameIsRunning", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, true);
                Invoke(form, "UpdateTrayMenu");
                // Invoke the click event: a hidden ContextMenuStrip intentionally suppresses PerformClick.
                typeof(ToolStripItem).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Field(form, "trayGameRestartItem"), new object[] { EventArgs.Empty }); Application.DoEvents();
                Check((bool)Field(form, "recoveryVisible"), "tray restart shares recovery pipeline");
                Invoke(form, "LeaveRecovery");
                form.GetType().GetField("gameIsRunning", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, true);
                Invoke(form, "RunOverlayAction", MainForm.ActionKind.CleanRestart); Application.DoEvents();
                Check((bool)Field(form, "recoveryVisible") && (MainForm.ActionKind)Field(form, "recoveryAction") == MainForm.ActionKind.CleanRestart, "overlay Clean Restart shares recovery pipeline");
                Invoke(form, "LeaveRecovery"); Invoke(form, "ClearUnresolved");
                form.GetType().GetField("incident", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, incident);
                Invoke(form, "ShowRecovery", MainForm.ActionKind.Restart, true); Application.DoEvents();
                panel = (Panel)Field(form, "recoveryPanel");
                Check(panel.Controls.OfType<Button>().Select(b => b.Text).SequenceEqual(new[] { "Send report", "View report", "Back" }), "failure controls and order");
                Check(panel.Controls.OfType<Button>().First().Enabled, "reporting enabled after live backend verification");
                Render(form, Path.Combine(output, "recovery-failure.png"));
                Invoke(form, "ShowReportPreview"); Application.DoEvents();
                Check(panel.Controls.OfType<TextBox>().Single().ReadOnly, "sanitized report preview is read only");
                Render(form, Path.Combine(output, "report-preview.png"));
                if (args.Length > 2)
                {
                    var liveReport = RecoveryHelper.Json.Deserialize<IncidentReport>(File.ReadAllText(args[2]));
                    form.GetType().GetField("incident", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, liveReport);
                    Invoke(form, "ShowRecovery", MainForm.ActionKind.Restart, true);
                    ((Panel)Field(form, "recoveryPanel")).Controls.OfType<Button>().First().PerformClick();
                    var wait = Stopwatch.StartNew();
                    while (Field(form, "issueUrl") == null && wait.ElapsedMilliseconds < 22000) { Application.DoEvents(); Thread.Sleep(25); }
                    Check((string)Field(form, "issueUrl") == "https://github.com/j117mw3/ForHonorQuickActions/issues/1", "live desktop Send report returns existing synthetic issue");
                    Render(form, Path.Combine(output, "report-sent.png"));
                }
            }
            Console.WriteLine("Passed " + passed + " checks."); return 0;
        }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
    }
    private static object Field(object value, string name) { return value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value); }
    private static void Invoke(object value, string name, params object[] args) { value.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(value, args); }
    private static object InvokeResult(object value, string name) { return value.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(value, null); }
    private static void Render(Form form, string path) { using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, form.ClientRectangle); bitmap.Save(path); } }
}
