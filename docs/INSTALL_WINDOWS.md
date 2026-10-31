# Install or run Cubase Project Explorer on Windows 11

This is an **unpackaged, unsigned Uno Platform Desktop application**, **not** a signed MSIX/MSI.
The installer script performs a local folder installation and Start Menu shortcut
only; it does not require administrator rights or modify Cubase.

## Download

Open the latest successful [Windows CI](https://github.com/AlexanderSeel/CubaseProjectExplorer/actions/workflows/windows-ci.yml)
run and under **Artifacts** download `CubaseProjectExplorer-Uno-Desktop-win-x64`.
Extract the ZIP fully before running anything.

## Portable launch

Run `CubaseInspector.App.exe` inside the extracted folder. If Windows cannot
find the executable at the ZIP root, look inside its `win-x64` subfolder.
**Do not copy only the EXE**: adjacent runtime and component files are required.

## Local install (optional)

Open PowerShell from the extracted ZIP folder:

```powershell
# Validates package without changing your computer
.\Install-CubaseProjectExplorer.ps1 -CheckOnly

# Installs into %LOCALAPPDATA%\Programs\CubaseProjectExplorer, without admin rights
.\Install-CubaseProjectExplorer.ps1

# Uninstalls this app and its Start Menu shortcut only
.\Install-CubaseProjectExplorer.ps1 -Uninstall
```

The script refuses to overwrite existing unrecognized directories or perform
an in-place update. Uninstall before replacing an earlier version. It deletes
only a marked app-installation directory and its own Start Menu shortcut;
it never touches Cubase files or DAW projects.

## Requirements and limitations

- Windows 11 x64.
- Matching **Microsoft Windows App SDK runtime** must be available for this
  unpackaged Uno Desktop build. An error about a missing runtime is a prerequisite
  issue, not a CPR parser failure. The script deliberately does not download
  or install runtime software.
- No executable signing or SmartScreen reputation is provided.
- GUI design/DPI/accessibility **manual visual acceptance has not been completed**.
- Native CPR scanning is read-only and candidate-only where evidence is
  unconfirmed. Plugin deactivation in CPR files is not implemented.
