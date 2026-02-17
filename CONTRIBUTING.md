# Contributing to Aska Mod Manager

Thank you for your interest in contributing! We want to make it as easy and transparent as possible, whether it's:

- Reporting a bug
- Discussing the current state of the code
- Submitting a fix
- Proposing new features

## [ Getting Started ]

### Prerequisites
- **.NET 8.0 SDK**
- Visual Studio 2022 or VS Code (with C# Dev Kit)

### Setup
1. Fork the repo on GitHub.
2. Clone your fork locally:
   ```bash
   git clone https://github.com/YOUR-USERNAME/Aska-ModLoader.git
   ```
3. Build the solution:
   ```bash
   dotnet build src/ModManager.DesktopUI
   ```

## [ Workflow ]

1. Create a feature branch from `master`:
   ```bash
   git checkout -b feature/amazing-feature
   ```
2. Make your changes.
3. **Test your changes** carefully.
4. Commit your changes using descriptive messages.
   > We follow [Conventional Commits](https://www.conventionalcommits.org/).  
   > Example: `feat: add support for Thunderstore v2 API`
5. Push to your fork and submit a Pull Request to the `master` branch.

## [ Project Structure ]

- **`src/ModManager.Core`**: The brain. Contains all business logic, services, and models.
- **`src/ModManager.DesktopUI`**: The face. WPF application using MVVM and MahApps.Metro.
- **`src/ModManager.BepInExPlugin`**: In-game hook (placeholder/wip).

## [ Reporting Bugs ]

Bugs are tracked as GitHub issues. When opening an issue, please include:
- A clear title and description.
- Steps to reproduce the issue.
- Expected vs. actual behavior.
- **Log files** (from `AppData/Roaming/AskaModManager/logs` or the "Diagnostics" tab).

## [ Feature Requests ]

We love new ideas! Please open an issue to discuss your idea before implementing it. This saves time and ensures alignment with the project roadmap.

## [ License ]

By contributing, you agree that your contributions will be licensed under its [BSD 3-Clause License](LICENSE).
