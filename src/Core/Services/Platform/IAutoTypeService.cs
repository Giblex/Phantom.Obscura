using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Platform
{

    public interface IAutoTypeService
    {

        Task TypeCredentialsAsync(string username, string password, bool submit = false);

        Task TypeCustomSequenceAsync(string sequence, string username, string password);

        Task TypeTextAsync(string text, int delayMs = 10);

        Task PressKeyAsync(SpecialKey key);
    }

    public enum SpecialKey
    {
        Tab,
        Enter,
        Backspace,
        Delete,
        Escape,
        Up,
        Down,
        Left,
        Right
    }
}

