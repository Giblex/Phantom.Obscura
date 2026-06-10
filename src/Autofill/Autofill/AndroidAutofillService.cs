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

                var fields = ParseStructure(structure);
                if (fields.Count == 0)
                {
                    callback.OnFailure("No compatible fields found");
                    return;
                }

                var packageName = structure.ActivityComponent?.PackageName ?? "";
                var webDomain = ExtractWebDomain(structure);

                var credentials = GetCredentialsForDomain(webDomain ?? packageName);
                if (credentials.Count == 0)
                {
                    callback.OnFailure("No credentials found for this domain");
                    return;
                }

                var responseBuilder = new FillResponse.Builder();

                foreach (var credential in credentials)
                {
                    var datasetBuilder = new Dataset.Builder();

                    var presentation = CreatePresentation(credential.Username, credential.Title);

                    if (fields.TryGetValue("username", out var usernameId))
                    {
                        datasetBuilder.SetValue(usernameId, AutofillValue.ForText(credential.Username), presentation);
                    }

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

            try
            {
                var context = request.FillContexts?.LastOrDefault();
                if (context == null)
                {
                    callback.OnFailure("No save context available");
                    return;
                }

                var structure = context.Structure;
                var fields = ParseStructure(structure);

                var packageName = structure.ActivityComponent?.PackageName ?? "unknown";
                string domain = ExtractDomainFromStructure(structure) ?? packageName;

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

            for (int i = 0; i < node.ChildCount; i++)
            {
                ParseNode(node.GetChildAt(i), fields);
            }
        }

        private string? ExtractWebDomain(AssistStructure structure)
        {

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

            var webDomain = ExtractWebDomain(structure);
            if (!string.IsNullOrEmpty(webDomain))
                return webDomain;

            return structure.ActivityComponent?.PackageName;
        }

        private string? GetFieldValue(AssistStructure structure, AutofillId autofillId)
        {

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

            if (node.AutofillId?.Equals(targetId) == true)
            {

                return node.Text?.ToString() ?? node.AutofillValue?.TextValue?.ToString();
            }

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

                var task = _credentialRepository.GetCredentialsByDomainAsync(domain);
                task.Wait();
                return task.Result;
            }
            catch
            {
                return new List<Credential>();
            }
        }

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

            return false;
        }
    }
#else

    public class AndroidAutofillService : IAutofillProvider
    {
        public bool IsSupported => false;
        public bool TryFill(string domain) => false;
    }
#endif
}

