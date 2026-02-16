using ModManager.Core.Interfaces;
using ModManager.Core.Models;
using ModManager.Core.Services;
using Serilog;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ModManager.Core.Services;

/// <summary>
/// Lightweight profile management service for mod configurations
/// </summary>
public class ProfileService
{
    private readonly ILogger _logger;
    private readonly IModRepository _modRepository;
    private readonly string _profilesPath;

    public ProfileService(ILogger logger, IModRepository modRepository, string askaPath)
    {
        _logger = logger;
        _modRepository = modRepository;
        _profilesPath = Path.Combine(askaPath, "BepInEx", "Profiles");
        Directory.CreateDirectory(_profilesPath);
    }

    /// <summary>
    /// Represents a mod profile
    /// </summary>
    public class Profile
    {
        public string Name { get; set; } = string.Empty;
        public List<string> EnabledMods { get; set; } = new();
    }

    /// <summary>
    /// Gets all available profiles
    /// </summary>
    public IEnumerable<Profile> GetProfiles()
    {
        try
        {
            var profileFiles = Directory.GetFiles(_profilesPath, "*.json");
            var profiles = new List<Profile>();

            foreach (var file in profileFiles)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var profile = JsonSerializer.Deserialize<Profile>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (profile != null)
                    {
                        profile.Name = Path.GetFileNameWithoutExtension(file);
                        profiles.Add(profile);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to load profile: {File}", file);
                }
            }

            return profiles.OrderBy(p => p.Name);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to list profiles");
            return Enumerable.Empty<Profile>();
        }
    }

    /// <summary>
    /// Switches to a profile by name
    /// </summary>
    public async Task<bool> SwitchToProfileAsync(string profileName)
    {
        try
        {
            var profilePath = Path.Combine(_profilesPath, profileName + ".json");
            if (!File.Exists(profilePath))
            {
                _logger.Warning("Profile not found: {Profile}", profileName);
                return false;
            }

            var json = await File.ReadAllTextAsync(profilePath);
            var profile = JsonSerializer.Deserialize<Profile>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (profile == null)
            {
                _logger.Warning("Invalid profile format: {Profile}", profileName);
                return false;
            }

            // Get all installed mods
            var allMods = _modRepository.ListInstalledAsync().GetAwaiter().GetResult();

            // Disable all mods first
            foreach (var mod in allMods.Where(m => m.IsEnabled))
            {
                await _modRepository.SetEnabledAsync(mod.Id, false);
                _logger.Debug("Disabled mod for profile switch: {Mod}", mod.Id);
            }

            // Enable mods from profile
            foreach (var modId in profile.EnabledMods)
            {
                var mod = allMods.FirstOrDefault(m => m.Id == modId);
                if (mod != null)
                {
                    try
                    {
                        await _modRepository.SetEnabledAsync(modId, true);
                        _logger.Debug("Enabled mod from profile: {Mod}", modId);
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.Warning(ex, "Failed to enable mod from profile due to dependencies: {Mod}", modId);
                        // Continue with other mods
                    }
                }
                else
                {
                    _logger.Warning("Mod from profile not installed: {Mod}", modId);
                }
            }

            // Save active profile (simple text file for now)
            var activeProfilePath = Path.Combine(_profilesPath, "active.txt");
            await File.WriteAllTextAsync(activeProfilePath, profileName);

            _logger.Information("Switched to profile: {Profile}", profileName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to switch to profile: {Profile}", profileName);
            return false;
        }
    }

    /// <summary>
    /// Saves the current enabled mods as a new profile
    /// </summary>
    public async Task<bool> SaveCurrentAsProfileAsync(string profileName)
    {
        try
        {
            var allMods = _modRepository.ListInstalledAsync().GetAwaiter().GetResult();
            var enabledMods = allMods.Where(m => m.IsEnabled).Select(m => m.Id).ToList();

            var profile = new Profile
            {
                Name = profileName,
                EnabledMods = enabledMods
            };

            var profilePath = Path.Combine(_profilesPath, profileName + ".json");
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(profilePath, json);

            _logger.Information("Saved current mods as profile: {Profile}", profileName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save profile: {Profile}", profileName);
            return false;
        }
    }

    /// <summary>
    /// Gets the currently active profile name
    /// </summary>
    public string? GetActiveProfile()
    {
        try
        {
            var activeProfilePath = Path.Combine(_profilesPath, "active.txt");
            if (File.Exists(activeProfilePath))
            {
                return File.ReadAllText(activeProfilePath).Trim();
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get active profile");
            return null;
        }
    }
}
