using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Autofill
{

    public class NativeMessagingHostService : INativeMessagingHost
    {
        private readonly ICredentialRepository _credentialRepository;
        private readonly IAutofillVaultContext _vaultContext;
        private readonly ISyncStateBridge? _syncBridge;
        private bool _isRunning;

        private readonly HashSet<string> _allowedOrigins;

        // Serializes all writes to stdout: response writes from the read loop and
        // unsolicited onSyncChanged push writes from the sync watcher can race.
        private readonly SemaphoreSlim _stdoutGate = new(1, 1);
        private Stream? _stdout;

        public bool IsRunning => _isRunning;

        public event EventHandler<FormDetectedEventArgs>? FormDetected;

        public event EventHandler<FormSubmittedEventArgs>? FormSubmitted;

        private PendingFillRequest? _pendingFill;

        public void SetPendingFill(string username, string password, string? totpCode = null)
        {
            _pendingFill = new PendingFillRequest
            {
                Username = username,
                Password = password,
                TotpCode = totpCode
            };
        }

        public void ClearPendingFill() => _pendingFill = null;

        public NativeMessagingHostService(
            ICredentialRepository credentialRepository,
            IAutofillVaultContext vaultContext,
            IEnumerable<string>? allowedOrigins = null,
            ISyncStateBridge? syncBridge = null)
        {
            _credentialRepository = credentialRepository ?? throw new ArgumentNullException(nameof(credentialRepository));
            _vaultContext = vaultContext ?? throw new ArgumentNullException(nameof(vaultContext));
            _allowedOrigins = BuildAllowlist(allowedOrigins);
            _syncBridge = syncBridge;
        }

        public NativeMessagingHostService(ICredentialRepository credentialRepository)
            : this(credentialRepository, new LockedVaultContext(), null)
        {
        }

        public bool ValidateOrigin(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin) || _allowedOrigins.Count == 0)
                return false;

            return _allowedOrigins.Contains(origin.Trim());
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isRunning)
                throw new InvalidOperationException("Native messaging host is already running");

            _isRunning = true;

            try
            {
                using var stdin = Console.OpenStandardInput();
                using var stdout = Console.OpenStandardOutput();
                _stdout = stdout;

                // Push non-secret sync changes to the extension as they happen.
                if (_syncBridge != null)
                    _syncBridge.Changed += OnSyncBridgeChanged;

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {

                        var lengthBytes = new byte[4];
                        var bytesRead = await stdin.ReadAsync(lengthBytes, 0, 4, cancellationToken);

                        if (bytesRead == 0)
                        {

                            break;
                        }

                        if (bytesRead != 4)
                        {
                            await SendErrorAsync(stdout, "Invalid message length header", cancellationToken);
                            continue;
                        }

                        var messageLength = BitConverter.ToInt32(lengthBytes, 0);

                        if (messageLength <= 0 || messageLength > 1024 * 1024)
                        {
                            await SendErrorAsync(stdout, "Message length out of bounds", cancellationToken);
                            continue;
                        }

                        var messageBytes = new byte[messageLength];
                        var totalRead = 0;

                        while (totalRead < messageLength && !cancellationToken.IsCancellationRequested)
                        {
                            var read = await stdin.ReadAsync(
                                messageBytes,
                                totalRead,
                                messageLength - totalRead,
                                cancellationToken);

                            if (read == 0)
                                break;

                            totalRead += read;
                        }

                        if (totalRead != messageLength)
                        {
                            await SendErrorAsync(stdout, "Incomplete message received", cancellationToken);
                            continue;
                        }

                        var messageJson = Encoding.UTF8.GetString(messageBytes);

                        var response = await HandleMessageAsync(messageJson, cancellationToken);

                        await SendMessageAsync(stdout, response, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {

                        await SendErrorAsync(stdout, $"Error processing message: {ex.Message}", cancellationToken);
                    }
                }
            }
            finally
            {
                if (_syncBridge != null)
                    _syncBridge.Changed -= OnSyncBridgeChanged;
                _stdout = null;
                _isRunning = false;
            }
        }

        private async Task<string> HandleMessageAsync(string messageJson, CancellationToken cancellationToken)
        {
            try
            {
                var message = JsonSerializer.Deserialize<NativeMessage>(messageJson);

                if (message == null)
                    return CreateErrorResponse("Invalid message format", null);

                if (string.IsNullOrEmpty(message.Origin) || !ValidateOrigin(message.Origin))
                    return CreateErrorResponse("Unauthorized origin", message.RequestId);

                if (message.Type == "getCredentials" || message.Type == "saveCredential")
                {
                    var (allowed, reason) = EnsureAutofillAllowed();
                    if (!allowed)
                    {
                        return CreateErrorResponse(reason ?? "Autofill is disabled while the vault is locked.", message.RequestId);
                    }
                }

                return message.Type switch
                {
                    "getCredentials" => await HandleGetCredentialsAsync(message, cancellationToken),
                    "saveCredential" => await HandleSaveCredentialAsync(message, cancellationToken),
                    "detectForm" => HandleDetectForm(message),
                    "submitForm" => HandleSubmitForm(message),

                    "fill" => HandleFill(message),

                    "detectTotp" => HandleDetectTotp(message),
                    "getSyncState" => HandleGetSyncState(message.RequestId),
                    "pushSyncState" => HandlePushSyncState(message, message.RequestId),
                    "ping" => CreateSuccessResponse(new { status = "ok" }, message.RequestId),
                    _ => CreateErrorResponse($"Unknown message type: {message.Type}", message.RequestId)
                };
            }
            catch (JsonException ex)
            {
                return CreateErrorResponse($"JSON parsing error: {ex.Message}", null);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Error handling message: {ex.Message}", null);
            }
        }

        private async Task<string> HandleGetCredentialsAsync(NativeMessage message, CancellationToken cancellationToken)
        {
            try
            {
                var domain = message.Data?.GetProperty("domain").GetString();

                if (string.IsNullOrWhiteSpace(domain))
                    return CreateErrorResponse("Domain not specified", message.RequestId);

                var credentials = await _credentialRepository.GetCredentialsByDomainAsync(domain, cancellationToken);

                var credentialList = credentials
                    .OrderByDescending(c => c.LastUsedUtc ?? DateTimeOffset.MinValue)
                    .Take(3)
                    .Select(c => new
                    {
                        id = c.Title,
                        username = c.Username,
                        password = c.Password,
                        title = c.Title,
                        domain = ExtractDomain(c.Url),
                        url = c.Url
                    }).ToArray();

                return CreateSuccessResponse(new { credentials = credentialList }, message.RequestId);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Failed to get credentials: {ex.Message}", message.RequestId);
            }
        }

        private async Task<string> HandleSaveCredentialAsync(NativeMessage message, CancellationToken cancellationToken)
        {
            try
            {
                var data = message.Data;
                if (!data.HasValue)
                    return CreateErrorResponse("No data provided", message.RequestId);

                var domain = data.Value.GetProperty("domain").GetString();
                var username = data.Value.GetProperty("username").GetString();
                var password = data.Value.GetProperty("password").GetString();
                var title = data.Value.TryGetProperty("title", out var titleProp)
                    ? titleProp.GetString()
                    : $"{domain} - {username}";

                if (string.IsNullOrWhiteSpace(domain) ||
                    string.IsNullOrWhiteSpace(username) ||
                    string.IsNullOrWhiteSpace(password))
                {
                    return CreateErrorResponse("Missing required fields", message.RequestId);
                }

                var credential = new Credential
                {
                    Url = domain,
                    Username = username!,
                    Password = password!,
                    Title = title ?? $"{domain} - {username}",
                    CreatedUtc = DateTimeOffset.UtcNow,
                    LastUpdatedUtc = DateTimeOffset.UtcNow
                };

                await _credentialRepository.SaveCredentialAsync(credential, cancellationToken);

                return CreateSuccessResponse(new { saved = true }, message.RequestId);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Failed to save credential: {ex.Message}", message.RequestId);
            }
        }

        private string HandleDetectForm(NativeMessage message)
        {
            try
            {
                var data = message.Data;
                if (!data.HasValue)
                    return CreateErrorResponse("No data provided", message.RequestId);

                var url = data.Value.GetProperty("url").GetString();
                if (string.IsNullOrWhiteSpace(url))
                    return CreateErrorResponse("URL not specified", message.RequestId);

                var fieldsJson = data.Value.GetProperty("fields");
                var fields = new List<FormFieldInfo>();

                foreach (var fieldJson in fieldsJson.EnumerateArray())
                {
                    var field = new FormFieldInfo
                    {
                        Id = fieldJson.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "",
                        Name = fieldJson.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
                        Type = fieldJson.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "" : "",
                        Placeholder = fieldJson.TryGetProperty("placeholder", out var placeholderProp) ? placeholderProp.GetString() ?? "" : "",
                        Label = fieldJson.TryGetProperty("label", out var labelProp) ? labelProp.GetString() ?? "" : "",
                        AutoComplete = fieldJson.TryGetProperty("autocomplete", out var autoCompleteProp) ? autoCompleteProp.GetString() ?? "" : "",
                        Value = fieldJson.TryGetProperty("value", out var valueProp) ? valueProp.GetString() ?? "" : "",
                        BoundingBox = new BoundingBox
                        {
                            X = fieldJson.TryGetProperty("x", out var xProp) ? xProp.GetDouble() : 0,
                            Y = fieldJson.TryGetProperty("y", out var yProp) ? yProp.GetDouble() : 0,
                            Width = fieldJson.TryGetProperty("width", out var widthProp) ? widthProp.GetDouble() : 0,
                            Height = fieldJson.TryGetProperty("height", out var heightProp) ? heightProp.GetDouble() : 0
                        }
                    };

                    fields.Add(field);
                }

                FormDetected?.Invoke(this, new FormDetectedEventArgs
                {
                    Url = url,
                    Fields = fields
                });

                return CreateSuccessResponse(new { detected = true, fieldCount = fields.Count }, message.RequestId);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Failed to detect form: {ex.Message}", message.RequestId);
            }
        }

        private string HandleSubmitForm(NativeMessage message)
        {
            try
            {
                var data = message.Data;
                if (!data.HasValue)
                    return CreateErrorResponse("No data provided", message.RequestId);

                var url = data.Value.GetProperty("url").GetString();
                if (string.IsNullOrWhiteSpace(url))
                    return CreateErrorResponse("URL not specified", message.RequestId);

                var fieldsJson = data.Value.GetProperty("fields");
                var fields = new List<FormFieldInfo>();
                var fieldValues = new Dictionary<string, string>();

                foreach (var fieldJson in fieldsJson.EnumerateArray())
                {
                    var fieldId = fieldJson.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                    var fieldValue = fieldJson.TryGetProperty("value", out var valueProp) ? valueProp.GetString() ?? "" : "";

                    var field = new FormFieldInfo
                    {
                        Id = fieldId,
                        Name = fieldJson.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
                        Type = fieldJson.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "" : "",
                        Placeholder = fieldJson.TryGetProperty("placeholder", out var placeholderProp) ? placeholderProp.GetString() ?? "" : "",
                        Label = fieldJson.TryGetProperty("label", out var labelProp) ? labelProp.GetString() ?? "" : "",
                        AutoComplete = fieldJson.TryGetProperty("autocomplete", out var autoCompleteProp) ? autoCompleteProp.GetString() ?? "" : "",
                        Value = fieldValue,
                        BoundingBox = new BoundingBox
                        {
                            X = fieldJson.TryGetProperty("x", out var xProp) ? xProp.GetDouble() : 0,
                            Y = fieldJson.TryGetProperty("y", out var yProp) ? yProp.GetDouble() : 0,
                            Width = fieldJson.TryGetProperty("width", out var widthProp) ? widthProp.GetDouble() : 0,
                            Height = fieldJson.TryGetProperty("height", out var heightProp) ? heightProp.GetDouble() : 0
                        }
                    };

                    fields.Add(field);

                    if (!string.IsNullOrEmpty(fieldId))
                        fieldValues[fieldId] = fieldValue;
                }

                FormSubmitted?.Invoke(this, new FormSubmittedEventArgs
                {
                    Url = url,
                    Fields = fields,
                    FieldValues = fieldValues
                });

                return CreateSuccessResponse(new { submitted = true }, message.RequestId);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Failed to handle form submission: {ex.Message}", message.RequestId);
            }
        }

        private string HandleFill(NativeMessage message)
        {
            var (allowed, reason) = EnsureAutofillAllowed();
            if (!allowed)
            {
                _pendingFill = null;
                return CreateErrorResponse(reason ?? "Autofill is disabled.", message.RequestId);
            }

            if (_pendingFill is null)
                return CreateSuccessResponse(new { hasFill = false }, message.RequestId);

            var fill = _pendingFill;
            _pendingFill = null;

            return CreateSuccessResponse(new
            {
                hasFill = true,
                username = fill.Username,
                password = fill.Password,
                totpCode = fill.TotpCode
            }, message.RequestId);
        }

        private string HandleDetectTotp(NativeMessage message)
        {
            try
            {
                var data = message.Data;
                if (!data.HasValue)
                    return CreateErrorResponse("No data provided", message.RequestId);

                var url = data.Value.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                var fieldId = data.Value.TryGetProperty("fieldId", out var idProp) ? idProp.GetString() ?? "" : "";
                var fieldName = data.Value.TryGetProperty("fieldName", out var nameProp) ? nameProp.GetString() ?? "" : "";

                FormDetected?.Invoke(this, new FormDetectedEventArgs
                {
                    Url = url,
                    Fields = new List<FormFieldInfo>
                    {
                        new FormFieldInfo
                        {
                            Id = fieldId,
                            Name = fieldName,
                            Type = "text",
                            AutoComplete = "one-time-code"
                        }
                    }
                });

                return CreateSuccessResponse(new { received = true }, message.RequestId);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"detectTotp error: {ex.Message}", message.RequestId);
            }
        }

        private string ExtractDomain(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            try
            {
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                }

                var uri = new Uri(url);
                return uri.Host;
            }
            catch
            {
                return url;
            }
        }

        // ── Sync (non-secret layer only) ──────────────────────────────────────
        // getSyncState / pushSyncState / onSyncChanged mirror theme, UI prefs and
        // the origin allowlist between the desktop app and the extension. They are
        // deliberately NOT gated on vault unlock because they carry no secrets, and
        // they never touch credentials or TOTP secrets.

        private static readonly JsonSerializerOptions SyncJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private string HandleGetSyncState(int? requestId)
        {
            if (_syncBridge == null)
                return CreateErrorResponse("Sync is not available.", requestId);

            try
            {
                return JsonSerializer.Serialize(
                    new { success = true, data = _syncBridge.Read(), _reqId = requestId }, SyncJsonOptions);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Failed to read sync state: {ex.Message}");
            }
        }

        private string HandlePushSyncState(NativeMessage message, int? requestId)
        {
            if (_syncBridge == null)
                return CreateErrorResponse("Sync is not available.", requestId);

            if (message.Data == null)
                return CreateErrorResponse("pushSyncState requires a data payload.", requestId);

            try
            {
                var incoming = JsonSerializer.Deserialize<SyncState>(
                    message.Data.Value.GetRawText(), SyncJsonOptions);
                if (incoming == null)
                    return CreateErrorResponse("Invalid sync payload.", requestId);

                // Defence in depth: never accept secret-bearing fields from the
                // extension. Only theme + UI prefs are writable back to the app.
                incoming.TotpIssuers = new System.Collections.Generic.List<string>();
                _syncBridge.Apply(incoming);
                return CreateSuccessResponse(new { applied = true }, requestId);
            }
            catch (JsonException ex)
            {
                return CreateErrorResponse($"Invalid sync payload: {ex.Message}", requestId);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Failed to apply sync state: {ex.Message}", requestId);
            }
        }

        private void OnSyncBridgeChanged(object? sender, EventArgs e)
        {
            if (_syncBridge == null) return;

            try
            {
                var state = _syncBridge.Read();
                var push = JsonSerializer.Serialize(
                    new { type = "syncChanged", data = state }, SyncJsonOptions);
                // Fire-and-forget; the stdout gate serializes against response writes.
                _ = PushToExtensionAsync(push);
            }
            catch
            {
                // Best-effort push; a missed notification is recovered on next getSyncState.
            }
        }

        private async Task PushToExtensionAsync(string message)
        {
            var stdout = _stdout;
            if (stdout == null) return;

            try
            {
                await SendMessageAsync(stdout, message, CancellationToken.None);
            }
            catch
            {
                // Extension may have disconnected; ignore.
            }
        }

        private string CreateSuccessResponse(object data, int? requestId = null)
        {
            var response = new
            {
                success = true,
                data,
                _reqId = requestId
            };
            var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            return JsonSerializer.Serialize(response, options);
        }

        private string CreateErrorResponse(string error, int? requestId = null)
        {
            var response = new
            {
                success = false,
                error,
                _reqId = requestId
            };
            var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            return JsonSerializer.Serialize(response, options);
        }

        private async Task SendMessageAsync(Stream stdout, string message, CancellationToken cancellationToken)
        {
            var messageBytes = Encoding.UTF8.GetBytes(message);
            var lengthBytes = BitConverter.GetBytes(messageBytes.Length);

            await _stdoutGate.WaitAsync(cancellationToken);
            try
            {
                await stdout.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken);
                await stdout.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
                await stdout.FlushAsync(cancellationToken);
            }
            finally
            {
                _stdoutGate.Release();
            }
        }

        private async Task SendErrorAsync(Stream stdout, string error, CancellationToken cancellationToken)
        {
            var errorResponse = CreateErrorResponse(error);
            await SendMessageAsync(stdout, errorResponse, cancellationToken);
        }

        private class NativeMessage
        {
            public string Type { get; set; } = string.Empty;
            public string? Origin { get; set; }
            [JsonPropertyName("_reqId")]
            public int? RequestId { get; set; }
            public JsonElement? Data { get; set; }
        }

        private sealed class LockedVaultContext : IAutofillVaultContext
        {
            public bool IsUnlocked => false;
            public VaultManifest? CurrentManifest => null;
        }

        private sealed class PendingFillRequest
        {
            public string Username { get; init; } = string.Empty;
            public string Password { get; init; } = string.Empty;
            public string? TotpCode { get; init; }
        }

        private static HashSet<string> BuildAllowlist(IEnumerable<string>? allowedOrigins)
        {
            if (allowedOrigins == null)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var origin in allowedOrigins)
            {
                var trimmed = origin?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                set.Add(trimmed);
            }
            return set;
        }

        private (bool Allowed, string? Reason) EnsureAutofillAllowed()
        {
            if (!_vaultContext.IsUnlocked)
            {
                return (false, "Vault is locked. Unlock the vault to enable autofill.");
            }

            var manifest = _vaultContext.CurrentManifest;
            if (manifest == null)
            {
                return (false, "Vault manifest is not available; autofill is disabled.");
            }

            if (!manifest.AutoFillEnabled)
            {
                return (false, "Autofill is disabled in this vault's manifest.");
            }

            return (true, null);
        }
    }

    public sealed class FormDetectedEventArgs : EventArgs
    {
        public string Url { get; set; } = string.Empty;
        public List<FormFieldInfo> Fields { get; set; } = new();
    }

    public sealed class FormSubmittedEventArgs : EventArgs
    {
        public string Url { get; set; } = string.Empty;
        public List<FormFieldInfo> Fields { get; set; } = new();
        public Dictionary<string, string> FieldValues { get; set; } = new();
    }
}

