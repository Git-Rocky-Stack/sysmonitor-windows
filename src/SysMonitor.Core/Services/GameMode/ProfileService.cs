using System.Text.Json;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.GameMode;

/// <summary>
/// Service for managing performance profiles with JSON storage.
/// </summary>
public class ProfileService : IProfileService
{
    private readonly string _profilesPath;
    private readonly IGameModeService _gameModeService;
    private readonly List<PerformanceProfile> _profiles = new();
    private PerformanceProfile? _activeProfile;

    // Power plan GUIDs
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    public IReadOnlyList<PerformanceProfile> Profiles => _profiles.AsReadOnly();
    public PerformanceProfile? ActiveProfile => _activeProfile;

    public event EventHandler<PerformanceProfile?>? ProfileChanged;

    public ProfileService(IGameModeService gameModeService)
    {
        _gameModeService = gameModeService;
        _profilesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysMonitor", "profiles.json");

        // Ensure directory exists
        var directory = Path.GetDirectoryName(_profilesPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task LoadProfilesAsync()
    {
        _profiles.Clear();

        if (File.Exists(_profilesPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_profilesPath);
                var data = JsonSerializer.Deserialize<ProfilesData>(json);
                if (data?.Profiles != null)
                {
                    _profiles.AddRange(data.Profiles);
                }
            }
            catch
            {
                // Failed to load, create defaults
            }
        }

        // Create default profiles if none exist
        if (_profiles.Count == 0)
        {
            CreateDefaultProfiles();
            await SaveAllProfilesAsync();
        }
    }

    private void CreateDefaultProfiles()
    {
        // Maximum Performance
        _profiles.Add(new PerformanceProfile
        {
            Name = "Maximum Performance",
            Description = "Kills browsers, chat apps, cloud sync for maximum performance",
            IsDefault = true,
            PowerPlanGuid = HighPerformanceGuid,
            OptimizeMemory = true,
            ProcessesToKill = new List<string>
            {
                "chrome", "firefox", "msedge", "opera", "brave", "vivaldi",
                "discord", "slack", "teams", "skype", "zoom", "telegram", "whatsapp",
                "spotify", "itunes",
                "onedrive", "dropbox", "googledrivesync",
                "steamwebhelper"
            }
        });

        // Balanced Gaming
        _profiles.Add(new PerformanceProfile
        {
            Name = "Balanced Gaming",
            Description = "Kills browsers and cloud sync, keeps chat apps",
            IsDefault = true,
            PowerPlanGuid = HighPerformanceGuid,
            OptimizeMemory = true,
            ProcessesToKill = new List<string>
            {
                "chrome", "firefox", "msedge", "opera", "brave", "vivaldi",
                "onedrive", "dropbox", "googledrivesync",
                "steamwebhelper"
            }
        });

        // Streaming Mode
        _profiles.Add(new PerformanceProfile
        {
            Name = "Streaming Mode",
            Description = "Keeps Discord and OBS for streaming",
            IsDefault = true,
            PowerPlanGuid = HighPerformanceGuid,
            OptimizeMemory = false,
            ProcessesToKill = new List<string>
            {
                "chrome", "firefox", "msedge", "opera", "brave", "vivaldi",
                "slack", "teams", "skype", "zoom", "telegram",
                "spotify", "itunes",
                "onedrive", "dropbox", "googledrivesync"
            }
        });
    }

    public async Task SaveProfileAsync(PerformanceProfile profile)
    {
        var existing = _profiles.FindIndex(p => p.Id == profile.Id);
        if (existing >= 0)
        {
            _profiles[existing] = profile;
        }
        else
        {
            _profiles.Add(profile);
        }

        await SaveAllProfilesAsync();
    }

    public async Task DeleteProfileAsync(string profileId)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile != null && !profile.IsDefault)
        {
            _profiles.Remove(profile);
            await SaveAllProfilesAsync();

            if (_activeProfile?.Id == profileId)
            {
                _activeProfile = null;
                ProfileChanged?.Invoke(this, null);
            }
        }
    }

    public PerformanceProfile CreateDefaultProfile(string name)
    {
        return new PerformanceProfile
        {
            Name = name,
            Description = "Custom profile",
            IsDefault = false,
            PowerPlanGuid = HighPerformanceGuid,
            OptimizeMemory = true,
            ProcessesToKill = new List<string>
            {
                "chrome", "firefox", "msedge"
            }
        };
    }

    public async Task ApplyProfileAsync(string profileId)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;

        _activeProfile = profile;

        // Enable Game Mode with profile settings
        if (!_gameModeService.IsEnabled)
        {
            await _gameModeService.EnableAsync();
        }

        ProfileChanged?.Invoke(this, profile);

        // Save last active profile
        await SaveAllProfilesAsync();
    }

    public async Task DeactivateProfileAsync()
    {
        if (_activeProfile != null)
        {
            _activeProfile = null;

            if (_gameModeService.IsEnabled)
            {
                await _gameModeService.DisableAsync();
            }

            ProfileChanged?.Invoke(this, null);
            await SaveAllProfilesAsync();
        }
    }

    public PerformanceProfile? GetProfile(string profileId)
    {
        return _profiles.FirstOrDefault(p => p.Id == profileId);
    }

    private async Task SaveAllProfilesAsync()
    {
        var data = new ProfilesData
        {
            Profiles = _profiles,
            LastActiveProfileId = _activeProfile?.Id
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_profilesPath, json);
    }
}
