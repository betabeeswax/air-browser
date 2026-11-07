#define AppVersion GetEnv("AIR_VERSION")
[Setup]
AppId={{A1B2C3D4-E5F6-47A8-9B10-112233445566}
AppName=AIR Browser
AppVersion={#AppVersion}
AppPublisher=AIR
SetupIconFile=..\assets\air-browser.ico
DefaultDirName={localappdata}\Programs\AIR Browser
DefaultGroupName=AIR Browser
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=AIR-Browser-Setup
UninstallDisplayIcon={app}\AIRBrowser.exe
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
Name: "{autoprograms}\AIR Browser"; Filename: "{app}\AIRBrowser.exe"
Name: "{autodesktop}\AIR Browser"; Filename: "{app}\AIRBrowser.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Code]
var
  CheckUpdatesButton: TNewButton;

procedure CheckUpdatesButtonClick(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open',
    'https://github.com/betabeeswax/air-browser/releases/latest',
    '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure InitializeWizard;
begin
  CheckUpdatesButton := TNewButton.Create(WizardForm);
  CheckUpdatesButton.Parent := WizardForm.WelcomePage;
  CheckUpdatesButton.Caption := 'Check for updates';
  CheckUpdatesButton.Width := ScaleX(130);
  CheckUpdatesButton.Height := ScaleY(28);
  CheckUpdatesButton.Left := WizardForm.WelcomePage.ClientWidth - CheckUpdatesButton.Width - ScaleX(16);
  CheckUpdatesButton.Top := WizardForm.WelcomePage.ClientHeight - CheckUpdatesButton.Height - ScaleY(16);
  CheckUpdatesButton.OnClick := @CheckUpdatesButtonClick;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  CheckUpdatesButton.Visible := CurPageID = wpWelcome;
end;
