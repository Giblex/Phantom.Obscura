using System;
using System.Text.Json.Serialization;

namespace PhantomVault.Core.Models
{

    public class SharedTotpEntry
    {

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string LinkedPasswordEntryId { get; set; } = string.Empty;

        public string Issuer { get; set; } = string.Empty;

        public string AccountName { get; set; } = string.Empty;

        public string Secret { get; set; } = string.Empty;

        public int Digits { get; set; } = 6;

        public int Period { get; set; } = 30;

        public string Algorithm { get; set; } = "SHA1";

        public string? Label { get; set; }

        public string? Notes { get; set; }

        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset LastModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.MinValue;

        public string ModifiedByApp { get; set; } = "PhantomObscura";

        public void Normalize()
        {

            if (LastModifiedUtc == default && ModifiedUtc != default)
            {
                LastModifiedUtc = ModifiedUtc;
            }

            if (CreatedUtc == default)
            {
                CreatedUtc = LastModifiedUtc == default ? DateTimeOffset.UtcNow : LastModifiedUtc;
            }

            Issuer ??= string.Empty;
            AccountName ??= string.Empty;
            Secret ??= string.Empty;
            Algorithm ??= "SHA1";
            LinkedPasswordEntryId ??= string.Empty;
        }
    }
}

