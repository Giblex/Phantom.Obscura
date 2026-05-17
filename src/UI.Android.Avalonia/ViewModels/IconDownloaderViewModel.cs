using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PhantomVault.UI.ViewModels;

public sealed partial class IconDownloaderViewModel : ObservableObject
{
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _hasResults;

    public ObservableCollection<IconCandidateViewModel> Candidates { get; } = new();

    [RelayCommand]
    private async Task FetchAsync()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            Status = "Enter a URL first.";
            return;
        }
        Status = "Fetching favicons…";
        HasResults = false;
        Candidates.Clear();

        await Task.Delay(450); // placeholder — real fetch lands once HttpClient + favicon parser wired

        var host = Url.Replace("https://", "").Replace("http://", "").TrimEnd('/');
        Candidates.Add(new() { Label = $"favicon.ico @ {host}" });
        Candidates.Add(new() { Label = $"apple-touch-icon @ {host}" });
        Candidates.Add(new() { Label = $"icon-192 @ {host}" });
        Candidates.Add(new() { Label = $"icon-512 @ {host}" });

        HasResults = true;
        Status = $"Found {Candidates.Count} candidate icons.";
    }
}

public sealed partial class IconCandidateViewModel : ObservableObject
{
    [ObservableProperty] private string _label = "";
}
