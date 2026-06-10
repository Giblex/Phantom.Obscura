using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Platform.Android
{

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

