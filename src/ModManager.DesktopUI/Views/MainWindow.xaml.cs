using MahApps.Metro.Controls;
using ModManager.Core.Interfaces;
using ModManager.Core.Services;
using ModManager.DesktopUI.Interfaces;
using ModManager.DesktopUI.Services;
using ModManager.DesktopUI.ViewModels;
using Serilog;
using System.IO;
using System.Windows;

namespace ModManager.DesktopUI.Views
{
    public partial class MainWindow : MetroWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize with proper game path resolution
            try
            {
                // Configure logger (owned by DesktopUI)
                var logger = Log.Logger;
                
                // Create game path service
                var askaSteamDetectionService = new AskaSteamDetectionService(logger);
                var gamePathService = new GamePathService(logger, askaSteamDetectionService);

                // Resolve game path properly
                var gamePath = gamePathService.ResolveGamePath();

                IModManagerFacade? facade = null;
                if (!string.IsNullOrEmpty(gamePath))
                {
                    // Log resolved paths
                    var pluginsPath = Path.Combine(gamePath, "BepInEx", "plugins");
                    logger.Information("Resolved game path: {GamePath}", gamePath);
                    logger.Information("Resolved plugins path: {PluginsPath}", pluginsPath);

                    // Create Core services with real path
                    var modRepository = new ModManager.Core.Services.ModRepository(logger, gamePath);

                    // Create facade with injected dependencies
                    facade = new RealModManagerFacade(modRepository, logger, gamePath);
                }

                var viewModel = new MainWindowViewModel(facade, gamePathService, logger);
                DataContext = viewModel;

                logger.Information("Application initialized successfully");
            }
            catch (Exception ex)
            {
                var logger = Log.Logger;
                logger.Error(ex, "Failed to initialize application");
                
                // Show error to user
                var errorFacade = new MockModManagerFacade();
                errorFacade.SetStatusMessage($"Initialization failed: {ex.Message}");

                var fallbackDetection = new AskaSteamDetectionService(logger);
                var fallbackGamePathService = new GamePathService(logger, fallbackDetection);

                var errorViewModel = new MainWindowViewModel(errorFacade, fallbackGamePathService, logger);
                DataContext = errorViewModel;
            }
        }
    }
}
