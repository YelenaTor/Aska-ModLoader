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

public enum ModSortOption
{
    LastUpdated,
    Name,
    Author
}

/// <summary>
/// ViewModel for the main window
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private IModManagerFacade? _modManagerFacade;
    private readonly IGamePathService _gamePathService;
    private readonly IGameLauncherService _gameLauncherService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly ILogger _logger;
    private CrashRollbackService? _crashRollback;

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
    private string? bepInExVersion;

    [ObservableProperty]
    private bool isHarmonyInstalled;

    [ObservableProperty]
    private string? harmonyVersion;

    [ObservableProperty]
    private bool isGameRunning;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? busyMessage;

    [ObservableProperty]
    private bool isAppUpdateAvailable;

    [ObservableProperty]
    private string? appUpdateVersion;



    [ObservableProperty]
    private bool closeOnLaunch;

    partial void OnCloseOnLaunchChanged(bool value)
    {
        if (_appSettingsService != null)
        {
            _appSettingsService.Settings.CloseOnLaunch = value;
            _ = _appSettingsService.SaveSettingsAsync();
        }
    }

    public ObservableCollection<RuntimeError> RuntimeErrors { get; } = new();
    public ObservableCollection<RemoteModInfo> DiscoveredMods { get; } = new();
    private List<RemoteModInfo> _allDiscoveredMods = new();

    [ObservableProperty]
    private string searchQuery = string.Empty;
    
    partial void OnSearchQueryChanged(string value) => FilterMods();

    [ObservableProperty]
    private ModSortOption selectedSortOption;

    partial void OnSelectedSortOptionChanged(ModSortOption value) => FilterMods();

    public ObservableCollection<ModSortOption> SortOptions { get; } = new(Enum.GetValues<ModSortOption>());

    private void FilterMods()
    {
        if (_allDiscoveredMods == null) return;
        
        var query = SearchQuery?.Trim();
        var result = _allDiscoveredMods.AsEnumerable();
        
        if (!string.IsNullOrEmpty(query))
        {
            result = result.Where(m => 
                (m.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (m.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (m.Author?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        
        result = SelectedSortOption switch
        {
            ModSortOption.Name => result.OrderBy(m => m.Name),
            ModSortOption.Author => result.OrderBy(m => m.Author),
            ModSortOption.LastUpdated => result.OrderByDescending(m => m.LastUpdated),
            _ => result
        };
        
        // Update observable collection on UI thread if needed (WPF usually handles it if called from command)
        // But for SearchQueryChanged usage, we might need Dispatcher if invoked from background?
        // ObservableProperty usually invokes on UI thread if source updated on UI thread.
        
        DiscoveredMods.Clear();
        foreach (var mod in result)
        {
            DiscoveredMods.Add(mod);
        }
    }

    private readonly System.Windows.Threading.DispatcherTimer _gameStatusTimer;

    public ObservableCollection<string> Profiles { get; } = new();

    [ObservableProperty]
    private string? selectedProfile;

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand InstallFromZipCommand { get; }
    public IAsyncRelayCommand EnableModCommand { get; }
    public IAsyncRelayCommand DisableModCommand { get; }
    public IAsyncRelayCommand UninstallModCommand { get; }
    public IRelayCommand ConfigureGamePathCommand { get; }
    public IAsyncRelayCommand InstallBepInExCommand { get; }
    public IAsyncRelayCommand<string?> SaveProfileCommand { get; }
    public IAsyncRelayCommand CopyDiagnosticsCommand { get; }
    public IAsyncRelayCommand KillGameCommand { get; }
    public IAsyncRelayCommand CheckForUpdatesCommand { get; }
    public IAsyncRelayCommand LaunchGameCommand { get; }
    public IAsyncRelayCommand UpdateSelectedModCommand { get; }
    public IAsyncRelayCommand FetchAvailableModsCommand { get; }
    public IAsyncRelayCommand<RemoteModInfo> InstallRemoteModCommand { get; }
    public IAsyncRelayCommand CheckForAppUpdateCommand { get; }
    public IAsyncRelayCommand UpdateAppCommand { get; }

    public MainWindowViewModel(
        IModManagerFacade? modManagerFacade,
        IGamePathService gamePathService,
        IGameLauncherService gameLauncherService,
        IAppSettingsService appSettingsService,
        ILogger logger)
    {
        _gamePathService = gamePathService;
        _gameLauncherService = gameLauncherService;
        _appSettingsService = appSettingsService;
        _logger = logger;
        
        RefreshCommand = new AsyncRelayCommand(RefreshModsAsync);
        InstallFromZipCommand = new AsyncRelayCommand(InstallFromZipAsync);
        EnableModCommand = new AsyncRelayCommand(EnableSelectedModAsync, CanEnableSelectedMod);
        DisableModCommand = new AsyncRelayCommand(DisableSelectedModAsync, CanDisableSelectedMod);
        UninstallModCommand = new AsyncRelayCommand(UninstallSelectedModAsync, CanUninstallSelectedMod);
        SaveProfileCommand = new AsyncRelayCommand<string?>(SaveProfileAsync);
        ConfigureGamePathCommand = new RelayCommand(ConfigureGamePath);
        InstallBepInExCommand = new AsyncRelayCommand(InstallBepInExAsync, CanInstallBepInEx);
        CopyDiagnosticsCommand = new AsyncRelayCommand(CopyDiagnosticsAsync);
        KillGameCommand = new AsyncRelayCommand(KillGameAsync);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        UpdateSelectedModCommand = new AsyncRelayCommand(UpdateSelectedModAsync, CanUpdateSelectedMod);
        FetchAvailableModsCommand = new AsyncRelayCommand(FetchAvailableModsAsync);
        InstallRemoteModCommand = new AsyncRelayCommand<RemoteModInfo>(InstallRemoteModAsync);
        CheckForAppUpdateCommand = new AsyncRelayCommand(CheckForAppUpdateAsync_VM);
        UpdateAppCommand = new AsyncRelayCommand(UpdateAppAsync);
        LaunchGameCommand = new AsyncRelayCommand(LaunchGameAsync, () => IsGamePathConfigured);

        SetFacade(modManagerFacade);

        InitializeGamePath();

        if (_modManagerFacade != null)
        {
            LastRuntimeError = _modManagerFacade.GetLastRuntimeError();
            _ = LoadRuntimeErrorsAsync();
        }

        // Setup timer to check game status
        _gameStatusTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _gameStatusTimer.Tick += (s, e) => CheckGameStatus();
        _gameStatusTimer.Start();

        // Initial fetch
        _ = FetchAvailableModsAsync();
        _ = CheckForAppUpdateAsync_VM();

        // Initial crash rollback check
        _ = CheckCrashRollbackAsync();

        // Load settings
        _ = InitializeSettingsAsync();
    }

    private async Task InitializeSettingsAsync()
    {
        await _appSettingsService.LoadSettingsAsync();
        CloseOnLaunch = _appSettingsService.Settings.CloseOnLaunch;
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
                _ = LoadRuntimeErrorsAsync();
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
            _ = LoadInstalledModsAsync(); // Fire and forget on UI start
            _ = LoadProfilesAsync();
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

    private async Task InstallBepInExAsync()
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

        try
        {
            IsBusy = true;
            BusyMessage = "Installing BepInEx...";
            _logger.Information("BepInEx install initiated by user");

            var result = await _modManagerFacade.InstallBepInExAsync(CurrentGamePath);
            StatusMessage = result.Message;

            ValidateRuntime(CurrentGamePath);

            if (RuntimeStatus == BepInExRuntimeStatus.Installed)
            {
                _logger.Information("BepInEx install completed successfully");
                await LoadInstalledModsAsync();
                StatusMessage = "BepInEx installed successfully.";
            }
            else
            {
                _logger.Warning("BepInEx install failed or incomplete. Status: {Status}", RuntimeStatus);
            }
        }
        finally
        {
            IsBusy = false;
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
            _ = LoadInstalledModsAsync();
            _ = LoadProfilesAsync();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Manual game path selection failed");
            StatusMessage = "Failed to set game path. See log for details.";
        }
    }

    private async Task LoadInstalledModsAsync()
    {
        try
        {
            if (!IsRuntimeReady)
            {
                StatusMessage = "BepInEx not installed. Please install to manage mods.";
                return;
            }

            if (_modManagerFacade == null)
            {
                StatusMessage = "ASKA not detected. Please configure the game path.";
                return;
            }

            IsBusy = true;
            BusyMessage = "Loading mods...";
            StatusMessage = "Loading installed mods...";

            var mods = await _modManagerFacade.GetInstalledModsAsync();
            InstalledMods.Clear();
            
            foreach (var mod in mods)
            {
                InstalledMods.Add(mod);
            }
            
            StatusMessage = $"Loaded {InstalledMods.Count} mods";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading mods: {ex.Message}";
            _logger.Error(ex, "Error loading mods in ViewModel");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshModsAsync()
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

        try
        {
            IsBusy = true;
            BusyMessage = "Refreshing mods...";
            var result = await _modManagerFacade.RefreshModsAsync();
            if (result.Success)
            {
                await LoadInstalledModsAsync();
            }
            else
            {
                StatusMessage = result.Message;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallFromZipAsync()
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
            try
            {
                IsBusy = true;
                BusyMessage = "Installing mod from ZIP...";
                var result = await _modManagerFacade.InstallFromZipAsync(dialog.FileName);
                if (result.Success)
                {
                    await LoadInstalledModsAsync();
                }
                StatusMessage = result.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private async Task EnableSelectedModAsync()
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
            try
            {
                IsBusy = true;
                BusyMessage = $"Enabling {SelectedMod.Name}...";
                
                // Validate before enabling
                var validationResult = await _modManagerFacade.ValidateModEnableAsync(SelectedMod.Id);
                if (!validationResult.Success)
                {
                    StatusMessage = $"Cannot enable {SelectedMod.Name}: {validationResult.Message}";
                    
                    if (validationResult.DependencyErrors.Any())
                    {
                        ShowDependencyErrorDialog(SelectedMod.Name, validationResult.DependencyErrors);
                    }
                    return;
                }

                var result = await _modManagerFacade.EnableModAsync(SelectedMod.Id);
                if (!result.Success)
                {
                    StatusMessage = result.Message;
                    if (result.DependencyErrors.Any())
                    {
                        ShowDependencyErrorDialog(SelectedMod.Name, result.DependencyErrors);
                    }
                }
                else
                {
                    // Update the mod in the collection
                    var index = InstalledMods.IndexOf(SelectedMod);
                    if (index >= 0)
                    {
                        var mod = InstalledMods[index];
                        mod.IsEnabled = true;
                        InstalledMods[index] = mod; // Trigger property change
                    }

                    // Track for crash rollback
                    _crashRollback?.RecordModEnabled(SelectedMod.Id, SelectedMod.Name);
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private async Task DisableSelectedModAsync()
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
            try
            {
                IsBusy = true;
                BusyMessage = $"Disabling {SelectedMod.Name}...";
                var result = await _modManagerFacade.DisableModAsync(SelectedMod.Id);
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
            finally
            {
                IsBusy = false;
            }
        }
    }

    private async Task UninstallSelectedModAsync()
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
            try
            {
                IsBusy = true;
                BusyMessage = $"Uninstalling {SelectedMod.Name}...";
                var result = await _modManagerFacade.UninstallModAsync(SelectedMod.Id);
                if (result.Success)
                {
                    InstalledMods.Remove(SelectedMod);
                    SelectedMod = null;
                    await LoadInstalledModsAsync();
                    UpdateStatus();
                    await LoadProfilesAsync();
                }
                StatusMessage = result.Message;
            }
            finally
            {
                IsBusy = false;
            }
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
            if (currentlyRunning)
            {
                // Game just launched
                _crashRollback?.RecordGameLaunch();
                StatusMessage = "ASKA is running. Mod operations are disabled.";
            }
            else
            {
                // Game just exited
                _crashRollback?.RecordGameExit();
                StatusMessage = "Ready";

                // Check if rollback is needed after game exit
                if (_crashRollback?.ShouldRollback() == true)
                {
                    _ = CheckCrashRollbackAsync();
                }
            }

            IsGameRunning = currentlyRunning;

            // Refresh all command states
            EnableModCommand.NotifyCanExecuteChanged();
            DisableModCommand.NotifyCanExecuteChanged();
            UninstallModCommand.NotifyCanExecuteChanged();
            InstallFromZipCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task LoadRuntimeErrorsAsync()
    {
        if (_modManagerFacade == null) return;
        
        var errors = await _modManagerFacade.GetRuntimeErrorsAsync();
        
        App.Current.Dispatcher.Invoke(() =>
        {
            RuntimeErrors.Clear();
            foreach (var error in errors)
            {
                RuntimeErrors.Add(error);
            }
        });
    }

    private async Task SaveProfileAsync(string? profileName)
    {
        try
        {
            if (_modManagerFacade == null || string.IsNullOrWhiteSpace(profileName)) return;

            IsBusy = true;
            BusyMessage = $"Saving profile: {profileName}...";
            var result = await _modManagerFacade.SaveCurrentAsProfileAsync(profileName);
            StatusMessage = result.Message;
            if (result.Success)
            {
                await LoadProfilesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save profile: {ProfileName}", profileName);
            StatusMessage = $"Failed to save profile: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CopyDiagnosticsAsync()
    {
        try
        {
            if (_modManagerFacade == null)
            {
                StatusMessage = "Cannot generate diagnostics - mod manager not initialized";
                return;
            }

            IsBusy = true;
            BusyMessage = "Generating diagnostic bundle...";
            StatusMessage = "Generating diagnostic bundle...";
            var bundle = await _modManagerFacade.GenerateDiagnosticBundleAsync();
            
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
        finally
        {
            IsBusy = false;
        }
    }

    private async Task KillGameAsync()
    {
        if (_modManagerFacade == null) return;

        var result = await _modManagerFacade.KillGameAsync();
        StatusMessage = result.Message;
        
        // Re-check status immediately
        CheckGameStatus();
    }

    private async Task LoadProfilesAsync()
    {
        try
        {
            if (_modManagerFacade == null) return;

            var profiles = await _modManagerFacade.GetProfilesAsync();
            
            App.Current.Dispatcher.Invoke(() =>
            {
                Profiles.Clear();
                foreach (var profile in profiles)
                {
                    Profiles.Add(profile);
                }
            });

            SelectedProfile = await _modManagerFacade.GetActiveProfileAsync();
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
        BepInExVersion = runtime.Version;
        IsHarmonyInstalled = runtime.HarmonyInstalled;
        HarmonyVersion = runtime.HarmonyVersion;
        IsRuntimeReady = RuntimeStatus == BepInExRuntimeStatus.Installed;

        // Initialize crash rollback service with discovered game path
        _crashRollback = new CrashRollbackService(_logger, gamePath);

        _logger.Information("Runtime validation completed. Status: {Status}", RuntimeStatus);

        InstallBepInExCommand.NotifyCanExecuteChanged();
        EnableModCommand.NotifyCanExecuteChanged();
        DisableModCommand.NotifyCanExecuteChanged();
        UninstallModCommand.NotifyCanExecuteChanged();
        InstallFromZipCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedModChanged(ModDisplayModel? value)
    {
        // Refresh command states when selection changes
        EnableModCommand.NotifyCanExecuteChanged();
        DisableModCommand.NotifyCanExecuteChanged();
        UninstallModCommand.NotifyCanExecuteChanged();
        InstallBepInExCommand.NotifyCanExecuteChanged();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_modManagerFacade == null) return;

        try
        {
            IsBusy = true;
            BusyMessage = "Checking for updates...";
            var updates = await _modManagerFacade.CheckForUpdatesAsync();

            foreach (var mod in InstalledMods)
            {
                var update = updates.FirstOrDefault(u => u.ModId == mod.Id);
                if (update != null)
                {
                    mod.IsUpdateAvailable = true;
                    mod.LatestVersion = update.LatestVersion;
                }
                else
                {
                    mod.IsUpdateAvailable = false;
                    mod.LatestVersion = null;
                }
            }
            
            // Force refresh of the collection to update UI
            var tempSelected = SelectedMod;
            var tempMods = InstalledMods.ToList();
            InstalledMods.Clear();
            foreach (var mod in tempMods) InstalledMods.Add(mod);
            SelectedMod = tempSelected;

            StatusMessage = $"Update check complete. Found {updates.Count()} updates.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to check for updates");
            StatusMessage = "Failed to check for updates.";
        }
        finally
        {
            IsBusy = false;
            UpdateSelectedModCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task UpdateSelectedModAsync()
    {
        if (_modManagerFacade == null || SelectedMod == null) return;

        try
        {
            IsBusy = true;
            BusyMessage = $"Updating {SelectedMod.Name}...";
            var result = await _modManagerFacade.UpdateModAsync(SelectedMod.Id);
            
            if (result.Success)
            {
                await RefreshModsAsync();
                StatusMessage = $"{SelectedMod.Name} updated successfully.";
            }
            else
            {
                StatusMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update mod: {ModId}", SelectedMod.Id);
            StatusMessage = "Update failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUpdateSelectedMod()
    {
        return SelectedMod != null && SelectedMod.IsUpdateAvailable;
    }

    private async Task FetchAvailableModsAsync()
    {
        if (_modManagerFacade == null) return;

        try
        {
            IsBusy = true;
            BusyMessage = "Fetching available mods...";
            var available = await _modManagerFacade.GetAvailableModsAsync();
            
            _allDiscoveredMods = available.ToList();
            FilterMods();
            
            StatusMessage = $"Found {_allDiscoveredMods.Count} mods available for download.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch available mods");
            StatusMessage = "Failed to fetch available mods.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallRemoteModAsync(RemoteModInfo? mod)
    {
        if (_modManagerFacade == null || mod == null) return;

        if (mod.DownloadUrl == null)
        {
            StatusMessage = "Mod has no download URL.";
            return;
        }

        try
        {
            IsBusy = true;
            BusyMessage = $"Installing {mod.Name} and dependencies...";
            
            var result = await _modManagerFacade.InstallModWithDependenciesAsync(mod);

            if (result.Success)
            {
                StatusMessage = result.Message;
                await RefreshModsAsync();
            }
            else
            {
                 StatusMessage = result.Message;
                 // Add errors to runtime errors or show dialog?
                 // Status message is usually enough for now, or log detailed error.
                 _logger.Error("Installation failed: {Errors}", string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to install remote mod: {ModId}", mod.Id);
            StatusMessage = "Installation failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CheckForAppUpdateAsync_VM()
    {
        if (_modManagerFacade == null) return;

        try
        {
            var update = await _modManagerFacade.CheckForAppUpdateAsync();
            if (update != null)
            {
                IsAppUpdateAvailable = true;
                AppUpdateVersion = update.LatestVersion;
                StatusMessage = $"Mod Manager update available: v{update.LatestVersion}";
            }
            else
            {
                IsAppUpdateAvailable = false;
                AppUpdateVersion = null;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to check for app updates");
        }
    }

    private async Task UpdateAppAsync()
    {
        if (_modManagerFacade == null) return;

        try
        {
            IsBusy = true;
            BusyMessage = "Downloading Mod Manager update...";
            var success = await _modManagerFacade.InitiateAppUpdateAsync();

            if (success)
            {
                StatusMessage = "Update downloaded. Restart to apply.";
                IsAppUpdateAvailable = false;
            }
            else
            {
                StatusMessage = "Update failed. Please try again later.";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update Mod Manager");
            StatusMessage = "Update failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CheckCrashRollbackAsync()
    {
        if (_crashRollback == null || !_crashRollback.ShouldRollback()) return;

        var modId = _crashRollback.GetRollbackModId();
        if (string.IsNullOrEmpty(modId) || _modManagerFacade == null) return;

        var message = _crashRollback.GetRollbackMessage();

        var answer = MessageBox.Show(
            message,
            "Crash Protection",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Yes)
        {
            try
            {
                IsBusy = true;
                BusyMessage = "Disabling crash-suspect mod...";
                await _modManagerFacade.DisableModAsync(modId);
                await RefreshModsAsync();
                StatusMessage = $"Disabled crash-suspect mod. Crash counter reset.";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to disable crash-suspect mod: {ModId}", modId);
                StatusMessage = "Failed to disable mod.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        // Reset counter regardless of user choice to avoid re-showing
        _crashRollback.ResetCrashCounter();
    }

    private void ShowDependencyErrorDialog(string modName, IEnumerable<ModDependencyDisplayModel> dependencies)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new Views.DependencyErrorDialog(modName, dependencies)
            {
                Owner = App.Current.MainWindow
            };
            dialog.ShowDialog();
        });
    }

    private async Task LaunchGameAsync()
    {
        if (CurrentGamePath == null)
        {
            StatusMessage = "Game path not configured.";
            return;
        }

        if (IsGameRunning)
        {
            StatusMessage = "Game is already running.";
            return;
        }

        try
        {
            StatusMessage = "Launching ASKA...";
            var success = await _gameLauncherService.LaunchGameAsync(CurrentGamePath);
            
            if (success)
            {
                StatusMessage = "Game launched successfully.";
                if (CloseOnLaunch)
                {
                    System.Windows.Application.Current.Shutdown();
                }
            }
            else
            {
                StatusMessage = "Failed to launch game. Check logs.";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to launch game");
            StatusMessage = "Error launching game.";
        }
    }
}
