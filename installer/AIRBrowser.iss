#define AppVersion GetEnv("AIR_VERSION")
[Setup]
AppId={{A1B2C3D4-E5F6-47A8-9B10-112233445566}
AppName=AIR Browser
AppVersion={#AppVersion}
AppPublisher=AIR
DefaultDirName={localappdata}\Programs\AIR Browser
DefaultGroupName=AIR Browser
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=AIR-Browser-Setup
SetupIconFile=..\assets\air-browser.ico
UninstallDisplayIcon={app}\AIRBrowser.WinUI.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
UsePreviousAppDir=yes
CloseApplications=yes
RestartApplications=no
Uninstallable=yes

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\AIR Browser"; Filename: "{app}\AIRBrowser.WinUI.exe"
Name: "{autodesktop}\AIR Browser"; Filename: "{app}\AIRBrowser.WinUI.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"
