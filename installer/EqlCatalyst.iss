; Compiled by scripts/build-beta.py with a verified Inno Setup 7.1.0 compiler.
#ifndef Payload
  #error Payload must point to a clean, self-contained publish directory.
#endif
#define ProductId "{A1839104-954F-4E08-9D9E-580C5D944289}"

[Setup]
AppId={{A1839104-954F-4E08-9D9E-580C5D944289}
AppName=EQL Catalyst
AppVersion={#ReleaseVersion}
AppVerName=EQL Catalyst {#ReleaseLabel}
AppPublisher=EQL Catalyst
VersionInfoVersion={#WindowsVersion}
VersionInfoProductVersion={#WindowsVersion}
VersionInfoProductTextVersion={#ReleaseVersion}
DefaultDirName={code:DefaultDirectory}
DefaultGroupName=EQL Catalyst
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=yes
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.17763
WizardStyle=modern
WizardSizePercent=110
SetupIconFile=..\src\EqlTracker.App\Assets\catalyst.ico
UninstallDisplayIcon={app}\EQLTracker.exe
OutputDir=..\dist
OutputBaseFilename=EQL-Catalyst-{#ReleaseVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
AppMutex=Local\EQLCatalyst-AppRunning
SetupMutex=Local\EQLCatalyst-SetupRunning
CloseApplications=no
RestartApplications=no
RestartIfNeededByRun=no
Uninstallable=yes
InfoBeforeFile=..\docs\installer-before.txt
LicenseFile=..\LICENSE
AppPublisherURL=https://github.com/Runelaron/EQL-Catalyst
AppSupportURL=https://github.com/Runelaron/EQL-Catalyst/issues
AppUpdatesURL=https://github.com/Runelaron/EQL-Catalyst/releases

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\EQL Catalyst"; Filename: "{app}\EQLTracker.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\EQL Catalyst"; Filename: "{app}\EQLTracker.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\EQLTracker.exe"; Description: "Open EQL Catalyst"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Registry]
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\{#StringChange(ProductId, "{", "{{")}_is1"; ValueType: string; ValueName: "ReleaseOrder"; ValueData: "{#ReleaseOrder}"

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#ProductId}_is1';

function DefaultDirectory(Param: String): String;
begin
  if IsAdminInstallMode then Result := ExpandConstant('{autopf}\EQL Catalyst')
  else Result := ExpandConstant('{localappdata}\Programs\EQL Catalyst');
end;

function CheckRegistration: String;
var OtherRoot: HKEY; Saved: String; OldVersion, NewVersion: Int64;
begin
  Result := '';
  if IsAdminInstallMode then OtherRoot := HKCU64 else OtherRoot := HKLM64;
  if RegKeyExists(OtherRoot, UninstallKey) then begin
    Result := 'EQL Catalyst is already installed in the other installation scope. Uninstall that copy first, then run Setup again. Character data is retained.'; Exit;
  end;
  if RegQueryStringValue(HKA, UninstallKey, 'ReleaseOrder', Saved) then
    if StrToVersion(Saved, OldVersion) and StrToVersion('{#ReleaseOrder}', NewVersion) then
      if ComparePackedVersion(OldVersion, NewVersion) > 0 then Result := 'A newer EQL Catalyst release is installed. This installer will not downgrade it.';
end;

function InitializeSetup: Boolean;
var Reason: String;
begin
  Reason := CheckRegistration;
  Result := Reason = '';
  if not Result then SuppressibleMsgBox(Reason, mbError, MB_OK, IDOK);
end;

function FileAvailable(Path: String): Boolean;
var Stream: TFileStream;
begin
  Result := True;
  if not FileExists(Path) then Exit;
  try
    Stream := TFileStream.Create(Path, fmOpenReadWrite or fmShareExclusive);
    Stream.Free;
  except
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var OldVersion, NewVersion: Int64;
begin
  Result := CheckRegistration;
  if Result <> '' then Exit;
  if GetPackedVersion(ExpandConstant('{app}\EQLTracker.exe'), OldVersion) then
    if StrToVersion('{#WindowsVersion}', NewVersion) and (ComparePackedVersion(OldVersion, NewVersion) > 0) then begin
      Result := 'The destination contains a newer EQL Catalyst executable. Choose another folder or use the newer installer.'; Exit;
    end;
  if not FileAvailable(ExpandConstant('{app}\EQLTracker.exe')) or not FileAvailable(ExpandConstant('{app}\EQLTracker.dll')) or not FileAvailable(ExpandConstant('{app}\EqlTracker.Core.dll')) then
    Result := 'Close EQL Catalyst from its tray menu, finish any Save/Discard/Cancel prompt, and retry. Setup will not force the application to close or replace locked files.';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var Destination, DataRoot: String;
begin
  Result := True;
  if CurPageID <> wpSelectDir then Exit;
  Destination := AddBackslash(ExpandFileName(WizardDirValue));
  DataRoot := AddBackslash(ExpandConstant('{localappdata}\EQLTracker'));
  if (CompareText(Copy(Destination, 1, Length(DataRoot)), DataRoot) = 0) or
     (CompareText(Copy(DataRoot, 1, Length(Destination)), Destination) = 0) or
     FileExists(AddBackslash(WizardDirValue) + 'eqgame.exe') then begin
    SuppressibleMsgBox('Choose an application folder separate from character data and the EverQuest game folder.', mbError, MB_OK, IDOK); Result := False;
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := FileAvailable(ExpandConstant('{app}\EQLTracker.exe')) and FileAvailable(ExpandConstant('{app}\EQLTracker.dll')) and FileAvailable(ExpandConstant('{app}\EqlTracker.Core.dll'));
  if not Result then SuppressibleMsgBox('Close EQL Catalyst from its tray menu, finish any unsaved-build prompt, and run Uninstall again. Character data will be kept.', mbError, MB_OK, IDOK);
end;
