using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysMonitor.Core.Services.Utilities;
using System.Collections.ObjectModel;
using Windows.Storage.Pickers;

namespace SysMonitor.App.ViewModels;

public partial class DriveWiperViewModel : ObservableObject
{
    private readonly ILogger _logger;

    private readonly IDriveWiper _driveWiper;

    [ObservableProperty] private ObservableCollection<FileToWipe> _filesToWipe = new();
    [ObservableProperty] private bool _isWiping;
    [ObservableProperty] private bool _hasFiles;
    [ObservableProperty] private string _statusMessage = "Add files or folders to securely delete";
    [ObservableProperty] private WipeMethod _selectedMethod = WipeMethod.DoD3Pass;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _currentFile = "";
    [ObservableProperty] private int _totalFiles;
    [ObservableProperty] private int _completedFiles;

    // Method descriptions
    [ObservableProperty] private string _methodDescription = "";

    public ObservableCollection<WipeMethodOption> WipeMethods { get; } = new()
    {
        new WipeMethodOption(WipeMethod.SinglePass, "1 pass (zeros)", "Writes zeros over the file once. Quick."),
        new WipeMethodOption(WipeMethod.DoD3Pass, "3 passes (recommended)", "Zeros, then ones, then random bytes - the pattern DoD 5220.22-M describes."),
        new WipeMethodOption(WipeMethod.DoD7Pass, "7 passes", "Seven alternating patterns. Takes about twice as long as three."),
        new WipeMethodOption(WipeMethod.Gutmann, "35 passes (Gutmann)", "The 1996 Gutmann patterns, meant for drive encodings of that era. Very slow.")
    };

    /// <summary>
    /// Shown when the files chosen sit on a solid-state drive. Overwriting a file there writes to different
    /// flash than the copy being replaced, because the drive decides where writes land.
    /// </summary>
    [ObservableProperty] private string _mediaWarning = "";

    [ObservableProperty] private bool _hasMediaWarning;

    public DriveWiperViewModel(IDriveWiper driveWiper,
        ILogger<DriveWiperViewModel>? logger = null)
    {
        _logger = logger ?? NullLogger<DriveWiperViewModel>.Instance;
        _driveWiper = driveWiper;
        UpdateMethodDescription();
    }

    partial void OnSelectedMethodChanged(WipeMethod value)
    {
        UpdateMethodDescription();
    }

    private void UpdateMethodDescription()
    {
        MethodDescription = SelectedMethod switch
        {
            WipeMethod.SinglePass => "One pass of zeros over the file's own blocks, then the file is deleted. The last pass is read back to check it landed.",
            WipeMethod.DoD3Pass => "Zeros, then 0xFF, then random bytes, as DoD 5220.22-M describes. The last pass is read back to check it landed.",
            WipeMethod.DoD7Pass => "Seven passes of alternating patterns and random bytes. The last pass is read back to check it landed.",
            WipeMethod.Gutmann => "Four random passes, the 27 Gutmann patterns, then four more random passes. Written for 1990s drive encodings; on anything modern the extra passes buy little over three.",
            _ => ""
        };
    }

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            if (files != null)
            {
                foreach (var file in files)
                {
                    if (!FilesToWipe.Any(f => f.Path == file.Path))
                    {
                        var props = await file.GetBasicPropertiesAsync();
                        FilesToWipe.Add(new FileToWipe
                        {
                            Path = file.Path,
                            Name = file.Name,
                            Size = (long)props.Size,
                            FormattedSize = FormatSize((long)props.Size),
                            IsDirectory = false
                        });
                    }
                }
            }

            UpdateFileStats();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error adding files: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null && !FilesToWipe.Any(f => f.Path == folder.Path))
            {
                var size = await GetDirectorySizeAsync(folder.Path);
                FilesToWipe.Add(new FileToWipe
                {
                    Path = folder.Path,
                    Name = folder.Name,
                    Size = size,
                    FormattedSize = FormatSize(size),
                    IsDirectory = true
                });
            }

            UpdateFileStats();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error adding folder: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RemoveFile(FileToWipe file)
    {
        FilesToWipe.Remove(file);
        UpdateFileStats();
    }

    [RelayCommand]
    private void ClearAll()
    {
        FilesToWipe.Clear();
        UpdateFileStats();
    }

    [RelayCommand]
    private async Task WipeFilesAsync()
    {
        if (!HasFiles || IsWiping) return;

        IsWiping = true;
        Progress = 0;
        CompletedFiles = 0;
        TotalFiles = FilesToWipe.Count;
        var successCount = 0;
        var errorCount = 0;
        var linksRemoved = 0;

        try
        {
            var filesToProcess = FilesToWipe.ToList();

            foreach (var file in filesToProcess)
            {
                CurrentFile = file.Name;

                var progress = new Progress<double>(p =>
                {
                    Progress = ((CompletedFiles + p) / TotalFiles) * 100;
                });

                WipeResult result;
                if (file.IsDirectory)
                {
                    result = await _driveWiper.SecureDeleteDirectoryAsync(file.Path, SelectedMethod, progress);
                }
                else
                {
                    result = await _driveWiper.SecureDeleteFileAsync(file.Path, SelectedMethod, progress);
                }

                linksRemoved += result.LinksRemoved;

                if (result.Success)
                {
                    successCount++;
                    FilesToWipe.Remove(file);
                }
                else
                {
                    errorCount++;
                    file.Error = result.ErrorMessage;
                }

                CompletedFiles++;
            }

            Progress = 100;
            CurrentFile = "";

            var linkNote = linksRemoved > 0
                ? $" {linksRemoved} link(s) were removed; the files they pointed to were not touched."
                : "";

            if (errorCount == 0)
            {
                StatusMessage = $"Successfully wiped {successCount} items using {SelectedMethod}.{linkNote}";
            }
            else
            {
                StatusMessage = $"Wiped {successCount} items, {errorCount} failed.{linkNote}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error during wipe: {ex.Message}";
        }
        finally
        {
            IsWiping = false;
            UpdateFileStats();
        }
    }

    /// <summary>
    /// Says so when the chosen files sit on a solid-state drive. Overwriting a file there does not
    /// necessarily reach the flash that held it: the drive writes elsewhere and remaps, so the old contents
    /// can survive in blocks nothing can address from here.
    /// </summary>
    private void UpdateMediaWarning()
    {
        var ssdDrives = FilesToWipe
            .Select(f => Path.GetPathRoot(f.Path))
            .Where(root => !string.IsNullOrEmpty(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(root => _driveWiper.IsSolidStateDrive(root!))
            .Select(root => root!.TrimEnd('\\'))
            .ToList();

        HasMediaWarning = ssdDrives.Count > 0;
        MediaWarning = HasMediaWarning
            ? $"{string.Join(", ", ssdDrives)} is a solid-state drive. Overwriting a file there cannot promise the old contents are gone, because the drive chooses where writes land. Use the drive's own secure erase, or keep the disk encrypted so what is left cannot be read."
            : "";
    }

    private void UpdateFileStats()
    {
        UpdateMediaWarning();
        HasFiles = FilesToWipe.Count > 0;
        TotalFiles = FilesToWipe.Count;

        if (HasFiles)
        {
            var totalSize = FilesToWipe.Sum(f => f.Size);
            StatusMessage = $"{TotalFiles} items ({FormatSize(totalSize)}) ready to wipe";
        }
        else
        {
            StatusMessage = "Add files or folders to securely delete";
        }
    }

    private async Task<long> GetDirectorySizeAsync(string path)
    {
        return await Task.Run(() =>
        {
            long size = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { size += new FileInfo(file).Length; }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "GetDirectorySizeAsync failed");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "GetDirectorySizeAsync failed");
            }
            return size;
        });
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F2} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F2} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }
}

public class FileToWipe
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string FormattedSize { get; set; } = "";
    public bool IsDirectory { get; set; }
    public string? Error { get; set; }
    public string Icon => IsDirectory ? "\uE8B7" : "\uE8A5";
}

public class WipeMethodOption
{
    public WipeMethod Method { get; }
    public string Name { get; }
    public string Description { get; }

    public WipeMethodOption(WipeMethod method, string name, string description)
    {
        Method = method;
        Name = name;
        Description = description;
    }
}
