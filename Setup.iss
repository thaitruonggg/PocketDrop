[Setup]
; App Information
AppName=PocketDrop
AppVersion=1.0.0
AppPublisher=Naofunyan
AppCopyright=Copyright (C) 2026 Naofunyan

; 1. Changed to Relative Path
SetupIconFile=Assets\PocketDrop.ico

DisableWelcomePage=no

; 2. Changed to Relative Paths
WizardImageFile=Assets\GithubBanner.bmp
WizardSmallImageFile=Assets\PocketDrop.bmp

; 3. Changed to Relative Path (Note: Check if you saved this as .txt or .rtf!)
LicenseFile=Assets\License.txt

; Where it installs by default
DefaultDirName={autopf}\PocketDrop
DisableProgramGroupPage=yes

; 4. Changed to a generic "Output" folder so GitHub Actions can find it
OutputDir=Output
OutputBaseFilename=PocketDrop_Setup_x64_1.0.0

; Makes the installer smaller and faster
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

ArchitecturesInstallIn64BitMode=x64compatible


[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 5. Changed to match the GitHub Actions output folder!
; We grab everything inside the 'publish' folder just in case there are extra DLLs
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\PocketDrop"; Filename: "{app}\PocketDrop.exe"
Name: "{autodesktop}\PocketDrop"; Filename: "{app}\PocketDrop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PocketDrop.exe"; Description: "{cm:LaunchProgram,PocketDrop}"; Flags: nowait postinstall skipifsilent