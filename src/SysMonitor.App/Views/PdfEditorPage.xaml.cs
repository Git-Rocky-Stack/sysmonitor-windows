using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class PdfEditorPage : Page
{
    public PdfEditorViewModel ViewModel { get; }

    public PdfEditorPage()
    {
        ViewModel = App.GetService<PdfEditorViewModel>();
        InitializeComponent();
    }

    // File Operations
    private async void OpenPdf_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenPdfCommand.ExecuteAsync(null);
    }

    private async void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveCommand.ExecuteAsync(null);
    }

    // Page Navigation
    private async void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.PreviousPageCommand.ExecuteAsync(null);
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.NextPageCommand.ExecuteAsync(null);
    }

    private async void PageThumbnail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int pageNumber)
        {
            await ViewModel.NavigateToPageCommand.ExecuteAsync(pageNumber);
        }
    }

    // Page Operations
    private async void RotateLeft_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RotateCurrentPageCommand.ExecuteAsync(-90);
    }

    private async void RotateRight_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RotateCurrentPageCommand.ExecuteAsync(90);
    }

    private async void DeletePage_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.DeleteCurrentPageCommand.ExecuteAsync(null);
    }

    private async void MovePageUp_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.MovePageUpCommand.ExecuteAsync(null);
    }

    private async void MovePageDown_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.MovePageDownCommand.ExecuteAsync(null);
    }

    // Annotation Tools
    private void SelectTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string toolName)
        {
            ViewModel.SelectToolCommand.Execute(toolName);
        }
    }

    private void ClearAnnotations_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearAnnotationsCommand.Execute(null);
    }

    // Formatting Controls
    private void ColorPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item && item.Tag is string color)
        {
            ViewModel.AnnotationColor = color;
        }
    }

    private void FontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item && item.Tag is string sizeStr)
        {
            if (double.TryParse(sizeStr, out double size))
            {
                ViewModel.FontSize = size;
            }
        }
    }

    private void StrokeWidth_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item && item.Tag is string widthStr)
        {
            if (double.TryParse(widthStr, out double width))
            {
                ViewModel.StrokeWidth = width;
            }
        }
    }

    private void BoldToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            ViewModel.IsBold = toggleButton.IsChecked ?? false;
        }
    }

    private void ItalicToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            ViewModel.IsItalic = toggleButton.IsChecked ?? false;
        }
    }

    // Zoom Controls
    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ZoomInCommand.Execute(null);
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ZoomOutCommand.Execute(null);
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetZoomCommand.Execute(null);
    }
}
