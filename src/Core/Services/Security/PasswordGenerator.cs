using System;
using System.Security.Cryptography;
using System.Text;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Options for <see cref="PasswordGenerator"/>.
    /// </summary>
    public sealed class PasswordGenerationOptions
    {
        public int Length { get; set; } = 20;
        public bool IncludeLowercase { get; set; } = true;
        public bool IncludeUppercase { get; set; } = true;
        public bool IncludeDigits { get; set; } = true;
        public bool IncludeSymbols { get; set; } = true;

        /// <summary>
        /// Drops characters that are easy to confuse when read aloud or transcribed
        /// (O/0, l/1/I, etc). Costs a little entropy per character but avoids the
        /// far more common failure of a password being typed back incorrectly.
        /// </summary>
        public bool ExcludeAmbiguous { get; set; } = true;
    }

    /// <summary>
    /// Cryptographically secure password generation.
    ///
    /// Lives in Core rather than in a ViewModel because generation is now needed from
    /// several places — the add/edit form, the AutoFill save prompt, and in-place
    /// generation when focusing an empty password field.
    /// </summary>
    public static class PasswordGenerator
    {
        private const string Lower = "abcdefghijkmnopqrstuvwxyz";      // no 'l'
        private const string LowerFull = "abcdefghijklmnopqrstuvwxyz";
        private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";       // no 'I', 'O'
        private const string UpperFull = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string Digits = "23456789";                      // no '0', '1'
        private const string DigitsFull = "0123456789";
        private const string Symbols = "!@#$%^&*()-_=+[]{};:,.?";

        public static string Generate(PasswordGenerationOptions? options = null)
        {
            options ??= new PasswordGenerationOptions();

            var pools = new System.Collections.Generic.List<string>(4);
            if (options.IncludeLowercase) pools.Add(options.ExcludeAmbiguous ? Lower : LowerFull);
            if (options.IncludeUppercase) pools.Add(options.ExcludeAmbiguous ? Upper : UpperFull);
            if (options.IncludeDigits) pools.Add(options.ExcludeAmbiguous ? Digits : DigitsFull);
            if (options.IncludeSymbols) pools.Add(Symbols);

            // Never return an empty or single-class password just because the caller
            // switched everything off.
            if (pools.Count == 0) pools.Add(LowerFull);

            int length = Math.Clamp(options.Length, 8, 128);
            string all = string.Concat(pools);

            var chars = new char[length];

            // Seed one character from each selected pool so the result actually
            // satisfies the requested classes, then fill the remainder freely.
            for (int i = 0; i < pools.Count && i < length; i++)
                chars[i] = Pick(pools[i]);

            for (int i = pools.Count; i < length; i++)
                chars[i] = Pick(all);

            Shuffle(chars);
            return new string(chars);
        }

        /// <summary>Generates a passphrase-style separated group password, e.g. XKCD-7F2Q-M4TP.</summary>
        public static string GenerateGrouped(int groups = 4, int groupSize = 4)
        {
            groups = Math.Clamp(groups, 2, 12);
            groupSize = Math.Clamp(groupSize, 2, 8);

            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var sb = new StringBuilder(groups * (groupSize + 1));

            for (int g = 0; g < groups; g++)
            {
                if (g > 0) sb.Append('-');
                for (int i = 0; i < groupSize; i++) sb.Append(Pick(alphabet));
            }

            return sb.ToString();
        }

        private static char Pick(string pool) => pool[RandomNumberGenerator.GetInt32(pool.Length)];

        /// <summary>Fisher-Yates using a CSPRNG, so the seeded positions are not predictable.</summary>
        private static void Shuffle(char[] buffer)
        {
            for (int i = buffer.Length - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
            }
        }
    }
}
