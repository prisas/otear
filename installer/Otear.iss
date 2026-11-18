; Otear installer (Inno Setup 6). Built by tools/Build-Release.ps1, which passes:
;   /DAppVersion=x.y.z /DArch=x64|arm64 /DSourceDir=<publish folder> /DAssocInclude=<generated .iss>
; Per-user install by default (no admin prompt); an all-users install can be chosen in the first dialog.

#ifndef AppVersion
  #define AppVersion "0.1.5"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-" + Arch
#endif
#ifndef AssocInclude
  #define AssocInclude "obj\Associations.iss"
#endif

[Setup]
AppId={{B7C3E0A2-5F0D-4C55-9A1E-0E7A3F2B6C11}
AppName=Otear
AppVersion={#AppVersion}
AppVerName=Otear {#AppVersion}
AppPublisher=Otear contributors
AppComments=An mpv-based media player for Windows
DefaultDirName={autopf}\Otear
DefaultGroupName=Otear
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.22000
OutputDir=..\artifacts
OutputBaseFilename=Otear-{#AppVersion}-{#Arch}-setup
SetupIconFile=..\src\Otear\Assets\Otear.ico
UninstallDisplayIcon={app}\Otear.exe
UninstallDisplayName=Otear
LicenseFile=..\LICENSE
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
ChangesAssociations=yes
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "fileassoc"; Description: "Add Otear to ""Open with"" and Default apps for video and audio files"; GroupDescription: "File types:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Otear"; Filename: "{app}\Otear.exe"
Name: "{autodesktop}\Otear"; Filename: "{app}\Otear.exe"; Tasks: desktopicon

[Registry]
; "Applications" entry: lets Windows show Otear in Open with for the supported types.
Root: HKA; Subkey: "Software\Classes\Applications\Otear.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Otear"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\Otear.exe\DefaultIcon"; ValueType: string; ValueData: "{app}\Otear.exe,0"
Root: HKA; Subkey: "Software\Classes\Applications\Otear.exe\shell\open\command"; ValueType: string; ValueData: """{app}\Otear.exe"" ""%1"""

; ProgIDs
Root: HKA; Subkey: "Software\Classes\Otear.Video"; ValueType: string; ValueData: "Video file (Otear)"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Otear.Video\DefaultIcon"; ValueType: string; ValueData: "{app}\Otear.exe,0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Otear.Video\shell\open"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Otear"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Otear.Video\shell\open\command"; ValueType: string; ValueData: """{app}\Otear.exe"" ""%1"""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Otear.Audio"; ValueType: string; ValueData: "Audio file (Otear)"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Otear.Audio\DefaultIcon"; ValueType: string; ValueData: "{app}\Otear.exe,0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Otear.Audio\shell\open"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Otear"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Otear.Audio\shell\open\command"; ValueType: string; ValueData: """{app}\Otear.exe"" ""%1"""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Otear.Playlist"; ValueType: string; ValueData: "Playlist (Otear)"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Otear.Playlist\DefaultIcon"; ValueType: string; ValueData: "{app}\Otear.exe,0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Otear.Playlist\shell\open"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Otear"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Otear.Playlist\shell\open\command"; ValueType: string; ValueData: """{app}\Otear.exe"" ""%1"""; Tasks: fileassoc

; Default apps registration (Settings > Apps > Default apps > Otear)
Root: HKA; Subkey: "Software\Otear"; Flags: uninsdeletekeyifempty; Tasks: fileassoc
Root: HKA; Subkey: "Software\Otear\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "Otear"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Otear\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Plays video and audio in high quality, including 4K, HDR and Dolby Vision."; Tasks: fileassoc
Root: HKA; Subkey: "Software\Otear\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: "{app}\Otear.exe,0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "Otear"; ValueData: "Software\Otear\Capabilities"; Flags: uninsdeletevalue; Tasks: fileassoc

; Per-extension entries (generated by Build-Release.ps1)
#include AssocInclude

[Run]
Filename: "{app}\Otear.exe"; Description: "{cm:LaunchProgram,Otear}"; Flags: nowait postinstall skipifsilent
