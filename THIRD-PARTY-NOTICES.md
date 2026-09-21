# Third-Party Notices

STX.1 System Monitor ships as a self-contained build, so the components below are
redistributed inside the installed application.

Every entry was read from the package's own `.nuspec` at the exact version this
project references, not from memory. To re-check any line:

```bash
grep -o '<license[^>]*>[^<]*</license>' \
  "$USERPROFILE/.nuget/packages/<package>/<version>/<package>.nuspec"
```

Direct package references only — see `src/SysMonitor.App/SysMonitor.App.csproj` and
`src/SysMonitor.Core/SysMonitor.Core.csproj`. Transitive dependencies carry their own
terms, which are available through each project's own distribution.

---

## Mozilla Public License 2.0

### LibreHardwareMonitorLib 0.9.3
- Source: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- License: https://www.mozilla.org/en-US/MPL/2.0/

This library provides the CPU, GPU, temperature and fan sensor readings.

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
coverlet.collector — see `tests/SysMonitor.Tests/SysMonitor.Tests.csproj`.
