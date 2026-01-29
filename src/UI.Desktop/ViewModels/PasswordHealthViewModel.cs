using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for the password health dashboard. Maintains a
    /// collection of credentials and exposes commands to analyse them
    /// using <see cref="PasswordHealthService"/>. The report summary is
    /// bound to the view for display. In a real application the
    /// credentials would be loaded from the vault database when the
    /// vault is unlocked.
    /// </summary>
    public sealed class PasswordHealthViewModel : ReactiveObject
    {
        private readonly PasswordHealthService _healthService;
        private PasswordHealthReport _report = new PasswordHealthReport();

        public PasswordHealthViewModel(PasswordHealthService healthService)
        {
            _healthService = healthService;
            Credentials = new ObservableCollection<Credential>();
            AnalyzeCommand = ReactiveCommand.CreateFromTask(AnalyzeAsync);
        }

        /// <summary>
        /// Collection of credentials to analyse. In practice this would
        /// be populated from the decrypted vault database when the
        /// vault is mounted.
        /// </summary>
        public ObservableCollection<Credential> Credentials { get; }

        /// <summary>
        /// Latest password health report. Bind to the view to display
        /// summary statistics.
        /// </summary>
        public PasswordHealthReport Report
        {
            get => _report;
            private set => this.RaiseAndSetIfChanged(ref _report, value);
        }

        /// <summary>
        /// Command to perform analysis on the current set of
        /// credentials. Updates the <see cref="Report"/> property.
        /// </summary>
        public ReactiveCommand<Unit, Unit> AnalyzeCommand { get; }

        private async Task AnalyzeAsync()
        {
            Report = await _healthService.AnalyzeAsync(Credentials);
        }
    }
}