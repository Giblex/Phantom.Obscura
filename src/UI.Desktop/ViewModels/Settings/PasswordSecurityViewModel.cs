using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using ReactiveUI;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.ViewModels.Settings
{
    public class PasswordSecurityViewModel : ReactiveObject
    {
        private readonly PasswordHealthService _healthService;
        private PasswordHealthReport _report = new PasswordHealthReport();
        private DateTime? _lastAnalyzed;
        private bool _autoAnalyzeOnUnlock = true;
        private bool _showPasswordStrengthInEditor = true;
        private bool _flagShortPasswords = true;
        private double _entropyThreshold = 40.0;
        private int _ageThresholdDays = 365;

        public PasswordSecurityViewModel()
        {
            _healthService = new PasswordHealthService();
            Credentials = new ObservableCollection<Credential>();

            AnalyzeCommand = ReactiveCommand.CreateFromTask(AnalyzeAsync);
            ShowFlaggedPasswordsCommand = ReactiveCommand.Create(ShowFlaggedPasswords);
            ExportReportCommand = ReactiveCommand.CreateFromTask(ExportReportAsync);
            ViewWeakPasswordsCommand = ReactiveCommand.Create(ViewWeakPasswords);
            ViewReusedPasswordsCommand = ReactiveCommand.Create(ViewReusedPasswords);
            ViewOldPasswordsCommand = ReactiveCommand.Create(ViewOldPasswords);

            // Listen to property changes
            this.WhenAnyValue(x => x.EntropyThreshold, x => x.AgeThresholdDays)
                .Subscribe(_ => UpdateThresholdsAsync());
        }

        public ObservableCollection<Credential> Credentials { get; }

        public PasswordHealthReport Report
        {
            get => _report;
            private set
            {
                this.RaiseAndSetIfChanged(ref _report, value);
                this.RaisePropertyChanged(nameof(HasReport));
                this.RaisePropertyChanged(nameof(HasIssues));
                this.RaisePropertyChanged(nameof(HasWeakPasswords));
                this.RaisePropertyChanged(nameof(HasReusedPasswords));
                this.RaisePropertyChanged(nameof(HasOldPasswords));
                this.RaisePropertyChanged(nameof(HasFlaggedPasswords));
                this.RaisePropertyChanged(nameof(SecurityScore));
                this.RaisePropertyChanged(nameof(SecurityScoreLabel));
                this.RaisePropertyChanged(nameof(SecurityScoreColor));
                this.RaisePropertyChanged(nameof(SecurityStatusIcon));
                this.RaisePropertyChanged(nameof(SecurityStatusText));
                this.RaisePropertyChanged(nameof(AverageEntropyText));
                this.RaisePropertyChanged(nameof(AverageEntropyColor));
                this.RaisePropertyChanged(nameof(AverageEntropyBarWidth));
                this.RaisePropertyChanged(nameof(AverageEntropyDescription));
                this.RaisePropertyChanged(nameof(WeakPasswordsHeader));
                this.RaisePropertyChanged(nameof(ReusedPasswordsHeader));
                this.RaisePropertyChanged(nameof(OldPasswordsHeader));
                this.RaisePropertyChanged(nameof(WeakPasswordsSample));
                this.RaisePropertyChanged(nameof(ReusedPasswordsSample));
                this.RaisePropertyChanged(nameof(OldPasswordsSample));
            }
        }

        public bool AutoAnalyzeOnUnlock
        {
            get => _autoAnalyzeOnUnlock;
            set => this.RaiseAndSetIfChanged(ref _autoAnalyzeOnUnlock, value);
        }

        public bool ShowPasswordStrengthInEditor
        {
            get => _showPasswordStrengthInEditor;
            set => this.RaiseAndSetIfChanged(ref _showPasswordStrengthInEditor, value);
        }

        public bool FlagShortPasswords
        {
            get => _flagShortPasswords;
            set => this.RaiseAndSetIfChanged(ref _flagShortPasswords, value);
        }

        public double EntropyThreshold
        {
            get => _entropyThreshold;
            set => this.RaiseAndSetIfChanged(ref _entropyThreshold, value);
        }

        public int AgeThresholdDays
        {
            get => _ageThresholdDays;
            set => this.RaiseAndSetIfChanged(ref _ageThresholdDays, value);
        }

        public string LastAnalyzedText
        {
            get
            {
                if (_lastAnalyzed == null) return "No analysis performed yet";
                var elapsed = DateTime.UtcNow - _lastAnalyzed.Value;
                if (elapsed.TotalMinutes < 1) return "Analyzed just now";
                if (elapsed.TotalHours < 1) return $"Analyzed {(int)elapsed.TotalMinutes} minutes ago";
                if (elapsed.TotalDays < 1) return $"Analyzed {(int)elapsed.TotalHours} hours ago";
                return $"Analyzed {(int)elapsed.TotalDays} days ago";
            }
        }

        public bool HasReport => Report.TotalCredentials > 0;
        public bool HasIssues => HasWeakPasswords || HasReusedPasswords || HasOldPasswords;
        public bool HasWeakPasswords => Report.WeakCount > 0;
        public bool HasReusedPasswords => Report.ReusedCount > 0;
        public bool HasOldPasswords => Report.OldCount > 0;
        public bool HasFlaggedPasswords => Report.WeakCount > 0 || Report.ReusedCount > 0;

        // Security Score (0-100)
        public int SecurityScore
        {
            get
            {
                if (Report.TotalCredentials == 0) return 0;

                int score = 100;

                // Deduct points for weak passwords (max -40 points)
                double weakRatio = (double)Report.WeakCount / Report.TotalCredentials;
                score -= (int)(weakRatio * 40);

                // Deduct points for reused passwords (max -30 points)
                double reuseRatio = (double)Report.ReusedCount / Report.TotalCredentials;
                score -= (int)(reuseRatio * 30);

                // Deduct points for old passwords (max -20 points)
                double oldRatio = (double)Report.OldCount / Report.TotalCredentials;
                score -= (int)(oldRatio * 20);

                // Bonus/penalty based on average entropy
                if (Report.AverageEntropy < 30) score -= 10;
                else if (Report.AverageEntropy > 60) score += 10;

                return Math.Clamp(score, 0, 100);
            }
        }

        public string SecurityScoreLabel
        {
            get
            {
                var score = SecurityScore;
                if (score >= 90) return "Excellent Security";
                if (score >= 75) return "Good Security";
                if (score >= 60) return "Moderate Security";
                if (score >= 40) return "Weak Security";
                return "Critical - Action Required";
            }
        }

        public ISolidColorBrush SecurityScoreColor
        {
            get
            {
                var score = SecurityScore;
                if (score >= 90) return new SolidColorBrush(Color.Parse("#22C55E")); // Green
                if (score >= 75) return new SolidColorBrush(Color.Parse("#84CC16")); // Lime
                if (score >= 60) return new SolidColorBrush(Color.Parse("#EAB308")); // Yellow
                if (score >= 40) return new SolidColorBrush(Color.Parse("#F97316")); // Orange
                return new SolidColorBrush(Color.Parse("#EF4444")); // Red
            }
        }

        public string SecurityStatusIcon
        {
            get
            {
                var score = SecurityScore;
                if (score >= 90) return "🛡️";
                if (score >= 75) return "✅";
                if (score >= 60) return "⚠️";
                if (score >= 40) return "🔴";
                return "🚨";
            }
        }

        public string SecurityStatusText
        {
            get
            {
                var score = SecurityScore;
                if (score >= 90) return "Your vault is highly secure";
                if (score >= 75) return "Good protection";
                if (score >= 60) return "Some improvements needed";
                if (score >= 40) return "Vulnerable to attacks";
                return "Immediate action required";
            }
        }

        public string AverageEntropyText => $"{Report.AverageEntropy:F1} bits";

        public ISolidColorBrush AverageEntropyColor
        {
            get
            {
                if (Report.AverageEntropy >= 60) return new SolidColorBrush(Color.Parse("#22C55E")); // Green
                if (Report.AverageEntropy >= 40) return new SolidColorBrush(Color.Parse("#EAB308")); // Yellow
                return new SolidColorBrush(Color.Parse("#EF4444")); // Red
            }
        }

        public double AverageEntropyBarWidth
        {
            get
            {
                // Scale entropy to 0-100% (assuming max entropy ~80 bits)
                double percentage = Math.Clamp(Report.AverageEntropy / 80.0 * 100, 0, 100);
                return percentage * 3; // Scale to pixel width (max 300px)
            }
        }

        public string AverageEntropyDescription
        {
            get
            {
                if (Report.AverageEntropy >= 60) return "Strong passwords on average";
                if (Report.AverageEntropy >= 40) return "Moderate strength - consider using longer passwords";
                return "Weak passwords detected - use the password generator";
            }
        }

        public string WeakPasswordsHeader => $"{Report.WeakCount} Weak Password{(Report.WeakCount != 1 ? "s" : "")} Detected";
        public string ReusedPasswordsHeader => $"{Report.ReusedCount} Password{(Report.ReusedCount != 1 ? "s" : "")} Reused";
        public string OldPasswordsHeader => $"{Report.OldCount} Outdated Password{(Report.OldCount != 1 ? "s" : "")}";

        public IEnumerable<string> WeakPasswordsSample => Report.WeakTitles.Take(3);
        public IEnumerable<string> ReusedPasswordsSample => Report.ReusedTitles.Distinct().Take(3);
        public IEnumerable<string> OldPasswordsSample => Report.OldTitles.Take(3);

        public ReactiveCommand<Unit, Unit> AnalyzeCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowFlaggedPasswordsCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportReportCommand { get; }
        public ReactiveCommand<Unit, Unit> ViewWeakPasswordsCommand { get; }
        public ReactiveCommand<Unit, Unit> ViewReusedPasswordsCommand { get; }
        public ReactiveCommand<Unit, Unit> ViewOldPasswordsCommand { get; }

        private async Task AnalyzeAsync()
        {
            Report = await _healthService.AnalyzeAsync(
                Credentials,
                entropyThreshold: EntropyThreshold,
                reuseThreshold: 2,
                ageThreshold: AgeThresholdDays);

            _lastAnalyzed = DateTime.UtcNow;
            this.RaisePropertyChanged(nameof(LastAnalyzedText));
        }

        private void ShowFlaggedPasswords()
        {
            // This would trigger the flagged passwords overlay in VaultWindow
            // For now, just a placeholder
        }

        private async Task ExportReportAsync()
        {
            // TODO: Implement export to PDF or CSV
            await Task.CompletedTask;
        }

        private void ViewWeakPasswords()
        {
            // TODO: Open detailed view of weak passwords
        }

        private void ViewReusedPasswords()
        {
            // TODO: Open detailed view of reused passwords
        }

        private void ViewOldPasswords()
        {
            // TODO: Open detailed view of old passwords
        }

        private async void UpdateThresholdsAsync()
        {
            if (Credentials.Any() && HasReport)
            {
                await AnalyzeAsync();
            }
        }

        public void LoadCredentials(IEnumerable<Credential> credentials)
        {
            Credentials.Clear();
            foreach (var cred in credentials)
            {
                Credentials.Add(cred);
            }
        }
    }
}
