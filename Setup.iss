; 1. Listen for the architecture passed from GitHub (Defaults to x64 for local testing)
#ifndef MyAppArch
  #define MyAppArch "x64"
#endif

; Define your version once here, and it updates everywhere!
#define MyAppVersion "1.0.0"

[Setup]
; App Information
AppName=PocketDrop
AppVersion={#MyAppVersion}
AppPublisher=Naofunyan
AppCopyright=Copyright (C) 2026 Naofunyan

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
Source: "PublishOutput\PocketDrop.exe"; DestDir: "{app}"; Flags: ignoreversion

[Tasks]
; Creates a checkbox on the "Select Additional Tasks" page of the installer
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Icons]
; Creates the Start Menu shortcut
Name: "{autoprograms}\PocketDrop"; Filename: "{app}\PocketDrop.exe"
; Creates the Desktop shortcut (only if the user checked the box)
Name: "{autodesktop}\PocketDrop"; Filename: "{app}\PocketDrop.exe"; Tasks: desktopicon