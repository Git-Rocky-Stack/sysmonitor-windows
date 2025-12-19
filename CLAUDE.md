# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build all projects
dotnet build SysMonitor.sln

# Build specific configuration/platform
dotnet build -c Release -p:Platform=x64

# Run the application
dotnet run --project src/SysMonitor.App

# Run all tests
dotnet test

# Run a single test class
dotnet test --filter "FullyQualifiedName~CpuMonitorTests"

# Run a specific test
dotnet test --filter "FullyQualifiedName~CpuMonitorTests.GetUsage_ShouldReturnValidPercentage"

# Publish self-contained release (x64)
dotnet publish src/SysMonitor.App/SysMonitor.App.csproj -c Release -r win-x64 --self-contained true -o publish/

# Full release build with installer (requires Inno Setup)
.\Build-Release.ps1

# Release build with code signing
.\Build-Release.ps1 -SignCode -UseSelfSignedCert
```

## Architecture

### Solution Structure (3 projects)

```
SysMonitor.sln
├── src/SysMonitor.App      # WinUI 3 frontend (WinExe)
├── src/SysMonitor.Core     # Core library (class library)
└── tests/SysMonitor.Tests  # xUnit tests
```

### MVVM Pattern

**ViewModels** (`src/SysMonitor.App/ViewModels/`) use CommunityToolkit.Mvvm:
- Inherit from `ObservableObject`
- Use `[ObservableProperty]` for bindable properties
- Use `[RelayCommand]` for commands
- Receive services via constructor injection

**Views** (`src/SysMonitor.App/Views/`) are XAML pages:
- Each `FooPage.xaml` pairs with `FooViewModel.cs`
- DataContext set to ViewModel via DI

### Dependency Injection

All services and ViewModels registered in `App.xaml.cs`:
- Core services registered as singletons
- ViewModels registered as transient
- Access services via `App.GetService<T>()`

### Core Services Organization

**Monitors** (`src/SysMonitor.Core/Services/Monitors/`):
- `CpuMonitor`, `MemoryMonitor`, `DiskMonitor`, `BatteryMonitor`
- `NetworkMonitor`, `ProcessMonitor`, `TemperatureMonitor`
- Use LibreHardwareMonitor for hardware access

**Cleaners** (`src/SysMonitor.Core/Services/Cleaners/`):
- `TempFileCleaner`, `BrowserCacheCleaner`
- `RegistryCleaner` (with `ElevatedRegistryHelper` for UAC elevation)
- `BrowserPrivacyCleaner`

**Optimizers** (`src/SysMonitor.Core/Services/Optimizers/`):
- `StartupOptimizer` - manages startup programs via TaskScheduler
- `MemoryOptimizer` - trims process working sets

**Utilities** (`src/SysMonitor.Core/Services/Utilities/`):
- File tools: `LargeFileFinder`, `DuplicateFinder`, `FileConverter`
- PDF: `PdfTools`, `PdfEditor` (using PDFsharp)
- Network: `NetworkMapper`, `WiFiAnalyzer`, `BluetoothAnalyzer`
- System: `InstalledProgramsService`, `DriveWiper`, `HealthCheckService`
- Backup: `BackupService`, `SystemRestoreService`

### Key Dependencies

- **WinUI 3** (Windows App SDK 1.5) - UI framework
- **LibreHardwareMonitor** - CPU/GPU/temperature monitoring
- **LiveCharts2** - Real-time performance charts
- **Entity Framework Core SQLite** - Data persistence
- **PDFsharp** - PDF manipulation
- **TaskScheduler** - Windows Task Scheduler integration
- **Serilog** - Logging (logs to `%LocalAppData%\SysMonitor\Logs\`)

### Platform Support

Builds for x86, x64, and ARM64 Windows (min Windows 10 1809).
Self-contained deployment includes Windows App SDK runtime.
