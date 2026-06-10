using System.Threading.Tasks;

namespace PhantomVault.UI.Services
{

    public interface IResettableOnError
    {

        Task ResetAfterErrorAsync();
    }
}

