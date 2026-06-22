# Packaging — single-file installer

Builds **`ZombieShooterSetup.exe`**: one self-extracting executable that bundles
the whole game (Unity build) inside itself.

A Unity build is a *folder* (the `.exe` needs `UnityPlayer.dll` + the `_Data`
folder beside it), so it can't be shipped as a bare `.exe`. This packer embeds a
zip of the build as a resource inside a tiny C# launcher.

On run, the setup:
1. unpacks the game to `%LOCALAPPDATA%\ZombieShooter`,
2. creates a **ZombieShooter** desktop shortcut,
3. launches the game.

## Build it

```powershell
# uses the existing player in Build/Windows
powershell -ExecutionPolicy Bypass -File "Build-Installer.ps1"

# or rebuild the Unity player first (editor must be CLOSED — it locks the project)
powershell -ExecutionPolicy Bypass -File "Build-Installer.ps1" -Rebuild
```

Output: `ZombieShooterSetup.exe` next to the project folder.

## Files
- `Extractor.cs` — the self-extracting launcher (compiled with the bundled .NET `csc.exe`).
- `Build-Installer.ps1` — stages the build, zips it, embeds it and compiles the setup.

## Notes
- The setup is **unsigned**, so SmartScreen shows "unknown publisher" on first
  run — choose *More info → Run anyway*.
- IExpress was tried first but proved unreliable here; the C# + embedded-zip
  approach is deterministic and needs no third-party tools.
