#requires -version 7
<#
.SYNOPSIS
  Local build/packaging for Armadillo — mirrors the GitHub Actions release flow (same as Quiver/Quiver-Pro).
  Produces, in dist\:
    Armadillo-portable.exe   GUI dashboard, self-contained single-file (no .NET needed)
    armadillo.exe            CLI, self-contained single-file
    Armadillo-Setup.exe      Inno installer (only with -Installer and Inno Setup installed)
  Also stages publish-app\ (GUI folder publish + cli\armadillo.exe + install scripts) for the installer.

.EXAMPLE
  ./build.ps1               # portable GUI exe + CLI exe + installer payload
  ./build.ps1 -Installer    # also build dist\Armadillo-Setup.exe (needs Inno Setup)
#>
[CmdletBinding()]
param([switch]$Installer)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Remove-Item dist, publish-app, publish-portable, publish-cli -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force dist | Out-Null

$single = @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true')

Write-Host '==> GUI portable (self-contained single-file)' -ForegroundColor Cyan
dotnet publish src/Armadillo.App -c Release -r win-x64 --self-contained true @single -o publish-portable --nologo
Copy-Item publish-portable/Armadillo.exe dist/Armadillo-portable.exe -Force

Write-Host '==> GUI installer payload (folder)' -ForegroundColor Cyan
dotnet publish src/Armadillo.App -c Release -r win-x64 --self-contained true -o publish-app --nologo

Write-Host '==> CLI portable (self-contained single-file)' -ForegroundColor Cyan
dotnet publish src/Armadillo.Host -c Release -r win-x64 --self-contained true @single -o publish-cli --nologo
Copy-Item publish-cli/armadillo.exe dist/armadillo.exe -Force

# Bundle the CLI + install scripts into the installer payload so one install gives GUI + CLI (+PATH).
New-Item -ItemType Directory -Force publish-app/cli | Out-Null
Copy-Item publish-cli/armadillo.exe publish-app/cli/armadillo.exe -Force
Copy-Item installer/install.ps1, installer/uninstall.ps1 publish-app -Force

if ($Installer) {
  Write-Host '==> Inno Setup installer' -ForegroundColor Cyan
  $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
            Where-Object { Test-Path $_ } | Select-Object -First 1
  if ($iscc) { & $iscc installer/Armadillo.iss }
  else { Write-Warning 'Inno Setup not found. Install with: winget install JRSoftware.InnoSetup' }
}

Write-Host ''
Write-Host 'Artifacts:' -ForegroundColor Green
Get-ChildItem dist | Select-Object Name, @{n='MB'; e={ [math]::Round($_.Length / 1MB, 1) }} | Format-Table -AutoSize
