using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ForHonorQuickActions
{

internal enum LaunchPlatform
{
    Automatic,
    Steam,
    UbisoftConnect,
    DirectExecutable
}

internal static class LaunchPreferences
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ForHonorQuickActions", "launch-platform.txt");

    public static LaunchPlatform Load()
    {
        try
        {
            LaunchPlatform platform;
            if (File.Exists(SettingsPath) && Enum.TryParse(File.ReadAllText(SettingsPath).Trim(), true, out platform))
                return platform;
        }
        catch (IOException) { }

        return LaunchPlatform.Automatic;
    }

    public static void Save(LaunchPlatform platform)
    {
        var folder = Path.GetDirectoryName(SettingsPath);
        Directory.CreateDirectory(folder);
        File.WriteAllText(SettingsPath, platform.ToString());
    }
}

internal sealed class LaunchSettingsForm : Form
{
    private readonly ComboBox platformList;
    private readonly Label detailsLabel;
    private readonly List<PlatformChoice> choices = new List<PlatformChoice>();

    public LaunchPlatform SelectedPlatform { get; private set; }

    public LaunchSettingsForm(LaunchPlatform current, LaunchPlatformAvailability available)
    {
        Text = "Launch settings";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 245);
        BackColor = Color.FromArgb(19, 22, 29);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);

        var title = new Label
        {
            Text = "DEFAULT GAME LAUNCHER",
            Font = new Font("Segoe UI Semibold", 15F),
            ForeColor = Color.White,
            Location = new Point(24, 20),
            Size = new Size(472, 32)
        };
        var explanation = new Label
        {
            Text = "Choose the platform that owns your current For Honor installation.",
            ForeColor = Color.FromArgb(165, 174, 188),
            Location = new Point(26, 55),
            Size = new Size(468, 24)
        };

        platformList = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(27, 88),
            Size = new Size(466, 30)
        };
        AddChoice(LaunchPlatform.Automatic, "Automatic", "Uses a detected platform first, then the saved game executable.");
        if (available.SteamAvailable)
            AddChoice(LaunchPlatform.Steam, "Steam (detected)", available.SteamGamePath);
        if (available.UbisoftAvailable)
            AddChoice(LaunchPlatform.UbisoftConnect, "Ubisoft Connect (detected)", available.UbisoftGamePath);
        if (!string.IsNullOrWhiteSpace(available.DirectGamePath))
            AddChoice(LaunchPlatform.DirectExecutable, "Direct executable", available.DirectGamePath);

        detailsLabel = new Label
        {
            ForeColor = Color.FromArgb(165, 174, 188),
            Location = new Point(28, 127),
            Size = new Size(464, 44),
            AutoEllipsis = true
        };

        var saveButton = new Button
        {
            Text = "Save default",
            DialogResult = DialogResult.OK,
            BackColor = Color.FromArgb(50, 101, 161),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(280, 190),
            Size = new Size(104, 34)
        };
        saveButton.FlatAppearance.BorderSize = 0;
        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            BackColor = Color.FromArgb(52, 58, 70),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(390, 190),
            Size = new Size(104, 34)
        };
        cancelButton.FlatAppearance.BorderSize = 0;

        platformList.SelectedIndexChanged += (sender, args) => UpdateSelection();
        Controls.AddRange(new Control[] { title, explanation, platformList, detailsLabel, saveButton, cancelButton });
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        var selectedIndex = choices.FindIndex(choice => choice.Platform == current);
        platformList.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
    }

    private void AddChoice(LaunchPlatform platform, string name, string details)
    {
        var choice = new PlatformChoice(platform, name, details);
        choices.Add(choice);
        platformList.Items.Add(choice);
    }

    private void UpdateSelection()
    {
        var choice = platformList.SelectedItem as PlatformChoice;
        if (choice == null) return;
        SelectedPlatform = choice.Platform;
        detailsLabel.Text = choice.Details;
    }

    private sealed class PlatformChoice
    {
        public readonly LaunchPlatform Platform;
        public readonly string Details;
        private readonly string name;

        public PlatformChoice(LaunchPlatform platform, string name, string details)
        {
            Platform = platform;
            this.name = name;
            Details = details;
        }

        public override string ToString() { return name; }
    }
}

}
