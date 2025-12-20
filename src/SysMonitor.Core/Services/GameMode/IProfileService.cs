using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.GameMode;

/// <summary>
/// Service for managing performance profiles.
/// </summary>
public interface IProfileService
{
    /// <summary>
    /// All available profiles.
    /// </summary>
    IReadOnlyList<PerformanceProfile> Profiles { get; }

    /// <summary>
    /// Currently active profile (null if none).
    /// </summary>
    PerformanceProfile? ActiveProfile { get; }

    /// <summary>
    /// Event raised when the active profile changes.
    /// </summary>
    event EventHandler<PerformanceProfile?>? ProfileChanged;

    /// <summary>
    /// Load profiles from storage.
    /// </summary>
    Task LoadProfilesAsync();

    /// <summary>
    /// Save a profile (creates new or updates existing).
    /// </summary>
    Task SaveProfileAsync(PerformanceProfile profile);

    /// <summary>
    /// Delete a profile by ID.
    /// </summary>
    Task DeleteProfileAsync(string profileId);

    /// <summary>
    /// Create a default profile with common settings.
    /// </summary>
    PerformanceProfile CreateDefaultProfile(string name);

    /// <summary>
    /// Apply a profile by ID.
    /// </summary>
    Task ApplyProfileAsync(string profileId);

    /// <summary>
    /// Deactivate the current profile.
    /// </summary>
    Task DeactivateProfileAsync();

    /// <summary>
    /// Get a profile by ID.
    /// </summary>
    PerformanceProfile? GetProfile(string profileId);
}
