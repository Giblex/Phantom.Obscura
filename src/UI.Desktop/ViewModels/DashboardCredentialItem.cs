using System;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using PhantomVault.Core.Models;
using PhantomVault.UI.Helpers;

namespace PhantomVault.UI.ViewModels
{
    public sealed class DashboardCredentialItem
    {
        private readonly Credential _credential;
        private readonly bool _hasTwoFactor;
        private readonly Bitmap? _icon;
        private readonly string _categoryColor;

        public DashboardCredentialItem(Credential credential, bool hasTwoFactor, int securityScore, Bitmap? icon = null, string categoryColor = "#999999")
        {
            _credential = credential;
            _hasTwoFactor = hasTwoFactor;
            SecurityScore = securityScore;
            _icon = icon;
            _categoryColor = categoryColor;
        }

        public string CredentialTitle => _credential.Title ?? "Untitled";
        public string CredentialUsername => _credential.Username ?? "";
        public int SecurityScore { get; }
        public Bitmap? IconBitmap => _icon;
        public string CategoryColor => _categoryColor;
        public Credential Credential => _credential;

        public string SecurityScoreColor => SecurityScore switch
        {
            >= 81 => "#4CAF50",
            >= 61 => "#FFC107",
            >= 41 => "#FF9800",
            _ => "#F44336"
        };

        public ObservableCollection<StatusIndicator> StatusIndicators
        {
            get
            {
                var indicators = new ObservableCollection<StatusIndicator>();
                var passwordAssessment = PasswordStrengthEvaluator.Evaluate(_credential.Password);

                if (passwordAssessment.IsWeak)
                    indicators.Add(new StatusIndicator { Label = "W", Tooltip = "Weak Password", Color = "#FF9800" });

                if (_credential.ExpiryUtc.HasValue && _credential.ExpiryUtc < DateTimeOffset.UtcNow)
                    indicators.Add(new StatusIndicator { Label = "E", Tooltip = "Expired", Color = "#F44336" });
                else if (_credential.ExpiryUtc.HasValue && _credential.ExpiryUtc < DateTimeOffset.UtcNow.AddDays(30))
                    indicators.Add(new StatusIndicator { Label = "X", Tooltip = "Expiring Soon", Color = "#FF9800" });

                if (_hasTwoFactor)
                    indicators.Add(new StatusIndicator { Label = "2FA", Tooltip = "2FA Enabled", Color = "#2196F3" });

                return indicators;
            }
        }

        public bool IsTwoFactorEnabled => _hasTwoFactor;
        public string LastModifiedFormatted => _credential.ModifiedDate > DateTime.MinValue
            ? _credential.ModifiedDate.ToString("yyyy-MM-dd")
            : "N/A";
    }

    public sealed class StatusIndicator
    {
        public string Label { get; set; } = "";
        public string Tooltip { get; set; } = "";
        public string Color { get; set; } = "#666";
    }
}
