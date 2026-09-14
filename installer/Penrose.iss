; Penrose setup wizard (Inno Setup 6). scripts\pack.ps1 compiles it from the self-contained
; publish folder, the same files that go into the portable zip, and passes the defines below.

#ifndef AppVersion
  #error Pass /DAppVersion=<version>; see scripts\pack.ps1
#endif
#ifndef FileVersion
  #error Pass /DFileVersion=<a.b.c.d>; see scripts\pack.ps1
#endif
#ifndef PublishDir
  #error Pass /DPublishDir=<self-contained publish folder>; see scripts\pack.ps1
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#ifndef OutputBaseFilename
  #define OutputBaseFilename "Penrose-" + AppVersion + "-win-x64-Setup"
#endif

#define AppName "Penrose"
#define AppExe "Penrose.exe"
#define AppPublisher "Zhehao Yang"
#define AppUrl "https://github.com/zzzyang03/penrose"

[Setup]
AppId={{0A5060D7-9E15-4FD1-BDEF-1193B8548148}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#FileVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
VersionInfoCopyright=Copyright (C) 2026 {#AppPublisher}
; Per-user by default (%LOCALAPPDATA%\Programs\Penrose, no administrator rights).
; "Setup.exe /ALLUSERS" installs under Program Files for everyone instead.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
DisableWelcomePage=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
; A running Penrose holds files in {app}, so Setup asks to close it. The finish page has its
; own "launch" option, so the closed instance is not restarted automatically.
CloseApplications=yes
RestartApplications=no
Compression=lzma2/max
SolidCompression=yes
LZMAUseSeparateProcess=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile=..\src\Penrose.App.WinUI\Assets\AppIcon.ico
SetupLogging=yes
WizardStyle=modern
ShowLanguageDialog=auto

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "zh_CN"; MessagesFile: "Languages\ChineseSimplified.isl"

[Files]
; README.txt only describes the portable layout.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "README.txt"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"

[UninstallDelete]
; The desktop shortcut is created from the finish page (see [Code]), not by [Icons].
Type: files; Name: "{autodesktop}\{#AppName}.lnk"

[Code]
var
  DesktopIconCheck: TNewCheckBox;
  LaunchCheck: TNewCheckBox;

procedure InitializeWizard;
begin
  // Finish-page options. Positioned in CurPageChanged once the label above them has its size.
  DesktopIconCheck := TNewCheckBox.Create(WizardForm);
  DesktopIconCheck.Parent := WizardForm.FinishedPage;
  DesktopIconCheck.Caption := CustomMessage('CreateDesktopIcon');
  DesktopIconCheck.Checked := True;

  LaunchCheck := TNewCheckBox.Create(WizardForm);
  LaunchCheck.Parent := WizardForm.FinishedPage;
  LaunchCheck.Caption := FmtMessage(CustomMessage('LaunchProgram'), ['{#AppName}']);
  LaunchCheck.Checked := True;
end;

procedure CurPageChanged(CurPageID: Integer);
var
  Left, Top, Width: Integer;
begin
  if CurPageID = wpFinished then
  begin
    Left := WizardForm.FinishedLabel.Left;
    Width := WizardForm.FinishedLabel.Width;
    Top := WizardForm.FinishedLabel.Top + WizardForm.FinishedLabel.Height + ScaleY(8);
    DesktopIconCheck.SetBounds(Left, Top, Width, ScaleY(22));
    LaunchCheck.SetBounds(Left, Top + ScaleY(28), Width, ScaleY(22));
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Exe: String;
  ErrorCode: Integer;
begin
  if CurStep <> ssDone then
    Exit;

  // Runs after Finish. Silent installs keep the defaults: desktop shortcut yes, launch no.
  Exe := ExpandConstant('{app}\{#AppExe}');
  if DesktopIconCheck.Checked then
    CreateShellLink(ExpandConstant('{autodesktop}\{#AppName}.lnk'), '', Exe, '',
      ExpandConstant('{app}'), Exe, 0, SW_SHOWNORMAL);
  if LaunchCheck.Checked and not WizardSilent then
    ShellExecAsOriginalUser('', Exe, '', ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;
