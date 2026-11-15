#define AppVersion "3.0.0"
#define Root "..\.."
[Setup]
AppId={{093C03B0-4538-4DD4-BB72-19FA0F41CEAF}
AppName=AIEnter
AppVersion={#AppVersion}
AppPublisher=AIEnter
AppPublisherURL=https://github.com/bin-art-666/AIEnter
DefaultDirName={localappdata}\Programs\AIEnter
DefaultGroupName=AIEnter
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#Root}\.build\release-v{#AppVersion}
OutputBaseFilename=AIEnter-v{#AppVersion}-Setup-win-x64
SetupIconFile={#Root}\dist\Assets\AIEnter.ico
UninstallDisplayIcon={app}\AIEnter.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
InfoBeforeFile=install-info.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked
Name: "autostart"; Description: "登录 Windows 时启动 AIEnter 任务栏图标"; Flags: unchecked

[Files]
Source: "{#Root}\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Root}\.build\installer-payload\*"; DestDir: "{app}\TaskbarRuntime\AppData\Engine\Mods\64"; Flags: ignoreversion
Source: "{#Root}\tools\taskbar-ai\aienter-taskbar-ai.wh.cpp"; DestDir: "{app}\Taskbar"; Flags: ignoreversion
Source: "Start.vbs"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\docs\release-usage.md"; DestDir: "{app}"; DestName: "使用说明.md"; Flags: ignoreversion

[Icons]
Name: "{group}\AIEnter"; Filename: "{sys}\wscript.exe"; Parameters: """{app}\Start.vbs"""; IconFilename: "{app}\Assets\AIEnter.ico"
Name: "{autodesktop}\AIEnter"; Filename: "{sys}\wscript.exe"; Parameters: """{app}\Start.vbs"""; IconFilename: "{app}\Assets\AIEnter.ico"; Tasks: desktopicon
Name: "{group}\卸载 AIEnter"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AIEnterTaskbar"; ValueData: """{app}\TaskbarRuntime\windhawk.exe"" -tray-only"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{sys}\wscript.exe"; Parameters: """{app}\Start.vbs"""; Description: "启动 AIEnter 和任务栏图标"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\TaskbarRuntime\windhawk.exe"; Parameters: "-exit -wait"; RunOnceId: "StopAIEnterTaskbar"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{app}\AIEnter.exe"; Parameters: "--quit"; RunOnceId: "StopAIEnter"; Flags: runhidden waituntilterminated skipifdoesntexist

[UninstallDelete]
Type: filesandordirs; Name: "{app}\TaskbarRuntime"

[Code]
var DownloadPage: TDownloadWizardPage;

function InitializeSetup(): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage('下载运行组件', '首次安装需要联网，下载文件会校验 SHA256。', nil);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ExitCode: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\TaskbarRuntime\windhawk.exe')) then
    if not Exec(ExpandConstant('{app}\TaskbarRuntime\windhawk.exe'), '-exit -wait', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      Result := '无法停止 AIEnter 任务栏组件，请退出 AIEnter 后重试。';
  if FileExists(ExpandConstant('{app}\AIEnter.exe')) then
    if not Exec(ExpandConstant('{app}\AIEnter.exe'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      Result := '无法停止 AIEnter，请退出后重试。';
  if Result <> '' then Exit;
  DownloadPage.Clear;
  DownloadPage.Add('https://github.com/ramensoftware/windhawk/releases/download/v1.7.3/windhawk_setup_offline.exe',
    'windhawk_setup.exe', '4d93016570f982326eebdfc9068e924ff21f6448534ea32672bb4c1d52a8193b');
  DownloadPage.Show;
  try
    try DownloadPage.Download;
    except Result := '运行组件下载失败，请检查网络后重试：' + GetExceptionMessage; end;
  finally DownloadPage.Hide; end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer; RuntimePath, Config: String;
begin
  if CurStep <> ssPostInstall then Exit;
  RuntimePath := ExpandConstant('{app}\TaskbarRuntime');
  if not Exec(ExpandConstant('{tmp}\windhawk_setup.exe'), '/portable /S /D=' + RuntimePath,
      '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    RaiseException('任务栏组件安装未完成，请重新运行安装程序。');
  if not FileExists(RuntimePath + '\Engine\1.7.3\64\windhawk.dll') then
    RaiseException('未找到任务栏引擎，安装未完成。');
  Config := '[Mod]' + #13#10 + 'LibraryFileName=aienter-taskbar-ai.dll' + #13#10 +
    'Disabled=0' + #13#10 + 'LoggingEnabled=0' + #13#10 + 'Include=explorer.exe' + #13#10 +
    'Architecture=x86-64' + #13#10 + 'Version={#AppVersion}' + #13#10 +
    '[Settings]' + #13#10 + 'executable=' + ExpandConstant('{app}\AIEnter.exe') + #13#10;
  if not SaveStringToFile(RuntimePath + '\AppData\Engine\Mods\aienter-taskbar-ai.ini', Config, False) then
    RaiseException('无法配置任务栏入口，请重新运行安装程序。');
end;


function PsLiteral(Value: String): String;
begin
  StringChangeEx(Value, #39, #39 + #39, True);
  Result := #39 + Value + #39;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var RuntimePath, CleanupDir, CleanupScript, ScriptText: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  RuntimePath := ExpandConstant('{app}\TaskbarRuntime');
  if not DirExists(RuntimePath) then Exit;
  CleanupDir := ExpandConstant('{localappdata}\AIEnter-UninstallCleanup');
  ForceDirectories(CleanupDir);
  CleanupScript := CleanupDir + '\cleanup.ps1';
  if not SaveStringToFile(RuntimePath + '\.aienter-uninstall', '093C03B0-4538-4DD4-BB72-19FA0F41CEAF', False) then Exit;
  ScriptText := '$ErrorActionPreference = '+PsLiteral('Stop')+#13#10+
    '$target = '+PsLiteral(RuntimePath)+#13#10+
    'if ([IO.Path]::GetFullPath($target) -ne $target -or [IO.Path]::GetFileName($target) -ne '+PsLiteral('TaskbarRuntime')+') { exit 1 }'+#13#10+
    'if (Test-Path -LiteralPath $target) {'+#13#10+
    '  $item = Get-Item -LiteralPath $target -Force'+#13#10+
    '  if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { exit 1 }'+#13#10+
    '  $marker = Join-Path $target '+PsLiteral('.aienter-uninstall')+#13#10+
    '  if (!(Test-Path -LiteralPath $marker) -or [IO.File]::ReadAllText($marker) -ne '+PsLiteral('093C03B0-4538-4DD4-BB72-19FA0F41CEAF')+') { exit 1 }'+#13#10+
    '  Remove-Item -LiteralPath $target -Recurse -Force'+#13#10+
    '}'+#13#10+
    '[IO.File]::Delete($PSCommandPath)'+#13#10;
  if SaveStringToFile(CleanupScript, ScriptText, False) then
    RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\RunOnce', 'AIEnterUninstallCleanup',
      '"' + ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe') + '" -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + CleanupScript + '"');
end;
