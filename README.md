# Aska Mod Manager

![GitHub release (latest by date)](https://img.shields.io/github/v/release/YelenaTor/Aska-ModLoader)
![License](https://img.shields.io/github/license/YelenaTor/Aska-ModLoader)
![Build Status](https://img.shields.io/badge/build-passing-brightgreen)
![Platform](https://img.shields.io/badge/platform-windows-blue)

**The essential companion for modding ASKA.**

Aska Mod Manager provides a safe, modern, and reliable way to discover, install, and manage mods for ASKA. Built with robustness in mind, it handles complex dependencies, validates your runtime environment, and prevents common crashes before they happen.

---

## [ Features ]

### [ Core Management ]
- **One-Click Install**: Install mods directly from Thunderstore or local ZIP files.
- **Smart Dependencies**: Automatically resolves and installs all required dependencies recursively.
- **Safe Management**: Enable, disable, or uninstall mods without breaking your game.
- **Update Tracking**: Automatically checks for updates and notifies you of new versions.

### [ Safety & Stability ]
- **Crash Rollback**: Detects boot loops and offers to disable the problematic mod automatically.
- **Runtime Validation**: Verifies BepInEx integrity and repairs critical files like `0Harmony.dll`.
- **Conflict Detection**: Warns you about known incompatibilities between mods.
- **Atomic Operations**: Installations are transactional—no partial or corrupted installs.

### [ improved Experience ]
- **Game Launcher**: Launch ASKA directly with optional "Close on Launch" behavior.
- **Diagnostics**: Built-in log viewer and "Copy Diagnostic Bundle" for easy support.
- **Modern UI**: Clean, dark-themed interface powered by MahApps.Metro.

---

## [ Installation ]

1. **Download**: Grab the latest release from the [Releases page](https://github.com/YelenaTor/Aska-ModLoader/releases).
2. **Extract**: Unzip the contents to a **dedicated folder** (e.g., `C:\AskaModManager`).
   > **IMPORTANT**: Do NOT install inside the game folder or `BepInEx` folder.
3. **Run**: Launch `ModManager.DesktopUI.exe`.
4. **Setup**: Point the manager to your ASKA installation directory when prompted.

---

## [ Building from Source ]

### Prerequisites
- Windows 10/11
- .NET 8.0 SDK
- Git

### Instructions
```bash
git clone https://github.com/YelenaTor/Aska-ModLoader.git
cd Aska-ModLoader
dotnet build src/ModManager.DesktopUI
dotnet run --project src/ModManager.DesktopUI/ModManager.DesktopUI.csproj
```

---

## [ Roadmap ]

- [x] **Phase 1**: Production Hardening (Atomic installs, Race safety)
- [x] **Phase 2**: Observability (Error tracking, Diagnostics)
- [x] **Phase 3**: UX Refinement (Conflict dialogs, Progress status)
- [x] **Phase 4**: Thunderstore Integration (Discovery, Remote install)
- [x] **Phase 5**: Distribution (Game Launcher, Recursive Deps)
- [ ] **Phase 6**: Advanced (Profiles, One-Click Website Install)

See [roadmap.md](roadmap.md) for detailed progress.

---

## [ Contributing ]

We welcome contributions! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for details on how to get started on the `master` branch.

## [ License ]

This project is licensed under the [BSD 3-Clause License](LICENSE).

## [ Acknowledgments ]

- **BepInEx Team** for the incredible modding framework.
- **Thunderstore** for the API and hosting.
- **ASKA Community** for testing and feedback.
