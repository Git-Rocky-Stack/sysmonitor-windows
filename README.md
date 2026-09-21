# SysMonitor for Windows

A professional Windows system monitoring and optimization application built with WinUI 3 and .NET 8.

## Features

### System Monitoring
- **Dashboard** - Real-time health score with CPU, memory, disk, and battery stats
- **Process Manager** - View and manage running processes
- **Performance Charts** - Live CPU and memory usage graphs

### Optimization Tools
- **System Cleaner** - Remove temporary files and browser cache
- **Startup Manager** - Control programs that run at boot
- **Memory Optimizer** - Trims background processes' working sets, which moves their pages out to the standby list; Windows pages them back when those apps need them again

## Requirements

- Windows 10 version 2004 (build 19041) or later
- .NET 8.0 Runtime
- Visual Studio 2022 (for development)

## Building

1. Open `SysMonitor.sln` in Visual Studio 2022
2. Restore NuGet packages
3. Build the solution (F6)
4. Run the application (F5)

## Project Structure

```
SysMonitor/
├── src/
│   ├── SysMonitor.App/          # WinUI 3 application
│   │   ├── Views/               # XAML pages
│   │   ├── ViewModels/          # MVVM ViewModels
│   │   ├── Converters/          # Value converters
│   │   └── Styles/              # Colors and styles
│   │
│   └── SysMonitor.Core/         # Core library
│       ├── Models/              # Data models
│       ├── Services/
│       │   ├── Monitors/        # CPU, Memory, Disk, Network monitors
│       │   ├── Cleaners/        # Temp file and browser cache cleaners
│       │   └── Optimizers/      # Startup and memory optimizers
│       └── Data/                 # EF Core context and entities (SQLite)
│
└── tests/SysMonitor.Tests/      # Unit tests
```

## Technologies

- .NET 8.0
- WinUI 3 (Windows App SDK 1.5)
- CommunityToolkit.Mvvm
- LibreHardwareMonitor
- Entity Framework Core (SQLite)
- LiveCharts2

## License

MIT. See [LICENSE](LICENSE).
