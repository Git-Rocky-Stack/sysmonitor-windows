using System.Diagnostics;
using System.Text.Json;

namespace SysMonitor.Core.Services.GameMode;

/// <summary>
/// Service that automatically detects running games and enables Game Mode.
/// </summary>
public class AutoGameModeService : IAutoGameModeService
{
    private readonly IGameModeService _gameModeService;
    private readonly string _settingsPath;
    private readonly List<GameDefinition> _knownGames;
    private readonly List<GameDefinition> _customGames = new();
    private readonly List<GameDefinition> _runningGames = new();

    private CancellationTokenSource? _cts;
    private Task? _monitoringTask;
    private bool _autoModeEnabled;
    private bool _gameModeWasAutoEnabled;

    public bool IsMonitoring => _monitoringTask != null && !_monitoringTask.IsCompleted;

    public bool AutoModeEnabled
    {
        get => _autoModeEnabled;
        set
        {
            if (_autoModeEnabled != value)
            {
                _autoModeEnabled = value;
                SaveSettingsAsync().ConfigureAwait(false);

                if (value && !IsMonitoring)
                {
                    _ = StartMonitoringAsync();
                }
                else if (!value && IsMonitoring)
                {
                    _ = StopMonitoringAsync();
                }
            }
        }
    }

    public IReadOnlyList<GameDefinition> KnownGames => _knownGames.AsReadOnly();
    public IReadOnlyList<GameDefinition> CustomGames => _customGames.AsReadOnly();
    public IReadOnlyList<GameDefinition> RunningGames => _runningGames.AsReadOnly();

    public event EventHandler<GameDetectedEventArgs>? GameDetected;
    public event EventHandler<GameDetectedEventArgs>? GameClosed;

    public AutoGameModeService(IGameModeService gameModeService)
    {
        _gameModeService = gameModeService;
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysMonitor", "settings.json");

        // Initialize predefined games list
        _knownGames = CreateKnownGamesList();

        // Load custom games from settings
        LoadCustomGames();

        // Load auto mode setting
        LoadAutoModeSetting();
    }

    private static List<GameDefinition> CreateKnownGamesList()
    {
        return new List<GameDefinition>
        {
            // Valve/Steam Games
            new() { ProcessName = "csgo", DisplayName = "Counter-Strike: GO" },
            new() { ProcessName = "cs2", DisplayName = "Counter-Strike 2" },
            new() { ProcessName = "dota2", DisplayName = "Dota 2" },
            new() { ProcessName = "hl2", DisplayName = "Half-Life 2" },
            new() { ProcessName = "portal2", DisplayName = "Portal 2" },
            new() { ProcessName = "left4dead2", DisplayName = "Left 4 Dead 2" },

            // Riot Games
            new() { ProcessName = "VALORANT-Win64-Shipping", DisplayName = "Valorant" },
            new() { ProcessName = "valorant", DisplayName = "Valorant" },
            new() { ProcessName = "LeagueClient", DisplayName = "League of Legends" },
            new() { ProcessName = "League of Legends", DisplayName = "League of Legends" },

            // Epic Games
            new() { ProcessName = "FortniteClient-Win64-Shipping", DisplayName = "Fortnite" },
            new() { ProcessName = "RocketLeague", DisplayName = "Rocket League" },

            // EA Games
            new() { ProcessName = "apex_legends", DisplayName = "Apex Legends" },
            new() { ProcessName = "r5apex", DisplayName = "Apex Legends" },
            new() { ProcessName = "bf2042", DisplayName = "Battlefield 2042" },
            new() { ProcessName = "FIFA23", DisplayName = "FIFA 23" },
            new() { ProcessName = "FC24", DisplayName = "EA FC 24" },

            // Activision/Blizzard
            new() { ProcessName = "Overwatch", DisplayName = "Overwatch 2" },
            new() { ProcessName = "ModernWarfare", DisplayName = "Call of Duty: MW" },
            new() { ProcessName = "cod", DisplayName = "Call of Duty" },
            new() { ProcessName = "BlackOpsColdWar", DisplayName = "COD: Cold War" },
            new() { ProcessName = "Diablo IV", DisplayName = "Diablo IV" },
            new() { ProcessName = "Hearthstone", DisplayName = "Hearthstone" },
            new() { ProcessName = "StarCraft II", DisplayName = "StarCraft II" },

            // Rockstar
            new() { ProcessName = "GTA5", DisplayName = "GTA V" },
            new() { ProcessName = "RDR2", DisplayName = "Red Dead Redemption 2" },
            new() { ProcessName = "PlayGTAV", DisplayName = "GTA V" },

            // Ubisoft
            new() { ProcessName = "RainbowSix", DisplayName = "Rainbow Six Siege" },
            new() { ProcessName = "ACValhalla", DisplayName = "Assassin's Creed Valhalla" },
            new() { ProcessName = "FarCry6", DisplayName = "Far Cry 6" },

            // FromSoftware
            new() { ProcessName = "eldenring", DisplayName = "Elden Ring" },
            new() { ProcessName = "DarkSoulsIII", DisplayName = "Dark Souls III" },
            new() { ProcessName = "sekiro", DisplayName = "Sekiro" },
            new() { ProcessName = "armoredcore6", DisplayName = "Armored Core VI" },

            // Other Popular Games
            new() { ProcessName = "Minecraft.Windows", DisplayName = "Minecraft" },
            new() { ProcessName = "javaw", DisplayName = "Minecraft (Java)" },
            new() { ProcessName = "PUBG", DisplayName = "PUBG" },
            new() { ProcessName = "TslGame", DisplayName = "PUBG" },
            new() { ProcessName = "rust", DisplayName = "Rust" },
            new() { ProcessName = "RustClient", DisplayName = "Rust" },
            new() { ProcessName = "destiny2", DisplayName = "Destiny 2" },
            new() { ProcessName = "arma3", DisplayName = "Arma 3" },
            new() { ProcessName = "arma3_x64", DisplayName = "Arma 3" },
            new() { ProcessName = "DayZ", DisplayName = "DayZ" },
            new() { ProcessName = "Cyberpunk2077", DisplayName = "Cyberpunk 2077" },
            new() { ProcessName = "HogwartsLegacy", DisplayName = "Hogwarts Legacy" },
            new() { ProcessName = "bg3", DisplayName = "Baldur's Gate 3" },
            new() { ProcessName = "Starfield", DisplayName = "Starfield" },
            new() { ProcessName = "Palworld-Win64-Shipping", DisplayName = "Palworld" },
            new() { ProcessName = "TheForest", DisplayName = "The Forest" },
            new() { ProcessName = "SonsOfTheForest", DisplayName = "Sons of the Forest" },
            new() { ProcessName = "Terraria", DisplayName = "Terraria" },
            new() { ProcessName = "Stardew Valley", DisplayName = "Stardew Valley" },
            new() { ProcessName = "Warframe.x64", DisplayName = "Warframe" },
            new() { ProcessName = "PathOfExile_x64", DisplayName = "Path of Exile" },
            new() { ProcessName = "SMITE", DisplayName = "Smite" },
            new() { ProcessName = "Paladins", DisplayName = "Paladins" },
            new() { ProcessName = "DeadByDaylight-Win64-Shipping", DisplayName = "Dead by Daylight" },
            new() { ProcessName = "Phasmophobia", DisplayName = "Phasmophobia" },
            new() { ProcessName = "Among Us", DisplayName = "Among Us" },
            new() { ProcessName = "FallGuys_client_game", DisplayName = "Fall Guys" },
            new() { ProcessName = "NMS", DisplayName = "No Man's Sky" },
            new() { ProcessName = "Satisfactory", DisplayName = "Satisfactory" },
            new() { ProcessName = "Factorio", DisplayName = "Factorio" },
            new() { ProcessName = "WorldOfTanks", DisplayName = "World of Tanks" },
            new() { ProcessName = "WorldOfWarships", DisplayName = "World of Warships" },
            new() { ProcessName = "WoW", DisplayName = "World of Warcraft" },
            new() { ProcessName = "ffxiv_dx11", DisplayName = "Final Fantasy XIV" },
            new() { ProcessName = "GenshinImpact", DisplayName = "Genshin Impact" },
            new() { ProcessName = "ZenlessZoneZero", DisplayName = "Zenless Zone Zero" },
            new() { ProcessName = "HonkaiStarRail", DisplayName = "Honkai: Star Rail" },
        };
    }

    private void LoadCustomGames()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (settings != null && settings.TryGetValue("CustomGames", out var customGamesElement))
                {
                    var games = customGamesElement.Deserialize<List<GameDefinition>>();
                    if (games != null)
                    {
                        foreach (var game in games)
                        {
                            game.IsCustom = true;
                            _customGames.Add(game);
                        }
                    }
                }
            }
        }
        catch
        {
            // Failed to load custom games
        }
    }

    private void LoadAutoModeSetting()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (settings != null && settings.TryGetValue("AutoGameModeEnabled", out var autoElement))
                {
                    _autoModeEnabled = autoElement.GetBoolean();
                }
            }
        }
        catch
        {
            _autoModeEnabled = false;
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            Dictionary<string, object> settings;

            if (File.Exists(_settingsPath))
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
            }
            else
            {
                settings = new();
            }

            settings["AutoGameModeEnabled"] = _autoModeEnabled;
            settings["CustomGames"] = _customGames;

            var outputJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(_settingsPath, outputJson);
        }
        catch
        {
            // Failed to save settings
        }
    }

    public async Task StartMonitoringAsync()
    {
        if (IsMonitoring) return;

        _cts = new CancellationTokenSource();
        _monitoringTask = MonitorLoopAsync(_cts.Token);
        await Task.CompletedTask;
    }

    public async Task StopMonitoringAsync()
    {
        if (!IsMonitoring) return;

        _cts?.Cancel();

        if (_monitoringTask != null)
        {
            try
            {
                await _monitoringTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _cts?.Dispose();
        _cts = null;
        _monitoringTask = null;

        // Disable Game Mode if it was auto-enabled
        if (_gameModeWasAutoEnabled && _gameModeService.IsEnabled)
        {
            await _gameModeService.DisableAsync();
            _gameModeWasAutoEnabled = false;
        }
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckForGamesAsync();
                await timer.WaitForNextTickAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Continue on errors
            }
        }
    }

    private async Task CheckForGamesAsync()
    {
        var allGames = _knownGames.Concat(_customGames).Where(g => g.IsEnabled).ToList();
        var runningProcessNames = new HashSet<string>(
            Process.GetProcesses().Select(p => p.ProcessName.ToLower()));

        var previouslyRunning = _runningGames.ToList();
        _runningGames.Clear();

        foreach (var game in allGames)
        {
            if (runningProcessNames.Contains(game.ProcessName.ToLower()))
            {
                _runningGames.Add(game);

                // Check if this is a newly detected game
                if (!previouslyRunning.Any(g => g.ProcessName.Equals(game.ProcessName, StringComparison.OrdinalIgnoreCase)))
                {
                    // New game detected
                    var wasAutoEnabled = false;

                    if (_autoModeEnabled && !_gameModeService.IsEnabled)
                    {
                        await _gameModeService.EnableAsync();
                        _gameModeWasAutoEnabled = true;
                        wasAutoEnabled = true;
                    }

                    GameDetected?.Invoke(this, new GameDetectedEventArgs
                    {
                        Game = game,
                        WasAutoEnabled = wasAutoEnabled
                    });
                }
            }
        }

        // Check for games that were closed
        foreach (var game in previouslyRunning)
        {
            if (!_runningGames.Any(g => g.ProcessName.Equals(game.ProcessName, StringComparison.OrdinalIgnoreCase)))
            {
                GameClosed?.Invoke(this, new GameDetectedEventArgs { Game = game });
            }
        }

        // If no games are running and Game Mode was auto-enabled, disable it
        if (_runningGames.Count == 0 && _gameModeWasAutoEnabled && _gameModeService.IsEnabled)
        {
            await _gameModeService.DisableAsync();
            _gameModeWasAutoEnabled = false;
        }
    }

    public async Task AddCustomGameAsync(string processName, string displayName)
    {
        if (_customGames.Any(g => g.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)))
            return;

        _customGames.Add(new GameDefinition
        {
            ProcessName = processName,
            DisplayName = displayName,
            IsCustom = true,
            IsEnabled = true
        });

        await SaveSettingsAsync();
    }

    public async Task RemoveCustomGameAsync(string processName)
    {
        var game = _customGames.FirstOrDefault(g =>
            g.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));

        if (game != null)
        {
            _customGames.Remove(game);
            await SaveSettingsAsync();
        }
    }
}
