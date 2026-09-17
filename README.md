# For Honor Quick Actions

A fast, mouse-only Windows utility for players who need to close or restart For Honor without opening Task Manager.

## Actions

- **Open/Close For Honor** changes with the current game state, and **Close and reopen For Honor** becomes available while the game is running.
- **Start/Stop Ubisoft** and **Restart Ubisoft** share one row. They control Ubisoft Connect independently without intentionally closing For Honor.
- **Clean restart** stops For Honor, anti-cheat, and Ubisoft Connect before launching the game again.

Close and restart actions repeatedly terminate every matching For Honor process and wait until the processes remain gone before reporting success or relaunching. This also clears leftover background instances that would otherwise make Ubisoft report that the game is already running.

The main controls and settings now live in one borderless window. The title-bar settings button switches views, and the two equal-width settings tabs cover **Default Launch** and **Appearance**. The settings controls are created only when Settings is first opened. Default Launch detects Steam and Ubisoft Connect, supports a saved forhonor.exe, and remembers the selected launch method; launcher discovery is cached only in memory for the current app session. Appearance controls whether minimizing sends the app to the system tray (the default) or leaves it in the taskbar, window transparency, accent color, tooltips, and optional **Try Hard mode**. Try Hard mode is off by default; when enabled, it launches Quick Actions automatically at Windows sign-in and starts minimized using the selected tray or taskbar behavior. Preferences are saved per Windows user and apply immediately.

The utility stays open for every action and only exits from its close button or tray menu. Once For Honor's actual game window is available, the in-game quick-actions overlay pops out automatically; it waits through the launcher/splash phase and appears only once per game session. The overlay selects the display containing the largest visible For Honor window, including borderless/fullscreen layouts on multi-monitor systems. `ForHonorQuickActions/Logo.ico` is compiled into the EXE, window, and tray icon.

The app automatically searches the saved game location, Steam libraries (including extra Steam library drives), Ubisoft Connect registry entries, and common folders on every fixed drive—including paths such as `F:\ForHonor\forhonor.exe`. If it cannot find the game, use the **Game location** button once to choose `forhonor.exe`; the selection is saved in the current user's local app-data folder and remains available after the app is closed and reopened.

## Build the single EXE

On Windows, run:

```powershell
.\build.ps1
```

Distribute `release\ForHonorQuickActions.exe`. It is a single small native Windows application and uses the .NET Framework already included with supported Windows 10 and 11 installations; players do not need to install .NET or configure a game shortcut first.

## Signing

The supplied signed release is Authenticode-signed by the local self-signed **For Honor Quick Actions Developer** certificate. Its public certificate is included as `release\ForHonorQuickActions-Developer.cer`; the private key is not exported. Windows will not automatically trust a self-signed publisher on other PCs. Use a certificate from a public code-signing authority for a publicly distributed, automatically trusted publisher identity.
