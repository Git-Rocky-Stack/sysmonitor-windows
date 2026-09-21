using System.Text.Json.Serialization;
using SysMonitor.Core.Services.GameMode;

namespace SysMonitor.Core.Models;

/// <summary>
/// Represents a performance profile for Game Mode with customizable settings.
/// </summary>
public class PerformanceProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDefault { get; set; }

    // Profile Settings

    /// <summary>
    /// The background apps this profile moves out of the game's way. Stored under its old name so profiles
    /// saved by earlier versions still load; nothing is killed any more.
    /// </summary>
    [JsonPropertyName("ProcessesToKill")]
    public List<string> BackgroundApps { get; set; } = new();

    /// <summary>What to do with those apps. Lowering them out of the way is the default, and loses nothing.</summary>
    public BackgroundAppAction BackgroundAppAction { get; set; } = BackgroundAppAction.LowerPriority;
    public string PowerPlanGuid { get; set; } = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"; // High Performance
    public bool OptimizeMemory { get; set; } = true;
}

/// <summary>
/// Container for storing profiles in JSON.
/// </summary>
public class ProfilesData
{
    public List<PerformanceProfile> Profiles { get; set; } = new();
    public string? LastActiveProfileId { get; set; }
}
