using System;
using System.Drawing;
using System.IO;
using Microsoft.Win32;

namespace ForHonorQuickActions
{

internal enum LaunchPlatform
{
    Automatic,
    Steam,
    UbisoftConnect,
    DirectExecutable
}

internal enum AccentTheme
{
    Gold,
    Blue,
    Purple,
    Emerald
}

internal sealed class AppPreferences
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ForHonorQuickActions");
    private static readonly string SettingsPath = Path.Combine(SettingsFolder, "preferences.ini");
    private static readonly string LegacyLaunchPath = Path.Combine(SettingsFolder, "launch-platform.txt");

    public LaunchPlatform LaunchPlatform = LaunchPlatform.Automatic;
    public bool MinimizeToTray = true;
    public int WindowOpacityPercent = 96;
    public AccentTheme Accent = AccentTheme.Gold;
    public bool ToolTipsEnabled = true;
    public bool RunOnStartup = false;

    public Color AccentColor
    {
        get
        {
            switch (Accent)
            {
                case AccentTheme.Blue: return Color.FromArgb(45, 137, 235);
                case AccentTheme.Purple: return Color.FromArgb(137, 77, 232);
                case AccentTheme.Emerald: return Color.FromArgb(34, 170, 112);
                default: return Color.FromArgb(232, 181, 54);
            }
        }
    }

    public static AppPreferences Load()
    {
        var preferences = new AppPreferences();
        try
        {
            if (File.Exists(SettingsPath))
            {
                foreach (var rawLine in File.ReadAllLines(SettingsPath))
                {
                    var split = rawLine.IndexOf('=');
                    if (split <= 0) continue;
                    var key = rawLine.Substring(0, split).Trim();
                    var value = rawLine.Substring(split + 1).Trim();
                    LaunchPlatform platform;
                    AccentTheme accent;
                    bool flag;
                    int number;
                    if (key == "LaunchPlatform" && Enum.TryParse(value, true, out platform)) preferences.LaunchPlatform = platform;
                    else if (key == "MinimizeToTray" && bool.TryParse(value, out flag)) preferences.MinimizeToTray = flag;
                    else if (key == "WindowOpacityPercent" && int.TryParse(value, out number)) preferences.WindowOpacityPercent = Math.Max(70, Math.Min(100, number));
                    else if (key == "Accent" && Enum.TryParse(value, true, out accent)) preferences.Accent = accent;
                    else if (key == "ToolTipsEnabled" && bool.TryParse(value, out flag)) preferences.ToolTipsEnabled = flag;
                    else if (key == "RunOnStartup" && bool.TryParse(value, out flag)) preferences.RunOnStartup = flag;
                }
                return preferences;
            }

            LaunchPlatform legacyPlatform;
            if (File.Exists(LegacyLaunchPath) && Enum.TryParse(File.ReadAllText(LegacyLaunchPath).Trim(), true, out legacyPlatform))
                preferences.LaunchPlatform = legacyPlatform;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return preferences;
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsFolder);
        File.WriteAllLines(SettingsPath, new[]
        {
            "LaunchPlatform=" + LaunchPlatform,
            "MinimizeToTray=" + MinimizeToTray,
            "WindowOpacityPercent=" + WindowOpacityPercent,
            "Accent=" + Accent,
            "ToolTipsEnabled=" + ToolTipsEnabled,
            "RunOnStartup=" + RunOnStartup
        });
    }
}

internal static class StartupRegistration
{
    private const string RunKeyPath = @"SoftwareMicrosoftWindowsCurrentVersionRun";
    private const string ValueName = "ForHonorQuickActions";

    public static bool IsEnabled()
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
            {
                var value = key == null ? null : key.GetValue(ValueName) as string;
                return string.Equals(value, StartupCommand(), StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (System.Security.SecurityException) { return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
        {
            if (key == null) throw new InvalidOperationException("Windows startup settings could not be opened.");
            if (enabled) key.SetValue(ValueName, StartupCommand(), RegistryValueKind.String);
            else key.DeleteValue(ValueName, false);
        }
    }

    private static string StartupCommand()
    {
        return ((char)34) + System.Windows.Forms.Application.ExecutablePath + ((char)34) + " --startup";
    }
}

internal static class LaunchPreferences
{
    public static LaunchPlatform Load() { return AppPreferences.Load().LaunchPlatform; }

    public static void Save(LaunchPlatform platform)
    {
        var preferences = AppPreferences.Load();
        preferences.LaunchPlatform = platform;
        preferences.Save();
    }
}

internal sealed class DarkSlider : System.Windows.Forms.Control
{
    private int sliderValue = 100;
    private Color accentColor = Color.FromArgb(232, 181, 54);

    public event EventHandler ValueChanged;
    public int Minimum { get; set; }
    public int Maximum { get; set; }

    public int Value
    {
        get { return sliderValue; }
        set
        {
            var next = Math.Max(Minimum, Math.Min(Maximum, value));
            if (sliderValue == next) return;
            sliderValue = next;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }

    public Color AccentColor
    {
        get { return accentColor; }
        set { accentColor = value; Invalidate(); }
    }

    public DarkSlider()
    {
        Minimum = 70;
        Maximum = 100;
        Height = 28;
        BackColor = Color.FromArgb(18, 30, 43);
        SetStyle(System.Windows.Forms.ControlStyles.AllPaintingInWmPaint |
            System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer |
            System.Windows.Forms.ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(System.Windows.Forms.PaintEventArgs args)
    {
        base.OnPaint(args);
        var middle = Height / 2;
        var left = 8;
        var right = Width - 8;
        var ratio = Maximum == Minimum ? 0F : (sliderValue - Minimum) / (float)(Maximum - Minimum);
        var thumbX = left + (int)((right - left) * ratio);
        using (var track = new SolidBrush(Color.FromArgb(58, 73, 89)))
            args.Graphics.FillRectangle(track, left, middle - 2, right - left, 4);
        using (var fill = new SolidBrush(accentColor))
            args.Graphics.FillRectangle(fill, left, middle - 2, Math.Max(0, thumbX - left), 4);
        using (var thumb = new SolidBrush(Lighten(accentColor, 35)))
            args.Graphics.FillEllipse(thumb, thumbX - 7, middle - 7, 14, 14);
    }

    protected override void OnMouseDown(System.Windows.Forms.MouseEventArgs args)
    {
        base.OnMouseDown(args);
        SetFromMouse(args.X);
    }

    protected override void OnMouseMove(System.Windows.Forms.MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if ((args.Button & System.Windows.Forms.MouseButtons.Left) != 0) SetFromMouse(args.X);
    }

    private void SetFromMouse(int x)
    {
        var width = Math.Max(1, Width - 16);
        var ratio = Math.Max(0F, Math.Min(1F, (x - 8) / (float)width));
        Value = Minimum + (int)Math.Round((Maximum - Minimum) * ratio);
    }

    private static Color Lighten(Color color, int amount)
    {
        return Color.FromArgb(Math.Min(255, color.R + amount), Math.Min(255, color.G + amount), Math.Min(255, color.B + amount));
    }
}

internal sealed class AnimatedButton : System.Windows.Forms.Button
{
    public AnimatedButton()
    {
        FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        FlatAppearance.BorderSize = 1;
        Cursor = System.Windows.Forms.Cursors.Hand;
    }

    public void SetColors(Color normal, Color hover)
    {
        BackColor = normal;
        FlatAppearance.MouseOverBackColor = hover;
        FlatAppearance.MouseDownBackColor = Blend(normal, Color.Black, 0.18f);
    }

    private static Color Blend(Color from, Color to, float amount)
    {
        return Color.FromArgb(
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));
    }

}

}
