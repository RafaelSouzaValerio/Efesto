#define MyAppName "Efesto"
#define MyAppPublisher "Callistto"
#define MyAppExeName "Efesto.exe"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define SourceDir "..\artifacts\Efesto-minimal"

[Setup]
AppId={{E4A6B8F7-3F61-4D2F-9D2C-7B5F2D9A18C4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Efesto
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\artifacts\installer
OutputBaseFilename=Efesto-Setup-{#MyAppVersion}
SetupIconFile=..\src\Efesto\Assets\efesto.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} - Compilador de aplicações
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos adicionais:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Executar o Efesto"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; A configuração do usuário fica em %LOCALAPPDATA%\ApplicationCompiler.WinUI e é preservada para compatibilidade.
Type: filesandordirs; Name: "{app}"
