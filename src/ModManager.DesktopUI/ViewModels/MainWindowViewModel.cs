using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModManager.Core.Interfaces;
using ModManager.Core.Services;
using ModManager.Core.Models;
using ModManager.DesktopUI.Interfaces;
using ModManager.DesktopUI.Models;
using ModManager.DesktopUI.Services;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using System.Windows.Input;

namespace ModManager.DesktopUI.ViewModels;

/// <summary>
/// ViewModel for the main window
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private IModManagerFacade? _modManagerFacade;
    private readonly IGamePathService _gamePathService;
    private readonly ILogger _logger;

    public ObservableCollection<ModDisplayModel> InstalledMods { get; } = new();

    [ObservableProperty]
    private ModDisplayModel? selectedMod;

    [ObservableProperty]
    private string statusMessage = "Ready";

    [ObservableProperty]
    private bool isGamePathConfigured;

    [ObservableProperty]
    private string? currentGamePath;

    [ObservableProperty]
    private BepInExRuntimeStatus runtimeStatus = BepInExRuntimeStatus.NotInstalled;

    [ObservableProperty]
    private bool isRuntimeReady;

    [ObservableProperty]
    private string? lastRuntimeError;

    [ObservableProperty]
    private bool isGameRunning;

    public ObservableCollection<RuntimeError> RuntimeErrors { get; } = new();

    private readonly System.Windows.Threading.DispatcherTimer _gameStatusTimer;

    public ObservableCollection<string> Profiles { get; } = new();

    [ObservableProperty]
    private string? selectedProfile;

    public IRelayCommand RefreshCommand { get; }
    public IRelayCommand InstallFromZipCommand { get; }
    public IRelayCommand EnableModCommand { get; }
    public IRelayCommand DisableModCommand { get; }
    public IRelayCommand UninstallModCommand { get; }
    public IRelayCommand ConfigureGamePathCommand { get; }
    public IRelayCommand InstallBepInExCommand { get; }
    public IRelayCommand SaveProfileCommand { get; }
    public IRelayCommand CopyDiagnosticsCommand { get; }

    public MainWindowViewModel(IModManagerFacade? modManagerFacade, IGamePathService gamePathService, ILogger logger)
    {
        SetFacade(modManagerFacade);
        _gamePathService = gamePathService;
        _logger = logger;
        // Initialize commands (will become IAsyncRelayCommand when real async arrives)
        RefreshCommand = new RelayCommand(RefreshMods);
        InstallFromZipCommand = new RelayCommand(InstallFromZip);
        EnableModCommand = new RelayCommand(EnableSelectedMod, CanEnableSelectedMod);
        DisableModCommand = new RelayCommand(DisableSelectedMod, CanDisableSelectedMod);
        UninstallModCommand = new RelayCommand(UninstallSelectedMod, CanUninstallSelectedMod);
        SaveProfileCommand = new RelayCommand<string>(SaveProfile);
        ConfigureGamePathCommand = new RelayCommand(ConfigureGamePath);
        InstallBepInExCommand = new RelayCommand(InstallBepInEx, CanInstallBepInEx);
        CopyDiagnosticsCommand = new RelayCommand(CopyDiagnostics);

        SetFacade(modManagerFacade);
        _gamePathService = gamePathService;
        _logger = logger;

        InitializeGamePath();
        if (_modManagerFacade != null)
        {
            LastRuntimeError = _modManagerFacade.GetLastRuntimeError();
            LoadRuntimeErrors();
        }

        // Setup timer to check game status
        _gameStatusTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _gameStatusTimer.Tick += (s, e) => CheckGameStatus();
        _gameStatusTimer.Start();
    }

    private void SetFacade(IModManagerFacade? facade)
    {
        if (_modManagerFacade != null)
        {
            _modManagerFacade.CrashLogUpdated -= OnCrashLogUpdated;
        }

        _modManagerFacade = facade;

        if (_modManagerFacade != null)
        {
            _modManagerFacade.CrashLogUpdated += (s, e) =>
            {
                LastRuntimeError = e;
                LoadRuntimeErrors();
            };
            LastRuntimeError = _modManagerFacade.GetLastRuntimeError();
        }
        else
        {
            LastRuntimeError = null;
        }
    }

    private void OnCrashLogUpdated(object? sender, string? message)
    {
        LastRuntimeError = message;
    }

    private void InitializeGamePath()
    {
        try
        {
            _logger.Information("Resolving game path on startup");
            var resolvedPath = _gamePathService.ResolveGamePath();

            if (string.IsNullOrEmpty(resolvedPath))
            {
                IsGamePathConfigured = false;
                StatusMessage = "ASKA not detected. Please configure the game path.";
                return;
            }

            IsGamePathConfigured = true;
            CurrentGamePath = resolvedPath;
            StatusMessage = $"Game path: {resolvedPath}";
            ValidateRuntime(resolvedPath);
            LoadInstalledMods();
            LoadProfiles();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to resolve game path on startup");
            IsGamePathConfigured = false;
            StatusMessage = "ASKA not detected. Please configure the game path.";
        }
    }

    private bool CanInstallBepInEx()
    {
        return _modManagerFacade != null && IsGamePathConfigured && !string.IsNullOrWhiteSpace(CurrentGamePath) && RuntimeStatus != BepInExRuntimeStatus.Installed;
    }

    private void InstallBepInEx()
    {
        if (_modManagerFacade == null || string.IsNullOrWhiteSpace(CurrentGamePath))
        {
            StatusMessage = "ASKA not detected. Please configure the game path.";
            return;
        }

        var confirm = MessageBox.Show(
            "This will install BepInEx into your ASKA directory and modify the game folder. Continue?",
            "Install BepInEx",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            _logger.Information("BepInEx install cancelled by user");
            return;
        }

        _logger.Information("BepInEx install initiated by user");

        var result = _modManagerFacade.InstallBepInEx(CurrentGamePath);
        StatusMessage = result.Message;

        ValidateRuntime(CurrentGamePath);

        if (RuntimeStatus == BepInExRuntimeStatus.Installed)
        {
            _logger.Information("BepInEx install completed successfully");
            LoadInstalledMods();
            StatusMessage = "BepInEx installed successfully.";
        }
        else
        {
            _logger.Warning("BepInEx install failed or incomplete. Status: {Status}", RuntimeStatus);
        }
    }

    private void ConfigureGamePath()
    {
        try
        {
            _logger.Information("Manual game path selection initiated");

            using var dialog = new FolderBrowserDialog
            {
                Description = "Select ASKA installation folder"
            };

            var result = dialog.ShowDialog();
            if (result != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                _logger.Information("Manual game path selection cancelled");
                return;
            }

            var selectedPath = dialog.SelectedPath;
            _logger.Information("Game path selected: {Path}", selectedPath);

            if (IsGamePathConfigured && string.Equals(CurrentGamePath, selectedPath, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Game path already configured.";
                _logger.Information("Selected game path matches existing configuration; skipping rebuild.");
                return;
            }

            if (!_gamePathService.ValidateGamePath(selectedPath))
            {
                StatusMessage = "Invalid ASKA folder selected.";
                _logger.Warning("Manual game path validation failed for {Path}", selectedPath);
                return;
            }

            _gamePathService.SaveConfiguredPath(selectedPath);
            _logger.Information("Game path saved: {Path}", selectedPath);

            CurrentGamePath = selectedPath;
            IsGamePathConfigured = true;

            // Rebuild facade with the newly configured path
            SetFacade(BuildRealFacade(selectedPath));

            // Reload mods with the valid path
            ValidateRuntime(selectedPath);
            LoadInstalledMods();
            LoadProfiles();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Manual game path selection failed");
            StatusMessage = "Failed to set game path. See log for details.";
        }
    }

    private void LoadInstalledMods()
    {
        try
        {
            if (!IsRuntimeReady)
            {
                StatusMessage = "BepInEx not installed. Please install to manage mods.";
                return;
            }

            StatusMessage = "Loading installed mods...";

            // Add diagnostic logging
            System.Diagnostics.Debug.WriteLine("MainWindowViewModel: Loading mods from facade...");

            if (_modManagerFacade == null)
            {
                StatusMessage = "ASKA not detected. Please configure the game path.";
                return;
            }

            var mods = _modManagerFacade.GetInstalledMods();
            InstalledMods.Clear();
            
            foreach (var mod in mods)
            {
                InstalledMods.Add(mod);
            }
            
            StatusMessage = $"Loaded {InstalledMods.Count} mods";
            System.Diagnostics.Debug.WriteLine($"MainWindowViewModel: Loaded {InstalledMods.Count} mods");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading mods: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"MainWindowViewModel: Error loading mods: {ex.Message}");
        }
    }

    private void RefreshMods()
    {
        if (_modManagerFacade == null)
        {
            StatusMessage = "ASKA not detected. Please configure the game path.";
            return;
        }

        if (!IsRuntimeReady)
        {
            StatusMessage = "BepInEx not installed. Please install to refresh mods.";
            return;
        }

        var result = _modManagerFacade.RefreshMods();
        if (result.Success)
        {
            LoadInstalledMods();
        }
        else
        {
            StatusMessage = result.Message;
        }
    }

    private void InstallFromZip()
    {
        if (_modManagerFacade == null)
        {
            StatusMessage = "ASKA not detected. Please configure the game path.";
            return;
        }
        if (!IsRuntimeReady)
        {
            StatusMessage = "BepInEx not installed. Please install before adding mods.";
            return;
        }

        if (IsGameRunning)
        {
            StatusMessage = "Cannot install mods while ASKA is running.";
            return;
        }
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Mod ZIP File",
            Filter = "ZIP files (*.zip)|*.zip|All files (*.*)|*.*",
            DefaultExt = ".zip"
        };

        if (dialog.ShowDialog() == true)
        {
            var result = _modManagerFacade.InstallFromZip(dialog.FileName);
            if (result.Success)
            {
                LoadInstalledMods();
            }
            StatusMessage = result.Message;
        }
    }

    private void EnableSelectedMod()
    {
        if (_modManagerFacade == null)
        {
            StatusMessage = "ASKA not detected. Please configure the game path.";
            return;
        }

        if (!IsRuntimeReady)
        {
            StatusMessage = "BepInEx not installed. Please install before enabling mods.";
            return;
        }

        if (SelectedMod != null)
        {
            // Validate before enabling
            var validationResult = _modManagerFacade.ValidateModEnable(SelectedMod.Id);
            if (!validationResult.Success)
            {
                StatusMessage = $"Cannot enable {SelectedMod.Name}: {validationResult.Message}";
                return;
            }

            var result = _modManagerFacade.EnableMod(SelectedMod.Id);
            if (result.Success)
            {
                // Update the mod in the collection
                var index = InstalledMods.IndexOf(SelectedMod);
                if (index >= 0)
                {
                    var mod = InstalledMods[index];
                    mod.IsEnabled = true;
                    InstalledMods[index] = mod; // Trigger property change
                }
            }
            StatusMessage = result.Message;
        }
    }

    private void DisableSelectedMod()
    {
        if (_modManagerFacade == null)
        {
            StatusMessage = "ASKA not detected. Please configure the game path.";
            return;
        }

        if (!IsRuntimeReady)
        {
            StatusMessage = "BepInEx not installed. Please install before disabling mods.";
            return;
        }

        if (SelectedMod != null)
        {
            var result = _modManagerFacade.DisableMod(SelectedMod.Id);
            if (result.Success)
            {
                // Update the mod in the collection
                var index = InstalledMods.IndexOf(SelectedMod);
                if (index >= 0)
                {
                    var mod = InstalledMods[index];
                    mod.IsEnabled = false;
                    InstalledMods[index] = mod; // Trigger property change
                }
            }
            StatusMessage = result.Message;
        }
    }

    private void UninstallSelectedMod()
    {
        if (_modManagerFacade == null)
        {
            StatusMessage = "ASKA not detected. Please configure the game path.";
            return;
        }

        if (!IsRuntimeReady)
        {
            StatusMessage = "BepInEx not installed. Please install before uninstalling mods.";
            return;
        }

        if (SelectedMod != null)
        {
            var result = _modManagerFacade.UninstallMod(SelectedMod.Id);
            if (result.Success)
            {
                InstalledMods.Remove(SelectedMod);
                SelectedMod = null;
                LoadInstalledMods();
                UpdateStatus();
                LoadProfiles();
            }
            StatusMessage = result.Message;
        }
    }

    private bool CanEnableSelectedMod()
    {
        return SelectedMod != null && !SelectedMod.IsEnabled && IsRuntimeReady && !IsGameRunning;
    }

    private bool CanDisableSelectedMod()
    {
        return SelectedMod != null && SelectedMod.IsEnabled && IsRuntimeReady && !IsGameRunning;
    }

    private bool CanUninstallSelectedMod()
    {
        return SelectedMod != null && IsRuntimeReady && !IsGameRunning;
    }

    private void CheckGameStatus()
    {
        if (_modManagerFacade == null) return;

        bool currentlyRunning = _modManagerFacade.IsGameRunning();
        if (IsGameRunning != currentlyRunning)
        {
            IsGameRunning = currentlyRunning;
            if (IsGameRunning)
            {
                StatusMessage = "ASKA is running. Mod operations are disabled.";
            }
            else
            {
                StatusMessage = "Ready";
            }

            // Refresh all command states
            ((RelayCommand)EnableModCommand).NotifyCanExecuteChanged();
            ((RelayCommand)DisableModCommand).NotifyCanExecuteChanged();
            ((RelayCommand)UninstallModCommand).NotifyCanExecuteChanged();
            ((RelayCommand)InstallFromZipCommand).NotifyCanExecuteChanged();
        }
    }

    private void LoadRuntimeErrors()
    {
        if (_modManagerFacade == null) return;
        
        App.Current.Dispatcher.Invoke(() =>
        {
            var errors = _modManagerFacade.GetRuntimeErrors();
            RuntimeErrors.Clear();
            foreach (var error in errors)
            {
                RuntimeErrors.Add(error);
            }
        });
    }

    private void SaveProfile(string profileName)
    {
        try
        {
            if (_modManagerFacade == null || string.IsNullOrWhiteSpace(profileName)) return;

            var result = _modManagerFacade.SaveCurrentAsProfile(profileName);
            StatusMessage = result.Message;
            if (result.Success)
            {
                LoadProfiles();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save profile: {ProfileName}", profileName);
            StatusMessage = $"Failed to save profile: {ex.Message}";
        }
    }

    private void CopyDiagnostics()
    {
        try
        {
            if (_modManagerFacade == null)
            {
                StatusMessage = "Cannot generate diagnostics - mod manager not initialized";
                return;
            }

            StatusMessage = "Generating diagnostic bundle...";
            var bundle = _modManagerFacade.GenerateDiagnosticBundle();
            
            if (string.IsNullOrEmpty(bundle))
            {
                StatusMessage = "Failed to generate diagnostic bundle";
                return;
            }

            // Copy to clipboard
            System.Windows.Clipboard.SetText(bundle);
            StatusMessage = "Diagnostic bundle copied to clipboard";
            
            _logger.Information("Diagnostic bundle generated and copied to clipboard");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error generating diagnostics: {ex.Message}";
            _logger.Error(ex, "Failed to generate diagnostics");
        }
    }

    private void LoadProfiles()
    {
        try
        {
            if (_modManagerFacade == null) return;

            var profiles = _modManagerFacade.GetProfiles();
            Profiles.Clear();
            foreach (var profile in profiles)
            {
                Profiles.Add(profile);
            }

            SelectedProfile = _modManagerFacade.GetActiveProfile();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load profiles");
        }
    }

    private void UpdateStatus()
    {
        if (_modManagerFacade == null)
        {
            StatusMessage = "ASKA not detected. Please configure the game path.";
            return;
        }

        StatusMessage = _modManagerFacade.GetStatusMessage();
    }

    private IModManagerFacade BuildRealFacade(string gamePath)
    {
        var modRepository = new ModRepository(_logger, gamePath);
        return new RealModManagerFacade(modRepository, _logger, gamePath);
    }

    private void ValidateRuntime(string gamePath)
    {
        var validator = new BepInExRuntimeValidator(_logger);
        var runtime = validator.Validate(gamePath);

        RuntimeStatus = runtime.Status;
        IsRuntimeReady = RuntimeStatus == BepInExRuntimeStatus.Installed;

        _logger.Information("Runtime validation completed. Status: {Status}", RuntimeStatus);

        ((RelayCommand)InstallBepInExCommand).NotifyCanExecuteChanged();
        ((RelayCommand)EnableModCommand).NotifyCanExecuteChanged();
        ((RelayCommand)DisableModCommand).NotifyCanExecuteChanged();
        ((RelayCommand)UninstallModCommand).NotifyCanExecuteChanged();
        ((RelayCommand)InstallFromZipCommand).NotifyCanExecuteChanged();
        ((RelayCommand)RefreshCommand).NotifyCanExecuteChanged();
    }

    partial void OnSelectedModChanged(ModDisplayModel? value)
    {
        // Refresh command states when selection changes
        ((RelayCommand)EnableModCommand).NotifyCanExecuteChanged();
        ((RelayCommand)DisableModCommand).NotifyCanExecuteChanged();
        ((RelayCommand)UninstallModCommand).NotifyCanExecuteChanged();
        ((RelayCommand)InstallBepInExCommand).NotifyCanExecuteChanged();
    }
}
