[Setup]
AppName=Test
AppVersion=1.0
DefaultDirName={pf}\Test
OutputBaseFilename=test
DisableProgramGroupPage=yes

[Code]
function IsDotNetInstalled(): Boolean;
var
  DotNetPath: String;
begin
  Result := False;
  
  // Simple check: Does the 7.0.20 folder exist?
  DotNetPath := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App\7.0.20');
  MsgBox('Checking path: ' + DotNetPath, mbInformation, MB_OK);
  
  if DirExists(DotNetPath) then
  begin
    MsgBox('Found .NET 7.0.20!', mbInformation, MB_OK);
    Result := True;
    Exit;
  end
  else
  begin
    MsgBox('.NET 7.0.20 NOT found!', mbError, MB_OK);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := IsDotNetInstalled();
  if Result then
    MsgBox('Setup will continue', mbInformation, MB_OK)
  else
    MsgBox('Setup will abort', mbError, MB_OK);
end;
