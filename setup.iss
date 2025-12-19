[Setup]
AppName=CPU Temp Monitor
AppVersion=1.1.6
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

// Hàm tìm uninstall string từ registry
function GetUninstallString(): String;
var
  UninstallPath: String;
begin
  Result := '';
  
  // Tìm uninstall string cho Inno Setup app (thử nhiều registry key)
  // Key có thể là AppName hoặc AppName_is1
  if RegQueryStringValue(HKLM, 
       'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}', 
       'UninstallString', UninstallPath) then
  begin
    Result := UninstallPath;
    Exit;
  end;
  
  // Fallback: thử key với _is1
  if RegQueryStringValue(HKLM, 
       'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\CPU Temp Monitor_is1', 
       'UninstallString', UninstallPath) then
  begin
    Result := UninstallPath;
    Exit;
  end;
  
  // Thử trong HKCU nếu cài per-user
  if RegQueryStringValue(HKCU, 
       'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}', 
       'UninstallString', UninstallPath) then
  begin
    Result := UninstallPath;
  end;
end;

// Kiểm tra xem app đã cài đặt chưa
function IsAppAlreadyInstalled(): Boolean;
var
  InstallPath: String;
  UninstallStr: String;
begin
  Result := False;
  
  // Kiểm tra registry để lấy đường dẫn cài đặt cũ
  if RegQueryStringValue(HKLM, 
       'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\CPU Temp Monitor_is1', 
       'InstallLocation', InstallPath) then
  begin
    // Nếu tìm thấy registry entry và thư mục tồn tại
    if (InstallPath <> '') and DirExists(InstallPath) then
    begin
      Result := True;
    end;
  end;
  
  // Fallback: kiểm tra đường dẫn cài đặt mặc định
  if not Result then
  begin
    if DirExists(ExpandConstant('{pf}\CpuTempMonitor')) then
    begin
      Result := True;
    end;
  end;
end;

procedure InitializeWizard();
var
  ResultCode: Integer;
  ErrorCode: Integer;
  UninstallResult: Integer;
  UninstallStr: String;
  InstallPath: String;
  IsAutoUpdate: Boolean;
begin
  // Check nếu được gọi từ auto-update (parameter /AUTOUPDATE)
  IsAutoUpdate := False;
  if ParamCount > 0 then
  begin
    if Uppercase(ParamStr(1)) = '/AUTOUPDATE' then
      IsAutoUpdate := True;
  end;

  // Kiểm tra .NET trước
  if not IsDotNetInstalled() then
  begin
    MsgBox('.NET 7.0 Desktop Runtime chưa được cài đặt trên máy tính này.' + #13#10 + 
           'Bạn cần cài đặt nó trước khi tiếp tục.' + #13#10#13#10 + 
           'Vui lòng tải và cài đặt .NET 7.0 Desktop Runtime, sau đó chạy lại bộ cài này.', 
           mbError, MB_OK);
    ShellExec('open', 'https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-7.0.20-windows-x64-installer', '', '', SW_SHOW, ewNoWait, ErrorCode);
    Abort();
  end;
  
  // Kiểm tra xem app đã cài chưa
  if IsAppAlreadyInstalled() then
  begin
    // Nếu là auto-update, tự động gỡ cài đặt không hỏi
    if IsAutoUpdate or 
       (MsgBox('CPU Temp Monitor đã được cài đặt trên máy tính này.' + #13#10#13#10 + 
              'Bạn phải gỡ cài đặt phiên bản cũ trước khi cài đặt phiên bản mới.' + #13#10#13#10 + 
              'Nhấn "Có" để tự động gỡ cài đặt app cũ (Recommended)' + #13#10 + 
              'Nhấn "Không" để hủy cài đặt.', 
              mbConfirmation, MB_YESNO) = IDYES) then
    begin
      // BƯỚC 1: Đóng app nếu đang chạy
      Exec('taskkill.exe', '/F /IM CpuTempApp.exe', '', SW_HIDE, ewWaitUntilTerminated, UninstallResult);
      Sleep(1000); // Đợi 1 giây
      
      // BƯỚC 2: Lấy uninstall string từ registry
      UninstallStr := GetUninstallString();
      
      if UninstallStr <> '' then
      begin
        // Loại bỏ dấu ngoặc kép nếu có
        UninstallStr := RemoveQuotes(UninstallStr);
        
        // Chạy uninstall file với VERYSILENT để gỡ hoàn toàn tự động
        if Exec(UninstallStr, '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES', '', SW_HIDE, ewWaitUntilTerminated, UninstallResult) then
        begin
          Sleep(2000); // Đợi uninstall hoàn tất
          
          if UninstallResult = 0 then
          begin
            if not IsAutoUpdate then
            begin
              MsgBox('Đã gỡ cài đặt app cũ thành công!' + #13#10 + 
                     'Bây giờ sẽ tiếp tục cài đặt phiên bản mới.', 
                     mbInformation, MB_OK);
            end;
            // Tiếp tục cài đặt - không Abort
            Exit;
          end
          else
          begin
            MsgBox('Không thể gỡ cài đặt app cũ (Error code: ' + IntToStr(UninstallResult) + ')' + #13#10 + 
                   'Vui lòng gỡ cài đặt thủ công rồi chạy lại installer.', 
                   mbError, MB_OK);
          end;
        end
        else
        begin
          MsgBox('Không thể chạy uninstaller.' + #13#10 + 
                 'Vui lòng gỡ cài đặt thủ công từ Control Panel.', 
                 mbError, MB_OK);
        end;
      end
      else
      begin
        // Nếu không tìm thấy uninstall string, mở Control Panel
        MsgBox('Không tìm thấy thông tin gỡ cài đặt trong Registry.' + #13#10 + 
               'Vui lòng mở Control Panel > Programs and Features' + #13#10 + 
               'Tìm "CPU Temp Monitor" và gỡ cài đặt thủ công, sau đó chạy lại installer.', 
               mbInformation, MB_OK);
        ShellExec('open', 'appwiz.cpl', '', '', SW_SHOW, ewNoWait, ErrorCode);
      end;
    end;
    
    // Dừng lại và hủy cài đặt
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