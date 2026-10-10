using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ForHonorQuickActions
{
internal sealed partial class MainForm
{
    private Panel recoveryPanel;
    private StopResult unresolved;
    private ActionKind recoveryAction;
    private IncidentReport incident;
    private bool recoveryVisible;
    private string issueUrl;
    internal Func<System.Collections.Generic.IEnumerable<ProcessTarget>, System.Threading.Tasks.Task<RecoveryResponse>> RecoveryRunner = RecoveryHelper.RunAsync;
    private bool HasUnresolvedGame()
    { return unresolved != null && unresolved.Targets.Any(t => t.Name.Equals("forhonor", StringComparison.OrdinalIgnoreCase) && t.IsAlive()); }
    private void ClearUnresolved() { if (unresolved != null) unresolved.Dispose(); unresolved = null; }
    private void ShowRecovery(ActionKind action, bool failed = false)
    {
        recoveryAction = action; recoveryVisible = true; isRunning = true; showingSettings = false;
        mainPanel.Visible = settingsPanel.Visible = false; SetButtonsEnabled(false); RestoreMainApplication();
        if (recoveryPanel != null) { Controls.Remove(recoveryPanel); recoveryPanel.Dispose(); }
        recoveryPanel = new Panel { Location = mainPanel.Location, Size = mainPanel.Size, BackColor = BackColor };
        Controls.Add(recoveryPanel); recoveryPanel.BringToFront(); titleBar.BringToFront();
        stateLabel.Text = "Stuck Process Recovery";
        var title = new Label { Text = failed ? "Recovery unsuccessful" : "Stuck Process Recovery", Location = new Point(18, 12),
            Size = new Size(394, 30), Font = new Font("Segoe UI Semibold", 13F), ForeColor = ForeColor };
        var text = new Label { Location = new Point(18, 46), Size = new Size(394, 190), ForeColor = ForeColor, Font = new Font("Segoe UI", 9.5F),
            Text = failed
                ? "We couldn’t terminate the stuck For Honor process. Restarting Windows is the reliable fallback for this type of hang. Easy Anti-Cheat may be involved, but the cause has not been confirmed.\n\nSend report publishes the sanitized diagnostics to a public GitHub issue. No account is needed."
                : "For Honor’s process is still running after we tried to close it. Would you like to try administrator recovery? This will attempt to stop Easy Anti-Cheat services and terminate the stuck process. Other games using Easy Anti-Cheat may be interrupted." };
        recoveryPanel.Controls.AddRange(new Control[] { title, text });
        if (!failed)
        {
            var admin = MakeButton("Try as administrator", new Point(18, 245), new Size(250, 38), preferences.AccentColor, 10F);
            var cancel = MakeButton("Cancel", new Point(278, 245), new Size(132, 38), Color.FromArgb(45, 55, 70), 10F);
            recoveryPanel.Controls.AddRange(new Control[] { admin, cancel });
            cancel.Click += delegate { LeaveRecovery(); };
            admin.Click += async delegate
            {
                admin.Enabled = cancel.Enabled = false;
                text.Text = "Waiting for Windows administrator consent, then checking process identity, stopping EAC services, and retrying termination…";
                RecoveryResponse result;
                try
                {
                    var targets = unresolved.Targets.Where(t => t.Name.Equals("forhonor", StringComparison.OrdinalIgnoreCase) && t.IsAlive()).ToArray();
                    result = targets.Length == 0 ? new RecoveryResponse { Outcome = "success" } : await RecoveryRunner(targets);
                }
                catch (Exception) { result = new RecoveryResponse { Outcome = "helper-failed" }; }
                if (IsDisposed) return;
                foreach (var step in result.Steps) IncidentReporting.Record(step.Stage, step.Outcome, step.Pid, step.Error, step.Milliseconds);
                IncidentReporting.Record("recovery", result.Outcome);
                if (result.Outcome == "cancelled") { LeaveRecovery(); SetStatus("Administrator recovery cancelled."); return; }
                if (result.Outcome == "success" && !HasUnresolvedGame())
                {
                    ClearUnresolved(); LeaveRecovery();
                    if (action == ActionKind.Close) SetStatus("For Honor closed.");
                    else if (action == ActionKind.CleanRestart) await RunActionAsync(ActionKind.CleanRestart);
                    else await RunActionAsync(ActionKind.Open);
                }
                else
                {
                    incident = IncidentReporting.Create(action.ToString(), preferences.LaunchPlatform.ToString(), result.Outcome);
                    ShowRecovery(action, true);
                }
            };
        }
        else
        {
            var send = MakeButton("Send report", new Point(18, 245), new Size(125, 38), preferences.AccentColor, 9F);
            var preview = MakeButton("View report", new Point(151, 245), new Size(125, 38), Color.FromArgb(45, 55, 70), 9F);
            var back = MakeButton("Back", new Point(284, 245), new Size(126, 38), Color.FromArgb(45, 55, 70), 9F);
            recoveryPanel.Controls.AddRange(new Control[] { send, preview, back });
            back.Click += delegate { LeaveRecovery(); };
            preview.Click += delegate { ShowReportPreview(); };
            send.Enabled = IncidentReporting.Configured;
            if (!send.Enabled) text.Text += "\nReporting is not configured in this build. View report to save diagnostics.";
            send.Click += async delegate
            {
                send.Enabled = preview.Enabled = back.Enabled = false;
                try
                {
                    var reply = await IncidentReporting.SendAsync(incident); issueUrl = reply.issueUrl;
                    text.Text = "Report sent. Thank you for helping investigate this hang.";
                    send.Text = "Report sent"; preview.Text = "View issue";
                }
                catch (Exception)
                {
                    text.Text = "The report could not be sent. It has not been confirmed as published. Retry uses the same incident ID to avoid duplicate issues. View report to save a local copy.";
                    IncidentReporting.Save(incident); send.Text = "Retry"; send.Enabled = true;
                }
                preview.Enabled = back.Enabled = true;
            };
        }
    }
    private void ShowReportPreview()
    {
        if (issueUrl != null) { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(issueUrl) { UseShellExecute = true }); return; }
        recoveryPanel.Controls.Clear();
        var view = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Location = new Point(18, 12), Size = new Size(392, 226), Text = IncidentReporting.Serialize(incident),
            BackColor = Color.FromArgb(28, 32, 40), ForeColor = Color.White, Font = new Font("Consolas", 8F) };
        var save = MakeButton("Save report", new Point(18, 245), new Size(190, 38), preferences.AccentColor, 10F);
        var back = MakeButton("Back", new Point(218, 245), new Size(192, 38), Color.FromArgb(45, 55, 70), 10F);
        recoveryPanel.Controls.AddRange(new Control[] { view, save, back });
        save.Click += delegate { save.Text = IncidentReporting.Save(incident) ? "Saved locally" : "Could not save"; };
        back.Click += delegate { ShowRecovery(recoveryAction, true); };
    }
    private void LeaveRecovery()
    {
        recoveryVisible = false;
        if (recoveryPanel != null) recoveryPanel.Visible = false;
        mainPanel.Visible = true; settingsPanel.Visible = false; settingsButton.Text = "⚙"; FinishAction();
    }
}
}
