using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Autofill
{
    /// <summary>
    /// Native messaging host implementation for browser extension communication.
    /// Implements the Chrome/Firefox native messaging protocol:
    /// - Reads 4-byte message length (little-endian) from stdin
    /// - Reads JSON message body of specified length
    /// - Writes 4-byte response length + JSON response to stdout
    /// 
    /// Security:
    /// - Validates extension origins against allowlist
    /// - Never logs credentials
    /// - Validates message structure before processing
    /// </summary>
    public class NativeMessagingHostService : INativeMessagingHost
    {
        private readonly ICredentialRepository _credentialRepository;
        private readonly IAutofillVaultContext _vaultContext;
        private bool _isRunning;

        // Allowed extension IDs (in production, load from config). Defaults to an empty set to fail closed.
        private readonly HashSet<string> _allowedOrigins;

        public bool IsRunning => _isRunning;

        /// <summary>
        /// Event raised when a form is detected on a webpage.
        /// </summary>
        public event EventHandler<FormDetectedEventArgs>? FormDetected;

        /// <summary>
        /// Event raised when a form is submitted (for password capture).
        /// </summary>
        public event EventHandler<FormSubmittedEventArgs>? FormSubmitted;

        /// <summary>
        /// Pending fill request set by the AutoFill orchestrator so the extension
        /// can pick it up on its next <c>fill</c> or <c>detectForm</c> poll.
        /// </summary>
        private PendingFillRequest? _pendingFill;

        /// <summary>
        /// Registers a credential fill that the browser extension should apply to
        /// the active login form. Called by the AutoFill orchestrator after matching.
        /// </summary>
        public void SetPendingFill(string username, string password, string? totpCode = null)
        {
            _pendingFill = new PendingFillRequest
            {
                Username = username,
                Password = password,
                TotpCode = totpCode
            };
        }

        /// <summary>Clears any pending fill (e.g. after the extension confirms fill).</summary>
        public void ClearPendingFill() => _pendingFill = null;

        public NativeMessagingHostService(
            ICredentialRepository credentialRepository,
            IAutofillVaultContext vaultContext,
            IEnumerable<string>? allowedOrigins = null)
        {
            _credentialRepository = credentialRepository ?? throw new ArgumentNullException(nameof(credentialRepository));
            _vaultContext = vaultContext ?? throw new ArgumentNullException(nameof(vaultContext));
            _allowedOrigins = BuildAllowlist(allowedOrigins);
        }

        // Backward-compatible constructor that fails closed until a real context and allowlist are supplied.
        public NativeMessagingHostService(ICredentialRepository credentialRepository)
            : this(credentialRepository, new LockedVaultContext(), null)
        {
        }

        public bool ValidateOrigin(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin) || _allowedOrigins.Count == 0)
                return false;

            // Origin must match exactly (case-insensitive)
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

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Read message length (4 bytes, little-endian)
                        var lengthBytes = new byte[4];
                        var bytesRead = await stdin.ReadAsync(lengthBytes, 0, 4, cancellationToken);

                        if (bytesRead == 0)
                        {
                            // stdin closed - browser extension disconnected
                            break;
                        }

                        if (bytesRead != 4)
                        {
                            await SendErrorAsync(stdout, "Invalid message length header", cancellationToken);
                            continue;
                        }

                        var messageLength = BitConverter.ToInt32(lengthBytes, 0);

                        // Sanity check - max message size 1MB
                        if (messageLength <= 0 || messageLength > 1024 * 1024)
                        {
                            await SendErrorAsync(stdout, "Message length out of bounds", cancellationToken);
                            continue;
                        }

                        // Read message body
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

                        // Parse and handle message
                        var response = await HandleMessageAsync(messageJson, cancellationToken);

                        // Send response
                        await SendMessageAsync(stdout, response, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        // Log error but continue running
                        await SendErrorAsync(stdout, $"Error processing message: {ex.Message}", cancellationToken);
                    }
                }
            }
            finally
            {
                _isRunning = false;
            }
        }

        private async Task<string> HandleMessageAsync(string messageJson, CancellationToken cancellationToken)
        {
            try
            {
                var message = JsonSerializer.Deserialize<NativeMessage>(messageJson);

                if (message == null)
                    return CreateErrorResponse("Invalid message format");

                // Validate origin
                if (string.IsNullOrEmpty(message.Origin) || !ValidateOrigin(message.Origin))
                    return CreateErrorResponse("Unauthorized origin");

                // Enforce vault unlock + manifest flag for credential operations
                if (message.Type == "getCredentials" || message.Type == "saveCredential")
                {
                    var (allowed, reason) = EnsureAutofillAllowed();
                    if (!allowed)
                    {
                        return CreateErrorResponse(reason ?? "Autofill is disabled while the vault is locked.");
                    }
                }

                // Handle different message types
                return message.Type switch
                {
                    "getCredentials" => await HandleGetCredentialsAsync(message, cancellationToken),
                    "saveCredential" => await HandleSaveCredentialAsync(message, cancellationToken),
                    "detectForm" => HandleDetectForm(message),
                    "submitForm" => HandleSubmitForm(message),
                    // AutoFill Mode: extension polls for pending fill after USB insertion
                    "fill" => HandleFill(message),
                    // AutoFill Mode: extension signals that a TOTP field has appeared
                    "detectTotp" => HandleDetectTotp(message),
                    "ping" => CreateSuccessResponse(new { status = "ok" }),
                    _ => CreateErrorResponse($"Unknown message type: {message.Type}")
                };
            }
            catch (JsonException ex)
            {
                return CreateErrorResponse($"JSON parsing error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Error handling message: {ex.Message}");
            }
        }

        private async Task<string> HandleGetCredentialsAsync(NativeMessage message, CancellationToken cancellationToken)
        {
            try
            {
                var domain = message.Data?.GetProperty("domain").GetString();

                if (string.IsNullOrWhiteSpace(domain))
                    return CreateErrorResponse("Domain not specified");

                var credentials = await _credentialRepository.GetCredentialsByDomainAsync(domain, cancellationToken);

                // Map to response format (never send actual passwords in response - only metadata)
                var credentialList = credentials.Select(c => new
                {
                    id = c.Title, // Using Title as ID for now
                    username = c.Username,
                    title = c.Title,
                    domain = ExtractDomain(c.Url)
                }).ToArray();

                return CreateSuccessResponse(new { credentials = credentialList });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Failed to get credentials: {ex.Message}");
            }
        }

        private async Task<string> HandleSaveCredentialAsync(NativeMessage message, CancellationToken cancellationToken)
        {
            try
            {
                var data = message.Data;
                if (!data.HasValue)
                    return CreateErrorResponse("No data provided");

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
                    return CreateErrorResponse("Missing required fields");
                }

                // Save credential to repository
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

                return CreateSuccessResponse(new { saved = true });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Failed to save credential: {ex.Message}");
            }
        }

        private string HandleDetectForm(NativeMessage message)
        {
            try
            {
                var data = message.Data;
                if (!data.HasValue)
                    return CreateErrorResponse("No data provided");

                var url = data.Value.GetProperty("url").GetString();
                if (string.IsNullOrWhiteSpace(url))
                    return CreateErrorResponse("URL not specified");

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

                // Raise event for UI handling
                FormDetected?.Invoke(this, new FormDetectedEventArgs
                {
                    Url = url,
                    Fields = fields
                });

                return CreateSuccessResponse(new { detected = true, fieldCount = fields.Count });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Failed to detect form: {ex.Message}");
            }
        }

        private string HandleSubmitForm(NativeMessage message)
        {
            try
            {
                var data = message.Data;
                if (!data.HasValue)
                    return CreateErrorResponse("No data provided");

                var url = data.Value.GetProperty("url").GetString();
                if (string.IsNullOrWhiteSpace(url))
                    return CreateErrorResponse("URL not specified");

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

                // Raise event for password capture
                FormSubmitted?.Invoke(this, new FormSubmittedEventArgs
                {
                    Url = url,
                    Fields = fields,
                    FieldValues = fieldValues
                });

                return CreateSuccessResponse(new { submitted = true });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Failed to handle form submission: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles a <c>fill</c> message from the browser extension.
        /// If a pending fill request exists (set by the AutoFill orchestrator after USB
        /// insertion), it is returned and cleared. Otherwise responds with no-op.
        /// </summary>
        private string HandleFill(NativeMessage message)
        {
            var (allowed, reason) = EnsureAutofillAllowed();
            if (!allowed)
            {
                _pendingFill = null;
                return CreateErrorResponse(reason ?? "Autofill is disabled.");
            }

            if (_pendingFill is null)
                return CreateSuccessResponse(new { hasFill = false });

            var fill = _pendingFill;
            _pendingFill = null; // consume once

            return CreateSuccessResponse(new
            {
                hasFill = true,
                username = fill.Username,
                password = fill.Password,
                totpCode = fill.TotpCode
            });
        }

        /// <summary>
        /// Handles a <c>detectTotp</c> message: the browser extension reports that
        /// a 2FA code field has appeared after password fill. Raises <see cref="FormDetected"/>
        /// with the TOTP field so <c>TotpFieldPoller</c> can resolve its wait.
        /// </summary>
        private string HandleDetectTotp(NativeMessage message)
        {
            try
            {
                var data = message.Data;
                if (!data.HasValue)
                    return CreateErrorResponse("No data provided");

                var url = data.Value.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                var fieldId = data.Value.TryGetProperty("fieldId", out var idProp) ? idProp.GetString() ?? "" : "";
                var fieldName = data.Value.TryGetProperty("fieldName", out var nameProp) ? nameProp.GetString() ?? "" : "";

                // Synthesise a FormDetected event with a TwoFactor field so that
                // TotpFieldPoller's subscription resolves.
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

                return CreateSuccessResponse(new { received = true });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"detectTotp error: {ex.Message}");
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

        private string CreateSuccessResponse(object data)
        {
            var response = new
            {
                success = true,
                data
            };
            return JsonSerializer.Serialize(response);
        }

        private string CreateErrorResponse(string error)
        {
            var response = new
            {
                success = false,
                error
            };
            return JsonSerializer.Serialize(response);
        }

        private async Task SendMessageAsync(Stream stdout, string message, CancellationToken cancellationToken)
        {
            var messageBytes = Encoding.UTF8.GetBytes(message);
            var lengthBytes = BitConverter.GetBytes(messageBytes.Length);

            // Write length
            await stdout.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken);

            // Write message
            await stdout.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
            await stdout.FlushAsync(cancellationToken);
        }

        private async Task SendErrorAsync(Stream stdout, string error, CancellationToken cancellationToken)
        {
            var errorResponse = CreateErrorResponse(error);
            await SendMessageAsync(stdout, errorResponse, cancellationToken);
        }

        /// <summary>
        /// Native messaging message format
        /// </summary>
        private class NativeMessage
        {
            public string Type { get; set; } = string.Empty;
            public string? Origin { get; set; }
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

    /// <summary>
    /// Event args for form detection.
    /// </summary>
    public sealed class FormDetectedEventArgs : EventArgs
    {
        public string Url { get; set; } = string.Empty;
        public List<FormFieldInfo> Fields { get; set; } = new();
    }

    /// <summary>
    /// Event args for form submission.
    /// </summary>
    public sealed class FormSubmittedEventArgs : EventArgs
    {
        public string Url { get; set; } = string.Empty;
        public List<FormFieldInfo> Fields { get; set; } = new();
        public Dictionary<string, string> FieldValues { get; set; } = new();
    }
}
