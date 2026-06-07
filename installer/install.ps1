<#
.SYNOPSIS
  Per-user installer for the Armadillo dashboard (no admin, no Inno Setup required).
  Copies the app to %LOCALAPPDATA%\Programs\Armadillo, creates Start-Menu (and optional Desktop)
  shortcuts, registers in "Apps & features", and drops an uninstaller.

.EXAMPLE
  ./install.ps1                 # install (from the folder that contains Armadillo.exe / ../publish-app)
  ./install.ps1 -Desktop        # also create a desktop shortcut
  ./install.ps1 -DryRun         # show what it would do, change nothing
#>
[CmdletBinding()]
param(
  [string]$Source,
  [string]$InstallDir = "$env:LOCALAPPDATA\Programs\Armadillo",
  [switch]$Desktop,
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$AppName = 'Armadillo'
$ExeName = 'Armadillo.exe'
$Version = '0.1.0'

# Resolve the source folder that holds Armadillo.exe.
if (-not $Source) {
  if     (Test-Path "$PSScriptRoot\$ExeName")               { $Source = $PSScriptRoot }
  elseif (Test-Path "$PSScriptRoot\..\publish-app\$ExeName"){ $Source = (Resolve-Path "$PSScriptRoot\..\publish-app").Path }
  else { throw "Could not find $ExeName. Pass -Source <folder> (the folder containing $ExeName)." }
}
if (-not (Test-Path (Join-Path $Source $ExeName))) { throw "$ExeName not found in '$Source'." }

$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$lnk       = Join-Path $startMenu "$AppName.lnk"
$deskLnk   = Join-Path ([Environment]::GetFolderPath('Desktop')) "$AppName.lnk"
$exePath   = Join-Path $InstallDir $ExeName
$regKey    = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"

function Do-Step($desc, [scriptblock]$action) {
  if ($DryRun) { Write-Host "[dry-run] $desc" } else { Write-Host $desc; & $action }
}

Write-Host "Installing $AppName $Version" -ForegroundColor Cyan
Write-Host "  from: $Source"
Write-Host "  to:   $InstallDir"

Do-Step "Copy files -> $InstallDir" {
  New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
  Copy-Item -Path (Join-Path $Source '*') -Destination $InstallDir -Recurse -Force
}

Do-Step "Create Start-Menu shortcut" {
  $w = New-Object -ComObject WScript.Shell
  $s = $w.CreateShortcut($lnk); $s.TargetPath = $exePath; $s.WorkingDirectory = $InstallDir; $s.Save()
}

if ($Desktop) {
  Do-Step "Create Desktop shortcut" {
    $w = New-Object -ComObject WScript.Shell
    $s = $w.CreateShortcut($deskLnk); $s.TargetPath = $exePath; $s.WorkingDirectory = $InstallDir; $s.Save()
  }
}

Do-Step "Register in Apps & features (HKCU)" {
  New-Item -Path $regKey -Force | Out-Null
  Set-ItemProperty $regKey DisplayName    $AppName
  Set-ItemProperty $regKey DisplayVersion $Version
  Set-ItemProperty $regKey Publisher      $AppName
  Set-ItemProperty $regKey InstallLocation $InstallDir
  Set-ItemProperty $regKey DisplayIcon    $exePath
  Set-ItemProperty $regKey UninstallString "powershell -NoProfile -ExecutionPolicy Bypass -File `"$InstallDir\uninstall.ps1`""
  Set-ItemProperty $regKey NoModify 1; Set-ItemProperty $regKey NoRepair 1
}

$cliDir = Join-Path $InstallDir 'cli'
if (Test-Path (Join-Path $Source 'cli\armadillo.exe')) {
  Do-Step "Add CLI to user PATH ($cliDir)" {
    $p = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (($p -split ';') -notcontains $cliDir) {
      [Environment]::SetEnvironmentVariable('Path', ($p.TrimEnd(';') + ';' + $cliDir), 'User')
    }
  }
}

Do-Step "Write uninstaller" {
  Copy-Item -Path (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination (Join-Path $InstallDir 'uninstall.ps1') -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "$(if($DryRun){'[dry-run] would be '}else{''})installed. Launch the app from the Start Menu ($exePath)." -ForegroundColor Green
if (Test-Path (Join-Path $Source 'cli\armadillo.exe')) {
  Write-Host "CLI: open a NEW terminal and run 'armadillo doctor' (PATH updated)." -ForegroundColor Green
}
