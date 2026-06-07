<#
.SYNOPSIS  Uninstall the per-user Armadillo dashboard installed by install.ps1.
.EXAMPLE   ./uninstall.ps1            # remove app, shortcuts, and registry entry
           ./uninstall.ps1 -DryRun
#>
[CmdletBinding()]
param(
  [string]$InstallDir = "$env:LOCALAPPDATA\Programs\Armadillo",
  [switch]$DryRun
)
$ErrorActionPreference = 'SilentlyContinue'
$AppName = 'Armadillo'
$startLnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$AppName.lnk"
$deskLnk  = Join-Path ([Environment]::GetFolderPath('Desktop')) "$AppName.lnk"
$regKey   = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"

function Do-Step($desc, [scriptblock]$action) {
  if ($DryRun) { Write-Host "[dry-run] $desc" } else { Write-Host $desc; & $action }
}

Write-Host "Uninstalling $AppName" -ForegroundColor Cyan
Do-Step "Remove Start-Menu shortcut" { Remove-Item $startLnk -Force }
Do-Step "Remove Desktop shortcut"    { Remove-Item $deskLnk -Force }
Do-Step "Remove registry entry"      { Remove-Item $regKey -Recurse -Force }
Do-Step "Remove install dir ($InstallDir)" { Remove-Item $InstallDir -Recurse -Force }
Write-Host "Done. (Your workspace at %LOCALAPPDATA%\Armadillo was left intact.)" -ForegroundColor Green
