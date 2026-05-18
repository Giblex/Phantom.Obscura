using CommunityToolkit.Mvvm.ComponentModel;

namespace PhantomVault.UI.ViewModels;

public sealed partial class SecurityDashboardViewModel : ObservableObject
{
    [ObservableProperty] private int    _securityScore = 78;
    [ObservableProperty] private int    _totalCount    = 0;
    [ObservableProperty] private int    _weakCount     = 0;
    [ObservableProperty] private int    _reusedCount   = 0;
    [ObservableProperty] private int    _oldCount      = 0;
    [ObservableProperty] private int    _breachedCount = 0;
    [ObservableProperty] private int    _strongCount   = 0;

    [ObservableProperty] private string _testPassword              = "";
    [ObservableProperty] private int    _testPasswordScore         = 0;
    [ObservableProperty] private string _testPasswordStrengthLabel = "";

    partial void OnTestPasswordChanged(string value)
    {
        // Lightweight local strength heuristic — replaces the desktop's
        // zxcvbn-based scorer until PasswordHealthService is wired on Android.
        if (string.IsNullOrEmpty(value))
        {
            TestPasswordScore = 0;
            TestPasswordStrengthLabel = "";
            return;
        }
        int len = value.Length;
        int cls = 0;
        if (System.Linq.Enumerable.Any(value, char.IsLower)) cls++;
        if (System.Linq.Enumerable.Any(value, char.IsUpper)) cls++;
        if (System.Linq.Enumerable.Any(value, char.IsDigit)) cls++;
        if (System.Linq.Enumerable.Any(value, c => !char.IsLetterOrDigit(c))) cls++;
        int score = System.Math.Min(100, (len * 6) + (cls * 12));
        TestPasswordScore = score;
        TestPasswordStrengthLabel = score switch
        {
            < 30 => "Very weak",
            < 50 => "Weak",
            < 70 => "Moderate",
            < 90 => "Strong",
            _    => "Excellent",
        };
    }
}
