using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PhantomVault.Core.Models;

namespace PhantomVault.Android.Platforms.Android;

/// <summary>
/// Android AutofillService implementation for Phantom Obscura.
/// Registered in AndroidManifest.xml as an autofill service, this class is
/// instantiated by the Android system when the user focuses an autofillable field.
///
/// Requires Android API 26+ (Oreo). The service:
/// 1. Receives a fill request describing the current activity's view hierarchy
/// 2. Identifies username/password fields using standard autofill hints
/// 3. Returns fill datasets backed by the in-memory Phantom vault (if unlocked)
///    or an authentication intent that launches the unlock flow
/// </summary>
#if ANDROID
[global::Android.App.Service(
    Permission = "android.permission.BIND_AUTOFILL_SERVICE",
    Label = "Phantom Obscura Autofill",
    Exported = true)]
[global::Android.App.IntentFilter(
    new[] { "android.service.autofill.AutofillService" })]
public sealed class PhantomAutofillService : global::Android.Service.Autofill.AutofillService
{
    private List<Credential> _cachedCredentials = new();

    public override void OnFillRequest(
        global::Android.Service.Autofill.FillRequest request,
        global::Android.OS.CancellationSignal cancellationSignal,
        global::Android.Service.Autofill.FillCallback callback)
    {
        try
        {
            var context = request.FillContexts?.LastOrDefault();
            var structure = context?.Structure;
            if (structure is null)
            {
                callback.OnSuccess(null);
                return;
            }

            var (usernameId, passwordId) = FindLoginFields(structure);
            if (usernameId is null && passwordId is null)
            {
                callback.OnSuccess(null);
                return;
            }

            LoadCredentialsIfNeeded();

            if (_cachedCredentials.Count == 0)
            {
                // Vault is locked: offer unlock intent
                var unlockIntent = new global::Android.Content.Intent(this, typeof(global::PhantomVault.Android.MainActivity));
                unlockIntent.SetFlags(global::Android.Content.ActivityFlags.SingleTop | global::Android.Content.ActivityFlags.ClearTop);
                var pi = global::Android.App.PendingIntent.GetActivity(this, 0, unlockIntent,
                    global::Android.App.PendingIntentFlags.UpdateCurrent | global::Android.App.PendingIntentFlags.Mutable)!;

                var unlockPresentation = new global::Android.Widget.RemoteViews(PackageName, Resource.Layout.autofill_item);
                unlockPresentation.SetTextViewText(Resource.Id.autofill_title, "Tap to unlock Phantom Obscura");

                var menuPresentation = new global::Android.Service.Autofill.Presentations.Builder()
                    .SetMenuPresentation(unlockPresentation)
                    .Build();

                var responseBuilder = new global::Android.Service.Autofill.FillResponse.Builder()
                    .SetAuthentication(
                        usernameId is not null ? new[] { usernameId } : passwordId is not null ? new[] { passwordId } : Array.Empty<global::Android.Views.Autofill.AutofillId>(),
                        pi.IntentSender,
                        menuPresentation
                    );
                callback.OnSuccess(responseBuilder.Build());
                return;
            }

            var fillBuilder = new global::Android.Service.Autofill.FillResponse.Builder();
            var webDomain = GetWebDomain(structure);

            foreach (var cred in _cachedCredentials.Where(c => MatchesDomain(c, webDomain)).Take(5))
            {
                var datasetBuilder = new global::Android.Service.Autofill.Dataset.Builder();
                var presentation = CreatePresentation(cred.Title, cred.Username);

                if (usernameId is not null)
                    datasetBuilder.SetValue(usernameId, global::Android.Views.Autofill.AutofillValue.ForText(cred.Username), presentation);
                if (passwordId is not null)
                    datasetBuilder.SetValue(passwordId, global::Android.Views.Autofill.AutofillValue.ForText(cred.Password), presentation);

                fillBuilder.AddDataset(datasetBuilder.Build());
            }

            callback.OnSuccess(fillBuilder.Build());
        }
        catch
        {
            callback.OnFailure("Phantom Obscura autofill failed");
        }
    }

    public override void OnSaveRequest(
        global::Android.Service.Autofill.SaveRequest request,
        global::Android.Service.Autofill.SaveCallback callback)
    {
        // Capture the entered credentials and queue them for the MAUI app to
        // confirm + add to the encrypted vault on next launch. We cannot write
        // to the vault directly here because the master key lives only in the
        // unlocked MAUI process.
        try
        {
            var context = request.FillContexts?.LastOrDefault();
            var structure = context?.Structure;
            if (structure is null)
            {
                callback.OnSuccess();
                return;
            }

            var (usernameId, passwordId) = FindLoginFields(structure);
            string? username = usernameId is null ? null : ReadFieldValue(structure, usernameId);
            string? password = passwordId is null ? null : ReadFieldValue(structure, passwordId);

            if (string.IsNullOrEmpty(password))
            {
                // Nothing useful to save
                callback.OnSuccess();
                return;
            }

            var domain = GetWebDomain(structure)
                ?? structure.ActivityComponent?.PackageName
                ?? "unknown";

            var pending = new PendingAutofillSave
            {
                Title = domain,
                Username = username ?? string.Empty,
                Password = password,
                Url = domain.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? domain : $"android://{domain}",
                CapturedUtc = DateTimeOffset.UtcNow,
            };

            QueuePendingSave(pending);
            callback.OnSuccess();
        }
        catch
        {
            // Never crash the autofill framework — silently acknowledge.
            try { callback.OnSuccess(); } catch { }
        }
    }

    private static string? ReadFieldValue(
        global::Android.App.Assist.AssistStructure structure,
        global::Android.Views.Autofill.AutofillId targetId)
    {
        for (int i = 0; i < structure.WindowNodeCount; i++)
        {
            var v = ReadFieldValueFromNode(structure.GetWindowNodeAt(i).RootViewNode, targetId);
            if (v is not null) return v;
        }
        return null;
    }

    private static string? ReadFieldValueFromNode(
        global::Android.App.Assist.AssistStructure.ViewNode? node,
        global::Android.Views.Autofill.AutofillId targetId)
    {
        if (node is null) return null;
        if (node.AutofillId?.Equals(targetId) == true)
        {
            var v = node.AutofillValue;
            if (v is not null && v.IsText) return v.TextValue?.ToString();
            return node.Text?.ToString();
        }
        for (int i = 0; i < node.ChildCount; i++)
        {
            var v = ReadFieldValueFromNode(node.GetChildAt(i), targetId);
            if (v is not null) return v;
        }
        return null;
    }

    private void QueuePendingSave(PendingAutofillSave pending)
    {
        // Write to the app's private external dir so the MAUI app can read it.
        // FilesDir on the service shares the app's private storage.
        var queueDir = Path.Combine(FilesDir!.AbsolutePath, "autofill");
        Directory.CreateDirectory(queueDir);
        var queuePath = Path.Combine(queueDir, "pending_saves.json");

        var list = new List<PendingAutofillSave>();
        if (File.Exists(queuePath))
        {
            try
            {
                var existing = File.ReadAllText(queuePath);
                list = JsonSerializer.Deserialize<List<PendingAutofillSave>>(existing) ?? new();
            }
            catch { /* corrupt file: start fresh */ }
        }

        // Dedupe by (Url, Username) — keep the newest password
        list.RemoveAll(p =>
            string.Equals(p.Url, pending.Url, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Username, pending.Username, StringComparison.OrdinalIgnoreCase));
        list.Add(pending);

        // Cap queue size to avoid runaway growth
        if (list.Count > 50)
            list = list.OrderByDescending(p => p.CapturedUtc).Take(50).ToList();

        File.WriteAllText(queuePath, JsonSerializer.Serialize(list));
    }

    private sealed class PendingAutofillSave
    {
        public string Title { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string Url { get; set; } = "";
        public DateTimeOffset CapturedUtc { get; set; }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (global::Android.Views.Autofill.AutofillId? username, global::Android.Views.Autofill.AutofillId? password)
        FindLoginFields(global::Android.App.Assist.AssistStructure structure)
    {
        global::Android.Views.Autofill.AutofillId? username = null;
        global::Android.Views.Autofill.AutofillId? password = null;

        for (int i = 0; i < structure.WindowNodeCount; i++)
            TraverseNode(structure.GetWindowNodeAt(i).RootViewNode, ref username, ref password);

        return (username, password);
    }

    private static void TraverseNode(
        global::Android.App.Assist.AssistStructure.ViewNode node,
        ref global::Android.Views.Autofill.AutofillId? username,
        ref global::Android.Views.Autofill.AutofillId? password)
    {
        var hints = node.GetAutofillHints();
        if (hints is not null)
        {
            if (hints.Contains(global::Android.Views.View.AutofillHintUsername) ||
                hints.Contains(global::Android.Views.View.AutofillHintEmailAddress))
                username ??= node.AutofillId;
            else if (hints.Contains(global::Android.Views.View.AutofillHintPassword))
                password ??= node.AutofillId;
        }

        for (int i = 0; i < node.ChildCount; i++)
            TraverseNode(node.GetChildAt(i), ref username, ref password);
    }

    private static string? GetWebDomain(global::Android.App.Assist.AssistStructure structure)
    {
        for (int i = 0; i < structure.WindowNodeCount; i++)
        {
            var domain = structure.GetWindowNodeAt(i).RootViewNode.WebDomain;
            if (!string.IsNullOrEmpty(domain))
                return domain;
        }
        return null;
    }

    private static bool MatchesDomain(Credential cred, string? domain)
    {
        if (string.IsNullOrEmpty(domain)) return true;
        var url = cred.Url?.ToLowerInvariant() ?? string.Empty;
        return url.Contains(domain.ToLowerInvariant());
    }

    private global::Android.Widget.RemoteViews CreatePresentation(string title, string username)
    {
        var view = new global::Android.Widget.RemoteViews(PackageName, Resource.Layout.autofill_item);
        view.SetTextViewText(Resource.Id.autofill_title, title);
        view.SetTextViewText(Resource.Id.autofill_subtitle, username);
        return view;
    }

    private void LoadCredentialsIfNeeded()
    {
        // Load from the app's local credentials cache file (written by the MAUI app when vault is open)
        var cachePath = Path.Combine(
            global::Android.OS.Environment.GetExternalStoragePublicDirectory("")!.AbsolutePath,
            ".phantom_autofill_cache");
        if (!File.Exists(cachePath)) return;
        try
        {
            var json = File.ReadAllText(cachePath);
            _cachedCredentials = JsonSerializer.Deserialize<List<Credential>>(json) ?? new List<Credential>();
        }
        catch { _cachedCredentials = new List<Credential>(); }
    }
}
#endif
