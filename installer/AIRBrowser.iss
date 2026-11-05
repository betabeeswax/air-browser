[Setup]
AppId={{A1B2C3D4-E5F6-47A8-9B10-112233445566}
AppName=AIR Browser
AppVersion=0.1.0
AppPublisher=AIR
DefaultDirName={localappdata}\Programs\AIR Browser
DefaultGroupName=AIR Browser
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=AIR-Browser-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\AIRBrowser.WinUI.exe

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\AIR Browser"; Filename: "{app}\AIRBrowser.WinUI.exe"
Name: "{autodesktop}\AIR Browser"; Filename: "{app}\AIRBrowser.WinUI.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"
