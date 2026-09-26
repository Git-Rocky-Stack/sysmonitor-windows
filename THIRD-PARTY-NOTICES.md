# Third-Party Notices

STX.1 System Monitor is itself released under the [MIT License](LICENSE). It ships as a
self-contained build, so the components below are redistributed inside the installed
application, and each keeps its own terms.

All of them are compatible with MIT distribution, but three carry obligations beyond
attribution: **LibreHardwareMonitorLib (MPL-2.0)** requires that recipients be told how
to obtain that library's source, **Serilog (Apache-2.0)** requires its notices be
preserved, and the **fonts (SIL Open Font License 1.1)** must travel with their
copyright notices and the license. All three are satisfied below.

Every package entry was read from the package's own `.nuspec` at the exact version this
project references, not from memory, and every font entry from the font's own name
table. To re-check a package line:

```bash
grep -o '<license[^>]*>[^<]*</license>' \
  "$USERPROFILE/.nuget/packages/<package>/<version>/<package>.nuspec"
```

Direct package references only - see `src/SysMonitor.App/SysMonitor.App.csproj` and
`src/SysMonitor.Core/SysMonitor.Core.csproj`. Transitive dependencies carry their own
terms, which are available through each project's own distribution.

---

## Mozilla Public License 2.0

### LibreHardwareMonitorLib 0.9.3
- Source: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- License: https://www.mozilla.org/en-US/MPL/2.0/

This library supplies the temperature sensors
(`src/SysMonitor.Core/Services/Monitors/TemperatureMonitor.cs:92`) and the fan speed
sensors (`:131-163`). It is the only place it is used - CPU and memory usage come from
`GetSystemTimes` and WMI instead (`src/SysMonitor.Core/Services/Monitors/CpuSampler.cs:134`).

MPL-2.0 requires that recipients of the executable form be told how to obtain the
Source Code Form of the covered software. The source is at the repository link above,
and the version used here is tagged `v0.9.3`. The MPL applies to that library's own
files; it does not extend to the rest of this application.

---

## Apache License 2.0

Full text: https://www.apache.org/licenses/LICENSE-2.0

| Component | Version | Project |
|---|---|---|
| Serilog | 4.0.0 | https://serilog.net/ |
| Serilog.Extensions.Hosting | 8.0.0 | https://github.com/serilog/serilog-extensions-hosting |
| Serilog.Sinks.File | 5.0.0 | https://serilog.net/ |
| Serilog.Sinks.Debug | 2.0.0 | https://github.com/serilog/serilog-sinks-debug |

---

## MIT License

| Component | Version | Project |
|---|---|---|
| CommunityToolkit.Mvvm | 8.2.2 | https://github.com/CommunityToolkit/dotnet |
| LiveChartsCore.SkiaSharpView.WinUI | 2.0.0-rc2 | https://livecharts.dev/ |
| LiveChartsCore.Behaviours | 2.0.0-rc2 | https://livecharts.dev/ |
| PDFsharp | 6.1.1 | https://docs.pdfsharp.net/ |
| DocumentFormat.OpenXml | 3.5.1 | https://github.com/dotnet/Open-XML-SDK |
| TaskScheduler | 2.11.0 | https://github.com/dahall/TaskScheduler |
| H.NotifyIcon.WinUI | 2.1.3 | https://github.com/HavenDV/H.NotifyIcon |
| Microsoft.EntityFrameworkCore.Sqlite | 8.0.31 | https://dot.net/ |
| Microsoft.Extensions.Hosting | 8.0.1 | https://dot.net/ |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | https://dot.net/ |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3 | https://dot.net/ |
| System.Management | 8.0.0 | https://dot.net/ |
| System.Drawing.Common | 8.0.31 | https://dot.net/ |
| System.Diagnostics.PerformanceCounter | 8.0.1 | https://dot.net/ |

Standard MIT terms: permission to use, copy, modify, merge, publish, distribute,
sublicense and sell, provided the copyright notice and permission notice are included,
and with no warranty. Each project's own copyright line is in its repository.

---

## SIL Open Font License 1.1

The interface is set in four typefaces, shipped as TrueType files in
`src/SysMonitor.App/Assets/Fonts`. Each family's copyright notice and the full license
sit beside its fonts as `OFL-<family>.txt`, which is what condition 2 of the license
asks for. Full text: https://openfontlicense.org

| Family | Copyright | Project |
|---|---|---|
| Public Sans | Copyright 2015 The Public Sans Project Authors | https://github.com/uswds/public-sans |
| Archivo | Copyright 2020 The Archivo Project Authors | https://github.com/Omnibus-Type/Archivo |
| Departure Mono | Copyright 2022-2024 Helena Zhang | https://helenazhang.com |
| Iosevka | Copyright 2015-2023, Renzhi Li (aka. Belleve Invis, belleve@typeof.net) | https://github.com/be5invis/Iosevka |

They are built from the Latin web fonts STX.1's sister application, System-X, serves
(`scripts/build-fonts.py`). Public Sans and Archivo are variable fonts there; what ships
are static instances at the weights and widths the design uses, each named as a family
of its own, which makes them Modified Versions under the license. No family declares a
Reserved Font Name. Departure Mono and Iosevka are converted from WOFF2 to TrueType and
otherwise unchanged.

---

## Microsoft software licenses

These ship under Microsoft's own terms rather than an SPDX expression. Their
`.nuspec` records `<license type="file">`, and the license file travels inside the
package.

| Component | Version | Terms |
|---|---|---|
| Microsoft.WindowsAppSDK | 1.5.240627000 | `license.txt` inside the package |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.1 | https://aka.ms/WinSDKProjectURL |
| Microsoft.Graphics.Win2D | 1.2.0 | http://go.microsoft.com/fwlink/?LinkID=519078 |

Because `WindowsAppSDKSelfContained` is enabled
(`src/SysMonitor.App/SysMonitor.App.csproj:12`), the Windows App SDK runtime is
redistributed with the application under Microsoft's redistribution terms.

---

## Build-time only

These are used to build and test the project and are **not** redistributed with the
application:

xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, Moq, FluentAssertions,
coverlet.collector - see `tests/SysMonitor.Tests/SysMonitor.Tests.csproj`.
