using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using PhantomVault.UI.Models;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the global command palette (Ctrl+K). Pure UI state:
    /// holds the source action list, the current search text, the filtered
    /// projection, and a selection cursor.
    /// </summary>
    /// <remarks>
    /// The View is responsible for raising <see cref="ActivateRequested"/>
    /// when the user presses Enter / clicks a row. We deliberately do NOT
    /// invoke the action on the ReactiveCommand thread here — closing the
    /// window first lets the underlying VM command open its own dialog
    /// without parent-window focus glitches.
    /// </remarks>
    public sealed class CommandPaletteViewModel : ReactiveObject
    {
        private readonly IReadOnlyList<CommandPaletteAction> _allActions;
        private string _searchText = string.Empty;
        private int _selectedIndex;
        private CommandPaletteAction? _selectedAction;

        public CommandPaletteViewModel(IEnumerable<CommandPaletteAction> actions)
        {
            _allActions = (actions ?? throw new ArgumentNullException(nameof(actions))).ToList();

            FilteredActions = new ObservableCollection<CommandPaletteAction>(_allActions);
            _selectedIndex = FilteredActions.Count > 0 ? 0 : -1;
            _selectedAction = FilteredActions.FirstOrDefault();

            MoveSelectionDownCommand = ReactiveCommand.Create(MoveSelectionDown);
            MoveSelectionUpCommand = ReactiveCommand.Create(MoveSelectionUp);
            ActivateSelectedCommand = ReactiveCommand.Create(ActivateSelected);
        }

        /// <summary>Filtered list bound to the palette ListBox.</summary>
        public ObservableCollection<CommandPaletteAction> FilteredActions { get; }

        /// <summary>Search box text. Filtering re-runs on every change.</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                var incoming = value ?? string.Empty;
                if (!string.Equals(_searchText, incoming, StringComparison.Ordinal))
                {
                    this.RaiseAndSetIfChanged(ref _searchText, incoming);
                    ApplyFilter();
                }
            }
        }

        public int SelectedIndex
        {
            get => _selectedIndex;
            set => this.RaiseAndSetIfChanged(ref _selectedIndex, value);
        }

        public CommandPaletteAction? SelectedAction
        {
            get => _selectedAction;
            set => this.RaiseAndSetIfChanged(ref _selectedAction, value);
        }

        public ReactiveCommand<Unit, Unit> MoveSelectionDownCommand { get; }
        public ReactiveCommand<Unit, Unit> MoveSelectionUpCommand { get; }
        public ReactiveCommand<Unit, Unit> ActivateSelectedCommand { get; }

        /// <summary>
        /// Raised when the user activates a row (Enter / double-click).
        /// The View handles closing the palette and invoking the action.
        /// </summary>
        public event EventHandler<CommandPaletteAction>? ActivateRequested;

        private void ApplyFilter()
        {
            var query = (_searchText ?? string.Empty).Trim();
            FilteredActions.Clear();

            if (string.IsNullOrEmpty(query))
            {
                foreach (var a in _allActions)
                {
                    FilteredActions.Add(a);
                }
            }
            else
            {
                // Lightweight substring match across Title / Subtitle / Category
                // / SearchKeywords. Avoids pulling in a fuzzy matcher dependency;
                // ranks "starts with" higher than "contains".
                var matches = _allActions
                    .Select(a => new { Action = a, Score = ScoreMatch(a, query) })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Action.Category, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Action.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Action);

                foreach (var a in matches)
                {
                    FilteredActions.Add(a);
                }
            }

            SelectedIndex = FilteredActions.Count > 0 ? 0 : -1;
            SelectedAction = FilteredActions.FirstOrDefault();
        }

        private static int ScoreMatch(CommandPaletteAction action, string query)
        {
            int best = 0;
            best = Math.Max(best, ScoreField(action.Title, query, weight: 10));
            best = Math.Max(best, ScoreField(action.Subtitle, query, weight: 5));
            best = Math.Max(best, ScoreField(action.Category, query, weight: 4));
            best = Math.Max(best, ScoreField(action.SearchKeywords, query, weight: 3));
            return best;
        }

        private static int ScoreField(string? value, string query, int weight)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            int idx = value.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return 0;
            // Boost for prefix matches.
            return idx == 0 ? weight * 3 : weight;
        }

        private void MoveSelectionDown()
        {
            if (FilteredActions.Count == 0) return;
            SelectedIndex = (SelectedIndex + 1) % FilteredActions.Count;
            SelectedAction = FilteredActions[SelectedIndex];
        }

        private void MoveSelectionUp()
        {
            if (FilteredActions.Count == 0) return;
            SelectedIndex = SelectedIndex <= 0 ? FilteredActions.Count - 1 : SelectedIndex - 1;
            SelectedAction = FilteredActions[SelectedIndex];
        }

        private void ActivateSelected()
        {
            var action = SelectedAction;
            if (action is null) return;
            ActivateRequested?.Invoke(this, action);
        }
    }
}
