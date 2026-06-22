<#
  Build-Installer.ps1 — produce a single self-extracting ZombieShooterSetup.exe.

  Pipeline:
    1) (optional) run a fresh Unity headless build via BuildScript.BuildWindows
    2) stage Build/Windows minus the Burst debug folder
    3) zip the stage
    4) compile Extractor.cs with the zip embedded as a resource (game.zip)

  The result installs to %LOCALAPPDATA%\ZombieShooter, makes a desktop
  shortcut and launches the game.

  Usage (from anywhere):
    powershell -ExecutionPolicy Bypass -File "Build-Installer.ps1"
    powershell -ExecutionPolicy Bypass -File "Build-Installer.ps1" -Rebuild
#>
param(
    [switch]$Rebuild,                                   # rebuild the Unity player first
    [string]$Unity = "D:\unity\6000.4.0f1\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"
$proj = Split-Path -Parent $PSScriptRoot               # ...\My project (2)
$buildDir = Join-Path $proj "Build\Windows"
$outExe   = Join-Path $proj "ZombieShooterSetup.exe"
$tmpZip   = Join-Path $env:TEMP "ZombieShooter_payload.zip"
$stage    = Join-Path $env:TEMP "ZombieShooter_stage"

if ($Rebuild) {
    Write-Host "Building Unity player..."
    & $Unity -batchmode -quit -nographics -projectPath $proj -executeMethod BuildScript.BuildWindows -logFile (Join-Path $proj "build.log")
}
if (-not (Test-Path -LiteralPath (Join-Path $buildDir "ZombieShooter.exe"))) {
    throw "No build found at $buildDir. Build the player first (-Rebuild) or in the editor."
}

# stage a clean copy without the Burst debug symbols
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
robocopy $buildDir $stage /MIR /XD "*BurstDebugInformation_DoNotShip*" /NFL /NDL /NJH /NJS /NP | Out-Null

# zip the stage
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $tmpZip) { Remove-Item -LiteralPath $tmpZip -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $tmpZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

# compile the self-extractor with the zip embedded
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$gac = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.IO.Compression"
$ref = (Get-ChildItem $gac -Recurse -Filter "System.IO.Compression.dll" | Select-Object -First 1).FullName
$src = Join-Path $PSScriptRoot "Extractor.cs"
if (Test-Path -LiteralPath $outExe) { Remove-Item -LiteralPath $outExe -Force }
& $csc /nologo /target:winexe "/out:$outExe" "/resource:$tmpZip,game.zip" "/reference:$ref" $src

Remove-Item -LiteralPath $tmpZip -Force
Remove-Item -LiteralPath $stage -Recurse -Force

if (Test-Path -LiteralPath $outExe) {
    "Done -> $outExe  ({0} MB)" -f [math]::Round((Get-Item $outExe).Length/1MB,1)
} else {
    throw "Compile failed: $outExe not produced."
}
