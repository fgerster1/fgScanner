; FG Scanner — Inno Setup script (phase 9: complete)
; Packages the publish output (created by: dotnet publish src/FgScanner.App -p:PublishProfile=win-x64)
; Build (run from repo root; finds ISCC.exe whether Inno Setup is machine-wide or per-user):
;   $iscc = Get-ChildItem "${env:ProgramFiles(x86)}\Inno Setup*","$env:ProgramFiles\Inno Setup*",
;     "$env:LOCALAPPDATA\Programs\Inno Setup*" -Filter ISCC.exe -Recurse -EA SilentlyContinue |
;     Sort-Object FullName -Descending | Select-Object -First 1
;   & $iscc.FullName build\installer\setup.iss
;
; The version is read off the published FgScanner.exe, whose number comes from <Version> in
; Directory.Build.props — bump it there and nowhere else. /DAppVersion=x.y.z still overrides,
; but a hand-typed number that disagrees with the payload is exactly what this avoids.
;
; Silent install (documented per PLAN prompt 9):
;   fgscanner-<ver>-win-x64.exe /VERYSILENT /NORESTART /SUPPRESSMSGBOXES
;   add /MERGETASKS="!desktopicon" to skip the desktop icon, /TASKS="aioptout" to disable AI.
;   /LOG="path" writes a setup log. Uninstall: "unins000.exe" /VERYSILENT.

#define AppName "FG Scanner"
#define Publisher "Franz Gerster"
#define ExeName "FgScanner.exe"
#define PublishDir "..\..\publish\win-x64"
#define ProgId "FGScanner.Document"

#ifndef AppVersion
  #define ExePath AddBackslash(SourcePath) + PublishDir + "\" + ExeName
  #if FileExists(ExePath)
    ; A FileVersion resource is always four-part; the release number is the first three.
    #define FourPart GetFileVersion(ExePath)
    #define AppVersion Copy(FourPart, 1, RPos(".", FourPart) - 1)
  #else
    #define AppVersion "0.0.0"
  #endif
#endif

[Setup]
AppId={{77A2D51A-B7C2-452F-A125-84191C2ABA38}
AppName={#AppName}
AppVersion={#AppVersion}
; Without this the setup exe ships a blank FileVersion resource: support triage
; can't identify a build from its properties, and unsigned installers with no
; version info score worse against AV/SmartScreen heuristics.
VersionInfoVersion={#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL=https://github.com/fgerster1/fgScanner
DefaultDirName={commonpf}\FGScanner
DefaultGroupName={#AppName}
OutputDir=..\..\dist
OutputBaseFilename=fgscanner-{#AppVersion}-win-x64
SetupIconFile=fgscanner.ico
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
; Privacy summary shown before install (SignPath requirement, PLAN §4)
InfoBeforeFile=privacy.txt
; Close a running FG Scanner on upgrade instead of failing on locked files
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "aioptout"; Description: "Disable the optional AI description feature (nothing can be sent to Google on this computer)"; GroupDescription: "Privacy:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "..\..\PRIVACY.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{group}\FG Scanner user guide"; Filename: "https://github.com/fgerster1/fgScanner/blob/main/docs/user-guide.md"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Registry]
; ---- Machine-wide AI opt-out (privacy task above; the app hides the AI feature when set) ----
Root: HKLM; Subkey: "SOFTWARE\FGScanner"; ValueType: dword; ValueName: "AiOptOut"; ValueData: 1; Tasks: aioptout; Flags: uninsdeletekeyifempty uninsdeletevalue

; ---- "Open with FG Scanner" for the formats the app imports (OpenWithProgids, NAPS2 pattern) ----
Root: HKLM; Subkey: "SOFTWARE\Classes\{#ProgId}"; ValueType: string; ValueData: "FG Scanner document"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueData: """{app}\{#ExeName}"",0"
Root: HKLM; Subkey: "SOFTWARE\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueData: """{app}\{#ExeName}"" ""%1"""
Root: HKLM; Subkey: "SOFTWARE\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\Classes\.jpg\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\Classes\.jpeg\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\Classes\.png\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\Classes\.tiff\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\Classes\.tif\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\Classes\.bmp\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue

; ---- StillImage registration: scanner hardware button / "Scan with FG Scanner" (NAPS2 pattern) ----
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\StillImage\Registered Applications"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#ExeName}"" /StiDevice:%1 /StiEvent:%2"; Flags: uninsdeletevalue

; ---- WIA AutoPlay handler ("Scan with FG Scanner" when a scanner is connected) ----
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers\Handlers\FGScannerScanHandler"; ValueType: string; ValueName: "Action"; ValueData: "Scan with {#AppName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers\Handlers\FGScannerScanHandler"; ValueType: string; ValueName: "Provider"; ValueData: "{#AppName}"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers\Handlers\FGScannerScanHandler"; ValueType: string; ValueName: "InvokeProgID"; ValueData: "{#ProgId}"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers\Handlers\FGScannerScanHandler"; ValueType: string; ValueName: "InvokeVerb"; ValueData: "open"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers\Handlers\FGScannerScanHandler"; ValueType: string; ValueName: "DefaultIcon"; ValueData: """{app}\{#ExeName}"",0"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers\EventHandlers\WiaDeviceArrived"; ValueType: string; ValueName: "FGScannerScanHandler"; ValueData: ""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#ExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[InstallDelete]
; Purge stale files from previous versions on upgrade (NAPS2 pattern)
Type: filesandordirs; Name: "{app}\*.dll"
Type: filesandordirs; Name: "{app}\_win64"
Type: filesandordirs; Name: "{app}\_win32"
Type: filesandordirs; Name: "{app}\_winarm"
Type: filesandordirs; Name: "{app}\tessdata"
