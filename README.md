# For Honor Quick Actions

A fast, mouse-only Windows utility for players who need to close or restart For Honor without opening Task Manager.

## Actions

- When **For Honor is running**: **Close For Honor**, **Close and reopen For Honor**, and **Clean restart** are shown.
- When **For Honor is not running**: **Open For Honor**, **Clean restart Ubisoft**, and **Clean restart (Ubisoft + open game)** are shown. The Ubisoft-only option stops For Honor, anti-cheat, and Ubisoft processes, then starts Ubisoft Connect without launching the game.

The utility stays open for every action and only closes when the player closes its window. The title-bar settings button detects Steam and Ubisoft Connect installations and lets the player save a default launch platform; platform launches go through the selected client instead of an arbitrary duplicate executable. Once For Honor's actual game window is available, the in-game quick-actions overlay pops out automatically; it waits through the launcher/splash phase and appears only once per game session. It uses a borderless, draggable custom title strip with a fade-out close animation. `ForHonorQuickActions/Logo.ico` is compiled into the EXE and taskbar icon.

The app automatically searches the saved game location, Steam libraries (including extra Steam library drives), Ubisoft Connect registry entries, and common folders on every fixed drive—including paths such as `F:\ForHonor\forhonor.exe`. If it cannot find the game, use the **Game location** button once to choose `forhonor.exe`; the selection is saved in the current user's local app-data folder and remains available after the app is closed and reopened.

## Build the single EXE

On Windows, run:

```powershell
.\build.ps1
```

Distribute `release\ForHonorQuickActions.exe`. It is a single small native Windows application and uses the .NET Framework already included with supported Windows 10 and 11 installations; players do not need to install .NET or configure a game shortcut first.

## Signing

The supplied signed release is Authenticode-signed by the local self-signed **For Honor Quick Actions Developer** certificate. Its public certificate is included as `release\ForHonorQuickActions-Developer.cer`; the private key is not exported. Windows will not automatically trust a self-signed publisher on other PCs. Use a certificate from a public code-signing authority for a publicly distributed, automatically trusted publisher identity.
