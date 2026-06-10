namespace PhantomVault.Core.Services.Autofill
{

    public interface IAutofillProvider
    {

        bool IsSupported { get; }

        bool TryFill(string domain);
    }
}

