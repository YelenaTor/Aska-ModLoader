# Changelog

All notable changes to the **Aska Mod Manager** project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.5.0] - 2024-02-17

### Added
- **Thunderstore Integration**: Browse, filter, and install mods directly from Thunderstore.io community repository.
- **Recursive Dependency Installation**: Automatically resolving and installing required dependencies for any mod.
- **Game Launcher**: Direct "Play" button with option to auto-close manager on launch.
- **Safety Features**:
  - **Crash Rollback**: Detects repeated crashes and offers to disable the last enabled mod.
  - **HarmonyX Repair**: Validates and repairs corrupted `0Harmony.dll`.
  - **Incompatibility Detection**: Warns users about known conflicting mods.
- **UI Improvements**: New dashboard layout, sort/filter controls, and enhanced diagnostics panel.

### Changed
- Refactored `ModRepository` to support complex dependency graphs.
- Updated `DiscoveryService` to use real live data instead of mocks.
- Improved error handling for network operations and file conflicts.

### Fixed
- Resolved "Entry point not found: winhttp.dll" crash caused by BepInEx proxy conflict.
- Fixed circular dependency handling during installation.

## [0.4.0] - 2024-01-20

### Added
- Basic mod management (Enable/Disable/Uninstall).
- Local ZIP installation support.
- BepInEx runtime validation.
- Diagnostics tab with log export.

### Changed
- Migrated UI to MahApps.Metro for modern look and feel.
