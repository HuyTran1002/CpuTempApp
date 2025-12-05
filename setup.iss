[Setup]
AppName=CPU Temp Monitor
AppVersion=2.0.0
AppPublisher=CPU Temp
AppPublisherURL=https://github.com
AppSupportURL=https://github.com
AppUpdatesURL=https://github.com
DefaultDirName={pf}\CpuTempMonitor
DefaultGroupName=CPU Temp Monitor
AllowNoIcons=yes
OutputDir=D:\Program Files\Code\CpuTempApp\Output
OutputBaseFilename=CpuTempSetup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
SetupIconFile=D:\Program Files\Code\CpuTempApp\temperature_icon_175973.ico
UninstallIconFile=D:\Program Files\Code\CpuTempApp\temperature_icon_175973.ico
DisableProgramGroupPage=no
DisableFinishedPage=no
ShowLanguageDialog=no
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Full installation"
Name: "compact"; Description: "Compact installation"

[Components]
Name: "app"; Description: "CPU Temp Monitor Application"; Types: full compact; Flags: fixed

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode
Name: "startup"; Description: "Start CPU Temp Monitor at Windows startup"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main application executable
Source: "D:\Program Files\Code\CpuTempApp\bin\Release\net7.0-windows\CpuTempApp.exe"; DestDir: "{app}"; Flags: ignoreversion; Components: app
Source: "D:\Program Files\Code\CpuTempApp\bin\Release\net7.0-windows\CpuTempApp.dll"; DestDir: "{app}"; Flags: ignoreversion; Components: app

; Runtime config and dependencies
Source: "D:\Program Files\Code\CpuTempApp\bin\Release\net7.0-windows\*.json"; DestDir: "{app}"; Flags: ignoreversion; Components: app
Source: "D:\Program Files\Code\CpuTempApp\bin\Release\net7.0-windows\*.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion; Components: app
Source: "D:\Program Files\Code\CpuTempApp\bin\Release\net7.0-windows\*.deps.json"; DestDir: "{app}"; Flags: ignoreversion; Components: app

; All DLL files (framework and dependencies)
Source: "D:\Program Files\Code\CpuTempApp\bin\Release\net7.0-windows\*.dll"; DestDir: "{app}"; Flags: ignoreversion; Components: app
Source: "D:\Program Files\Code\CpuTempApp\bin\Release\net7.0-windows\runtimes\*"; DestDir: "{app}\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: app

; Application icon
Source: "D:\Program Files\Code\CpuTempApp\temperature_icon_175973.ico"; DestDir: "{app}"; Flags: ignoreversion; Components: app

[Icons]
Name: "{group}\CPU Temp Monitor"; Filename: "{app}\CpuTempApp.exe"; IconFilename: "{app}\temperature_icon_175973.ico"; IconIndex: 0
Name: "{group}\{cm:UninstallProgram,CPU Temp Monitor}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\CPU Temp Monitor"; Filename: "{app}\CpuTempApp.exe"; IconFilename: "{app}\temperature_icon_175973.ico"; IconIndex: 0; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\CPU Temp Monitor"; Filename: "{app}\CpuTempApp.exe"; IconFilename: "{app}\temperature_icon_175973.ico"; IconIndex: 0; Tasks: quicklaunchicon

[Run]
Filename: "{app}\CpuTempApp.exe"; Description: "{cm:LaunchProgram,CPU Temp Monitor}"; Flags: nowait postinstall skipifsilent shellexec runasoriginaluser

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CpuTempMonitor"; ValueData: "{app}\CpuTempApp.exe"; Flags: uninsdeletevalue; Tasks: startup

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
Type: dirifempty; Name: "{app}"

[Code]
function IsDotNetInstalled(): Boolean;
var
  DotNetPath: String;
begin
  Result := False;
  
  // Check C:\Program Files first
  DotNetPath := 'C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\7.0.20';
  
  if DirExists(DotNetPath) then
  begin
    Result := True;
    Exit;
  end;
  
  // Try Program Files (x86) for 32-bit
  DotNetPath := 'C:\Program Files (x86)\dotnet\shared\Microsoft.WindowsDesktop.App\7.0.20';
  if DirExists(DotNetPath) then
  begin
    Result := True;
    Exit;
  end;
  
  // Last resort: check any 7.x version in Program Files
  DotNetPath := 'C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App';
  if DirExists(DotNetPath + '\7.0.0') or 
     DirExists(DotNetPath + '\7.0.1') or 
     DirExists(DotNetPath + '\7.0.2') or 
     DirExists(DotNetPath + '\7.0.3') or 
     DirExists(DotNetPath + '\7.0.4') or 
     DirExists(DotNetPath + '\7.0.5') or 
     DirExists(DotNetPath + '\7.0.10') or 
     DirExists(DotNetPath + '\7.0.11') or 
     DirExists(DotNetPath + '\7.0.12') or 
     DirExists(DotNetPath + '\7.0.13') or 
     DirExists(DotNetPath + '\7.0.14') or 
     DirExists(DotNetPath + '\7.0.15') or 
     DirExists(DotNetPath + '\7.0.16') or 
     DirExists(DotNetPath + '\7.0.17') or 
     DirExists(DotNetPath + '\7.0.18') or 
     DirExists(DotNetPath + '\7.0.19') or 
     DirExists(DotNetPath + '\7.0.20') then
  begin
    Result := True;
  end;
end;

procedure InitializeWizard();
var
  ResultCode: Integer;
  ErrorCode: Integer;
begin
  if not IsDotNetInstalled() then
  begin
    MsgBox('.NET 7.0 Desktop Runtime chưa được cài đặt trên máy tính này.' + #13#10 + 
           'Bạn cần cài đặt nó trước khi tiếp tục.' + #13#10#13#10 + 
           'Vui lòng tải và cài đặt .NET 7.0 Desktop Runtime, sau đó chạy lại bộ cài này.', 
           mbError, MB_OK);
    ShellExec('open', 'https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-7.0.20-windows-x64-installer', '', '', SW_SHOW, ewNoWait, ErrorCode);
    Abort();
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    // Pre-installation checks can be added here if needed
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Cleanup code if needed
end;