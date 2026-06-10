namespace PhantomVault.Core.Services.Security
{

    public interface IExportGuard
    {

        bool CanExport(string exportType);

        void RegisterExport(string exportType);
    }
}

