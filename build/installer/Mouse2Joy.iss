; Inno Setup script for Mouse2Joy
;
; Build via CI:
;   iscc.exe build/installer/Mouse2Joy.iss /DAppVersion=1.2.3 /O=installer-out /F=Mouse2Joy-Setup-1.2.3
;
; The {#AppVersion} macro must be supplied via /DAppVersion=... on the ISCC command line.
; The script expects the published Mouse2Joy.App output to live at ../../publish/
; relative to this .iss file (i.e. <repo>/publish/), produced by:
;   dotnet publish src/Mouse2Joy.App -c Release -p:Version={#AppVersion} -o publish

#ifndef AppVersion
  #error "AppVersion is required. Pass /DAppVersion=x.y.z on the ISCC command line."
#endif

#define AppName       "Mouse2Joy"
#define AppPublisher  "Zanges"
#define AppURL        "https://github.com/Zanges/Mouse2Joy"
#define AppExeName    "Mouse2Joy.exe"
; Stable AppId — do NOT change between releases; Inno uses it for upgrade detection.
#define AppId         "{{0f24b18b-9f60-4dab-a1b3-bf52cb7b35f0}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
LicenseFile=..\..\LICENSE
OutputBaseFilename=Mouse2Joy-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} installer
VersionInfoProductName={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Main executable + bundled native DLL.
; Paths are relative to this .iss file: <repo>/build/installer/Mouse2Joy.iss
; Published artifacts live at <repo>/publish/
Source: "..\..\publish\Mouse2Joy.exe";   DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\publish\interception.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\..\LICENSE";                 DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\THIRD_PARTY_NOTICES.md";  DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "README-FIRST.txt";              DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";        Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";  Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
const
  ViGEmDownloadURL       = 'https://github.com/nefarius/ViGEmBus/releases/latest';
  InterceptionDownloadURL = 'http://www.oblita.com/interception';

var
  ViGEmMissing: Boolean;
  InterceptionMissing: Boolean;
  PrereqPage: TWizardPage;

function IsViGEmInstalled(): Boolean;
var
  ImagePath: string;
begin
  Result := False;
  // ViGEmBus 1.x registers as the "ViGEmBus" service.
  if RegQueryStringValue(HKEY_LOCAL_MACHINE,
       'SYSTEM\CurrentControlSet\Services\ViGEmBus', 'ImagePath', ImagePath) then
  begin
    Result := True;
    exit;
  end;
  // Newer (nefarius) versions sometimes register as "ViGEmBus3".
  if RegQueryStringValue(HKEY_LOCAL_MACHINE,
       'SYSTEM\CurrentControlSet\Services\ViGEmBus3', 'ImagePath', ImagePath) then
  begin
    Result := True;
    exit;
  end;
end;

function IsInterceptionInstalled(): Boolean;
var
  SysDir: string;
begin
  // Interception ships three driver files under %windir%\System32\drivers\:
  //   keyboard.sys, mouse.sys (uppercase filter drivers from oblita.com)
  // We check for the registered upper-filter on the keyboard class as a more
  // reliable signal than a file presence check (those names collide with
  // shipping Windows files).
  Result := False;
  SysDir := ExpandConstant('{sys}');
  // The Interception driver registers the "keyboard" upper class filter on the
  // Keyboard class GUID {4D36E96B-E325-11CE-BFC1-08002BE10318}.
  // A more user-friendly probe: look for the install-interception.exe install
  // marker (HKLM\SOFTWARE\Interception) which the upstream installer creates.
  if RegKeyExists(HKEY_LOCAL_MACHINE, 'SOFTWARE\Interception') then
  begin
    Result := True;
    exit;
  end;
  // Fall back: probe the upper-filter registration.
  if RegValueExists(HKEY_LOCAL_MACHINE,
       'SYSTEM\CurrentControlSet\Control\Class\{4D36E96B-E325-11CE-BFC1-08002BE10318}',
       'UpperFilters') then
  begin
    // We can't easily parse REG_MULTI_SZ in Pascal Script; existence is a weak
    // signal but ok for a "warn but allow" flow.
    Result := True;
    exit;
  end;
end;

procedure OpenViGEmPage(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', ViGEmDownloadURL, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure OpenInterceptionPage(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', InterceptionDownloadURL, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure InitializeWizard();
var
  Lbl: TNewStaticText;
  Btn: TNewButton;
  TopY: Integer;
begin
  ViGEmMissing := not IsViGEmInstalled();
  InterceptionMissing := not IsInterceptionInstalled();

  if (not ViGEmMissing) and (not InterceptionMissing) then
    exit;

  PrereqPage := CreateCustomPage(wpWelcome,
    'Missing prerequisites',
    'Mouse2Joy needs two free drivers to be installed. Installation can continue without them, but the app will not function fully until they are installed.');

  TopY := 0;

  if ViGEmMissing then
  begin
    Lbl := TNewStaticText.Create(PrereqPage);
    Lbl.Parent := PrereqPage.Surface;
    Lbl.Caption := 'ViGEmBus (virtual gamepad driver) was not detected.';
    Lbl.Top := TopY;
    Lbl.Left := 0;
    Lbl.AutoSize := True;

    Btn := TNewButton.Create(PrereqPage);
    Btn.Parent := PrereqPage.Surface;
    Btn.Caption := 'Open ViGEmBus download page';
    Btn.Top := TopY + 20;
    Btn.Left := 0;
    Btn.Width := 240;
    Btn.Height := 25;
    Btn.OnClick := @OpenViGEmPage;

    TopY := TopY + 60;
  end;

  if InterceptionMissing then
  begin
    Lbl := TNewStaticText.Create(PrereqPage);
    Lbl.Parent := PrereqPage.Surface;
    Lbl.Caption := 'Interception (mouse capture driver) was not detected.';
    Lbl.Top := TopY;
    Lbl.Left := 0;
    Lbl.AutoSize := True;

    Btn := TNewButton.Create(PrereqPage);
    Btn.Parent := PrereqPage.Surface;
    Btn.Caption := 'Open Interception download page';
    Btn.Top := TopY + 20;
    Btn.Left := 0;
    Btn.Width := 240;
    Btn.Height := 25;
    Btn.OnClick := @OpenInterceptionPage;

    TopY := TopY + 60;
  end;

  Lbl := TNewStaticText.Create(PrereqPage);
  Lbl.Parent := PrereqPage.Surface;
  Lbl.Caption := 'You can continue installing Mouse2Joy now; the in-app Setup tab will guide you if drivers are still missing on first run. Interception requires a reboot after installing its kernel driver.';
  Lbl.Top := TopY + 10;
  Lbl.Left := 0;
  Lbl.AutoSize := True;
  Lbl.Width := PrereqPage.SurfaceWidth;
  Lbl.WordWrap := True;
end;
