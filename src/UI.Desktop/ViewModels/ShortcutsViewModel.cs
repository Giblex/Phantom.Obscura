using System.Collections.ObjectModel;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{
    public class ShortcutEntry
    {
        public string Action { get; init; } = string.Empty;
        public string Keys { get; init; } = string.Empty;
    }

    public class ShortcutsViewModel : ReactiveObject
    {
        public ObservableCollection<ShortcutEntry> Shortcuts { get; } = new ObservableCollection<ShortcutEntry>
        {
            new ShortcutEntry { Action = "New credential", Keys = "Ctrl + N" },
            new ShortcutEntry { Action = "Save credential", Keys = "Ctrl + S" },
            new ShortcutEntry { Action = "Search", Keys = "Ctrl + F" },
            new ShortcutEntry { Action = "Lock vault", Keys = "Ctrl + L" },
            new ShortcutEntry { Action = "Toggle theme", Keys = "Ctrl + T" },
            new ShortcutEntry { Action = "Open settings", Keys = "Ctrl + ," },
            new ShortcutEntry { Action = "Close dialog", Keys = "Esc" }
        };
    }
}
