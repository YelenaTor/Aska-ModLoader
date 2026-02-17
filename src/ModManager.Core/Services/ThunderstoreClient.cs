using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ModManager.Core.Models.Thunderstore;
using Serilog;

namespace ModManager.Core.Services;

/// <summary>
/// Client for interacting with the Thunderstore API
/// </summary>
public class ThunderstoreClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    
    // Cache the package index to avoid hitting the API too frequently
    private List<PackageIndexEntry>? _packageIndexCache;
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

    private const string BaseUrl = "https://thunderstore.io";
    private const string CommunityId = "aska"; // TODO: Verify community ID

    public ThunderstoreClient(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(BaseUrl);
        }
    }

    /// <summary>
    /// Fetches the package index for the ASKA community on Thunderstore.
    /// Uses in-memory caching.
    /// </summary>
    public async Task<List<PackageIndexEntry>> GetPackageIndexAsync(bool forceRefresh = false)
    {
        try
        {
            if (!forceRefresh && _packageIndexCache != null && DateTime.UtcNow - _lastCacheUpdate < _cacheDuration)
            {
                _logger.Debug("Returning cached Thunderstore package index");
                return _packageIndexCache;
            }

            _logger.Information("Fetching Thunderstore package index...");
            
            // The package-index endpoint returns all packages. 
            // In a real scenario for a specific game, we might filter by community if the endpoint supports it,
            // or filter client-side if we use the global index. 
            // For now, assuming we use the global index and filter client-side or use a community-specific endpoint if available.
            // Documentation says: /api/experimental/package-index/
            
            // Note: The provided API docs show /api/experimental/package-index/ returns everything.
            // There is also /api/cyberstorm/community/{community_id}/ but per docs package-index is efficient stream.
            // Let's use the package index and filter for ASKA if we can identify ASKA mods, 
            // OR use the community packages endpoint: /api/experimental/community/{community}/packages/
            
            // Let's try the community identifier first as it's more efficient for just one game.
            // If that fails, we might need to fallback or investigation.
            // For this implementation, I'll target the community packages endpoint.
            
            // Confirmed working endpoint: /c/{CommunityId}/api/v1/package/
            // This returns a JSON array of packages directly.
            var endpoint = $"/c/{CommunityId}/api/v1/package/";
            
            _logger.Information("Fetching packages from {Endpoint}...", endpoint);
            
            var packages = await _httpClient.GetFromJsonAsync<List<PackageIndexEntry>>(endpoint);
            
            if (packages != null)
            {
                _packageIndexCache = packages;
                _lastCacheUpdate = DateTime.UtcNow;
                _logger.Information("Fetched {Count} packages from Thunderstore", _packageIndexCache.Count);
                return _packageIndexCache;
            }
            
            return new List<PackageIndexEntry>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch Thunderstore package index");
            return new List<PackageIndexEntry>();
        }
    }
}
