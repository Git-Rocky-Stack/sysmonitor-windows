# STX.1 System Monitor - Features & User Guide

**Strategic. Excellence. Engineered.**

Version 1.0.0 | Windows Desktop Application | Built with WinUI 3 & .NET 8

---

## Table of Contents

1. [Overview](#overview)
2. [Getting Started](#getting-started)
3. [Dashboard](#dashboard)
4. [System Monitoring](#system-monitoring)
5. [System Optimization](#system-optimization)
6. [Privacy & Security](#privacy--security)
7. [Utilities](#utilities)
8. [Settings](#settings)
9. [Keyboard Shortcuts](#keyboard-shortcuts)
10. [Troubleshooting](#troubleshooting)

---

## Overview

STX.1 System Monitor is a comprehensive Windows system utility that provides real-time hardware monitoring, system optimization, privacy protection, and productivity tools. Designed with a sleek AMOLED dark theme, it offers professional-grade features in an intuitive interface.

### Key Features at a Glance

| Category | Features |
|----------|----------|
| **Monitoring** | CPU, GPU, Memory, Disk, Battery, Temperature, Network, Processes |
| **Optimization** | System Cleaner, Registry Cleaner, Startup Manager, Memory Optimizer |
| **Privacy** | Browser Privacy Cleaner, Secure File Wiper, Data Management |
| **Utilities** | Backup Manager, Driver Updater, PDF Tools, Image Tools, File Compression |
| **Networking** | WiFi Manager, Bluetooth Manager, Network Mapper |

---

## Getting Started

### System Requirements

- **OS:** Windows 10 (version 1903+) or Windows 11
- **Architecture:** x64, x86, or ARM64
- **Runtime:** .NET 8.0 Desktop Runtime
- **RAM:** 4 GB minimum (8 GB recommended)
- **Disk Space:** 100 MB for installation

### First Launch

1. Launch STX.1 System Monitor
2. The Dashboard will display your system's health score
3. Use the left navigation menu to access different features
4. Configure your preferences in Settings

---

## Dashboard

The Dashboard provides a comprehensive overview of your system's health and performance.

### Health Score

A real-time score (0-100) indicating overall system health:

| Score | Rating | Description |
|-------|--------|-------------|
| 90-100 | Excellent | System running optimally |
| 75-89 | Good | Minor issues, no action needed |
| 60-74 | Fair | Some attention recommended |
| 40-59 | Poor | Action recommended |
| 0-39 | Critical | Immediate attention required |

### Dashboard Widgets

- **CPU Usage** - Current processor utilization percentage
- **Memory Usage** - RAM usage with used/total GB
- **Disk Usage** - Primary drive space utilization
- **Battery Status** - Charge level and charging state (laptops)
- **Network Status** - Connection type and current speeds
- **Temperature** - CPU and GPU temperatures with status
- **System Uptime** - Time since last restart
- **Cleanable Space** - Amount of junk files detected

### Quick Actions

| Action | Description |
|--------|-------------|
| **Quick Clean** | Instantly removes temporary files and browser cache |
| **Optimize Memory** | Frees up RAM by clearing unused memory |
| **Export Report** | Generates a detailed system diagnostic report (saved to Documents) |

---

## System Monitoring

### CPU Monitor

Real-time CPU performance monitoring:

- **Usage Percentage** - Current CPU utilization
- **Core Count** - Physical and logical processor count
- **Clock Speed** - Current and maximum frequency
- **Temperature** - CPU temperature with status indicators
- **Per-Core Usage** - Individual core utilization (where available)

### GPU Monitor

Graphics card monitoring:

- **GPU Temperature** - Current temperature with warning levels
- **GPU Model** - Detected graphics adapter name
- **Status Indicators** - Cool, Normal, Warm, Hot, Critical

### Memory Monitor

RAM usage and management:

- **Total Memory** - Installed RAM capacity
- **Used/Available** - Current memory allocation
- **Usage Percentage** - Memory utilization level
- **Memory Optimization** - One-click RAM cleanup

### Disk Monitor

Storage drive information:

- **All Drives** - View all connected storage devices
- **Space Usage** - Used/free/total for each drive
- **Drive Health** - SMART status (where supported)
- **Usage Percentage** - Visual bar for each drive

### Battery Monitor

Power management (laptops/tablets):

- **Charge Level** - Current battery percentage
- **Charging Status** - Charging, Plugged In, or On Battery
- **Health Alerts** - Low and critical battery warnings
- **Time Remaining** - Estimated battery life

### Temperature Monitor

Hardware temperature tracking:

- **CPU Temperature** - Processor thermal reading
- **GPU Temperature** - Graphics card thermal reading
- **Temperature Status** - Color-coded health indicators
- **Warning Thresholds** - Configurable alert levels

Temperature Status Levels:
| Status | CPU Range | GPU Range |
|--------|-----------|-----------|
| Cool | Below 113°F (45°C) | Below 113°F |
| Normal | 113-149°F (45-65°C) | 113-149°F |
| Warm | 149-176°F (65-80°C) | 149-176°F |
| Hot | 176-194°F (80-90°C) | 176-194°F |
| Critical | Above 194°F (90°C) | Above 194°F |

### Network Monitor

Network connectivity and speed:

- **Connection Status** - Connected/Disconnected
- **Connection Type** - WiFi, Ethernet, etc.
- **Download Speed** - Real-time download rate
- **Upload Speed** - Real-time upload rate
- **IP Address** - Current network address
- **Adapter Name** - Active network adapter

### Process Manager

Running application management:

- **Process List** - All running processes
- **CPU/Memory Usage** - Per-process resource consumption
- **Process Details** - Path, PID, and more
- **End Process** - Terminate unresponsive applications

---

## System Optimization

### System Cleaner

Remove junk files to free up disk space:

**Scan Categories:**
- Windows Temporary Files
- User Temporary Files
- Windows Update Cache
- Recycle Bin Contents
- Browser Cache (all major browsers)
- Thumbnail Cache
- Log Files

**How to Use:**
1. Click **Scan** to analyze your system
2. Review detected items by category
3. Uncheck items you want to keep
4. Click **Clean** to remove selected items

### Registry Cleaner

Fix Windows Registry issues:

**Issue Types Detected:**
- Invalid file associations
- Orphaned software entries
- Broken shortcuts
- Obsolete startup entries
- Invalid COM/ActiveX entries

**Safety Features:**
- Automatic backup before cleaning
- Selective cleaning options
- Undo capability via backup restore

**How to Use:**
1. Click **Scan** to analyze the registry
2. Review detected issues
3. Click **Fix Selected** to repair issues
4. Backup is automatically created

### Startup Manager

Control programs that launch at startup:

**Features:**
- View all startup programs
- Enable/disable startup items
- Impact assessment (Low/Medium/High)
- Program location and details

**Impact Levels:**
| Impact | Description |
|--------|-------------|
| High | Significantly slows boot time |
| Medium | Moderate impact on boot |
| Low | Minimal impact on boot |

**How to Use:**
1. View startup programs list
2. Select a program to see details
3. Click **Disable** to prevent auto-start
4. Click **Enable** to restore auto-start

### Scheduled Cleaning

Automate system maintenance:

- **Schedule Options** - Daily, Weekly, Monthly
- **Time Selection** - Choose when cleaning runs
- **Category Selection** - Choose what to clean
- **Notification Options** - Get alerts on completion

### Health Check

Comprehensive system analysis:

**Checks Performed:**
- Disk space analysis
- Memory usage patterns
- Startup program impact
- Browser data accumulation
- Registry health
- System file integrity
- Temperature trends

**Results Include:**
- Health Score (0-100)
- Health Grade (A through F)
- Critical issues count
- Warning count
- Potential space savings
- Recommended actions

**How to Use:**
1. Click **Run Health Check**
2. Wait for analysis to complete
3. Review issues by severity
4. Click **Fix** on individual issues
5. Create restore point before major fixes

---

## Privacy & Security

### Browser Privacy Cleaner

Clean browser data across all installed browsers:

**Supported Browsers:**
- Google Chrome
- Mozilla Firefox
- Microsoft Edge
- Opera
- Brave
- Vivaldi

**Data Types:**
| Type | Risk Level | Description |
|------|------------|-------------|
| Cache | Safe | Temporary internet files |
| Cookies | Low | Session and tracking data |
| History | Medium | Browsing history |
| Passwords | High | Saved login credentials |
| Form Data | Medium | Auto-fill information |

**Risk-Based Filtering:**
- Toggle visibility by risk level
- Safe items selected by default
- High-risk items require explicit selection

**How to Use:**
1. Click **Scan** to detect browser data
2. Filter by risk level using toggles
3. Select items to remove
4. Click **Clean** to delete selected data

### Secure File Wiper

Permanently delete sensitive files beyond recovery:

**Wipe Methods:**

| Method | Passes | Security | Speed |
|--------|--------|----------|-------|
| Quick (1-Pass) | 1 | Basic | Fast |
| DoD 3-Pass | 3 | Standard | Medium |
| DoD 7-Pass | 7 | High | Slow |
| Gutmann 35-Pass | 35 | Maximum | Very Slow |

**How It Works:**
- Overwrites file data with patterns
- Multiple passes prevent recovery
- Works on files and folders
- Progress indicator shows status

**How to Use:**
1. Click **Add Files** or **Add Folder**
2. Select wipe method based on sensitivity
3. Click **Wipe** to permanently destroy
4. Confirm the action (cannot be undone)

---

## Utilities

### Backup Manager

Create and manage file backups:

**Backup Types:**
- **Full Backup** - Complete copy of selected files
- **Incremental** - Only new/changed files since last backup
- **Differential** - Changes since last full backup

**Destination Options:**
- Local Drive
- External Drive
- Network Location

**Features:**
- Encryption support (AES-256)
- Compression options (None, Normal, Maximum)
- Volume Shadow Copy (VSS) support
- Backup verification
- Retention policies (keep last N backups)
- Include/exclude patterns
- Hidden files option

**Wizard Steps:**
1. **Select Type** - Choose backup type
2. **Select Source** - Add folders to back up
3. **Select Destination** - Choose where to save
4. **Options** - Configure encryption and compression
5. **Running** - Progress display
6. **Complete** - Results summary

**Backup History:**
- View all completed backups
- Restore from any backup
- Delete old backups
- Verify backup integrity

### Driver Updater

Scan and manage system drivers:

**Features:**
- Full driver inventory scan
- Problem driver detection
- Outdated driver identification
- Unsigned driver warnings
- Device Manager integration
- Windows Update access

**Driver Status:**
| Status | Icon | Description |
|--------|------|-------------|
| Up to Date | Green | Driver is current |
| Outdated | Yellow | Newer version may exist |
| Problem | Red | Driver has issues |
| Unsigned | Orange | Not digitally signed |

**How to Use:**
1. Click **Scan Drivers** to inventory
2. Review driver list and status
3. Filter to show problems only
4. Use **Open Device Manager** for updates
5. Use **Windows Update** for official drivers

### PDF Tools

Comprehensive PDF management:

**Merge PDFs:**
1. Add multiple PDF files
2. Arrange order with up/down buttons
3. Click **Merge** to combine
4. Save the merged PDF

**Split PDFs:**
- Split all pages into separate files
- Split by page range
- Split by pages per file

**Extract Pages:**
- Select start and end pages
- Extract to new PDF file

**Convert to PDF:**
- Convert images (JPG, PNG, BMP) to PDF
- Convert text files to PDF

**Sign PDFs:**
- Draw signature with mouse/touchscreen
- Upload signature image
- Type signature in cursive font
- Add signer name and date
- Position signature on page

### Image Tools

Image processing and optimization:

**Compress Images:**
- Reduce file size without visible quality loss
- Adjustable compression quality (1-100%)
- Batch processing support

**Convert Images:**
- Supported formats: JPEG, PNG, BMP, GIF, TIFF, WebP
- Quality settings for lossy formats
- Batch conversion

**Resize Images:**
- Set custom dimensions
- Maintain aspect ratio option
- Preview before saving

**Image Info:**
- Dimensions (width x height)
- Color depth
- DPI resolution
- File format details

### File Tools

File compression and archiving:

**Compression Formats:**
- ZIP (most compatible)
- 7Z (best compression)
- TAR.GZ (Unix compatible)

**How to Use:**
1. Select file or folder to compress
2. Choose compression format
3. Click **Compress**
4. View compression results

### Duplicate Finder

Find and remove duplicate files:

**Features:**
- Scan by file hash (accurate matching)
- Scan by name and size (fast)
- Filter by file type
- Filter by minimum size
- Preview duplicates before deletion

**How to Use:**
1. Select folder(s) to scan
2. Choose matching method
3. Click **Scan**
4. Review duplicate groups
5. Select duplicates to remove (keeps one copy)
6. Click **Delete Selected**

### Large Files Finder

Locate space-consuming files:

**Features:**
- Scan any drive or folder
- Sort by file size
- Filter by minimum size
- Filter by file type
- Quick delete or move options

**How to Use:**
1. Select drive or folder
2. Set minimum file size
3. Click **Scan**
4. Review large files list
5. Delete or relocate as needed

### Installed Programs

View and manage installed applications:

**Features:**
- Complete list of installed programs
- Installation date and size
- Publisher information
- Quick uninstall access
- Search and filter

### Network Mapper

Discover devices on your network:

**Features:**
- Scan local network
- Identify connected devices
- Show IP and MAC addresses
- Device type detection
- Network topology view

### WiFi Manager

Wireless network management:

**Features:**
- View available networks
- Current connection details
- Signal strength
- Security type
- Connect/disconnect

### Bluetooth Manager

Bluetooth device management:

**Features:**
- View paired devices
- Discover new devices
- Connect/disconnect
- Device information

### System Info

Detailed system specifications:

**Categories:**
- Operating System (name, version, build)
- Processor (model, cores, speed)
- Memory (total, type, speed)
- Graphics (GPU model, driver)
- Storage (drives, capacity)
- Network (adapters, MAC)
- BIOS/UEFI information

---

## Settings

### Appearance

- **Theme** - Light, Dark, or System default

### Behavior

- **Run at Startup** - Launch app when Windows starts
- **Minimize to Tray** - Keep running in system tray
- **Show Notifications** - Enable/disable alerts

### Monitoring

- **Refresh Interval** - Data update frequency (1-10 seconds)
- **Auto-Optimize Memory** - Automatic RAM optimization
- **Memory Threshold** - Trigger level for auto-optimization

### Alert Thresholds

**Temperature Alerts:**
- CPU Warning Temperature (default: 167°F / 75°C)
- CPU Critical Temperature (default: 194°F / 90°C)
- GPU Warning Temperature (default: 176°F / 80°C)
- GPU Critical Temperature (default: 203°F / 95°C)

**Battery Alerts:**
- Low Battery Warning (default: 20%)
- Critical Battery Warning (default: 10%)

### Data & Privacy

- **Clear All Data** - Reset all app settings and data
- **Privacy Notice** - All data stays local on your device

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+D | Open Dashboard |
| Ctrl+C | Open Cleaner |
| Ctrl+S | Open Settings |
| Ctrl+R | Refresh current page |
| F5 | Refresh data |
| Esc | Cancel current operation |

---

## Troubleshooting

### App Won't Start

1. Ensure .NET 8.0 Desktop Runtime is installed
2. Run as Administrator
3. Check Windows Event Viewer for errors

### Temperature Readings Show N/A

1. Ensure LibreHardwareMonitor drivers are loaded
2. Run app as Administrator
3. Some hardware may not support temperature sensors

### Cleaner Not Finding All Files

1. Some system files require Administrator privileges
2. Locked files (in use) cannot be deleted
3. Antivirus may protect certain files

### Settings Not Saving

1. App stores settings in `%LocalAppData%\SysMonitor\`
2. Ensure write permissions to that folder
3. Check for disk space issues

### High CPU Usage

1. Reduce refresh interval in Settings
2. Close unused monitoring pages
3. Disable auto-refresh when not needed

### Network Features Not Working

1. Ensure Windows Firewall allows the app
2. Check network adapter status
3. Run as Administrator for full network access

---

## Support

For issues and feature requests:
- GitHub: [Report Issues](https://github.com/strategia/sysmonitor-windows/issues)
- Email: support@strategia.com

---

**STX.1 System Monitor**
*Strategic. Excellence. Engineered.*

Copyright 2024 Strategia / Rocky Stack. All rights reserved.
