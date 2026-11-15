; Inno Setup script – per-user installer (no admin rights), used for GitHub releases and winget.
; Compile: ISCC.exe /DAppVersion=0.2.0 /DPublishDir=..\..\publish\win-x64 QuickCommands.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\publish\win-x64"
#endif

[Setup]
AppId={{9E3C1B52-6C1E-4F7A-9B0E-5C2D7A1F4E31}
AppName=QuickCommands
AppVersion={#AppVersion}
AppVerName=QuickCommands {#AppVersion}
AppPublisher=Felix Kriechbaum
AppPublisherURL=https://github.com/felixkriechbaum/QuickCommands
AppSupportURL=https://github.com/felixkriechbaum/QuickCommands/issues
DefaultDirName={localappdata}\Programs\QuickCommands
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\..\publish\installer
OutputBaseFilename=QuickCommands-{#AppVersion}-win-x64-setup
SetupIconFile=..\..\src\QuickCommands.App\Assets\icon.ico
UninstallDisplayIcon={app}\QuickCommands.exe
UninstallDisplayName=QuickCommands
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Files]
Source: "{#PublishDir}\QuickCommands.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\QuickCommands"; Filename: "{app}\QuickCommands.exe"

[Run]
Filename: "{app}\QuickCommands.exe"; Description: "{cm:LaunchProgram,QuickCommands}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Removes the profile lines and the PATH entry. Your commands (~\.quickcommands) are kept.
Filename: "{app}\QuickCommands.exe"; Parameters: "--remove-integration"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveIntegration"
