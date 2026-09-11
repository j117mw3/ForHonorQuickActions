using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ForHonorQuickActions
{

internal sealed class LaunchPlatformAvailability
{
    public string SteamClientPath;
    public string SteamGamePath;
    public string UbisoftClientPath;
    public string UbisoftGamePath;
    public string UbisoftProductId;
    public string DirectGamePath;

    public bool SteamAvailable { get { return SteamClientPath != null && SteamGamePath != null; } }
    public bool UbisoftAvailable
    {
        get { return UbisoftClientPath != null && UbisoftGamePath != null && UbisoftProductId != null; }
    }
}

internal sealed class GameLaunchTarget
{
    private readonly string fileName;
    private readonly string arguments;
    private readonly string workingDirectory;

    public readonly LaunchPlatform Platform;
    public readonly string DisplayName;

    public GameLaunchTarget(LaunchPlatform platform, string displayName, string fileName, string arguments, string workingDirectory)
    {
        Platform = platform;
        DisplayName = displayName;
        this.fileName = fileName;
        this.arguments = arguments;
        this.workingDirectory = workingDirectory;
    }

    public void Start()
    {
        var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = true };
        if (!string.IsNullOrWhiteSpace(arguments)) startInfo.Arguments = arguments;
        if (!string.IsNullOrWhiteSpace(workingDirectory)) startInfo.WorkingDirectory = workingDirectory;
        Process.Start(startInfo);
    }
}

internal sealed class UbisoftGameRegistration
{
    public readonly string ProductId;
    public readonly string GamePath;

    public UbisoftGameRegistration(string productId, string gamePath)
    {
        ProductId = productId;
        GamePath = gamePath;
    }
}

internal static class GameLocator
{
    private const string GameExe = "forhonor.exe";
    private const string SteamAppId = "304390";
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ForHonorQuickActions", "settings.json");

    public static string Find()
    {
        var saved = LoadSavedPath();
        if (IsGameExecutable(saved)) return saved;

        foreach (var steamRoot in SteamRoots())
        {
            var byManifest = FindInSteamLibrary(steamRoot);
            if (byManifest != null) return byManifest;
        }

        foreach (var candidate in UbisoftCandidates())
            if (IsGameExecutable(candidate)) return candidate;

        foreach (var candidate in FixedDriveCandidates())
            if (IsGameExecutable(candidate)) return candidate;

        return null;
    }

    public static LaunchPlatformAvailability DetectLaunchPlatforms()
    {
        var steamClient = FindSteamClient();
        var steamGame = FindSteamGame();
        var ubisoftClient = FindUbisoftConnect();
        var ubisoftGame = FindUbisoftGameRegistration();
        var directGame = Find();

        return new LaunchPlatformAvailability
        {
            SteamClientPath = steamClient,
            SteamGamePath = steamGame,
            UbisoftClientPath = ubisoftClient,
            UbisoftGamePath = ubisoftGame == null ? null : ubisoftGame.GamePath,
            UbisoftProductId = ubisoftGame == null ? null : ubisoftGame.ProductId,
            DirectGamePath = directGame
        };
    }

    public static GameLaunchTarget FindLaunchTarget(LaunchPlatform platform)
    {
        var available = DetectLaunchPlatforms();
        if (platform == LaunchPlatform.Automatic)
        {
            if (available.SteamAvailable) platform = LaunchPlatform.Steam;
            else if (available.UbisoftAvailable) platform = LaunchPlatform.UbisoftConnect;
            else platform = LaunchPlatform.DirectExecutable;
        }

        if (platform == LaunchPlatform.Steam && available.SteamAvailable)
            return new GameLaunchTarget(LaunchPlatform.Steam, "Steam", available.SteamClientPath,
                "-applaunch " + SteamAppId, Path.GetDirectoryName(available.SteamClientPath));

        if (platform == LaunchPlatform.UbisoftConnect && available.UbisoftAvailable)
            return new GameLaunchTarget(LaunchPlatform.UbisoftConnect, "Ubisoft Connect",
                "uplay://launch/" + available.UbisoftProductId + "/0", null, null);

        if (platform == LaunchPlatform.DirectExecutable && IsGameExecutable(available.DirectGamePath))
            return new GameLaunchTarget(LaunchPlatform.DirectExecutable, "the saved executable",
                available.DirectGamePath, null, Path.GetDirectoryName(available.DirectGamePath));

        return null;
    }

    public static string FindUbisoftConnect()
    {
        foreach (var candidate in UbisoftLauncherCandidates())
            if (File.Exists(candidate)) return candidate;

        return null;
    }

    public static void Save(string gamePath)
    {
        var folder = Path.GetDirectoryName(SettingsPath);
        Directory.CreateDirectory(folder);
        File.WriteAllText(SettingsPath, gamePath);
    }

    private static string LoadSavedPath()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            return File.ReadAllText(SettingsPath).Trim();
        }
        catch (IOException) { return null; }
    }

    private static IEnumerable<string> SteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyPath in new[] { @"SOFTWARE\Valve\Steam", @"SOFTWARE\WOW6432Node\Valve\Steam" })
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using (var key = hive.OpenSubKey(keyPath))
                    {
                        var path = key == null ? null : key.GetValue("SteamPath") as string;
                        var installPath = key == null ? null : key.GetValue("InstallPath") as string;
                        if (path != null) roots.Add(path);
                        if (installPath != null) roots.Add(installPath);
                    }
                }
                catch (System.Security.SecurityException) { }
            }
        }

        foreach (var root in roots.ToArray())
        {
            yield return root;
            var libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile)) continue;
            string content;
            try { content = File.ReadAllText(libraryFile); }
            catch (IOException) { continue; }

            foreach (Match match in Regex.Matches(content, "\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                yield return match.Groups["path"].Value.Replace("\\\\", "\\");
            foreach (Match match in Regex.Matches(content, "^\\s*\\\"\\d+\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.Multiline))
                yield return match.Groups["path"].Value.Replace("\\\\", "\\");
        }
    }

    private static string FindSteamClient()
    {
        foreach (var root in SteamRoots())
        {
            var candidate = Path.Combine(root, "steam.exe");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static string FindSteamGame()
    {
        foreach (var root in SteamRoots())
        {
            var gamePath = FindInSteamLibrary(root);
            if (gamePath != null) return gamePath;
        }

        return null;
    }

    private static string FindInSteamLibrary(string libraryRoot)
    {
        try
        {
            var manifest = Path.Combine(libraryRoot, "steamapps", "appmanifest_" + SteamAppId + ".acf");
            if (File.Exists(manifest))
            {
                var match = Regex.Match(File.ReadAllText(manifest), "\\\"installdir\\\"\\s+\\\"(?<dir>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var fromManifest = Path.Combine(libraryRoot, "steamapps", "common", match.Groups["dir"].Value, GameExe);
                    if (IsGameExecutable(fromManifest)) return fromManifest;
                }
            }

            var conventional = Path.Combine(libraryRoot, "steamapps", "common", "For Honor", GameExe);
            return IsGameExecutable(conventional) ? conventional : null;
        }
        catch (IOException) { return null; }
    }

    private static IEnumerable<string> UbisoftCandidates()
    {
        foreach (var candidate in UbisoftRegistryCandidates()) yield return candidate;

        var programFiles = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        foreach (var root in programFiles.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(root, "Ubisoft", "Ubisoft Game Launcher", "games", "For Honor", GameExe);
            yield return Path.Combine(root, "Ubisoft Game Launcher", "games", "For Honor", GameExe);
        }
    }

    private static IEnumerable<string> UbisoftLauncherCandidates()
    {
        foreach (var launcherRoot in UbisoftLauncherRegistryRoots())
        {
            yield return Path.Combine(launcherRoot, "UbisoftConnect.exe");
            yield return Path.Combine(launcherRoot, "upc.exe");
        }

        var programFiles = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        foreach (var root in programFiles.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var launcherRoot = Path.Combine(root, "Ubisoft", "Ubisoft Game Launcher");
            yield return Path.Combine(launcherRoot, "UbisoftConnect.exe");
            yield return Path.Combine(launcherRoot, "upc.exe");
        }
    }

    private static IEnumerable<string> UbisoftLauncherRegistryRoots()
    {
        var keyPaths = new[]
        {
            @"SOFTWARE\Ubisoft\Launcher",
            @"SOFTWARE\WOW6432Node\Ubisoft\Launcher"
        };
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var keyPath in keyPaths)
            {
                RegistryKey key = null;
                try { key = hive.OpenSubKey(keyPath); }
                catch (System.Security.SecurityException) { }
                if (key == null) continue;
                using (key)
                {
                    foreach (var valueName in new[] { "InstallDir", "InstallPath", "Path" })
                    {
                        var path = key.GetValue(valueName) as string;
                        if (!string.IsNullOrWhiteSpace(path)) yield return Environment.ExpandEnvironmentVariables(path);
                    }
                }
            }
        }
    }

    private static UbisoftGameRegistration FindUbisoftGameRegistration()
    {
        foreach (var registration in UbisoftInstallRegistrations())
            if (IsGameExecutable(registration.GamePath) && Regex.IsMatch(registration.ProductId, @"^\d+$"))
                return registration;

        return null;
    }

    private static IEnumerable<string> UbisoftRegistryCandidates()
    {
        foreach (var registration in UbisoftInstallRegistrations())
            yield return registration.GamePath;
    }

    private static IEnumerable<UbisoftGameRegistration> UbisoftInstallRegistrations()
    {
        var keyPaths = new[]
        {
            @"SOFTWARE\Ubisoft\Launcher\Installs",
            @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs"
        };
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var keyPath in keyPaths)
            {
                RegistryKey installsKey = null;
                try { installsKey = hive.OpenSubKey(keyPath); }
                catch (System.Security.SecurityException) { }
                if (installsKey == null) continue;
                using (installsKey)
                {
                    // Some older launcher versions stored product IDs as values.
                    foreach (var valueName in installsKey.GetValueNames())
                    {
                        var installPath = installsKey.GetValue(valueName) as string;
                        var registration = CreateUbisoftRegistration(valueName, installPath);
                        if (registration != null) yield return registration;
                    }

                    // Current Ubisoft Connect versions normally use one subkey per product ID.
                    foreach (var productId in installsKey.GetSubKeyNames())
                    {
                        RegistryKey gameKey = null;
                        try { gameKey = installsKey.OpenSubKey(productId); }
                        catch (System.Security.SecurityException) { }
                        if (gameKey == null) continue;
                        using (gameKey)
                        {
                            var installPath = gameKey.GetValue("InstallDir") as string ??
                                              gameKey.GetValue("InstallPath") as string ??
                                              gameKey.GetValue("Path") as string;
                            var registration = CreateUbisoftRegistration(productId, installPath);
                            if (registration != null) yield return registration;
                        }
                    }
                }
            }
        }
    }

    private static UbisoftGameRegistration CreateUbisoftRegistration(string productId, string installPath)
    {
        if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(installPath)) return null;
        var expandedPath = Environment.ExpandEnvironmentVariables(installPath.Trim().Trim('"'));
        var gamePath = expandedPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? expandedPath
            : Path.Combine(expandedPath, GameExe);
        return new UbisoftGameRegistration(productId, gamePath);
    }

    private static IEnumerable<string> FixedDriveCandidates()
    {
        var relativeFolders = new[]
        {
            "ForHonor", "For Honor",
            @"Games\ForHonor", @"Games\For Honor",
            @"Ubisoft Games\ForHonor", @"Ubisoft Games\For Honor",
            @"Ubisoft\ForHonor", @"Ubisoft\For Honor",
            @"Program Files\Ubisoft\Ubisoft Game Launcher\games\For Honor",
            @"Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\For Honor"
        };
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
            foreach (var folder in relativeFolders)
                yield return Path.Combine(drive.RootDirectory.FullName, folder, GameExe);
        }
    }

    private static bool IsGameExecutable(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               string.Equals(Path.GetFileName(path), GameExe, StringComparison.OrdinalIgnoreCase) &&
               File.Exists(path);
    }
}
}
