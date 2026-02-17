using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ModManager.Core.Interfaces;
using ModManager.Core.Models;
using ModManager.Core.Models.Thunderstore;
using Serilog;

namespace ModManager.Core.Services;

/// <summary>
/// Service for discovering mods from remote sources
/// </summary>
public class DiscoveryService
{
    private readonly ILogger _logger;
    private readonly ThunderstoreClient _thunderstoreClient;

    public DiscoveryService(ILogger logger, ThunderstoreClient thunderstoreClient)
    {
        _logger = logger;
        _thunderstoreClient = thunderstoreClient;
    }

    /// <summary>
    /// Fetches a list of available mods from Thunderstore
    /// </summary>
    public async Task<IEnumerable<RemoteModInfo>> GetAvailableModsAsync()
    {
        try
        {
            _logger.Information("Fetching available mods from Thunderstore");
            var packages = await _thunderstoreClient.GetPackageIndexAsync();
            
            return packages.Select(p => 
            {
                var latestVersion = p.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
                return new RemoteModInfo
                {
                    Id = p.FullName, // Namespace-Name is usually the ID
                    Name = p.Name,
                    Version = latestVersion?.VersionNumber ?? "0.0.0",
                    Author = p.Owner,
                    Description = latestVersion?.Description ?? p.Name,
                    DownloadUrl = !string.IsNullOrEmpty(latestVersion?.DownloadUrl) ? new Uri(latestVersion.DownloadUrl) : null,
                    LastUpdated = p.DateUpdated,
                    Dependencies = latestVersion?.Dependencies?.ToList() ?? new List<string>()
                };
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get available mods from Thunderstore");
            return Enumerable.Empty<RemoteModInfo>();
        }
    }
}

