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
        
        base.OnStartup(e);
    }
}
