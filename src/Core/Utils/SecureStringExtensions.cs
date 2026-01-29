using System;
using System.Runtime.InteropServices;
using System.Security;

namespace PhantomVault.Core.Utils
{
    public static class SecureStringExtensions
    {
        public static SecureString ToSecureString(this string input)
        {
            var secure = new SecureString();
            if (string.IsNullOrEmpty(input))
            {
                secure.MakeReadOnly();
                return secure;
            }

            foreach (var c in input)
            {
                secure.AppendChar(c);
            }
            secure.MakeReadOnly();
            return secure;
        }

        public static string ToUnsecureString(this SecureString secureString)
        {
            if (secureString == null) return string.Empty;

            IntPtr unmanagedString = IntPtr.Zero;
            try
            {
                unmanagedString = Marshal.SecureStringToGlobalAllocUnicode(secureString);
                return Marshal.PtrToStringUni(unmanagedString) ?? string.Empty;
            }
            finally
            {
                if (unmanagedString != IntPtr.Zero)
                {
                    Marshal.ZeroFreeGlobalAllocUnicode(unmanagedString);
                }
            }
        }
    }
}
