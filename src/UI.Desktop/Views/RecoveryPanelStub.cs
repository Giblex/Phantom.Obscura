using System;
using Avalonia.Controls;

namespace PhantomVault.UI.Views
{
    /// <summary>
    /// Stub for RecoveryPanel when PhantomRecovery is not available
    /// </summary>
    public partial class RecoveryPanel : UserControl
    {
#pragma warning disable CS0067 // Event never used - stub implementation
        public event EventHandler? CloseRequested;
#pragma warning restore CS0067
        
        public RecoveryPanel()
        {
            // Stub implementation
        }
    }
}
