using System;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{

    public interface IPasskeyService
    {

        bool IsSupported { get; }

        bool IsBiometricAvailable { get; }

        Task<byte[]> RegisterAsync(string userId, string userName, string rpId, byte[] challenge);

        Task<bool> AuthenticateAsync(byte[] credentialId, string rpId, byte[] challenge);

        string AuthenticatorDescription { get; }

        Task<PasskeyCredential> RotateCredentialAsync(byte[] oldCredentialId, string relyingParty, string userId);

        Task DeleteCredentialAsync(byte[] credentialId);
    }
}

