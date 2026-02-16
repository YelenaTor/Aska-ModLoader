# Contributing to Aska Mod Manager

Thank you for your interest in contributing to the Aska Mod Manager! This document provides guidelines and information for contributors.

## Getting Started

### Development Environment Setup

1. Clone the repository
2. Install .NET 8.0 SDK
3. Use Visual Studio 2022 or VS Code with C# extensions
4. Run `dotnet build src/ModManager.DesktopUI` (restores Core as well)
5. Optional: `dotnet test` when tests are available

### Running Tests

```bash
dotnet test
```

## Development Guidelines

### Code Style

- Follow C# naming conventions
- Use meaningful variable and method names
- Add XML documentation for public APIs
- Keep methods small and focused

### Commit Messages

- Use clear, descriptive commit messages
- Start with a verb (e.g., "Add", "Fix", "Update")
- Limit to 72 characters for the first line
- Reference the area (e.g., Core, DesktopUI, BepInEx) when useful

### Pull Request Process

1. Fork the repository
2. Create a feature branch from `main`
3. Make your changes
4. Add tests for new functionality (when applicable)
5. Ensure `dotnet build src/ModManager.DesktopUI` succeeds
6. Submit a pull request

## Project Structure

### Core Components

- **ModManager.Core**: Contains all business logic, data models, and services
- **ModManager.DesktopUI**: WPF application using MVVM pattern
- **ModManager.BepInExPlugin**: Unity plugin for in-game functionality

### Key Areas for Contribution

1. **UI/UX Improvements**: Enhance the desktop application interface
2. **Online Integration**: Add support for additional mod repositories
3. **Diagnostics**: Improve crash analysis and log parsing
4. **Testing**: Add unit and integration tests
5. **Documentation**: Improve user and developer documentation

## Testing

### Unit Tests

- Test all core business logic in ModManager.Core
- Mock external dependencies (HTTP calls, file system)
- Aim for high code coverage

### Integration Tests

- Test mod installation workflows
- Test UI interactions
- Test BepInEx plugin functionality

## Bug Reports

When reporting bugs, please include:

- Operating system and version
- Aska version
- BepInEx version
- Steps to reproduce
- Expected vs actual behavior
- Relevant log files

## Feature Requests

Feature requests should include:

- Clear description of the feature
- Use case and motivation
- Proposed implementation (if known)
- Potential impact on existing functionality

## Code of Conduct

Be respectful and constructive in all interactions. We welcome contributors of all experience levels and backgrounds.

## Questions

Feel free to open an issue for questions about development or the project structure.

Thank you for contributing!
