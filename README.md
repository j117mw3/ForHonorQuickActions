# For Honor Quick Actions

A fast, mouse-only Windows utility for players who need to close or restart For Honor without opening Task Manager.

## Actions

- When **For Honor is running**: **Close For Honor**, **Close and reopen For Honor**, and **Clean restart** are shown.
- When **For Honor is not running**: **Open For Honor** and **Clean restart** are shown. **Open For Honor** starts the game and the utility switches to the three running-game controls as soon as `forhonor.exe` appears.

The utility stays open for every action and only closes when the player closes its window. `Logo.png` is embedded in the app window and compiled into the EXE icon.

The app automatically searches the saved game location, Steam libraries (including extra Steam library drives), Ubisoft Connect registry entries, and common folders on every fixed drive—including paths such as `F:\ForHonor\forhonor.exe`. If it cannot find the game, use the **Game location** button once to choose `forhonor.exe`; the selection is saved in the current user's local app-data folder and remains available after the app is closed and reopened.

## Build the single EXE

On Windows, run:

```powershell
.\build.ps1
```

Distribute `release\ForHonorQuickActions.exe`. It is a single small native Windows application and uses the .NET Framework already included with supported Windows 10 and 11 installations; players do not need to install .NET or configure a game shortcut first.

## Signing

The supplied signed release is Authenticode-signed by the local self-signed **For Honor Quick Actions Developer** certificate. Its public certificate is included as `release\ForHonorQuickActions-Developer.cer`; the private key is not exported. Windows will not automatically trust a self-signed publisher on other PCs. Use a certificate from a public code-signing authority for a publicly distributed, automatically trusted publisher identity.
