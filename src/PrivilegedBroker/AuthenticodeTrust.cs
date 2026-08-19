using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PhantomVault.PrivilegedBroker
{
    /// <summary>
    /// Validates an Authenticode chain with WinVerifyTrust and returns a stable
    /// SHA-256 pin of the leaf signing certificate. A path match alone is not an
    /// identity check because a same-user process may replace a writable binary.
    /// </summary>
    internal static class AuthenticodeTrust
    {
        private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        public static bool TryGetTrustedSignerSha256(string filePath, out string? signerSha256)
        {
            signerSha256 = null;
            if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return false;

            try
            {
                if (!WinVerifyTrustFile(filePath))
                    return false;

#pragma warning disable SYSLIB0057 // Required to read the embedded Authenticode signer certificate.
                using var signer = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
                using var signer2 = new X509Certificate2(signer);
                signerSha256 = Convert.ToHexString(SHA256.HashData(signer2.RawData));
                return true;
            }
            catch
            {
                signerSha256 = null;
                return false;
            }
        }

        private static bool WinVerifyTrustFile(string filePath)
        {
            var pathPtr = Marshal.StringToCoTaskMemUni(filePath);
            var fileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WintrustFileInfo>());
            var dataPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WintrustData>());
            try
            {
                var fileInfo = new WintrustFileInfo
                {
                    cbStruct = (uint)Marshal.SizeOf<WintrustFileInfo>(),
                    pcwszFilePath = pathPtr,
                };
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

                var data = new WintrustData
                {
                    cbStruct = (uint)Marshal.SizeOf<WintrustData>(),
                    dwUIChoice = 2,              // WTD_UI_NONE
                    fdwRevocationChecks = 0,     // Offline-friendly
                    dwUnionChoice = 1,           // WTD_CHOICE_FILE
                    pFile = fileInfoPtr,
                    dwProvFlags = 0x10,          // WTD_REVOCATION_CHECK_NONE
                };
                Marshal.StructureToPtr(data, dataPtr, false);
                var action = GenericVerifyV2;
                return WinVerifyTrust(IntPtr.Zero, ref action, dataPtr) == 0;
            }
            finally
            {
                Marshal.FreeCoTaskMem(dataPtr);
                Marshal.FreeCoTaskMem(fileInfoPtr);
                Marshal.FreeCoTaskMem(pathPtr);
            }
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid actionId, IntPtr trustData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WintrustFileInfo
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WintrustData
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }
    }
}
