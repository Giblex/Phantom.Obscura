using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.Autofill;

namespace PhantomVault.UI.Services.AutoFill
{
    /// <summary>
    /// <see cref="ICredentialRepository"/> implementation that delegates to the running
    /// desktop app via <see cref="PipeNativeHostClient"/>. Used exclusively by the
    /// <c>--native-messaging</c> subprocess so <see cref="NativeMessagingHostService"/>
    /// can read vault credentials without duplicating vault state in-process.
    /// </summary>
    public sealed class PipeBackedCredentialRepository : ICredentialRepository
    {
        private readonly PipeNativeHostClient _client;

        public PipeBackedCredentialRepository(PipeNativeHostClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<List<Credential>> GetCredentialsByDomainAsync(
            string domain, CancellationToken ct = default)
        {
            var req = JsonSerializer.Serialize(new { action = "getCredentials", domain });
            var resp = await _client.SendAsync(req, ct);
            return ParseCredentialList(resp);
        }

        public async Task SaveCredentialAsync(Credential credential, CancellationToken ct = default)
        {
            var req = JsonSerializer.Serialize(new
            {
                action = "saveCredential",
                domain = ExtractDomain(credential.Url),
                title = credential.Title,
                username = credential.Username,
                password = credential.Password
            });
            await _client.SendAsync(req, ct);
        }

        public Task UpdateCredentialAsync(Credential credential, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task DeleteCredentialAsync(string title, CancellationToken ct = default) =>
            Task.CompletedTask;

        public async Task<List<Credential>> GetAllCredentialsAsync(CancellationToken ct = default) =>
            await GetCredentialsByDomainAsync(string.Empty, ct);

        private static List<Credential> ParseCredentialList(string? responseJson)
        {
            if (responseJson == null) return new List<Credential>();

            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;

                if (!root.TryGetProperty("success", out var s) || !s.GetBoolean())
                    return new List<Credential>();

                if (!root.TryGetProperty("credentials", out var arr))
                    return new List<Credential>();

                var list = new List<Credential>();
                foreach (var item in arr.EnumerateArray())
                {
                    list.Add(new Credential
                    {
                        Title = GetString(item, "title"),
                        Username = GetString(item, "username"),
                        Url = GetString(item, "url")
                    });
                }
                return list;
            }
            catch
            {
                return new List<Credential>();
            }
        }

        private static string GetString(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var v) ? v.GetString() ?? string.Empty : string.Empty;

        private static string ExtractDomain(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            try { return new Uri(url).Host; }
            catch { return url; }
        }
    }
}
