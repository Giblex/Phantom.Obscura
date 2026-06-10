namespace PhantomVault.Core.Services.Update
{
    /// <summary>
    /// Release channel an update manifest belongs to. Stable is the default;
    /// beta is opt-in. The verifier rejects channel mismatches so that a beta
    /// manifest can never be served to a stable-only client.
    /// </summary>
    public enum UpdateChannel
    {
        Stable = 0,
        Beta = 1,
    }
}
