using System;
using System.Text.Json;
using ReactiveUI;
using PhantomVault.Core.Models;
using PhantomVault.UI.Models;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model that wraps a secure trash record so the UI can present
    /// credential details alongside trash metadata and selection state.
    /// </summary>
    public sealed class SecureTrashItemViewModel : ReactiveObject
    {
        private readonly CredentialViewModel _payload;
        private bool _isSelected;

        public SecureTrashItemViewModel(SecureTrashRecord record)
        {
            Record = record ?? throw new ArgumentNullException(nameof(record));
            _payload = new CredentialViewModel(CloneCredential(record.Payload ?? new Credential()));
        }

        /// <summary>
        /// Underlying secure trash record that contains retention metadata.
        /// </summary>
        public SecureTrashRecord Record { get; }

        /// <summary>
        /// Snapshot of the credential payload for display purposes.
        /// </summary>
        public CredentialViewModel Payload => _payload;

        public string Title => _payload.Title;
        public string Username => _payload.Username;
        public string OriginalGroup => string.IsNullOrWhiteSpace(Record.OriginalGroup) ? "Unsorted" : Record.OriginalGroup!;
        public DateTimeOffset DeletedUtc => Record.DeletedUtc;
        public DateTimeOffset? ScheduledPurgeUtc => Record.ScheduledPurgeUtc;

        public string DeletedRelative => FormatRelativeTime(Record.DeletedUtc);

        public string ScheduledPurgeLabel => Record.ScheduledPurgeUtc.HasValue
            ? $"Scheduled purge: {Record.ScheduledPurgeUtc.Value.ToLocalTime():MMM dd, yyyy HH:mm}"
            : "Manual purge";

        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        private static Credential CloneCredential(Credential credential)
        {
            var json = JsonSerializer.Serialize(credential);
            return JsonSerializer.Deserialize<Credential>(json) ?? new Credential();
        }

        private static string FormatRelativeTime(DateTimeOffset timestamp)
        {
            var delta = DateTimeOffset.UtcNow - timestamp;
            if (delta.TotalDays >= 1)
            {
                return $"{Math.Max(1, (int)delta.TotalDays)}d ago";
            }

            if (delta.TotalHours >= 1)
            {
                return $"{Math.Max(1, (int)delta.TotalHours)}h ago";
            }

            if (delta.TotalMinutes >= 1)
            {
                return $"{Math.Max(1, (int)delta.TotalMinutes)}m ago";
            }

            return "Just now";
        }
    }
}
