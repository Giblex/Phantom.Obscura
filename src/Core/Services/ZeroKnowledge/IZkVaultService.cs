using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.ZeroKnowledge
{

    public interface IZkVaultService
    {
        /// <summary>
        /// Unlocks the master key for the current vault session.
        /// Hard rule: <paramref name="keyfilePath"/> is MANDATORY. <paramref name="password"/> is OPTIONAL
        /// (may be empty when the user opts for keyfile-only unlock). Implementations call
        /// <c>KeyfileGuard.Require</c> at entry and throw if the keyfile is null, missing, or unreadable.
        /// </summary>
        Task<bool> UnlockMasterKeyAsync(string password, string? keyfilePath = null, string? deviceId = null);

        Task<bool> UnlockWithHybridKeyAsync(byte[] hybridDek);

        Task<Stream> OpenFileStreamForViewingAsync(string vaultPath, string? fileRelativePath = null, CancellationToken ct = default);

        Task<Stream> OpenEncryptedStreamForViewingAsync(Stream encryptedVaultStream, CancellationToken ct = default);

        Task<string> ExtractFileToSecureTempAsync(string containerPath, string fileRelativePath, TimeSpan ttl);

        Task<IEnumerable<string>> ListContainerEntriesAsync(string containerPath);

        Task EncryptFileAsync(string plaintextPath, string encryptedOutputPath, CancellationToken ct = default);

        Task EncryptStreamAsync(Stream plaintextStream, string encryptedOutputPath, CancellationToken ct = default);

        Task EncryptStreamToStreamAsync(Stream plaintextStream, Stream encryptedOutputStream, CancellationToken ct = default);

        Task LockAndWipeKeysAsync();

        bool IsUnlocked { get; }

        Task CleanupOrphanedTempFilesAsync(TimeSpan maxAge);
    }
}

