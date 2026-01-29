using System;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;

#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using Android.Service.Autofill;
using Android.Views;
using Android.Views.Autofill;
using Android.Widget;
#endif

namespace PhantomVault.Core.Services.Autofill
{
#if ANDROID
    /// <summary>
    /// Android Autofill Service implementation for PhantomVault.
    /// Integrates with the Android Autofill Framework (API 26+) to provide
    /// seamless password filling in apps and browsers.
    /// </summary>
    [Service(
        Permission = "android.permission.BIND_AUTOFILL_SERVICE",
        Label = "PhantomVault Autofill",
        Exported = true)]
    [IntentFilter(new[] { "android.service.autofill.AutofillService" })]
    [MetaData("android.autofill", Resource = "@xml/autofill_service_config")]
    public class AndroidAutofillService : AutofillService, IAutofillProvider
    {
        private ICredentialRepository? _credentialRepository;

        public bool IsSupported => Build.VERSION.SdkInt >= BuildVersionCodes.O;

        public override void OnFillRequest(FillRequest request, CancellationSignal cancellationSignal, FillCallback callback)
        {
            try
            {
                var context = request.FillContexts?.LastOrDefault();
                if (context == null)
                {
                    callback.OnFailure("No fill context available");
                    return;
                }

                var structure = context.Structure;
                if (structure == null)
                {
                    callback.OnFailure("No view structure available");
                    return;
                }

                // Parse the view structure to find username/password fields
                var fields = ParseStructure(structure);
                if (fields.Count == 0)
                {
                    callback.OnFailure("No compatible fields found");
                    return;
                }

                // Get the package name / domain
                var packageName = structure.ActivityComponent?.PackageName ?? "";
                var webDomain = ExtractWebDomain(structure);

                // Query vault for matching credentials
                var credentials = GetCredentialsForDomain(webDomain ?? packageName);
                if (credentials.Count == 0)
                {
                    callback.OnFailure("No credentials found for this domain");
                    return;
                }

                // Build autofill response
                var responseBuilder = new FillResponse.Builder();
                
                foreach (var credential in credentials)
                {
                    var datasetBuilder = new Dataset.Builder();
                    
                    // Set presentation (what the user sees in the dropdown)
                    var presentation = CreatePresentation(credential.Username, credential.Title);
                    
                    // Fill username field
                    if (fields.TryGetValue("username", out var usernameId))
                    {
                        datasetBuilder.SetValue(usernameId, AutofillValue.ForText(credential.Username), presentation);
                    }
                    
                    // Fill password field
                    if (fields.TryGetValue("password", out var passwordId))
                    {
                        datasetBuilder.SetValue(passwordId, AutofillValue.ForText(credential.Password), presentation);
                    }
                    
                    responseBuilder.AddDataset(datasetBuilder.Build());
                }

                callback.OnSuccess(responseBuilder.Build());
            }
            catch (Exception ex)
            {
                callback.OnFailure($"Autofill failed: {ex.Message}");
            }
        }

        public override void OnSaveRequest(SaveRequest request, SaveCallback callback)
        {
            // Handle save requests (when user enters new credentials)
            try
            {
                var context = request.FillContexts?.LastOrDefault();
                if (context == null)
                {
                    callback.OnFailure("No save context available");
                    return;
                }

                // Extract credentials from the request
                var structure = context.Structure;
                var fields = ParseStructure(structure);

                // Extract domain from package name or web domain
                var packageName = structure.ActivityComponent?.PackageName ?? "unknown";
                string domain = ExtractDomainFromStructure(structure) ?? packageName;

                // Extract username and password from the parsed fields
                string? username = null;
                string? password = null;

                foreach (var kvp in fields)
                {
                    var fieldValue = GetFieldValue(structure, kvp.Value);
                    if (kvp.Key.Contains("username") || kvp.Key.Contains("email"))
                    {
                        username = fieldValue;
                    }
                    else if (kvp.Key.Contains("password"))
                    {
                        password = fieldValue;
                    }
                }

                // Save to vault if we have at least a password
                if (!string.IsNullOrEmpty(password) && _credentialRepository != null)
                {
                    var credential = new Credential
                    {
                        Title = domain,
                        Username = username ?? "",
                        Password = password,
                        Url = domain.StartsWith("http") ? domain : $"android://{domain}",
                        Group = "Android Apps",
                        CreatedUtc = DateTimeOffset.UtcNow,
                        LastUpdatedUtc = DateTimeOffset.UtcNow
                    };

                    // Save asynchronously but wait for completion
                    var saveTask = _credentialRepository.AddCredentialAsync(credential);
                    saveTask.Wait();

                    callback.OnSuccess();
                }
                else
                {
                    callback.OnFailure("No password found to save");
                }
            }
            catch (Exception ex)
            {
                callback.OnFailure($"Save failed: {ex.Message}");
            }
        }

        private Dictionary<string, AutofillId> ParseStructure(AssistStructure structure)
        {
            var fields = new Dictionary<string, AutofillId>();
            
            for (int i = 0; i < structure.WindowNodeCount; i++)
            {
                var windowNode = structure.GetWindowNodeAt(i);
                ParseNode(windowNode.RootViewNode, fields);
            }
            
            return fields;
        }

        private void ParseNode(AssistStructure.ViewNode node, Dictionary<string, AutofillId> fields)
        {
            if (node == null) return;

            var autofillHints = node.AutofillHints;
            var autofillId = node.AutofillId;
            
            if (autofillHints != null && autofillId != null)
            {
                if (autofillHints.Contains(View.AutofillHintUsername) || 
                    autofillHints.Contains(View.AutofillHintEmailAddress))
                {
                    fields["username"] = autofillId;
                }
                else if (autofillHints.Contains(View.AutofillHintPassword))
                {
                    fields["password"] = autofillId;
                }
            }
            else
            {
                // Fallback: Try to detect fields by input type or ID
                var inputType = node.InputType;
                var idEntry = node.IdEntry?.ToLower();
                
                if (inputType.HasFlag(Android.Text.InputTypes.TextVariationPassword))
                {
                    if (!fields.ContainsKey("password") && autofillId != null)
                        fields["password"] = autofillId;
                }
                else if (idEntry != null && 
                         (idEntry.Contains("user") || idEntry.Contains("email")) &&
                         autofillId != null)
                {
                    if (!fields.ContainsKey("username"))
                        fields["username"] = autofillId;
                }
            }

            // Recursively parse child nodes
            for (int i = 0; i < node.ChildCount; i++)
            {
                ParseNode(node.GetChildAt(i), fields);
            }
        }

        private string? ExtractWebDomain(AssistStructure structure)
        {
            // Try to extract the web domain if this is a browser
            for (int i = 0; i < structure.WindowNodeCount; i++)
            {
                var windowNode = structure.GetWindowNodeAt(i);
                var webDomain = windowNode.RootViewNode?.WebDomain;
                if (!string.IsNullOrEmpty(webDomain))
                    return webDomain;
            }
            return null;
        }

        private string? ExtractDomainFromStructure(AssistStructure structure)
        {
            // Try web domain first (for browsers)
            var webDomain = ExtractWebDomain(structure);
            if (!string.IsNullOrEmpty(webDomain))
                return webDomain;

            // Fall back to package name for apps
            return structure.ActivityComponent?.PackageName;
        }

        private string? GetFieldValue(AssistStructure structure, AutofillId autofillId)
        {
            // Search through the structure to find the field with the given AutofillId and return its value
            for (int i = 0; i < structure.WindowNodeCount; i++)
            {
                var windowNode = structure.GetWindowNodeAt(i);
                var value = GetFieldValueFromNode(windowNode.RootViewNode, autofillId);
                if (value != null)
                    return value;
            }
            return null;
        }

        private string? GetFieldValueFromNode(AssistStructure.ViewNode? node, AutofillId targetId)
        {
            if (node == null)
                return null;

            // Check if this node has the target autofill ID
            if (node.AutofillId?.Equals(targetId) == true)
            {
                // Return the text value
                return node.Text?.ToString() ?? node.AutofillValue?.TextValue?.ToString();
            }

            // Search children
            for (int i = 0; i < node.ChildCount; i++)
            {
                var value = GetFieldValueFromNode(node.GetChildAt(i), targetId);
                if (value != null)
                    return value;
            }

            return null;
        }

        private List<Credential> GetCredentialsForDomain(string domain)
        {
            if (_credentialRepository == null)
                return new List<Credential>();

            try
            {
                // Get credentials from repository (synchronous call - Android framework expects sync)
                var task = _credentialRepository.GetCredentialsByDomainAsync(domain);
                task.Wait(); // Not ideal, but Android AutofillService callbacks are synchronous
                return task.Result;
            }
            catch
            {
                return new List<Credential>();
            }
        }

        /// <summary>
        /// Initializes the credential repository. Should be called during service onCreate.
        /// </summary>
        public void Initialize(ICredentialRepository credentialRepository)
        {
            _credentialRepository = credentialRepository;
        }

        private RemoteViews CreatePresentation(string username, string title)
        {
            var presentation = new RemoteViews(PackageName, Android.Resource.Layout.SimpleListItem2);
            presentation.SetTextViewText(Android.Resource.Id.Text1, title);
            presentation.SetTextViewText(Android.Resource.Id.Text2, username);
            return presentation;
        }

        public bool TryFill(string domain)
        {
            // This is called from the app, not by the Android Autofill Framework
            // For Android, autofill is handled by OnFillRequest
            return false;
        }
    }
#else
    /// <summary>
    /// Placeholder for when Android-specific code is not being compiled.
    /// </summary>
    public class AndroidAutofillService : IAutofillProvider
    {
        public bool IsSupported => false;
        public bool TryFill(string domain) => false;
    }
#endif
}
