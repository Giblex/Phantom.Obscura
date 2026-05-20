using System.Threading.Tasks;
using PhantomVault.Platform.Services;

namespace PhantomVault.Core.Services.Platform.Android
{
    /// <summary>
    /// Android implementation of <see cref="IAutoTypeService"/>.
    /// Keyboard auto-type via P/Invoke is not available on Android; the
    /// Android AutofillService delivers credentials through the system
    /// autofill API instead. All members are intentional no-ops so that the
    /// shared autofill orchestrator can run unchanged on this platform.
    /// </summary>
    public sealed class AndroidAutoTypeService : IAutoTypeService
    {
        public Task TypeCredentialsAsync(string username, string password, bool submit = false)
            => Task.CompletedTask;

        public Task TypeCustomSequenceAsync(string sequence, string username, string password)
            => Task.CompletedTask;

        public Task TypeTextAsync(string text, int delayMs = 10)
            => Task.CompletedTask;

        public Task PressKeyAsync(SpecialKey key)
            => Task.CompletedTask;
    }
}
