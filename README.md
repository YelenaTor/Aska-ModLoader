# Aska Mod Manager

A comprehensive BepInEx-based mod manager for Aska, providing both an external desktop application and an in-game plugin for seamless mod management.

## Features

- **External Desktop Application**: Modern WPF interface for mod installation, updates, and management
- **In-Game Plugin**: Unity-based UI for real-time mod information and configuration
- **Thunderstore Integration**: Browse and install mods directly from Thunderstore
- **Profile Management**: Switch between different mod configurations
- **Dependency Resolution**: Automatic handling of mod dependencies
- **Crash Diagnostics**: Advanced log analysis and crash reporting
- **Load Order Control**: Manage mod loading sequence
- **Safety Features**: Backup, rollback, and integrity verification

## Architecture

The solution consists of three main projects:

- **ModManager.Core**: Business logic, manifest handling, and data management
- **ModManager.DesktopUI**: WPF desktop application with MVVM architecture
- **ModManager.BepInExPlugin**: In-game Unity plugin for runtime integration

## Getting Started

### Prerequisites

- .NET 8.0 SDK
- Windows 10/11
- Aska game installation
- BepInEx IL2CPP build for Aska

### Building

```bash
git clone https://github.com/your-org/AskaModManager.git
cd AskaModManager
dotnet build
```

### Installation

1. Install BepInEx into your Aska game folder
2. Run the ModManager.DesktopUI installer
3. Point the manager to your Aska installation
4. Start managing mods!

## Development

### Project Structure

```
/src/
  ModManager.Core/           # Core business logic
  ModManager.DesktopUI/      # WPF desktop application  
  ModManager.BepInExPlugin/  # In-game Unity plugin
/docs/                       # Documentation
/samples/                    # Sample mods and test data
/tools/                      # Development and testing tools
```

### Technology Stack

- **.NET 8.0**: Modern framework with LTS support
- **WPF with MahApps.Metro**: Modern Windows UI framework
- **LiteDB**: Lightweight NoSQL database for metadata
- **Mono.Cecil**: Assembly inspection without loading
- **Serilog**: Structured logging
- **CommunityToolkit.Mvvm**: MVVM framework

## Contributing

We welcome contributions! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- BepInEx team for the excellent Unity modding framework
- Thunderstore for providing mod distribution platform
- The Aska modding community for inspiration and feedback
