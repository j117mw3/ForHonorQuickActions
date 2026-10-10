using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ForHonorQuickActions
{
internal sealed partial class MainForm : Form
{
    private readonly AppPreferences preferences;
    private readonly ToolTip toolTips = new ToolTip();
    private Panel titleBar, mainPanel, settingsPanel, defaultLaunchPanel, appearancePanel;
    private Label stateDot, stateLabel, opacityValueLabel, executablePathLabel;
    private AnimatedButton closeButton, restartButton, ubisoftToggleButton, ubisoftRestartButton;
    private AnimatedButton cleanRestartButton, settingsButton, overlayButton;
    private AnimatedButton closeAppButton, defaultLaunchTab, appearanceTab;
    private RadioButton automaticRadio, steamRadio, ubisoftRadio, executableRadio, trayRadio, taskbarRadio;
    private DarkSlider opacitySlider;
    private ComboBox accentList;
    private CheckBox toolTipsCheck, startupCheck;
    private NotifyIcon trayIcon;
    private ToolStripMenuItem trayGameToggleItem, trayGameRestartItem;
    private ToolStripMenuItem trayUbisoftToggleItem, trayUbisoftRestartItem;
    private Timer gameStateTimer;
    private GameOverlay gameOverlay;
    private Screen forHonorScreen;
    private bool isRunning, gameIsRunning, waitingForGameStart, overlayShownForCurrentGame;
    private bool isFadingOut, showingSettings, loadingSettings, stateRefreshPending;
    private bool settingsControlsCreated;
    private bool startupBackgroundMode;
    private bool? displayedGameRunning, displayedUbisoftRunning;

    private const int WmNclButtonDown = 0xA1;
    private const int HtCaption = 0x2;
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public MainForm(bool startMinimized = false)
    {
        startupBackgroundMode = startMinimized;
        preferences = AppPreferences.Load();
        Text = "For Honor Quick Actions";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(430, 345);
        MinimumSize = MaximumSize = Size;
        BackColor = Color.FromArgb(19, 22, 29);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);
        DoubleBuffered = true;
        Opacity = preferences.WindowOpacityPercent / 100.0;

        BuildChrome();
        BuildMainView();
        BuildSettingsView();
        Controls.AddRange(new Control[] { titleBar, mainPanel, settingsPanel });
        WireEvents();
        ConfigureToolTips();
        ApplyAppearance();

        trayIcon = new NotifyIcon
        {
            Icon = Icon,
            Text = "For Honor Quick Actions",
            Visible = true,
            ContextMenuStrip = CreateTrayMenu()
        };
        trayIcon.MouseClick += delegate(object sender, MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left) RestoreMainApplication();
        };
        Resize += delegate
        {
            if (WindowState == FormWindowState.Minimized && preferences.MinimizeToTray) Hide();
        };

        gameStateTimer = new Timer { Interval = 1500 };
        gameStateTimer.Tick += delegate { RefreshGameState(); };
        Shown += delegate
        {
            RefreshGameState();
            gameStateTimer.Start();
            if (startMinimized) BeginInvoke(new Action(delegate { WindowState = FormWindowState.Minimized; }));
        };
    }

    private AnimatedButton MakeButton(string text, Point location, Size size, Color color, float fontSize)
    {
        var button = new AnimatedButton
        {
            Text = text,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", fontSize),
            Location = location,
            Size = size,
            TabStop = false
        };
        button.FlatAppearance.BorderColor = Lighten(color, 45);
        button.SetColors(color, Lighten(color, 20));
        return button;
    }

    private AnimatedButton MakeChromeButton(string text, Point location, int width)
    {
        var button = MakeButton(text, location, new Size(width, 43), Color.FromArgb(14, 29, 43), 12F);
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static Label MakeLabel(string text, Point location, Size size, bool heading)
    {
        return new Label
        {
            Text = text,
            Location = location,
            Size = size,
            ForeColor = heading ? Color.FromArgb(185, 199, 216) : Color.FromArgb(145, 162, 181),
            Font = heading ? new Font("Segoe UI Semibold", 9F) : new Font("Segoe UI", 9.5F),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
    }

    private static RadioButton MakeRadio(string text, Point location)
    {
        return new RadioButton
        {
            Text = text, Location = location, Size = new Size(255, 30),
            ForeColor = Color.FromArgb(222, 230, 240), BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
    }

    private static CheckBox MakeCheckBox(string text, Point location)
    {
        return new CheckBox
        {
            Text = text, Location = location, Size = new Size(245, 31),
            ForeColor = Color.FromArgb(222, 230, 240), BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
    }

    private void BuildChrome()
    {
        titleBar = new Panel { BackColor = Color.FromArgb(24, 28, 36), Location = new Point(1, 1), Size = new Size(428, 44) };
        stateDot = new Label
        {
            Text = "●", ForeColor = Color.FromArgb(117, 128, 145), Font = new Font("Segoe UI", 12F),
            TextAlign = ContentAlignment.MiddleCenter, Location = new Point(8, 3), Size = new Size(25, 38)
        };
        stateLabel = new Label
        {
            Text = "Checking For Honor…", ForeColor = Color.FromArgb(218, 227, 239),
            Font = new Font("Segoe UI", 9.5F), TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(34, 3), Size = new Size(245, 38)
        };
        settingsButton = MakeChromeButton("⚙", new Point(358, 1), 36);
        settingsButton.Font = new Font("Segoe UI Symbol", 12F);
        overlayButton = MakeChromeButton("↗", new Point(322, 1), 36);
        closeAppButton = MakeChromeButton("×", new Point(394, 1), 34);
        closeAppButton.Font = new Font("Segoe UI", 16F);
        titleBar.Controls.AddRange(new Control[] { stateDot, stateLabel, settingsButton, overlayButton, closeAppButton });
        titleBar.MouseDown += BeginWindowDrag;
        stateLabel.MouseDown += BeginWindowDrag;
    }

    private void BuildMainView()
    {
        mainPanel = new Panel { Location = new Point(1, 45), Size = new Size(428, 299), BackColor = Color.Transparent };
        var logo = new PictureBox
        {
            Image = Icon == null ? null : Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(28, 10), Size = new Size(56, 56)
        };
        var title = new Label
        {
            Text = "QUICK ACTIONS", ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 20F),
            TextAlign = ContentAlignment.MiddleCenter, Location = new Point(88, 10), Size = new Size(314, 56)
        };
        closeButton = MakeButton("Close For Honor", new Point(28, 76), new Size(374, 43), Color.FromArgb(165, 58, 58), 11F);
        restartButton = MakeButton("Close and reopen For Honor", new Point(28, 129), new Size(374, 43), Color.FromArgb(50, 101, 161), 11F);
        ubisoftToggleButton = MakeButton("Start Ubisoft", new Point(28, 182), new Size(182, 43), Color.FromArgb(50, 101, 161), 9.5F);
        ubisoftRestartButton = MakeButton("Restart Ubisoft", new Point(220, 182), new Size(182, 43), Color.FromArgb(50, 101, 161), 9.5F);
        cleanRestartButton = MakeButton("Clean restart (game + Ubisoft)", new Point(28, 235), new Size(374, 43), Color.FromArgb(116, 82, 168), 11F);
        mainPanel.Controls.AddRange(new Control[]
        {
            logo, title, closeButton, restartButton, ubisoftToggleButton,
            ubisoftRestartButton, cleanRestartButton
        });
    }

    private void BuildSettingsView()
    {
        settingsPanel = new Panel
        {
            Location = new Point(1, 45), Size = new Size(428, 299),
            BackColor = Color.FromArgb(19, 22, 29), Visible = false
        };
    }

    private void EnsureSettingsControls()
    {
        if (settingsControlsCreated) return;
        settingsControlsCreated = true;
        var title = new Label
        {
            Text = "SETTINGS", ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 18F),
            Location = new Point(20, 5), Size = new Size(388, 38)
        };
        defaultLaunchTab = MakeButton("DEFAULT LAUNCH", new Point(20, 45), new Size(194, 36), preferences.AccentColor, 8.5F);
        appearanceTab = MakeButton("APPEARANCE", new Point(214, 45), new Size(194, 36), Color.FromArgb(31, 36, 47), 8.5F);
        settingsPanel.Controls.AddRange(new Control[] { title, defaultLaunchTab, appearanceTab });
        defaultLaunchTab.Click += delegate { ShowSettingsTab(false); };
        appearanceTab.Click += delegate { ShowSettingsTab(true); };
        defaultLaunchPanel = new Panel { Location = new Point(20, 87), Size = new Size(388, 205), BackColor = Color.Transparent };
        appearancePanel = new Panel
        {
            Location = new Point(20, 87), Size = new Size(388, 205),
            BackColor = Color.Transparent, Visible = false
        };
        BuildLaunchSettings();
        BuildAppearanceSettings();
        settingsPanel.Controls.AddRange(new Control[] { defaultLaunchPanel, appearancePanel });
        WireSettingsEvents();
        LoadSettingsControls();
        ConfigureSettingsToolTips();
        ApplyAppearance();
    }

    private void BuildLaunchSettings()
    {
        var help = MakeLabel("Choose what opens For Honor by default.", new Point(3, 0), new Size(380, 22), false);
        automaticRadio = MakeRadio("Automatic (first detected)", new Point(8, 24));
        steamRadio = MakeRadio("Steam", new Point(8, 53));
        steamRadio.Size = new Size(170, 28);
        ubisoftRadio = MakeRadio("Ubisoft Connect", new Point(194, 53));
        ubisoftRadio.Size = new Size(186, 28);
        executableRadio = MakeRadio("Saved forhonor.exe", new Point(8, 82));
        executablePathLabel = MakeLabel("No executable selected.", new Point(31, 109), new Size(349, 31), false);
        var browse = MakeButton("Browse for forhonor.exe", new Point(8, 151), new Size(180, 34), Color.FromArgb(31, 36, 47), 8.5F);
        browse.Click += delegate { ChooseGameLocation(true); };
        defaultLaunchPanel.Controls.AddRange(new Control[]
        {
            help, automaticRadio, steamRadio, ubisoftRadio, executableRadio, executablePathLabel, browse
        });
    }

    private void BuildAppearanceSettings()
    {
        var minimizeHeading = MakeLabel("WHEN MINIMIZED", new Point(3, 0), new Size(380, 22), true);
        trayRadio = MakeRadio("Minimize to the system tray (default)", new Point(8, 23));
        trayRadio.Size = new Size(370, 27);
        taskbarRadio = MakeRadio("Keep the app in the taskbar", new Point(8, 49));
        taskbarRadio.Size = new Size(370, 27);
        var opacityHeading = MakeLabel("WINDOW TRANSPARENCY", new Point(3, 76), new Size(260, 22), true);
        opacityValueLabel = MakeLabel("", new Point(318, 76), new Size(65, 22), false);
        opacityValueLabel.TextAlign = ContentAlignment.MiddleRight;
        opacitySlider = new DarkSlider
        {
            Location = new Point(0, 99), Size = new Size(388, 28),
            Minimum = 70, Maximum = 100, Value = preferences.WindowOpacityPercent
        };
        var accentHeading = MakeLabel("ACCENT COLOR", new Point(3, 122), new Size(160, 24), true);
        accentList = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(190, 121),
            Size = new Size(193, 28), BackColor = Color.FromArgb(31, 36, 47), ForeColor = Color.White
        };
        accentList.Items.AddRange(new object[] { "Gold", "Blue", "Purple", "Emerald" });
        toolTipsCheck = MakeCheckBox("Enable helpful tooltips", new Point(8, 150));
        toolTipsCheck.Size = new Size(185, 26);
        startupCheck = MakeCheckBox("Try Hard mode", new Point(8, 176));
        startupCheck.Size = new Size(125, 27);
        var startupExplanation = MakeLabel("Run automatically when you sign in.", new Point(132, 176), new Size(250, 27), false);
        startupExplanation.Font = new Font("Segoe UI", 8.5F);
        appearancePanel.Controls.AddRange(new Control[]
        {
            minimizeHeading, trayRadio, taskbarRadio, opacityHeading, opacityValueLabel,
            opacitySlider, accentHeading, accentList, toolTipsCheck,
            startupCheck, startupExplanation
        });
    }

    private void WireEvents()
    {
        closeButton.Click += async delegate { await RunActionAsync(gameIsRunning ? ActionKind.Close : ActionKind.Open); };
        restartButton.Click += async delegate { await RunActionAsync(ActionKind.Restart); };
        ubisoftToggleButton.Click += async delegate { await RunActionAsync(ActionKind.UbisoftToggle); };
        ubisoftRestartButton.Click += async delegate { await RunActionAsync(ActionKind.UbisoftRestart); };
        cleanRestartButton.Click += async delegate { await RunActionAsync(ActionKind.CleanRestart); };
        settingsButton.Click += delegate { ToggleSettings(); };
        closeAppButton.Click += delegate { MinimizeToTray(); };
        overlayButton.Click += delegate { ToggleGameOverlay(); };
    }

    private void WireSettingsEvents()
    {
        automaticRadio.CheckedChanged += delegate { SaveLaunchChoice(automaticRadio, LaunchPlatform.Automatic); };
        steamRadio.CheckedChanged += delegate { SaveLaunchChoice(steamRadio, LaunchPlatform.Steam); };
        ubisoftRadio.CheckedChanged += delegate { SaveLaunchChoice(ubisoftRadio, LaunchPlatform.UbisoftConnect); };
        executableRadio.CheckedChanged += delegate { SaveLaunchChoice(executableRadio, LaunchPlatform.DirectExecutable); };
        trayRadio.CheckedChanged += delegate { if (!loadingSettings && trayRadio.Checked) { preferences.MinimizeToTray = true; SavePreferences(); } };
        taskbarRadio.CheckedChanged += delegate { if (!loadingSettings && taskbarRadio.Checked) { preferences.MinimizeToTray = false; SavePreferences(); } };
        opacitySlider.ValueChanged += delegate
        {
            opacityValueLabel.Text = opacitySlider.Value + "%";
            if (!loadingSettings)
            {
                preferences.WindowOpacityPercent = opacitySlider.Value;
                Opacity = opacitySlider.Value / 100.0;
            }
        };
        opacitySlider.MouseUp += delegate { if (!loadingSettings) SavePreferences(); };
        accentList.SelectedIndexChanged += delegate
        {
            if (loadingSettings || accentList.SelectedIndex < 0) return;
            preferences.Accent = (AccentTheme)accentList.SelectedIndex;
            ApplyAppearance();
            SavePreferences();
        };
        toolTipsCheck.CheckedChanged += delegate
        {
            if (loadingSettings) return;
            preferences.ToolTipsEnabled = toolTipsCheck.Checked;
            toolTips.Active = toolTipsCheck.Checked;
            SavePreferences();
        };
        startupCheck.CheckedChanged += delegate
        {
            if (loadingSettings) return;
            try
            {
                StartupRegistration.SetEnabled(startupCheck.Checked);
                preferences.RunOnStartup = startupCheck.Checked;
                SavePreferences();
            }
            catch (Exception ex)
            {
                loadingSettings = true;
                startupCheck.Checked = !startupCheck.Checked;
                loadingSettings = false;
                MessageBox.Show(this, ex.Message, "Windows startup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
    }

    private async Task RunActionAsync(ActionKind action)
    {
        if (isRunning) return;
        IncidentReporting.Record("action", action.ToString());
        if ((action == ActionKind.Open || action == ActionKind.Restart || action == ActionKind.CleanRestart || action == ActionKind.Close) && HasUnresolvedGame())
        { ShowRecovery(action); return; }
        isRunning = true;
        SetButtonsEnabled(false);
        try
        {
            switch (action)
            {
                case ActionKind.Close:
                    SetStatus("Closing For Honor…");
                    if (!await StopProcessesAsync(ProcessActions.GameProcesses, action)) return;
                    SetStatus("For Honor closed.");
                    FinishAction();
                    break;
                case ActionKind.Open:
                    waitingForGameStart = false;
                    await LaunchGameAsync();
                    break;
                case ActionKind.Restart:
                    SetStatus("Closing For Honor…");
                    if (!await StopProcessesAsync(ProcessActions.GameProcesses, action)) return;
                    await LaunchGameAsync();
                    break;
                case ActionKind.UbisoftToggle:
                    var ubisoftIsRunning = await Task.Run(delegate { return ProcessActions.IsUbisoftRunning(); });
                    if (ubisoftIsRunning)
                    {
                        SetStatus("Stopping Ubisoft Connect…");
                        if (!await StopProcessesAsync(ProcessActions.UbisoftProcesses, action)) return;
                        SetStatus("Ubisoft Connect stopped.");
                        FinishAction();
                    }
                    else await LaunchUbisoftAsync();
                    break;
                case ActionKind.UbisoftRestart:
                    SetStatus("Restarting Ubisoft Connect…");
                    if (!await StopProcessesAsync(ProcessActions.UbisoftProcesses, action)) return;
                    await LaunchUbisoftAsync();
                    break;
                case ActionKind.CleanRestart:
                    SetStatus("Closing game, anti-cheat, and Ubisoft…");
                    if (!await StopProcessesAsync(ProcessActions.CleanRestartProcesses, action)) return;
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

    private async Task<bool> StopProcessesAsync(string[] processNames, ActionKind action)
    {
        var result = await Task.Run(delegate { return ProcessActions.Stop(processNames); });
        foreach (var target in result.Targets) IncidentReporting.Record("normal-termination", target.IsAlive() ? "survived" : "exited",
            target.Pid, target.Error, result.ElapsedMilliseconds, target.Created);
        foreach (var error in result.EnumerationErrors) IncidentReporting.Record("enumeration", "unavailable", 0, error);
        if (result.Succeeded) { result.Dispose(); ClearUnresolved(); return true; }
        if (result.Targets.Any(t => t.Name.Equals("forhonor", StringComparison.OrdinalIgnoreCase) && t.IsAlive()))
        {
            ClearUnresolved(); unresolved = result; incident = null; issueUrl = null;
            ShowRecovery(action); return false;
        }
        result.Dispose();
            throw new InvalidOperationException("One or more requested processes could not be closed. Close them with Task Manager or run this app as administrator, then try again.");
    }

    private async Task LaunchGameAsync()
    {
        if (ProcessActions.IsForHonorRunning())
        { SetStatus("For Honor is already running."); FinishAction(); return; }
        SetStatus("Finding and starting For Honor…");
        var launchTarget = await Task.Factory.StartNew(delegate
        {
            return GameLocator.FindLaunchTarget(preferences.LaunchPlatform);
        });
        if (launchTarget == null)
        {
            SetStatus("The selected launcher is unavailable.");
            MessageBox.Show(this,
                "The selected launcher or For Honor installation could not be found. Open Settings and choose a detected launcher, or browse to forhonor.exe.",
                "Launcher unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetButtonsEnabled(true);
            isRunning = false;
            return;
        }
        launchTarget.Start();
        IncidentReporting.Record("launch", preferences.LaunchPlatform.ToString());
        waitingForGameStart = true;
        SetStatus("Launch sent through " + launchTarget.DisplayName + ".");
        isRunning = false;
        SetButtonsEnabled(true);
        RefreshGameState();
    }

    private async Task LaunchUbisoftAsync()
    {
        SetStatus("Finding and starting Ubisoft Connect…");
        var launcherPath = await Task.Factory.StartNew(delegate { return GameLocator.FindUbisoftConnect(); });
        if (launcherPath == null)
        {
            SetStatus("Ubisoft Connect could not be found.");
            MessageBox.Show(this, "Ubisoft Connect could not be found automatically.",
                "Ubisoft Connect not found", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetButtonsEnabled(true);
            isRunning = false;
            return;
        }
        Process.Start(new ProcessStartInfo(launcherPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(launcherPath)
        });
        SetStatus("Ubisoft Connect is starting.");
        FinishAction();
    }

    private void FinishAction()
    {
        isRunning = false;
        SetButtonsEnabled(true);
        RefreshGameState();
    }

    private void ToggleSettings()
    {
        if (recoveryVisible) return;
        showingSettings = !showingSettings;
        if (showingSettings) EnsureSettingsControls();
        mainPanel.Visible = !showingSettings;
        settingsPanel.Visible = showingSettings;
        settingsButton.Text = showingSettings ? "‹" : "⚙";
        if (showingSettings)
        {
            stateLabel.Text = "Settings";
            stateDot.ForeColor = preferences.AccentColor;
            UpdateLaunchAvailability();
        }
        else RefreshGameState();
    }

    private void ShowSettingsTab(bool appearance)
    {
        if (!settingsControlsCreated) return;
        defaultLaunchPanel.Visible = !appearance;
        appearancePanel.Visible = appearance;
        var dark = Color.FromArgb(29, 45, 61);
        defaultLaunchTab.SetColors(appearance ? dark : preferences.AccentColor,
            appearance ? Lighten(dark, 20) : Lighten(preferences.AccentColor, 20));
        appearanceTab.SetColors(appearance ? preferences.AccentColor : dark,
            appearance ? Lighten(preferences.AccentColor, 20) : Lighten(dark, 20));
    }

    private async void UpdateLaunchAvailability()
    {
        var available = await Task.Run(delegate { return GameLocator.DetectLaunchPlatforms(); });
        if (IsDisposed) return;
        steamRadio.Enabled = available.SteamAvailable;
        steamRadio.Text = available.SteamAvailable ? "Steam (detected)" : "Steam (not detected)";
        ubisoftRadio.Enabled = available.UbisoftAvailable;
        ubisoftRadio.Text = available.UbisoftAvailable ? "Ubisoft Connect (detected)" : "Ubisoft Connect (not detected)";
        executableRadio.Enabled = !string.IsNullOrWhiteSpace(available.DirectGamePath);
        executablePathLabel.Text = string.IsNullOrWhiteSpace(available.DirectGamePath)
            ? "No executable selected. Browse below to choose forhonor.exe."
            : available.DirectGamePath;
    }

    private void LoadSettingsControls()
    {
        loadingSettings = true;
        automaticRadio.Checked = preferences.LaunchPlatform == LaunchPlatform.Automatic;
        steamRadio.Checked = preferences.LaunchPlatform == LaunchPlatform.Steam;
        ubisoftRadio.Checked = preferences.LaunchPlatform == LaunchPlatform.UbisoftConnect;
        executableRadio.Checked = preferences.LaunchPlatform == LaunchPlatform.DirectExecutable;
        trayRadio.Checked = preferences.MinimizeToTray;
        taskbarRadio.Checked = !preferences.MinimizeToTray;
        opacitySlider.Value = preferences.WindowOpacityPercent;
        opacityValueLabel.Text = preferences.WindowOpacityPercent + "%";
        accentList.SelectedIndex = (int)preferences.Accent;
        toolTipsCheck.Checked = preferences.ToolTipsEnabled;
        preferences.RunOnStartup = StartupRegistration.IsEnabled();
        startupCheck.Checked = preferences.RunOnStartup;
        loadingSettings = false;
    }

    private void SaveLaunchChoice(RadioButton source, LaunchPlatform platform)
    {
        if (loadingSettings || !source.Checked) return;
        preferences.LaunchPlatform = platform;
        SavePreferences();
        SetStatus("Default launcher: " + PlatformDisplayName(platform) + ".");
    }

    private static string PlatformDisplayName(LaunchPlatform platform)
    {
        switch (platform)
        {
            case LaunchPlatform.Steam: return "Steam";
            case LaunchPlatform.UbisoftConnect: return "Ubisoft Connect";
            case LaunchPlatform.DirectExecutable: return "saved executable";
            default: return "Automatic";
        }
    }

    private void ChooseGameLocation(bool selectAsDefault)
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
            executablePathLabel.Text = dialog.FileName;
            executableRadio.Enabled = true;
            if (selectAsDefault)
            {
                executableRadio.Checked = true;
                preferences.LaunchPlatform = LaunchPlatform.DirectExecutable;
                SavePreferences();
            }
            SetStatus("Game location saved.");
        }
    }

    private void ApplyAppearance()
    {
        var accent = preferences.AccentColor;
        if (opacitySlider != null) opacitySlider.AccentColor = accent;
        if (showingSettings) stateDot.ForeColor = accent;
        if (appearancePanel != null) ShowSettingsTab(appearancePanel.Visible);
        Invalidate();
    }

    private void ConfigureToolTips()
    {
        toolTips.AutomaticDelay = 350;
        toolTips.AutoPopDelay = 6000;
        toolTips.ShowAlways = true;
        toolTips.Active = preferences.ToolTipsEnabled;
        toolTips.SetToolTip(settingsButton, "Open appearance and default-launch settings.");
        toolTips.SetToolTip(overlayButton, "Move controls onto the screen where For Honor is running.");
        toolTips.SetToolTip(closeAppButton, "Minimize to the system tray.");
        toolTips.SetToolTip(closeButton, "Start or stop For Honor.");
        toolTips.SetToolTip(restartButton, "Close For Honor and launch it again.");
        toolTips.SetToolTip(ubisoftToggleButton, "Start Ubisoft Connect, or stop it when it is running.");
        toolTips.SetToolTip(ubisoftRestartButton, "Stop and reopen Ubisoft Connect.");
        toolTips.SetToolTip(cleanRestartButton, "Stop the game, anti-cheat, and Ubisoft, then launch the game.");
    }

    private void ConfigureSettingsToolTips()
    {
        toolTips.SetToolTip(opacitySlider, "Adjust the transparency of the app window.");
        toolTips.SetToolTip(startupCheck, "Try Hard mode launches Quick Actions automatically when you sign in to Windows.");
    }

    private void SavePreferences()
    {
        try { preferences.Save(); }
        catch (IOException) { SetStatus("Settings could not be saved."); }
        catch (UnauthorizedAccessException) { SetStatus("Settings could not be saved."); }
    }

    private ContextMenuStrip CreateTrayMenu()
    {
        var menu = new ContextMenuStrip();
        trayGameToggleItem = new ToolStripMenuItem("Open For Honor");
        trayGameRestartItem = new ToolStripMenuItem("Restart For Honor");
        trayUbisoftToggleItem = new ToolStripMenuItem("Start Ubisoft");
        trayUbisoftRestartItem = new ToolStripMenuItem("Restart Ubisoft");

        trayGameToggleItem.Click += async delegate
        {
            await RunActionAsync(gameIsRunning ? ActionKind.Close : ActionKind.Open);
        };
        trayGameRestartItem.Click += async delegate { await RunActionAsync(ActionKind.Restart); };
        trayUbisoftToggleItem.Click += async delegate { await RunActionAsync(ActionKind.UbisoftToggle); };
        trayUbisoftRestartItem.Click += async delegate { await RunActionAsync(ActionKind.UbisoftRestart); };

        menu.Items.Add(trayGameToggleItem);
        menu.Items.Add(trayGameRestartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(trayUbisoftToggleItem);
        menu.Items.Add(trayUbisoftRestartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Quick Actions", null, delegate { RestoreMainApplication(); });
        menu.Items.Add("Settings", null, delegate
        {
            RestoreMainApplication();
            if (!showingSettings) ToggleSettings();
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, delegate { BeginFadeOut(); });
        menu.Opening += delegate { UpdateTrayMenu(); };
        return menu;
    }

    private void UpdateTrayMenu()
    {
        if (trayGameToggleItem == null) return;
        trayGameToggleItem.Text = gameIsRunning ? "Close For Honor" : "Open For Honor";
        trayGameRestartItem.Enabled = gameIsRunning && !isRunning;
        trayUbisoftToggleItem.Text = displayedUbisoftRunning == true ? "Stop Ubisoft" : "Start Ubisoft";
        trayGameToggleItem.Enabled = !isRunning;
        trayUbisoftToggleItem.Enabled = !isRunning;
        trayUbisoftRestartItem.Enabled = !isRunning;
        if (trayIcon != null)
            trayIcon.Text = gameIsRunning ? "For Honor is running" : "For Honor Quick Actions";
    }

    private void SetStatus(string text)
    {
        if (!showingSettings) stateLabel.Text = text;
    }

    private void SetButtonsEnabled(bool enabled)
    {
        closeButton.Enabled = enabled;
        restartButton.Enabled = enabled && gameIsRunning;
        ubisoftToggleButton.Enabled = enabled;
        ubisoftRestartButton.Enabled = enabled;
        cleanRestartButton.Enabled = enabled;
        settingsButton.Enabled = enabled;
        overlayButton.Enabled = enabled && gameIsRunning && forHonorScreen != null;
    }

    private async void RefreshGameState()
    {
        if (isRunning || recoveryVisible || stateRefreshPending) return;
        stateRefreshPending = true;
        GameStateSnapshot snapshot;
        try
        {
            snapshot = await Task.Run(delegate
            {
                var running = ProcessActions.IsForHonorRunning();
                return new GameStateSnapshot
                {
                    GameRunning = running,
                    UbisoftRunning = ProcessActions.IsUbisoftRunning(),
                    GameScreen = running ? ProcessActions.FindForHonorScreen() : null
                };
            });
        }
        finally { stateRefreshPending = false; }
        if (IsDisposed) return;
        if (isRunning || recoveryVisible) return;
        if (displayedGameRunning != snapshot.GameRunning || (forHonorScreen == null) != (snapshot.GameScreen == null))
            IncidentReporting.Record("observation", snapshot.GameScreen != null ? "window-present" : snapshot.GameRunning ? "process-present" : "absent");

        gameIsRunning = snapshot.GameRunning;
        forHonorScreen = snapshot.GameScreen;
        if (displayedUbisoftRunning != snapshot.UbisoftRunning)
        {
            ubisoftToggleButton.Text = snapshot.UbisoftRunning ? "Stop Ubisoft" : "Start Ubisoft";
            displayedUbisoftRunning = snapshot.UbisoftRunning;
        }
        UpdateTrayMenu();

        if (gameIsRunning)
        {
            if (displayedGameRunning != true)
            {
                closeButton.Text = "Close For Honor";
                closeButton.SetColors(Color.FromArgb(165, 58, 58), Color.FromArgb(185, 78, 78));
                displayedGameRunning = true;
                if (waitingForGameStart) WindowState = FormWindowState.Minimized;
            }
            restartButton.Enabled = true;
            cleanRestartButton.Text = "Clean restart (game + Ubisoft)";
            var gameWindowReady = forHonorScreen != null;
            if (gameWindowReady) waitingForGameStart = false;
            if (!showingSettings)
            {
                stateLabel.Text = gameWindowReady ? "For Honor is running" : "For Honor is starting";
                stateDot.ForeColor = gameWindowReady ? Color.FromArgb(59, 223, 105) : Color.FromArgb(236, 183, 59);
            }
            overlayButton.Visible = true;
            overlayButton.Enabled = gameWindowReady;
            if (gameOverlay != null && gameOverlay.Visible)
            {
                if (forHonorScreen == null) gameOverlay.Hide();
                else gameOverlay.ShowOnScreen(forHonorScreen);
            }
            else if (gameWindowReady && !overlayShownForCurrentGame) ShowGameOverlay();
            else if (gameWindowReady && overlayShownForCurrentGame && WindowState == FormWindowState.Minimized) ShowGameOverlay();
        }
        else
        {
            forHonorScreen = null;
            overlayShownForCurrentGame = false;
            if (displayedGameRunning != false)
            {
                closeButton.Text = "Open For Honor";
                closeButton.SetColors(Color.FromArgb(51, 125, 86), Color.FromArgb(71, 145, 106));
                displayedGameRunning = false;
            }
            restartButton.Enabled = false;
            cleanRestartButton.Text = "Clean restart (Ubisoft + open game)";
            if (!showingSettings)
            {
                stateLabel.Text = waitingForGameStart ? "For Honor is starting" : "For Honor is not running";
                stateDot.ForeColor = waitingForGameStart
                    ? Color.FromArgb(236, 183, 59)
                    : Color.FromArgb(117, 128, 145);
            }
            overlayButton.Visible = false;
            if (gameOverlay != null && gameOverlay.Visible) gameOverlay.Hide();
            if (startupBackgroundMode) WindowState = FormWindowState.Minimized;
        }
    }

    private sealed class GameStateSnapshot
    {
        public bool GameRunning;
        public bool UbisoftRunning;
        public Screen GameScreen;
    }

    private void ToggleGameOverlay()
    {
        if (forHonorScreen == null) { SetStatus("Waiting for For Honor's game window…"); return; }
        if (gameOverlay != null && gameOverlay.Visible) { RestoreMainApplication(); return; }
        ShowGameOverlay();
    }

    private void ShowGameOverlay()
    {
        if (forHonorScreen == null) return;
        if (gameOverlay == null) gameOverlay = new GameOverlay(RunOverlayAction, RestoreMainApplication);
        gameOverlay.ShowOnScreen(forHonorScreen, startCollapsed: true);
        overlayShownForCurrentGame = true;
        WindowState = FormWindowState.Minimized;
    }

    private async void RunOverlayAction(ActionKind action)
    {
        if (!gameIsRunning || isRunning) return;
        RestoreMainApplication();
        await RunActionAsync(action);
    }

    private void RestoreMainApplication()
    {
        startupBackgroundMode = false;
        if (gameOverlay != null) gameOverlay.Hide();
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void MinimizeToTray()
    {
        startupBackgroundMode = false;
        Hide();
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
        Show();
        WindowState = FormWindowState.Normal;
        closeAppButton.Enabled = false;
        gameStateTimer.Stop();
        if (gameOverlay != null) gameOverlay.Hide();
        for (var opacity = Opacity; opacity > 0.05; opacity -= 0.10)
        {
            Opacity = opacity;
            await Task.Delay(16);
        }
        Opacity = 0;
        trayIcon.Visible = false;
        Close();
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        using (var pen = new Pen(Color.FromArgb(100, preferences.AccentColor), 1F))
            args.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        if (!isFadingOut && args.CloseReason == CloseReason.UserClosing)
        {
            args.Cancel = true;
            MinimizeToTray();
            return;
        }
        trayIcon.Visible = false;
        trayIcon.Dispose();
        ClearUnresolved();
        if (gameOverlay != null) gameOverlay.Dispose();
        base.OnFormClosing(args);
    }


    private static Color Lighten(Color color, int amount)
    {
        return Color.FromArgb(Math.Min(255, color.R + amount), Math.Min(255, color.G + amount), Math.Min(255, color.B + amount));
    }

    internal enum ActionKind { Close, Open, Restart, UbisoftToggle, UbisoftRestart, CleanRestart }
}
}
