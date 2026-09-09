using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ForHonorQuickActions
{

internal sealed class MainForm : Form
{
    private readonly Label statusLabel;
    private readonly Button closeButton;
    private readonly Button restartButton;
    private readonly Button ubisoftRestartButton;
    private readonly Button cleanRestartButton;
    private readonly Button locateButton;
    private readonly Button closeAppButton;
    private readonly Timer gameStateTimer;
    private bool isRunning;
    private bool gameIsRunning;
    private bool waitingForGameStart;
    private bool isFadingOut;

    private const int WmNclButtonDown = 0xA1;
    private const int HtCaption = 0x2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public MainForm()
    {
        Text = "For Honor Quick Actions";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(430, 345);
        BackColor = Color.FromArgb(19, 22, 29);
        Font = new Font("Segoe UI", 10F);
        DoubleBuffered = true;

        var titleBar = new Panel
        {
            BackColor = Color.FromArgb(24, 28, 36),
            Location = new Point(0, 0),
            Size = new Size(430, 36)
        };
        var appName = new Label
        {
            Text = "FOR HONOR QUICK ACTIONS",
            ForeColor = Color.FromArgb(151, 161, 178),
            Font = new Font("Segoe UI Semibold", 8.5F),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(14, 0),
            Size = new Size(300, 36)
        };
        closeAppButton = new Button
        {
            Text = "×",
            FlatStyle = FlatStyle.Flat,
            FlatAppearance =
            {
                BorderSize = 0,
                MouseOverBackColor = Color.FromArgb(184, 57, 57),
                MouseDownBackColor = Color.FromArgb(138, 43, 43)
            },
            BackColor = Color.FromArgb(24, 28, 36),
            ForeColor = Color.FromArgb(227, 231, 237),
            Font = new Font("Segoe UI", 18F),
            Location = new Point(394, 0),
            Size = new Size(36, 36),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        var title = new Label
        {
            Text = "FOR HONOR",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 22F),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 44),
            Size = new Size(390, 43)
        };
        var subtitle = new Label
        {
            Text = "Quick game controls",
            ForeColor = Color.FromArgb(165, 174, 188),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 83),
            Size = new Size(390, 24)
        };

        closeButton = MakeButton("Close For Honor", new Point(28, 110), Color.FromArgb(165, 58, 58));
        restartButton = MakeButton("Close and reopen For Honor", new Point(28, 163), Color.FromArgb(50, 101, 161));
        ubisoftRestartButton = MakeButton("Clean restart Ubisoft", new Point(28, 163), Color.FromArgb(50, 101, 161));
        cleanRestartButton = MakeButton("Clean restart (game + Ubisoft)", new Point(28, 216), Color.FromArgb(116, 82, 168));
        locateButton = new Button
        {
            Text = "Game location",
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderColor = Color.FromArgb(77, 86, 104), BorderSize = 1 },
            ForeColor = Color.FromArgb(190, 199, 214),
            BackColor = Color.FromArgb(31, 36, 47),
            Location = new Point(28, 283),
            Size = new Size(136, 32),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        statusLabel = new Label
        {
            Text = "Finds your installation automatically.",
            ForeColor = Color.FromArgb(165, 174, 188),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            Location = new Point(170, 288),
            Size = new Size(232, 21)
        };

        closeButton.Click += async (sender, args) => await RunActionAsync(gameIsRunning ? ActionKind.Close : ActionKind.Open);
        restartButton.Click += async (sender, args) => await RunActionAsync(ActionKind.Restart);
        ubisoftRestartButton.Click += async (sender, args) => await RunActionAsync(ActionKind.UbisoftRestart);
        cleanRestartButton.Click += async (sender, args) => await RunActionAsync(ActionKind.CleanRestart);
        locateButton.Click += (sender, args) => ChooseGameLocation();
        closeAppButton.Click += (sender, args) => BeginFadeOut();
        titleBar.MouseDown += BeginWindowDrag;
        appName.MouseDown += BeginWindowDrag;
        titleBar.Controls.AddRange(new Control[] { appName, closeAppButton });
        Controls.AddRange(new Control[] { titleBar, title, subtitle, closeButton, restartButton, ubisoftRestartButton, cleanRestartButton, locateButton, statusLabel });

        gameStateTimer = new Timer { Interval = 750 };
        gameStateTimer.Tick += (sender, args) => RefreshGameState();
        Shown += (sender, args) =>
        {
            RefreshGameState();
            gameStateTimer.Start();
        };
    }

    private Button MakeButton(string text, Point location, Color color)
    {
        var button = new Button();
        button.Text = text;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI Semibold", 11F);
        button.Location = location;
        button.Size = new Size(374, 43);
        button.Cursor = Cursors.Hand;
        button.TabStop = false;
        return button;
    }

    private async Task RunActionAsync(ActionKind action)
    {
        if (isRunning) return;
        isRunning = true;
        SetButtonsEnabled(false);

        try
        {
            switch (action)
            {
                case ActionKind.Close:
                    SetStatus("Closing For Honor…");
                    ProcessActions.Stop(ProcessActions.GameProcesses);
                    SetStatus("For Honor closed.");
                    await Task.Delay(250);
                    FinishAction();
                    break;

                case ActionKind.Open:
                    waitingForGameStart = false;
                    await LaunchGameAsync();
                    break;

                case ActionKind.Restart:
                    SetStatus("Closing For Honor…");
                    ProcessActions.Stop(ProcessActions.GameProcesses);
                    await Task.Delay(450);
                    await LaunchGameAsync();
                    break;

                case ActionKind.UbisoftRestart:
                    SetStatus("Closing game, anti-cheat, and Ubisoft…");
                    ProcessActions.Stop(ProcessActions.CleanRestartProcesses);
                    await Task.Delay(650);
                    await LaunchUbisoftAsync();
                    break;

                case ActionKind.CleanRestart:
                    SetStatus("Closing game, anti-cheat, and Ubisoft…");
                    ProcessActions.Stop(ProcessActions.CleanRestartProcesses);
                    await Task.Delay(650);
                    await LaunchGameAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            SetStatus("Could not complete that action.");
            MessageBox.Show(this, ex.Message, "For Honor Quick Actions", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetButtonsEnabled(true);
            isRunning = false;
        }
    }

    private async Task LaunchGameAsync()
    {
        SetStatus("Finding and starting For Honor…");
        Task<string> findTask = Task.Factory.StartNew<string>(delegate { return GameLocator.Find(); });
        var gamePath = await findTask;
        if (gamePath == null)
        {
            SetStatus("Game not found — choose it once below.");
            MessageBox.Show(this,
                "For Honor could not be found automatically. Select forhonor.exe once and this app will remember it.",
                "Choose For Honor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetButtonsEnabled(true);
            isRunning = false;
            return;
        }

        Process.Start(new ProcessStartInfo(gamePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(gamePath) });
        waitingForGameStart = true;
        SetStatus("For Honor launch sent. This utility will stay open.");
        isRunning = false;
        SetButtonsEnabled(true);
        RefreshGameState();
    }

    private async Task LaunchUbisoftAsync()
    {
        SetStatus("Finding and starting Ubisoft Connect…");
        Task<string> findTask = Task.Factory.StartNew<string>(delegate { return GameLocator.FindUbisoftConnect(); });
        var launcherPath = await findTask;
        if (launcherPath == null)
        {
            SetStatus("Ubisoft Connect could not be found.");
            MessageBox.Show(this,
                "Ubisoft Connect could not be found automatically. Install or open Ubisoft Connect, then try again.",
                "Ubisoft Connect not found", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetButtonsEnabled(true);
            isRunning = false;
            return;
        }

        Process.Start(new ProcessStartInfo(launcherPath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(launcherPath) });
        SetStatus("Ubisoft Connect is starting.");
        FinishAction();
    }

    private void FinishAction()
    {
        isRunning = false;
        SetButtonsEnabled(true);
        RefreshGameState();
    }

    private void ChooseGameLocation()
    {
        using (var dialog = new OpenFileDialog
        {
            Title = "Select forhonor.exe",
            Filter = "For Honor (forhonor.exe)|forhonor.exe|Programs (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            GameLocator.Save(dialog.FileName);
            SetStatus("Game location saved.");
        }
    }

    private void SetStatus(string text)
    {
        statusLabel.Text = text;
    }

    private void SetButtonsEnabled(bool enabled)
    {
        closeButton.Enabled = enabled;
        restartButton.Enabled = enabled;
        ubisoftRestartButton.Enabled = enabled;
        cleanRestartButton.Enabled = enabled;
        locateButton.Enabled = enabled;
    }

    private void RefreshGameState()
    {
        if (isRunning) return;

        gameIsRunning = ProcessActions.IsForHonorRunning();
        if (gameIsRunning) waitingForGameStart = false;
        if (gameIsRunning)
        {
            closeButton.Text = "Close For Honor";
            closeButton.BackColor = Color.FromArgb(165, 58, 58);
            closeButton.Location = new Point(28, 110);
            closeButton.Visible = true;
            restartButton.Visible = true;
            ubisoftRestartButton.Visible = false;
            cleanRestartButton.Text = "Clean restart (game + Ubisoft)";
            cleanRestartButton.Location = new Point(28, 216);
            statusLabel.Text = "For Honor is running.";
        }
        else
        {
            closeButton.Text = "Open For Honor";
            closeButton.BackColor = Color.FromArgb(51, 125, 86);
            closeButton.Location = new Point(28, 110);
            closeButton.Visible = true;
            restartButton.Visible = false;
            ubisoftRestartButton.Visible = true;
            ubisoftRestartButton.Location = new Point(28, 163);
            cleanRestartButton.Text = "Clean restart (Ubisoft + open game)";
            cleanRestartButton.Location = new Point(28, 216);
            statusLabel.Text = waitingForGameStart
                ? "For Honor launch sent. Waiting for the game…"
                : "For Honor is not running.";
        }
    }

    private void BeginWindowDrag(object sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WmNclButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    private async void BeginFadeOut()
    {
        if (isFadingOut) return;
        isFadingOut = true;
        closeAppButton.Enabled = false;
        gameStateTimer.Stop();

        for (var opacity = 1.0; opacity > 0.05; opacity -= 0.10)
        {
            Opacity = opacity;
            await Task.Delay(16);
        }
        Opacity = 0;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        if (!isFadingOut && args.CloseReason == CloseReason.UserClosing)
        {
            args.Cancel = true;
            BeginFadeOut();
            return;
        }
        base.OnFormClosing(args);
    }

    private enum ActionKind { Close, Open, Restart, UbisoftRestart, CleanRestart }
}
}
