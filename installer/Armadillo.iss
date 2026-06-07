; Inno Setup script for Armadillo (the standalone dashboard app)
; 1) dotnet publish src/Armadillo.App -c Release -r win-x64 --self-contained ^
;      -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
;      -p:EnableCompressionInSingleFile=true -o publish-app
; 2) Compile with: ISCC.exe installer\Armadillo.iss   (output -> dist\Armadillo-Setup.exe)

#define MyAppName "Armadillo"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "Armadillo"
#define MyAppExeName "Armadillo.exe"

[Setup]
AppId={{A7E3F2C1-9B4D-4E6A-8C2F-1D5B6E7A9C30}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\dist
OutputBaseFilename=Armadillo-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\publish-app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
