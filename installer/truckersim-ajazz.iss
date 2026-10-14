; Trucker Simulator para Ajazz StreamDock - instalador
; Compilar con Inno Setup 6 (ver docs/DESARROLLO.md).
; Mantener AppVersion sincronizada con manifest.json y CHANGELOG.md.

#define MyAppName "Trucker Simulator para Ajazz StreamDock"
#define MyAppVersion "3.0"
#define MyAppPublisher "HS-Gaming"
#define PluginId "com.hsgaming.scsmon.sdPlugin"

[Setup]
AppId={{8F3A2B41-7C6E-4A9B-9D2F-TRUCKERSIM01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\TruckerSimAjazz
; Se instala en AppData del usuario + carpetas de los juegos: no necesita
; carpeta de programa real, pero pedimos admin para escribir en Steam.
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
OutputDir=Output
OutputBaseFilename=TruckerSim-Ajazz-Setup-{#MyAppVersion}
WizardStyle=modern
ShowLanguageDialog=no
LanguageDetectionMethod=none

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Messages]
spanish.BeveledLabel=HS-Gaming

[Types]
Name: "full"; Description: "Completa"

[Components]
Name: "plugin"; Description: "Plugin StreamDock (Trucker Simulator)"; Types: full; Flags: fixed
Name: "dll"; Description: "DLL de telemetria en ATS/ETS2"; Types: full; Flags: fixed

; NOTA: el instalador se compila con backend.exe ya generado y
; scs-telemetry.dll colocada en ..\dll\ (ver docs/DESARROLLO.md).

[Files]
; --- Plugin a AppData ---
Source: "..\plugins\{#PluginId}\backend.exe"; DestDir: "{userappdata}\HotSpot\StreamDock\plugins\{#PluginId}"; Components: plugin; Flags: ignoreversion
Source: "..\plugins\{#PluginId}\manifest.json"; DestDir: "{userappdata}\HotSpot\StreamDock\plugins\{#PluginId}"; Components: plugin; Flags: ignoreversion
Source: "..\plugins\{#PluginId}\*.png"; DestDir: "{userappdata}\HotSpot\StreamDock\plugins\{#PluginId}"; Components: plugin; Flags: ignoreversion
Source: "..\plugins\{#PluginId}\propertyinspector\*"; DestDir: "{userappdata}\HotSpot\StreamDock\plugins\{#PluginId}\propertyinspector"; Components: plugin; Flags: ignoreversion recursesubdirs
; --- DLL a cada juego (carpetas elegidas en el asistente) ---
Source: "..\dll\scs-telemetry.dll"; DestDir: "{code:GetATSDllDir}"; Components: dll; Flags: ignoreversion; Check: ShouldInstallATS
Source: "..\dll\scs-telemetry.dll"; DestDir: "{code:GetETS2DllDir}"; Components: dll; Flags: ignoreversion; Check: ShouldInstallETS2

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\HotSpot\StreamDock\plugins\{#PluginId}\scsmon-test"
Type: files; Name: "{userappdata}\HotSpot\StreamDock\plugins\{#PluginId}\backend.log"

[Code]
var
  GameDirPage: TInputDirWizardPage;
  ATSDetected, ETS2Detected: String;

function NormalizePath(P: String): String;
begin
  StringChangeEx(P, '/', '\', True);
  Result := Trim(P);
  while (Length(Result) > 3) and (Copy(Result, Length(Result), 1) = '\') do
    Delete(Result, Length(Result), 1);
end;

function FileContains(FileName, Needle: String): Boolean;
var
  S: AnsiString;
begin
  Result := False;
  if LoadStringFromFile(FileName, S) then
    Result := Pos(Needle, String(S)) > 0;
end;

function ExtractVdfPaths(LibFile: String; Paths: TArrayOfString): TArrayOfString;
var
  Lines: TArrayOfString;
  I, P2: Integer;
  Line, Rest, Val: String;
begin
  Result := Paths;
  if not LoadStringsFromFile(LibFile, Lines) then Exit;
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    Line := Trim(Lines[I]);
    if Pos('"path"', Lowercase(Line)) = 1 then
    begin
      Rest := Trim(Copy(Line, 7, MaxInt));
      if (Length(Rest) >= 2) and (Copy(Rest, 1, 1) = '"') then
      begin
        Delete(Rest, 1, 1);
        P2 := Pos('"', Rest);
        if P2 > 1 then
        begin
          Val := NormalizePath(Copy(Rest, 1, P2 - 1));
          if (Val <> '') and DirExists(Val) then
          begin
            SetArrayLength(Result, GetArrayLength(Result) + 1);
            Result[GetArrayLength(Result) - 1] := Val;
          end;
        end;
      end;
    end;
  end;
end;

function ManifestInstallDir(Lib, AppId: String): String;
var
  Acf: String;
  Lines: TArrayOfString;
  I, P2: Integer;
  Line, Lower, Rest: String;
begin
  Result := '';
  Acf := Lib + '\steamapps\appmanifest_' + AppId + '.acf';
  if not LoadStringsFromFile(Acf, Lines) then Exit;
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    Line := Trim(Lines[I]);
    Lower := Lowercase(Line);
    if Pos('"installdir"', Lower) = 1 then
    begin
      Rest := Trim(Copy(Line, 13, MaxInt));
      if (Length(Rest) >= 2) and (Copy(Rest, 1, 1) = '"') then
      begin
        Delete(Rest, 1, 1);
        P2 := Pos('"', Rest);
        if P2 > 1 then
          Result := Copy(Rest, 1, P2 - 1);
      end;
      Break;
    end;
  end;
end;

function FindGameDir(AppId, CommonName: String): String;
var
  SteamPath, LibFile: String;
  Libs: TArrayOfString;
  I: Integer;
  Sub, Candidate: String;
begin
  Result := '';
  SetArrayLength(Libs, 0);
  if RegQueryStringValue(HKCU, 'Software\Valve\Steam', 'SteamPath', SteamPath) then
  begin
    SteamPath := NormalizePath(SteamPath);
    if DirExists(SteamPath) then
    begin
      SetArrayLength(Libs, 1);
      Libs[0] := SteamPath;
    end;
    LibFile := SteamPath + '\steamapps\libraryfolders.vdf';
    Libs := ExtractVdfPaths(LibFile, Libs);
  end;
  for I := 0 to GetArrayLength(Libs) - 1 do
  begin
    Sub := ManifestInstallDir(Libs[I], AppId);
    if Sub <> '' then
    begin
      Candidate := Libs[I] + '\steamapps\common\' + Sub;
      if DirExists(Candidate) then begin Result := Candidate; Exit; end;
    end;
    Candidate := Libs[I] + '\steamapps\common\' + CommonName;
    if DirExists(Candidate) then begin Result := Candidate; Exit; end;
  end;
end;

procedure InitializeWizard;
begin
  ATSDetected := FindGameDir('270880', 'American Truck Simulator');
  ETS2Detected := FindGameDir('227300', 'Euro Truck Simulator 2');

  GameDirPage := CreateInputDirPage(wpSelectComponents,
    'Carpetas de los juegos',
    'Donde estan instalados ATS y ETS2?',
    'Se detecto Steam automaticamente. Si tu juego NO es de Steam (o esta ' +
    'en otra ruta), pulsa Examinar y elige su carpeta. Deja vacio el juego ' +
    'que no quieras tocar.',
    False, '');
  GameDirPage.Add('American Truck Simulator:');
  GameDirPage.Add('Euro Truck Simulator 2:');
  GameDirPage.Values[0] := ATSDetected;
  GameDirPage.Values[1] := ETS2Detected;
end;

function IsGameDir(Dir: String): Boolean;
begin
  Result := (Dir <> '') and DirExists(Dir) and
    (FileExists(Dir + '\bin\win_x64\eurotrucks2.exe') or
     FileExists(Dir + '\bin\win_x64\amtrucks.exe') or
     FileExists(Dir + '\eurotrucks2.exe') or
     FileExists(Dir + '\amtrucks.exe'));
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = GameDirPage.ID then
  begin
    if (GameDirPage.Values[0] = '') and (GameDirPage.Values[1] = '') then
    begin
      MsgBox('Indica al menos la carpeta de un juego (o vuelve atras para ' +
        'instalar solo el plugin).', mbInformation, MB_OK);
      Result := False;
    end
    else if ((GameDirPage.Values[0] <> '') and not IsGameDir(GameDirPage.Values[0])) or
            ((GameDirPage.Values[1] <> '') and not IsGameDir(GameDirPage.Values[1])) then
    begin
      if MsgBox('Una carpeta no parece un juego valido (sin ejecutable). ' +
        'Continuar de todos modos?', mbConfirmation, MB_YESNO) <> IDYES then
        Result := False;
    end;
  end;
end;

function ShouldInstallATS: Boolean;
begin
  Result := (GameDirPage.Values[0] <> '') and DirExists(GameDirPage.Values[0]);
end;

function ShouldInstallETS2: Boolean;
begin
  Result := (GameDirPage.Values[1] <> '') and DirExists(GameDirPage.Values[1]);
end;

function GetATSDllDir(Param: String): String;
begin
  Result := GameDirPage.Values[0] + '\bin\win_x64\plugins';
end;

function GetETS2DllDir(Param: String): String;
begin
  Result := GameDirPage.Values[1] + '\bin\win_x64\plugins';
end;

procedure CloseStreamDock;
var
  RC: Integer;
begin
  Exec('taskkill.exe', '/F /IM "Stream Dock AJAZZ.exe"', '', SW_HIDE, ewWaitUntilTerminated, RC);
  Exec('taskkill.exe', '/F /IM backend.exe', '', SW_HIDE, ewWaitUntilTerminated, RC);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    CloseStreamDock;
end;
