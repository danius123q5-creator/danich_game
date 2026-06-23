<#
  Build-Launcher.ps1 — compile the standalone version-picker launcher (v1.8).
  Produces ZombieShooterLauncher.exe next to the project folder. The launcher
  lists the GitHub releases that ship a ZombieShooterSetup.exe and downloads +
  runs the chosen version.
#>
$ErrorActionPreference = "Stop"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src = Join-Path $PSScriptRoot "Launcher.cs"
$out = Join-Path (Split-Path -Parent $PSScriptRoot) "ZombieShooterLauncher.exe"

if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Force }
& $csc -nologo -target:winexe "-out:$out" `
    -reference:System.Windows.Forms.dll -reference:System.Drawing.dll -reference:System.dll $src

if (Test-Path -LiteralPath $out) {
    "Done -> $out  ({0} KB)" -f [math]::Round((Get-Item $out).Length / 1KB, 0)
} else {
    throw "Compile failed: $out not produced."
}
