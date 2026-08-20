; FG Scanner — Inno Setup script (phase 0 stub)
; Packages the publish output (created by: dotnet publish src/FgScanner.App -p:PublishProfile=win-x64)
; Later phases add: file associations, WIA AutoPlay + StillImage registration, privacy/consent page.
; Build:  ISCC.exe /DAppVersion=0.1.0 build\installer\setup.iss   (run from repo root)

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "FG Scanner"
#define Publisher "Franz Gerster"
#define ExeName "FgScanner.exe"
#define PublishDir "..\..\publish\win-x64"

[Setup]
AppId={{77A2D51A-B7C2-452F-A125-84191C2ABA38}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL=https://github.com/fgerster1/fgScanner
DefaultDirName={commonpf}\FGScanner
DefaultGroupName={#AppName}
OutputDir=..\..\dist
OutputBaseFilename=fgscanner-{#AppVersion}-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
; Windows 10 1607+ (matches .NET 10 / NAPS2 floor)
MinVersion=10.0.14393
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#ExeName}
LicenseFile=..\..\LICENSE

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\{#ExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[InstallDelete]
; Purge stale files from previous versions on upgrade (NAPS2 pattern)
Type: filesandordirs; Name: "{app}\*.dll"
