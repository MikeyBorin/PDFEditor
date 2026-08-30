; ============================================================================
; ArtiMax PDF Editor — Inno Setup installer script
; ============================================================================
; Called from publish.ps1 with the version defined on the command line:
;
;     iscc /DAppVersion=1.0.0 installer\ArtiMaxPDFEditor.iss
;
; Requires a completed publish (dist\ArtiMaxPDFEditor-<ver>-win-x64\ present)
; because it just packages that staging folder into a Setup.exe.
;
; Output: dist\ArtiMaxPDFEditor-Setup-<ver>.exe
; ============================================================================

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif

#define AppName         "ArtiMax PDF Editor"
#define AppPublisher    "ArtiMax"
#define AppURL          "https://github.com/MikeyBorin/PDFEditor"
#define AppExeName      "ArtiMaxPDFEditor.exe"
#define SourceDir       "..\dist\ArtiMaxPDFEditor-" + AppVersion + "-win-x64"

[Setup]
; AppId is a stable GUID that identifies THIS product across versions.
; DO NOT change it between releases — Windows uses it to detect upgrades vs new installs.
AppId={{B7C4E8F0-1A2B-4C3D-9E8F-6A5B4C3D2E1F}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\ArtiMax\PDF Editor
DefaultGroupName=ArtiMax
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}
OutputDir=..\dist
OutputBaseFilename=ArtiMaxPDFEditor-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Per-user install by default (no admin prompt). User can pick per-machine at install time.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline dialog
LicenseFile={#SourceDir}\LICENSE
SetupIconFile=..\PDFEditor\Assets\PDFEditor.ico
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut";           GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "associate";   Description: "Register as .pdf handler (Open With)"; GroupDescription: "File associations:";    Flags: unchecked

[Files]
; The self-contained exe (~216 MB) plus its shipping companions.
Source: "{#SourceDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE";       DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\README.md";     DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\Help\*";        DestDir: "{app}\Help";     Flags: ignoreversion recursesubdirs createallsubdirs
; Bundled English OCR training data — optional, only present if publish.ps1 found tessdata to include.
Source: "{#SourceDir}\tessdata\*";    DestDir: "{app}\tessdata"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}";           Filename: "{app}\{#AppExeName}"
Name: "{group}\{#AppName} Help";      Filename: "{app}\Help\help.html"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";     Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Optional file association — only written if user ticks the "associate" task at install.
; Uninstall removes the entire ProgID tree (uninsdeletekey on the root) but leaves the .pdf
; OpenWithProgids entry — Windows tolerates the orphan and it disappears the next time
; anything writes to that key.
Root: HKCU; Subkey: "Software\Classes\ArtiMax.PDFDocument"; ValueType: string; ValueName: ""; ValueData: "PDF Document"; Flags: uninsdeletekey; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\ArtiMax.PDFDocument"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\ArtiMax.PDFDocument\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"",0"; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\ArtiMax.PDFDocument\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "ArtiMax.PDFDocument"; ValueData: ""; Tasks: associate
Root: HKCU; Subkey: "Software\ArtiMaxPDFEditor\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"; Flags: uninsdeletekey; Tasks: associate
Root: HKCU; Subkey: "Software\ArtiMaxPDFEditor\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Free desktop PDF editor by ArtiMax"; Tasks: associate
Root: HKCU; Subkey: "Software\ArtiMaxPDFEditor\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: """{app}\{#AppExeName}"",0"; Tasks: associate
Root: HKCU; Subkey: "Software\ArtiMaxPDFEditor\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pdf"; ValueData: "ArtiMax.PDFDocument"; Tasks: associate
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "ArtiMaxPDFEditor"; ValueData: "Software\ArtiMaxPDFEditor\Capabilities"; Tasks: associate

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
