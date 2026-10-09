using Xunit;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// Serialises every test class that touches <c>BootRomSession</c>.
    ///
    /// The session is process-wide static state, and these classes clear it in Dispose. xUnit runs
    /// separate test classes in parallel by default, so one class's teardown was wiping another's
    /// registered contribution mid-test — which looked exactly like a binding failure. The tests
    /// passed individually and failed together, which is the signature of shared state rather than
    /// a product fault.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class BootRomSessionCollection
    {
        public const string Name = "BootRomSession";
    }
}
