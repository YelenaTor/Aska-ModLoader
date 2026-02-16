# Aska Mod Manager

Aska Mod Manager is a WIP BepInEx runtime validator and desktop management experience for the game **ASKA**. The current milestone focuses on:

- Deterministic BepInEx runtime validation
- Manual game-path configuration with logging
- Dependency-aware mod enable/disable
- Safe mod installation with rollback on failure
- Desktop UI (WPF/MVVM) for managing installs

Future milestones will bring full mod repository integration, runtime toggles, and advanced UX.

## Features (Current Phase)

- **Runtime Validation**: Checks ASKA executable, BepInEx core DLL, loader files, and plugin directory state before enabling mod management.
- **Manual Game Path Resolution**: Users can point the manager to any ASKA install; Windows-only.
- **BepInEx Bootstrap**: Guided installation flow with confirmation and status logging.
- **Mod Lifecycle Foundations**:
  - Install from ZIP with manifest validation, duplicate detection, and rollback safety.
  - Enable/disable operations honor dependency requirements and file backups.
  - Uninstall guards against removing mods that have dependents.
- **Desktop UI**: WPF front-end using MVVM + MahApps.Metro, featuring a dedicated Diagnostics tab for troubleshooting.
- **Observability & Diagnostics (Phase 2)**:
  - **Persistent Error Tracking**: All runtime errors are captured and stored in LiteDB, surviving application restarts.
  - **Smart Mod Attribution**: Automatically attributes BepInEx runtime crashes to specific mods using assembly matching logic.
  - **Diagnostic Bundles**: One-click generation of JSON diagnostic bundles for easy support and remote troubleshooting.
  - **Platform Hardening**: Includes game process detection and file lock retry logic to prevent data corruption.

## Project Structure

```
src/
  ModManager.Core/        # Business logic, dependency validation, runtime services
  ModManager.DesktopUI/   # WPF application (MVVM, Serilog logging)
  ModManager.BepInExPlugin/ (placeholder for future in-game integration)
```

## Getting Started

### Prerequisites

- Windows 10/11
- .NET 8.0 SDK
- ASKA installed via Steam
- BepInEx IL2CPP build (installed via manager or manually)

### Build Instructions

```bash
git clone https://github.com/YelenaTor/Aska-ModLoader.git
cd Aska-ModLoader
dotnet build src/ModManager.DesktopUI
```

### Running

```bash
dotnet run --project src/ModManager.DesktopUI/ModManager.DesktopUI.csproj
```

When prompted, configure your ASKA path and install BepInEx if needed.

## Contributing

We welcome contributions! See [CONTRIBUTING.md](CONTRIBUTING.md) for environment setup, commit conventions, and PR workflow.

## License

This project is licensed under the BSD 3-Clause License. See [LICENSE](LICENSE) for details.

## Acknowledgments

- BepInEx team for the Unity/IL2CPP modding framework
- Serilog, CommunityToolkit.Mvvm, and MahApps.Metro contributors
- Early ASKA modding community for feedback and test cases
