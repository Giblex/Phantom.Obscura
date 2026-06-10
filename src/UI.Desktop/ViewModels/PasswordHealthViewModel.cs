using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.ViewModels
{

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

        public ObservableCollection<Credential> Credentials { get; }

        public PasswordHealthReport Report
        {
            get => _report;
            private set => this.RaiseAndSetIfChanged(ref _report, value);
        }

        public ReactiveCommand<Unit, Unit> AnalyzeCommand { get; }

        private async Task AnalyzeAsync()
        {
            Report = await _healthService.AnalyzeAsync(Credentials);
        }
    }
}

