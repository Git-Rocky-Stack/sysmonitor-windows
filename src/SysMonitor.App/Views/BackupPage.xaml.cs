using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;
using SysMonitor.Core.Services.Backup;

namespace SysMonitor.App.Views;

public sealed partial class BackupPage : Page
{
    public BackupViewModel ViewModel { get; }

    public BackupPage()
    {
        ViewModel = App.GetService<BackupViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadDataCommand.ExecuteAsync(null);
    }

    // Quick Actions
    private void NewBackup_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.StartNewBackupCommand.Execute(null);
    }

    private async void CreateRestorePoint_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.CreateRestorePointCommand.ExecuteAsync(null);
    }

    private async void QuickBackupDocs_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.QuickBackupDocumentsCommand.ExecuteAsync(null);
    }

    // Wizard Navigation
    private void NextStep_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NextStepCommand.Execute(null);
    }

    private void PreviousStep_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PreviousStepCommand.Execute(null);
    }

    private void CancelWizard_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelWizardCommand.Execute(null);
    }

    // Backup Type Selection
    private void SelectBackupType_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string typeStr)
        {
            ViewModel.SelectedBackupType = typeStr switch
            {
                "Full" => BackupType.Full,
                "Incremental" => BackupType.Incremental,
                "SystemImage" => BackupType.SystemImage,
                "Mirror" => BackupType.Mirror,
                _ => BackupType.Full
            };
            ViewModel.NextStepCommand.Execute(null);
        }
    }

    // Source Selection
    private async void AddSourceFolder_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.AddSourceFolderCommand.ExecuteAsync(null);
    }

    private async void AddSourceFiles_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.AddSourceFilesCommand.ExecuteAsync(null);
    }

    private void AddQuickSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string sourceType)
        {
            ViewModel.AddQuickSourceCommand.Execute(sourceType);
        }
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string path)
        {
            ViewModel.RemoveSourcePathCommand.Execute(path);
        }
    }

    // Destination Selection
    private void SelectDrive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DriveDisplayInfo drive)
        {
            ViewModel.SelectDriveCommand.Execute(drive);
            ViewModel.NextStepCommand.Execute(null);
        }
    }

    private async void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.BrowseDestinationCommand.ExecuteAsync(null);
    }

    // Backup Execution
    private void StartBackup_Click(object sender, RoutedEventArgs e)
    {
        // Update compression from picker
        if (CompressionPicker.SelectedItem is ComboBoxItem item && item.Tag is string compression)
        {
            ViewModel.SelectedCompression = compression switch
            {
                "None" => BackupCompression.None,
                "Normal" => BackupCompression.Normal,
                "Maximum" => BackupCompression.Maximum,
                _ => BackupCompression.Normal
            };
        }

        ViewModel.NextStepCommand.Execute(null);
    }

    private void CancelBackup_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelBackupCommand.Execute(null);
    }

    // Backup History
    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BackupArchiveViewModel archive })
            return;

        if (!archive.IsEncrypted)
        {
            await ViewModel.RestoreBackupCommand.ExecuteAsync(archive);
            return;
        }

        var password = await AskForBackupPasswordAsync(archive, "Restore");
        if (password != null)
        {
            await ViewModel.RestoreBackupWithPasswordAsync(archive, password);
        }
    }

    private async void VerifyBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BackupArchiveViewModel archive })
            return;

        if (!archive.IsEncrypted)
        {
            await ViewModel.VerifyBackupCommand.ExecuteAsync(archive);
            return;
        }

        var password = await AskForBackupPasswordAsync(archive, "Verify");
        if (password != null)
        {
            await ViewModel.VerifyBackupWithPasswordAsync(archive, password);
        }
    }

    /// <summary>Prompts for an encrypted backup's password; returns null when cancelled or left empty.</summary>
    private async Task<string?> AskForBackupPasswordAsync(BackupArchiveViewModel archive, string actionText)
    {
        var passwordBox = new PasswordBox { PlaceholderText = "Backup password" };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = $"'{archive.Name}' is encrypted. Enter the password that was used when it was created.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(passwordBox);

        var dialog = new ContentDialog
        {
            Title = "Encrypted Backup",
            Content = content,
            PrimaryButtonText = actionText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrEmpty(passwordBox.Password)
            ? passwordBox.Password
            : null;
    }

    private async void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is BackupArchiveViewModel archive)
        {
            // Confirm deletion
            var dialog = new ContentDialog
            {
                Title = "Delete Backup",
                Content = $"Are you sure you want to delete '{archive.Name}'?\n\nThis action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteBackupCommand.ExecuteAsync(archive);
            }
        }
    }
}
