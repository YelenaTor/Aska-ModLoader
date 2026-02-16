using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModManager.Core.Interfaces;
using ModManager.Core.Services;
using ModManager.Core.Models;
using ModManager.DesktopUI.Interfaces;
using ModManager.DesktopUI.Models;
using ModManager.DesktopUI.Services;
using Serilog;
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

    public ICommand RefreshCommand { get; }
    public ICommand InstallFromZipCommand { get; }
    public ICommand EnableModCommand { get; }
    public ICommand DisableModCommand { get; }
    public ICommand UninstallModCommand { get; }
    public ICommand ConfigureGamePathCommand { get; }
    public ICommand InstallBepInExCommand { get; }

    public MainWindowViewModel(IModManagerFacade? modManagerFacade, IGamePathService gamePathService, ILogger logger)
    {
        _modManagerFacade = modManagerFacade;
        _gamePathService = gamePathService;
        _logger = logger;

        // Initialize commands (will become IAsyncRelayCommand when real async arrives)
        RefreshCommand = new RelayCommand(RefreshMods);
        InstallFromZipCommand = new RelayCommand(InstallFromZip);
        EnableModCommand = new RelayCommand(EnableSelectedMod, CanEnableSelectedMod);
        DisableModCommand = new RelayCommand(DisableSelectedMod, CanDisableSelectedMod);
        UninstallModCommand = new RelayCommand(UninstallSelectedMod, CanUninstallSelectedMod);
        ConfigureGamePathCommand = new RelayCommand(ConfigureGamePath);
        InstallBepInExCommand = new RelayCommand(InstallBepInEx, CanInstallBepInEx);

        InitializeGamePath();
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
            _modManagerFacade = BuildRealFacade(selectedPath);

            // Reload mods with the valid path
            ValidateRuntime(selectedPath);
            LoadInstalledMods();
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
            }
            StatusMessage = result.Message;
        }
    }

    private bool CanEnableSelectedMod()
    {
        return SelectedMod != null && !SelectedMod.IsEnabled && IsRuntimeReady;
    }

    private bool CanDisableSelectedMod()
    {
        return SelectedMod != null && SelectedMod.IsEnabled && IsRuntimeReady;
    }

    private bool CanUninstallSelectedMod()
    {
        return SelectedMod != null && IsRuntimeReady;
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
        return new RealModManagerFacade(modRepository, _logger);
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
