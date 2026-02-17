using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using Serilog;
using Serilog.Sinks.File;

namespace ModManager.DesktopUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var logsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AskaModManager", "logs");
        Directory.CreateDirectory(logsPath);

        // Configure logging - DesktopUI owns the logger configuration
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(logsPath, "desktopui.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        // Verify logging is working
        Log.Information("Aska Mod Manager starting - logging configured");
        
        // Check for conflicting winhttp.dll (BepInEx proxy) in app directory
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var winHttpPath = Path.Combine(appDir, "winhttp.dll");
        if (File.Exists(winHttpPath))
        {
            MessageBox.Show(
                "Critical Conflict Detected!\n\n" +
                "The file 'winhttp.dll' was found in the Mod Manager folder. This file is part of BepInEx and should NOT be in the Mod Manager folder.\n\n" +
                "It appears you have installed the Mod Manager inside the game folder or alongside BepInEx.\n" +
                "Please move the Mod Manager executable (and its files) to a separate dedicated folder (e.g., C:\\AskaModManager).\n\n" +
                "The application will now close to prevent crashes.",
                "Configuration Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        base.OnStartup(e);
    }
}
