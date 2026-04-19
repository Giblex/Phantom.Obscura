using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace PhantomVault.UI.Services;

internal static class WindowsCredentialStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int CredMaxCredentialBlobSize = 5 * 512;

    public static void WriteSecret(string targetName, ReadOnlySpan<byte> secret, string comment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        if (secret.Length == 0)
            throw new ArgumentException("Secret payload must not be empty.", nameof(secret));

        if (secret.Length > CredMaxCredentialBlobSize)
            throw new ArgumentOutOfRangeException(nameof(secret), $"Credential blob exceeds WinCred limit of {CredMaxCredentialBlobSize} bytes.");

        var secretBytes = secret.ToArray();
        var credentialBlob = Marshal.AllocCoTaskMem(secretBytes.Length);

        try
        {
            Marshal.Copy(secretBytes, 0, credentialBlob, secretBytes.Length);

            var credential = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = targetName,
                CredentialBlobSize = secretBytes.Length,
                CredentialBlob = credentialBlob,
                Persist = CredPersistLocalMachine,
                UserName = targetName,
                Comment = comment,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                TargetAlias = null
            };

            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CredWrite failed for target '{targetName}'.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
            ZeroAndFree(credentialBlob, secretBytes.Length);
        }
    }

    public static byte[]? ReadSecret(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        if (!CredRead(targetName, CredTypeGeneric, 0, out var credentialPtr))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168)
                return null;

            throw new Win32Exception(error, $"CredRead failed for target '{targetName}'.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize <= 0)
                return null;

            var secret = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, secret, 0, secret.Length);
            return secret;
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    public static void DeleteSecret(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        if (!CredDelete(targetName, CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1168)
                throw new Win32Exception(error, $"CredDelete failed for target '{targetName}'.");
        }
    }

    private static void ZeroAndFree(IntPtr pointer, int length)
    {
        if (pointer == IntPtr.Zero)
            return;

        Span<byte> zeros = stackalloc byte[Math.Min(length, 256)];
        zeros.Clear();

        var remaining = length;
        var offset = 0;
        while (remaining > 0)
        {
            var chunk = Math.Min(zeros.Length, remaining);
            Marshal.Copy(zeros[..chunk].ToArray(), 0, pointer + offset, chunk);
            offset += chunk;
            remaining -= chunk;
        }

        Marshal.FreeCoTaskMem(pointer);
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL userCredential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("Advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr credential);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
}
