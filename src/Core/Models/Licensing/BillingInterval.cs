namespace PhantomVault.Core.Models.Licensing
{
    /// <summary>
    /// How often a Premium subscription bills.
    ///
    /// The licensing flow previously assumed a single price, so the checkout it opened
    /// was always the monthly one even though a yearly product exists. The interval is
    /// carried through to the checkout call so the user's choice is honoured, and it
    /// maps to a distinct Stripe price on the backend.
    /// </summary>
    public enum BillingInterval
    {
        Monthly = 0,
        Yearly = 1
    }
}
