using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.UI.Services;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// Entries for the same service share one list tile and one display card, with each entry as
    /// its own account tile on that card. Grouping only: every entry keeps its own password, TOTP
    /// and history (see <see cref="AccountConsolidationService"/>).
    /// </summary>
    public sealed partial class VaultViewModel : IDetailHost
    {
        private IReadOnlyList<AccountTileContext> _selectedAccountTiles = Array.Empty<AccountTileContext>();

        /// <summary>One tile per account when the selected entry's service has more than one.</summary>
        public IReadOnlyList<AccountTileContext> SelectedAccountTiles
        {
            get => _selectedAccountTiles;
            private set => this.RaiseAndSetIfChanged(ref _selectedAccountTiles, value);
        }

        public bool ShowAccountTiles => _selectedAccountTiles.Count > 1;

        /// <summary>False on the single card: its header has Edit, so no per-account pencil.</summary>
        public bool IsAccountTile => false;

        CredentialViewModel? IDetailHost.SelectedCredential => SelectedCredential;

        Task<bool> IDetailHost.CopyFieldValueAsync(string? value, string label) => CopyFieldValueAsync(value, label);

        /// <summary>Rebuilds the account tiles for the current selection.</summary>
        internal void RefreshAccountTiles()
        {
            foreach (var old in _selectedAccountTiles)
                old.Dispose();

            var group = SelectedCredential?.SiblingAccounts;
            SelectedAccountTiles = group is { Count: > 1 }
                ? group.Select((account, i) => new AccountTileContext(this, account, i, group.Count)).ToList()
                : Array.Empty<AccountTileContext>();

            this.RaisePropertyChanged(nameof(ShowAccountTiles));
        }

        /// <summary>
        /// The key entries are merged under. Service entries (logins, authenticators, API keys,
        /// notes) share one key per service, so a GitHub password and its authenticator sit
        /// together. Personal records (IDs, cards, banks, contacts, PINs, Wi-Fi) only merge with
        /// the same type, so a Wi-Fi network never folds into a login.
        /// </summary>
        internal static string AccountGroupKey(Credential credential)
        {
            var serviceKey = AccountConsolidationService.ResolveServiceKey(credential);
            if (string.IsNullOrWhiteSpace(serviceKey))
                return "" + credential.Id;

            return credential.EntryType switch
            {
                EntryType.Password or EntryType.TotpGenerator or EntryType.ApiKey or EntryType.Blank => serviceKey,
                _ => serviceKey + "|" + credential.EntryType
            };
        }

        /// <summary>Groups entries by <see cref="AccountGroupKey"/>, keeping first-seen order.</summary>
        internal static List<(CredentialViewModel Primary, List<CredentialViewModel> Members)> GroupByService(
            IEnumerable<CredentialViewModel> items)
        {
            var ordered = new List<(CredentialViewModel, List<CredentialViewModel>)>();
            var byKey = new Dictionary<string, List<CredentialViewModel>>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                var key = AccountGroupKey(item.GetCredential());
                if (!byKey.TryGetValue(key, out var members))
                {
                    members = new List<CredentialViewModel>();
                    byKey[key] = members;
                    ordered.Add((item, members));
                }
                members.Add(item);
            }

            return ordered;
        }

        /// <summary>
        /// Title of an existing entry the given (unsaved) entry would join, or null. Drives the
        /// editor's live "will join" notice.
        /// </summary>
        internal string? FindMergeTargetTitle(Credential probe)
        {
            if (probe == null || string.IsNullOrWhiteSpace(probe.Title) && string.IsNullOrWhiteSpace(probe.Url))
                return null;

            var key = AccountGroupKey(probe);
            var match = _credentials.FirstOrDefault(c =>
                !string.Equals(c.Id, probe.Id, StringComparison.Ordinal) &&
                string.Equals(AccountGroupKey(c.GetCredential()), key, StringComparison.OrdinalIgnoreCase));

            return match?.Title;
        }
    }
}
