using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using PhantomVault.Core.Services;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PhantomVault.UI.Desktop.Controls;

/// <summary>
/// Represents a credential with password strength issues.
/// </summary>
public class WeakCredentialItem : ReactiveObject
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string MaskedPassword { get; set; } = "••••••••";
    public string IssueLabel { get; set; } = string.Empty;
    public IBrush SeverityColor { get; set; } = Brushes.Orange;
    public int Severity { get; set; } // 0=Low, 1=Medium, 2=High, 3=Critical
}

/// <summary>
/// Security dashboard widget displaying password health metrics and breach status.
/// Shows overall security score, weak passwords, breached passwords, and quick actions.
/// </summary>
public partial class SecurityDashboard : UserControl
{
    private TextBlock? _securityScoreValue;
    private TextBlock? _totalCredentialsValue;
    private TextBlock? _weakPasswordsValue;
    private TextBlock? _breachedPasswordsValue;
    private TextBlock? _reusedPasswordsValue;
    private TextBlock? _expiringSoonValue;
    private TextBlock? _twoFactorEnabledValue;
    private TextBlock? _lastBreachCheckValue;
    private ProgressBar? _securityScoreBar;
    private TextBlock? _issueCountText;
    private ItemsControl? _weakCredentialsList;

    public ICommand? EditCredentialCommand { get; set; }

    public static readonly StyledProperty<int> SecurityScoreProperty =
        AvaloniaProperty.Register<SecurityDashboard, int>(nameof(SecurityScore), defaultValue: 0);

    public static readonly StyledProperty<int> TotalCredentialsProperty =
        AvaloniaProperty.Register<SecurityDashboard, int>(nameof(TotalCredentials), defaultValue: 0);

    public static readonly StyledProperty<int> WeakPasswordsProperty =
        AvaloniaProperty.Register<SecurityDashboard, int>(nameof(WeakPasswords), defaultValue: 0);

    public static readonly StyledProperty<int> BreachedPasswordsProperty =
        AvaloniaProperty.Register<SecurityDashboard, int>(nameof(BreachedPasswords), defaultValue: 0);

    public static readonly StyledProperty<int> ReusedPasswordsProperty =
        AvaloniaProperty.Register<SecurityDashboard, int>(nameof(ReusedPasswords), defaultValue: 0);

    public static readonly StyledProperty<int> ExpiringSoonProperty =
        AvaloniaProperty.Register<SecurityDashboard, int>(nameof(ExpiringSoon), defaultValue: 0);

    public static readonly StyledProperty<int> TwoFactorEnabledProperty =
        AvaloniaProperty.Register<SecurityDashboard, int>(nameof(TwoFactorEnabled), defaultValue: 0);

    public static readonly StyledProperty<DateTime?> LastBreachCheckProperty =
        AvaloniaProperty.Register<SecurityDashboard, DateTime?>(nameof(LastBreachCheck));

    public int SecurityScore
    {
        get => GetValue(SecurityScoreProperty);
        set => SetValue(SecurityScoreProperty, value);
    }

    public int TotalCredentials
    {
        get => GetValue(TotalCredentialsProperty);
        set => SetValue(TotalCredentialsProperty, value);
    }

    public int WeakPasswords
    {
        get => GetValue(WeakPasswordsProperty);
        set => SetValue(WeakPasswordsProperty, value);
    }

    public int BreachedPasswords
    {
        get => GetValue(BreachedPasswordsProperty);
        set => SetValue(BreachedPasswordsProperty, value);
    }

    public int ReusedPasswords
    {
        get => GetValue(ReusedPasswordsProperty);
        set => SetValue(ReusedPasswordsProperty, value);
    }

    public int ExpiringSoon
    {
        get => GetValue(ExpiringSoonProperty);
        set => SetValue(ExpiringSoonProperty, value);
    }

    public int TwoFactorEnabled
    {
        get => GetValue(TwoFactorEnabledProperty);
        set => SetValue(TwoFactorEnabledProperty, value);
    }

    public DateTime? LastBreachCheck
    {
        get => GetValue(LastBreachCheckProperty);
        set => SetValue(LastBreachCheckProperty, value);
    }

    public bool HasWeakPasswords => WeakPasswords > 0;
    public bool HasBreachedPasswords => BreachedPasswords > 0;

    public SecurityDashboard()
    {
        InitializeComponent();

        _securityScoreValue = this.FindControl<TextBlock>("SecurityScoreValue");
        _totalCredentialsValue = this.FindControl<TextBlock>("TotalCredentialsValue");
        _weakPasswordsValue = this.FindControl<TextBlock>("WeakPasswordsValue");
        _breachedPasswordsValue = this.FindControl<TextBlock>("BreachedPasswordsValue");
        _reusedPasswordsValue = this.FindControl<TextBlock>("ReusedPasswordsValue");
        _expiringSoonValue = this.FindControl<TextBlock>("ExpiringSoonValue");
        _twoFactorEnabledValue = this.FindControl<TextBlock>("TwoFactorEnabledValue");
        _lastBreachCheckValue = this.FindControl<TextBlock>("LastBreachCheckValue");
        _securityScoreBar = this.FindControl<ProgressBar>("SecurityScoreBar");
        _issueCountText = this.FindControl<TextBlock>("IssueCountText");
        _weakCredentialsList = this.FindControl<ItemsControl>("WeakCredentialsList");

        // DataContext is set by parent (VaultViewModel)
        UpdateDisplay();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SecurityScoreProperty ||
            change.Property == TotalCredentialsProperty ||
            change.Property == WeakPasswordsProperty ||
            change.Property == BreachedPasswordsProperty ||
            change.Property == ReusedPasswordsProperty ||
            change.Property == ExpiringSoonProperty ||
            change.Property == TwoFactorEnabledProperty ||
            change.Property == LastBreachCheckProperty)
        {
            UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        if (_securityScoreValue != null)
            _securityScoreValue.Text = SecurityScore.ToString();

        if (_securityScoreBar != null)
            _securityScoreBar.Value = SecurityScore;

        if (_totalCredentialsValue != null)
            _totalCredentialsValue.Text = TotalCredentials.ToString();

        if (_weakPasswordsValue != null)
            _weakPasswordsValue.Text = WeakPasswords.ToString();

        if (_breachedPasswordsValue != null)
            _breachedPasswordsValue.Text = BreachedPasswords.ToString();

        if (_reusedPasswordsValue != null)
            _reusedPasswordsValue.Text = ReusedPasswords.ToString();

        if (_expiringSoonValue != null)
            _expiringSoonValue.Text = ExpiringSoon.ToString();

        if (_twoFactorEnabledValue != null)
            _twoFactorEnabledValue.Text = TwoFactorEnabled.ToString();

        if (_lastBreachCheckValue != null && LastBreachCheck.HasValue)
        {
            var timeAgo = DateTime.Now - LastBreachCheck.Value;
            _lastBreachCheckValue.Text = FormatTimeAgo(timeAgo);
        }
    }

    private string FormatTimeAgo(TimeSpan timeSpan)
    {
        if (timeSpan.TotalMinutes < 1)
            return "Just now";
        if (timeSpan.TotalMinutes < 60)
            return $"{(int)timeSpan.TotalMinutes} min ago";
        if (timeSpan.TotalHours < 24)
            return $"{(int)timeSpan.TotalHours} hrs ago";
        if (timeSpan.TotalDays < 7)
            return $"{(int)timeSpan.TotalDays} days ago";
        if (timeSpan.TotalDays < 30)
            return $"{(int)(timeSpan.TotalDays / 7)} weeks ago";
        return $"{(int)(timeSpan.TotalDays / 30)} months ago";
    }

    /// <summary>
    /// Calculate overall security score based on password health metrics.
    /// Score ranges from 0-100, with 100 being perfect security.
    /// </summary>
    public static int CalculateSecurityScore(
        int totalCredentials,
        int weakPasswords,
        int breachedPasswords,
        int reusedPasswords,
        int twoFactorEnabled)
    {
        if (totalCredentials == 0)
            return 100; // No credentials = no risk

        double score = 100.0;

        // Deduct points for weak passwords (up to -30 points)
        double weakRatio = (double)weakPasswords / totalCredentials;
        score -= Math.Min(30, weakRatio * 100);

        // Deduct points for breached passwords (up to -40 points, more severe)
        double breachedRatio = (double)breachedPasswords / totalCredentials;
        score -= Math.Min(40, breachedRatio * 150);

        // Deduct points for reused passwords (up to -20 points)
        double reusedRatio = (double)reusedPasswords / totalCredentials;
        score -= Math.Min(20, reusedRatio * 80);

        // Add points for 2FA usage (up to +15 points)
        double twoFactorRatio = (double)twoFactorEnabled / totalCredentials;
        score += Math.Min(15, twoFactorRatio * 15);

        return Math.Max(0, Math.Min(100, (int)Math.Round(score)));
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
