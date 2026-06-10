namespace PhantomVault.UI.Messages
{
    /// <summary>
    /// Asks the application chrome to open the SettingsWindow and pre-select a tab by key.
    /// Tab keys are arbitrary strings agreed by the publisher and subscriber.
    /// Known keys: "RecoveryActivation".
    /// </summary>
    public sealed record OpenSettingsTabMessage(string TabKey);
}
