using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;

namespace SysMonitor.App.ViewModels;

public partial class DonationViewModel : ObservableObject
{
    // PayPal hosted button ID
    private const string PayPalButtonId = "R6ZYA7LWXBTFW";
    private const string PayPalDonateUrl = "https://www.paypal.com/donate/?hosted_button_id=" + PayPalButtonId;

    public DonationViewModel()
    {
    }

    [RelayCommand]
    private void Donate()
    {
        try
        {
            // Open PayPal donation page in default browser
            Process.Start(new ProcessStartInfo
            {
                FileName = PayPalDonateUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Silently fail if browser cannot be opened
        }
    }
}
