; 1. Listen for the architecture passed from GitHub (Defaults to x64 for local testing)
#ifndef MyAppArch
  #define MyAppArch "x64"
#endif

; Define your version once here, and it updates everywhere!
#define MyAppVersion "1.1.1"

[Setup]
; App Information
AppName=PocketDrop
AppVersion={#MyAppVersion}
AppPublisher=Naofunyan
AppCopyright=Copyright (C) 2026 Naofunyan
UninstallDisplayIcon={app}\PocketDrop.exe

; Detects the running app via its named mutex and closes it gracefully before install/uninstall
AppMutex=PocketDropSingleInstanceMutex
CloseApplications=yes

DisableDirPage=no
DisableWelcomePage=no

; 2. Changed to Relative Paths
SetupIconFile=PocketDrop\Assets\PocketDrop.ico
WizardImageFile=PocketDrop\Assets\GithubBanner.bmp
WizardSmallImageFile=PocketDrop\Assets\PocketDrop.bmp

; 3. Changed to Relative Path (Note: Check if you saved this as .txt or .rtf!)
LicenseFile=PocketDrop\Assets\License.txt

; Where it installs by default
DefaultDirName={autopf}\PocketDrop
DisableProgramGroupPage=yes

; 4. Dynamically protect the installer based on the architecture
#if MyAppArch == "arm64"
  ArchitecturesAllowed=arm64
  ArchitecturesInstallIn64BitMode=arm64
#elif MyAppArch == "x64"
  ArchitecturesAllowed=x64 arm64
  ArchitecturesInstallIn64BitMode=x64 arm64
#else
  ArchitecturesAllowed=x86 x64 arm64
#endif

; 5. Dynamically name the output file using our variables
OutputDir=Output
OutputBaseFilename=PocketDrop_Setup_{#MyAppArch}_{#MyAppVersion}

; Makes the installer smaller and faster
Compression=zip
SolidCompression=no
WizardStyle=modern

[Files]
; This tells Inno Setup to grab the 40MB+ file from the GitHub build folder
Source: "GH_Publish\PocketDrop.exe"; DestDir: "{app}"; Flags: ignoreversion

[Tasks]
; Creates a checkbox on the "Select Additional Tasks" page of the installer
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Icons]
; Creates the Start Menu shortcut
Name: "{autoprograms}\PocketDrop"; Filename: "{app}\PocketDrop.exe"
; Creates the Desktop shortcut (if the user checked the box)
Name: "{autodesktop}\PocketDrop"; Filename: "{app}\PocketDrop.exe"; Tasks: desktopicon

[Registry]
; Register PocketDrop to run silently at Windows startup
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "PocketDrop"; \
    ValueData: """{app}\PocketDrop.exe"" -startup"; \
    Flags: uninsdeletevalue

; Only set the welcome screen flag to False if it doesn't already exist
; This prevents the welcome screen from annoying existing users during an update
Root: HKCU; Subkey: "Software\PocketDrop"; \
    ValueType: string; ValueName: "HasSeenWelcome"; \
    ValueData: "False"; \
    Flags: createvalueifdoesntexist uninsdeletevalue

[Run]
Filename: "{app}\PocketDrop.exe"; Description: "{cm:LaunchProgram,PocketDrop}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; This forcefully deletes the entire PocketDrop folder and everything inside it when the user uninstalls.
Type: filesandordirs; Name: "{app}"