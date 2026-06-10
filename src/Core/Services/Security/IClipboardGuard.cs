namespace PhantomVault.Core.Services.Security
{

    public interface IClipboardGuard
    {

        bool CanCopy();

        void RegisterCopy(string entryId);
    }
}

