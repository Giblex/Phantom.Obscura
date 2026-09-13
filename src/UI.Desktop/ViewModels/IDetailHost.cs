using System.Threading.Tasks;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// What the entry detail sections need from whatever they are showing: the entry itself and
    /// a way to copy one of its values. The vault implements it for the single display card;
    /// <see cref="AccountTileContext"/> implements it for each account tile when several entries
    /// for the same service share one card.
    /// </summary>
    internal interface IDetailHost
    {
        CredentialViewModel? SelectedCredential { get; }

        Task<bool> CopyFieldValueAsync(string? value, string label);
    }
}
