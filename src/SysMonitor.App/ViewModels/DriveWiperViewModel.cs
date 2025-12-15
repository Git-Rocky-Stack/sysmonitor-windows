using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysMonitor.Core.Services.Utilities;
using System.Collections.ObjectModel;
using Windows.Storage.Pickers;

namespace SysMonitor.App.ViewModels;

public partial class DriveWiperViewModel : ObservableObject
{
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
        new WipeMethodOption(WipeMethod.SinglePass, "Quick (1 Pass)", "Fast but basic overwrite. Good for non-sensitive data."),
        new WipeMethodOption(WipeMethod.DoD3Pass, "DoD 3-Pass (Recommended)", "US Department of Defense short method. Good balance of security and speed."),
        new WipeMethodOption(WipeMethod.DoD7Pass, "DoD 7-Pass", "US Department of Defense extended method. Very secure."),
        new WipeMethodOption(WipeMethod.Gutmann, "Gutmann 35-Pass", "Maximum security. Very slow. For extremely sensitive data only.")
    };

    public DriveWiperViewModel(IDriveWiper driveWiper)
    {
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
            WipeMethod.SinglePass => "Overwrites data once with zeros. Fast but recoverable with forensic tools.",
            WipeMethod.DoD3Pass => "Overwrites with 0x00, then 0xFF, then random bytes. Meets most security requirements.",
            WipeMethod.DoD7Pass => "Seven alternating pattern passes. Exceeds most compliance requirements.",
            WipeMethod.Gutmann => "35 passes with specific patterns. Maximum theoretical security but very time consuming.",
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

            if (errorCount == 0)
            {
                StatusMessage = $"Successfully wiped {successCount} items using {SelectedMethod}";
            }
            else
            {
                StatusMessage = $"Wiped {successCount} items, {errorCount} failed";
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

    private void UpdateFileStats()
    {
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

    private static async Task<long> GetDirectorySizeAsync(string path)
    {
        return await Task.Run(() =>
        {
            long size = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { size += new FileInfo(file).Length; }
                    catch { }
                }
            }
            catch { }
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
