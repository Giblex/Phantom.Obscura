# PhantomVault Phase 2 Development Roadmap
## Mobile App Resurrection & Cross-Platform Enhancement

**Timeline**: 4-6 months
**Status**: Planning
**Dependencies**: Phase 1 completion recommended (browser extension, advanced autofill)

---

## Overview

This roadmap outlines the resurrection and modernization of the PhantomVault mobile applications for Android and iOS. The mobile apps were previously developed but have been archived and require significant updates to integrate with the current vault architecture, security features, and cross-platform ecosystem.

### Goals

1. **Resurrect MAUI Project** - Restore and modernize the cross-platform mobile app
2. **Android Platform Implementation** - Full Android support with native autofill
3. **iOS Platform Implementation** - Full iOS support with AutoFill Credential Provider
4. **Biometric Authentication** - Face ID, Touch ID, fingerprint, face unlock
5. **USB Vault Portability** - Access vault from USB drive on any device (Windows, macOS, Linux, Android via USB OTG, iOS via USB-C/Lightning)
6. **QR Code Vault Unlock** - Desktop-to-mobile vault unlock via QR codes
7. **Cross-Platform USB Detection** - Automatic vault detection when USB drive is inserted

---

## Priority 1: MAUI Project Resurrection (Month 1-2)

### Current State Analysis

The mobile app exists in the archive but needs:
- Migration to .NET 8 MAUI
- Integration with updated Core library (ML-KEM-768, Argon2id, 5-layer defense)
- UI/UX refresh to match desktop Obscura theme
- USB vault detection and mounting system
- Testing on modern Android 14+ and iOS 17+

### Architecture

```
PhantomVault.Mobile/
├── PhantomVault.Mobile.csproj (MAUI project)
├── Platforms/
│   ├── Android/
│   │   ├── MainActivity.cs
│   │   ├── AutofillService.cs (existing, needs update)
│   │   ├── BiometricAuthService.cs (new)
│   │   └── UsbVaultDetectionService.Android.cs (new)
│   └── iOS/
│       ├── AppDelegate.cs
│       ├── CredentialProviderViewController.cs (new)
│       ├── BiometricAuthService.cs (new)
│       ├── UsbVaultDetectionService.iOS.cs (new)
│       └── Resources/
├── Views/
│   ├── LoginPage.xaml
│   ├── VaultPage.xaml
│   ├── CredentialDetailPage.xaml
│   ├── SettingsPage.xaml
│   └── QrScanPage.xaml (new)
├── ViewModels/
│   ├── LoginViewModel.cs
│   ├── VaultViewModel.cs
│   ├── CredentialDetailViewModel.cs
│   └── QrScanViewModel.cs (new)
└── Services/
    ├── BiometricAuthService.cs (interface)
    ├── UsbVaultDetectionService.cs (cross-platform)
    └── QrVaultUnlockService.cs (new)
```

### Task Breakdown

**Week 1-2: Project Setup & Core Integration**
- [ ] Create new .NET 8 MAUI project structure
- [ ] Reference PhantomVault.Core library
- [ ] Update NuGet packages (Avalonia → MAUI, ReactiveUI → CommunityToolkit.Mvvm)
- [ ] Migrate existing ViewModels to CommunityToolkit.Mvvm
- [ ] Test vault encryption/decryption on mobile

**Week 3-4: UI/UX Redesign**
- [ ] Create ObscuraTheme.xaml for MAUI (match desktop colors)
- [ ] Redesign LoginPage with biometric button
- [ ] Redesign VaultPage with search, categories, favorites
- [ ] Redesign CredentialDetailPage with TOTP, copy buttons
- [ ] Create SettingsPage with sync, backup, security options

**Week 5-6: Core Features**
- [ ] Implement vault creation workflow
- [ ] Implement credential CRUD operations
- [ ] Implement search and filtering
- [ ] Implement password generator integration
- [ ] Add TOTP support with QR scanner

**Week 7-8: Testing & Polish**
- [ ] Test on Android emulator (API 34)
- [ ] Test on iOS simulator (iOS 17)
- [ ] Test on physical devices (Pixel 8, iPhone 15)
- [ ] Performance profiling (startup time, search latency)
- [ ] Memory leak detection

### Code Example: MAUI Theme Integration

**ObscuraTheme.xaml**
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">
    <!-- Color Palette -->
    <Color x:Key="ObscuraCanvasColor">#0A0A0F</Color>
    <Color x:Key="ObscuraSurfaceColor">#12121A</Color>
    <Color x:Key="ObscuraSurfaceAltColor">#1A1A26</Color>
    <Color x:Key="ObscuraAccentColor">#7C3AED</Color>
    <Color x:Key="ObscuraAccentHoverColor">#8B5CF6</Color>
    <Color x:Key="ObscuraTextPrimaryColor">#E5E7EB</Color>
    <Color x:Key="ObscuraTextSecondaryColor">#9CA3AF</Color>
    <Color x:Key="ObscuraBorderColor">#2A2A3A</Color>

    <!-- Brushes -->
    <SolidColorBrush x:Key="ObscuraCanvasBrush" Color="{StaticResource ObscuraCanvasColor}"/>
    <SolidColorBrush x:Key="ObscuraSurfaceBrush" Color="{StaticResource ObscuraSurfaceColor}"/>
    <SolidColorBrush x:Key="ObscuraAccentBrush" Color="{StaticResource ObscuraAccentColor}"/>
    <SolidColorBrush x:Key="ObscuraTextPrimaryBrush" Color="{StaticResource ObscuraTextPrimaryColor}"/>

    <!-- Button Styles -->
    <Style TargetType="Button" x:Key="PrimaryButton">
        <Setter Property="BackgroundColor" Value="{StaticResource ObscuraAccentColor}"/>
        <Setter Property="TextColor" Value="White"/>
        <Setter Property="CornerRadius" Value="12"/>
        <Setter Property="Padding" Value="20,12"/>
        <Setter Property="FontSize" Value="15"/>
        <Setter Property="FontFamily" Value="InterMedium"/>
        <Setter Property="VisualStateManager.VisualStateGroups">
            <VisualStateGroupList>
                <VisualStateGroup x:Name="CommonStates">
                    <VisualState x:Name="Normal"/>
                    <VisualState x:Name="Pressed">
                        <VisualState.Setters>
                            <Setter Property="BackgroundColor" Value="{StaticResource ObscuraAccentHoverColor}"/>
                            <Setter Property="Scale" Value="0.98"/>
                        </VisualState.Setters>
                    </VisualState>
                </VisualStateGroup>
            </VisualStateGroupList>
        </Setter>
    </Style>

    <!-- Entry Styles -->
    <Style TargetType="Entry" x:Key="ObscuraEntry">
        <Setter Property="BackgroundColor" Value="{StaticResource ObscuraSurfaceColor}"/>
        <Setter Property="TextColor" Value="{StaticResource ObscuraTextPrimaryColor}"/>
        <Setter Property="PlaceholderColor" Value="{StaticResource ObscuraTextSecondaryColor}"/>
        <Setter Property="FontSize" Value="15"/>
        <Setter Property="HeightRequest" Value="48"/>
        <Setter Property="Margin" Value="0,8"/>
    </Style>
</ResourceDictionary>
```

**LoginPage.xaml**
```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:PhantomVault.Mobile.ViewModels"
             x:Class="PhantomVault.Mobile.Views.LoginPage"
             x:DataType="vm:LoginViewModel"
             BackgroundColor="{StaticResource ObscuraCanvasColor}">
    <Grid RowDefinitions="*,Auto,*" Padding="32">
        <!-- Logo Section -->
        <VerticalStackLayout Grid.Row="0" VerticalOptions="End" Spacing="16">
            <Image Source="phantom_logo.png"
                   HeightRequest="80"
                   WidthRequest="80"
                   HorizontalOptions="Center"/>
            <Label Text="PhantomVault"
                   FontSize="28"
                   FontFamily="InterBold"
                   TextColor="{StaticResource ObscuraTextPrimaryColor}"
                   HorizontalOptions="Center"/>
            <Label Text="Unlock your vault"
                   FontSize="14"
                   TextColor="{StaticResource ObscuraTextSecondaryColor}"
                   HorizontalOptions="Center"/>
        </VerticalStackLayout>

        <!-- Login Form -->
        <VerticalStackLayout Grid.Row="1" Spacing="16">
            <Border BackgroundColor="{StaticResource ObscuraSurfaceColor}"
                    StrokeThickness="1"
                    Stroke="{StaticResource ObscuraBorderColor}"
                    Padding="20"
                    StrokeShape="RoundRectangle 16">
                <VerticalStackLayout Spacing="16">
                    <Entry Placeholder="Master Password"
                           Text="{Binding MasterPassword}"
                           IsPassword="True"
                           Style="{StaticResource ObscuraEntry}"
                           ReturnCommand="{Binding UnlockCommand}"/>

                    <!-- Biometric Button -->
                    <Button Text="&#xf06e; Use Biometrics"
                            Command="{Binding BiometricUnlockCommand}"
                            IsVisible="{Binding IsBiometricAvailable}"
                            Style="{StaticResource SecondaryButton}"
                            FontFamily="FontAwesome"/>

                    <!-- Unlock Button -->
                    <Button Text="Unlock Vault"
                            Command="{Binding UnlockCommand}"
                            Style="{StaticResource PrimaryButton}"
                            IsEnabled="{Binding CanUnlock}"/>

                    <!-- QR Code Unlock -->
                    <Button Text="&#xf029; Unlock with QR Code"
                            Command="{Binding QrUnlockCommand}"
                            Style="{StaticResource SecondaryButton}"
                            FontFamily="FontAwesome"/>
                </VerticalStackLayout>
            </Border>

            <!-- Status Message -->
            <Label Text="{Binding StatusMessage}"
                   TextColor="{Binding StatusColor}"
                   FontSize="13"
                   HorizontalOptions="Center"
                   IsVisible="{Binding HasStatusMessage}"/>
        </VerticalStackLayout>

        <!-- Footer Links -->
        <VerticalStackLayout Grid.Row="2" VerticalOptions="Start" Spacing="12" Margin="0,24,0,0">
            <Label Text="Create New Vault"
                   TextColor="{StaticResource ObscuraAccentColor}"
                   FontSize="14"
                   HorizontalOptions="Center">
                <Label.GestureRecognizers>
                    <TapGestureRecognizer Command="{Binding CreateVaultCommand}"/>
                </Label.GestureRecognizers>
            </Label>
            <Label Text="Import from Backup"
                   TextColor="{StaticResource ObscuraTextSecondaryColor}"
                   FontSize="13"
                   HorizontalOptions="Center">
                <Label.GestureRecognizers>
                    <TapGestureRecognizer Command="{Binding ImportBackupCommand}"/>
                </Label.GestureRecognizers>
            </Label>
        </VerticalStackLayout>
    </Grid>
</ContentPage>
```

---

## Priority 2: Android Platform Implementation (Month 2-3)

### Android Autofill Service Enhancement

The existing `AndroidAutofillService.cs` needs updates:
- Support for Android 14+ autofill API changes
- Passkey/WebAuthn integration (future-proofing)
- Better field detection heuristics
- Inline autofill suggestions (Android 11+)

**Enhanced AndroidAutofillService.cs** (src/Autofill/Autofill/AndroidAutofillService.cs)

```csharp
using Android.Service.Autofill;
using Android.Views.Autofill;
using Android.App;
using Android.Content;
using Android.OS;
using PhantomVault.Core.Models;
using PhantomVault.Core.Repositories;
using System.Collections.Generic;
using System.Linq;

[Service(Permission = "android.permission.BIND_AUTOFILL_SERVICE", Exported = true)]
[IntentFilter(new[] { "android.service.autofill.AutofillService" })]
[MetaData("android.autofill", Resource = "@xml/autofill_service")]
public class PhantomAutofillService : AutofillService
{
    private ICredentialRepository? _credentialRepository;

    public override void OnFillRequest(FillRequest request, CancellationSignal cancellationSignal, FillCallback callback)
    {
        var context = request.FillContexts?.LastOrDefault();
        if (context == null)
        {
            callback.OnFailure("No fill context");
            return;
        }

        var structure = context.Structure;
        var packageName = structure.ActivityComponent?.PackageName ?? "";
        var webDomain = ExtractWebDomain(structure) ?? packageName;

        // Get credentials matching this domain/app
        var credentials = GetMatchingCredentials(webDomain, packageName);

        if (credentials.Count == 0)
        {
            callback.OnFailure("No credentials found");
            return;
        }

        // Build autofill response
        var response = BuildFillResponse(context, credentials, webDomain);
        callback.OnSuccess(response);
    }

    private FillResponse BuildFillResponse(FillContext context, List<Credential> credentials, string domain)
    {
        var responseBuilder = new FillResponse.Builder();
        var structure = context.Structure;

        // Parse fields
        var fields = ParseStructure(structure);
        var usernameId = fields.FirstOrDefault(f => f.Key.Contains("username") || f.Key.Contains("email")).Value;
        var passwordId = fields.FirstOrDefault(f => f.Key.Contains("password")).Value;

        if (usernameId == null || passwordId == null)
        {
            return responseBuilder.Build();
        }

        // Create dataset for each credential
        foreach (var credential in credentials.Take(5)) // Limit to 5 suggestions
        {
            var datasetBuilder = new Dataset.Builder();

            // Set presentation (what user sees in dropdown)
            var presentation = CreatePresentation(credential);

            // Username field
            datasetBuilder.SetValue(usernameId,
                AutofillValue.ForText(credential.Username),
                presentation);

            // Password field
            datasetBuilder.SetValue(passwordId,
                AutofillValue.ForText(credential.Password),
                presentation);

            // Authentication intent (require unlock before autofill)
            var authIntent = CreateUnlockIntent(credential);
            datasetBuilder.SetAuthentication(authIntent.IntentSender);

            responseBuilder.AddDataset(datasetBuilder.Build());
        }

        // Add "Save to PhantomVault" option
        responseBuilder.SetSaveInfo(new SaveInfo.Builder(
            SaveDataType.Password,
            new[] { usernameId, passwordId }
        ).Build());

        return responseBuilder.Build();
    }

    private RemoteViews CreatePresentation(Credential credential)
    {
        var presentation = new RemoteViews(PackageName, Resource.Layout.autofill_item);
        presentation.SetTextViewText(Resource.Id.autofill_title, credential.Title);
        presentation.SetTextViewText(Resource.Id.autofill_username, credential.Username);

        // Set icon based on credential type
        int iconResource = credential.Group switch
        {
            "Banking" => Resource.Drawable.ic_bank,
            "Social Media" => Resource.Drawable.ic_social,
            "Email" => Resource.Drawable.ic_email,
            _ => Resource.Drawable.ic_credential
        };
        presentation.SetImageViewResource(Resource.Id.autofill_icon, iconResource);

        return presentation;
    }

    private PendingIntent CreateUnlockIntent(Credential credential)
    {
        var intent = new Intent(this, typeof(AutofillUnlockActivity));
        intent.PutExtra("credential_id", credential.Id.ToString());
        intent.PutExtra("credential_username", credential.Username);
        intent.PutExtra("credential_password", credential.Password);

        return PendingIntent.GetActivity(
            this,
            credential.Id.GetHashCode(),
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
        );
    }

    private List<Credential> GetMatchingCredentials(string webDomain, string packageName)
    {
        if (_credentialRepository == null)
            return new List<Credential>();

        var allCredentials = _credentialRepository.GetAllCredentialsAsync().Result;

        // Match by URL domain or Android package name
        return allCredentials
            .Where(c =>
                c.Url?.Contains(webDomain, StringComparison.OrdinalIgnoreCase) == true ||
                c.Url?.Contains(packageName, StringComparison.OrdinalIgnoreCase) == true ||
                c.Title?.Contains(webDomain, StringComparison.OrdinalIgnoreCase) == true
            )
            .OrderByDescending(c => c.LastUsedUtc)
            .ToList();
    }

    public override void OnSaveRequest(SaveRequest request, SaveCallback callback)
    {
        // ... existing implementation from previous conversation ...
        // (already fully implemented in conversation history)
    }
}
```

**autofill_item.xml** (new layout for autofill suggestions)
```xml
<?xml version="1.0" encoding="utf-8"?>
<LinearLayout xmlns:android="http://schemas.android.com/apk/res/android"
    android:layout_width="match_parent"
    android:layout_height="wrap_content"
    android:orientation="horizontal"
    android:padding="12dp"
    android:background="#12121A">

    <ImageView
        android:id="@+id/autofill_icon"
        android:layout_width="32dp"
        android:layout_height="32dp"
        android:src="@drawable/ic_credential"
        android:tint="#7C3AED"/>

    <LinearLayout
        android:layout_width="0dp"
        android:layout_height="wrap_content"
        android:layout_weight="1"
        android:orientation="vertical"
        android:layout_marginStart="12dp">

        <TextView
            android:id="@+id/autofill_title"
            android:layout_width="wrap_content"
            android:layout_height="wrap_content"
            android:textSize="15sp"
            android:textColor="#E5E7EB"
            android:text="Example.com"/>

        <TextView
            android:id="@+id/autofill_username"
            android:layout_width="wrap_content"
            android:layout_height="wrap_content"
            android:textSize="13sp"
            android:textColor="#9CA3AF"
            android:text="user@example.com"/>
    </LinearLayout>
</LinearLayout>
```

### Android Biometric Authentication

**BiometricAuthService.cs** (Platforms/Android/)

```csharp
using Android.Hardware.Biometrics;
using Android.Security.Keystore;
using AndroidX.Biometric;
using Java.Security;
using Javax.Crypto;

namespace PhantomVault.Mobile.Platforms.Android
{
    public class BiometricAuthService : IBiometricAuthService
    {
        private readonly MainActivity _activity;
        private const string KEY_NAME = "PhantomVaultBiometricKey";

        public BiometricAuthService(MainActivity activity)
        {
            _activity = activity;
        }

        public bool IsBiometricAvailable()
        {
            var biometricManager = BiometricManager.From(_activity);
            var canAuthenticate = biometricManager.CanAuthenticate(BiometricManager.Authenticators.BiometricStrong);

            return canAuthenticate == BiometricManager.BiometricSuccess;
        }

        public async Task<BiometricAuthResult> AuthenticateAsync(string reason)
        {
            var tcs = new TaskCompletionSource<BiometricAuthResult>();

            var cipher = GetCipher();
            var cryptoObject = new BiometricPrompt.CryptoObject(cipher);

            var promptInfo = new BiometricPrompt.PromptInfo.Builder()
                .SetTitle("PhantomVault Authentication")
                .SetSubtitle(reason)
                .SetNegativeButtonText("Cancel")
                .SetAllowedAuthenticators(BiometricManager.Authenticators.BiometricStrong)
                .Build();

            var authCallback = new BiometricAuthCallback(
                onSuccess: (result) =>
                {
                    tcs.SetResult(new BiometricAuthResult
                    {
                        Success = true,
                        CryptoObject = result.CryptoObject
                    });
                },
                onError: (errorCode, errString) =>
                {
                    tcs.SetResult(new BiometricAuthResult
                    {
                        Success = false,
                        ErrorMessage = errString
                    });
                }
            );

            var biometricPrompt = new BiometricPrompt(_activity, authCallback);

            _activity.RunOnUiThread(() =>
            {
                biometricPrompt.Authenticate(promptInfo, cryptoObject);
            });

            return await tcs.Task;
        }

        private Cipher GetCipher()
        {
            var keyStore = KeyStore.GetInstance("AndroidKeyStore");
            keyStore.Load(null);

            if (!keyStore.ContainsAlias(KEY_NAME))
            {
                GenerateKey();
            }

            var key = keyStore.GetKey(KEY_NAME, null);
            var cipher = Cipher.GetInstance("AES/GCM/NoPadding");
            cipher.Init(CipherMode.EncryptMode, key);

            return cipher;
        }

        private void GenerateKey()
        {
            var keyGenerator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore");
            var builder = new KeyGenParameterSpec.Builder(KEY_NAME, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes(KeyProperties.BlockModeGcm)
                .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
                .SetUserAuthenticationRequired(true)
                .SetInvalidatedByBiometricEnrollment(true);

            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                builder.SetUserAuthenticationParameters(0, KeyProperties.AuthBiometricStrong);
            }

            keyGenerator.Init(builder.Build());
            keyGenerator.GenerateKey();
        }
    }

    internal class BiometricAuthCallback : BiometricPrompt.AuthenticationCallback
    {
        private readonly Action<BiometricPrompt.AuthenticationResult> _onSuccess;
        private readonly Action<int, string> _onError;

        public BiometricAuthCallback(
            Action<BiometricPrompt.AuthenticationResult> onSuccess,
            Action<int, string> onError)
        {
            _onSuccess = onSuccess;
            _onError = onError;
        }

        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result)
        {
            base.OnAuthenticationSucceeded(result);
            _onSuccess(result);
        }

        public override void OnAuthenticationError(int errorCode, ICharSequence errString)
        {
            base.OnAuthenticationError(errorCode, errString);
            _onError(errorCode, errString?.ToString() ?? "Unknown error");
        }

        public override void OnAuthenticationFailed()
        {
            base.OnAuthenticationFailed();
            _onError(-1, "Authentication failed");
        }
    }
}
```

---

## Priority 3: iOS Platform Implementation (Month 3-4)

### iOS AutoFill Credential Provider Extension

iOS uses a different architecture than Android - requires a separate app extension.

**Project Structure**:
```
PhantomVault.Mobile.iOS/
└── Extensions/
    └── CredentialProvider/
        ├── Info.plist
        ├── CredentialProviderViewController.cs
        └── Entitlements.plist
```

**CredentialProviderViewController.cs**

```csharp
using AuthenticationServices;
using Foundation;
using PhantomVault.Core.Repositories;
using PhantomVault.Core.Models;
using UIKit;

namespace PhantomVault.Mobile.iOS.Extensions.CredentialProvider
{
    [Register("CredentialProviderViewController")]
    public class CredentialProviderViewController : ASCredentialProviderViewController
    {
        private ICredentialRepository? _repository;

        public override void PrepareCredentialList(ASCredentialServiceIdentifier[] serviceIdentifiers)
        {
            // Called when user taps password field - show list of credentials
            var domain = serviceIdentifiers.FirstOrDefault()?.Identifier;

            if (string.IsNullOrEmpty(domain))
            {
                CancelRequest(ASExtensionError.FromCode(ASExtensionErrorCode.UserCanceled));
                return;
            }

            var credentials = GetMatchingCredentials(domain);

            if (credentials.Count == 0)
            {
                CancelRequest(ASExtensionError.FromCode(ASExtensionErrorCode.CredentialIdentityNotFound));
                return;
            }

            // Show UI to select credential
            ShowCredentialPicker(credentials, domain);
        }

        public override void ProvideCredentialWithoutUserInteraction(ASPasswordCredentialIdentity credentialIdentity)
        {
            // Called when user selects credential from QuickType bar
            var credentialId = credentialIdentity.RecordIdentifier;

            if (string.IsNullOrEmpty(credentialId))
            {
                ExtensionContext?.CancelRequest(ASExtensionError.FromCode(ASExtensionErrorCode.CredentialIdentityNotFound));
                return;
            }

            // Check if vault is unlocked
            if (!IsVaultUnlocked())
            {
                // Require user interaction to unlock
                ExtensionContext?.CancelRequest(ASExtensionError.FromCode(ASExtensionErrorCode.UserInteractionRequired));
                return;
            }

            var credential = GetCredentialById(credentialId);

            if (credential == null)
            {
                ExtensionContext?.CancelRequest(ASExtensionError.FromCode(ASExtensionErrorCode.CredentialIdentityNotFound));
                return;
            }

            var passwordCredential = new ASPasswordCredential(credential.Username, credential.Password);
            ExtensionContext?.CompleteRequest(passwordCredential, null);
        }

        private void ShowCredentialPicker(List<Credential> credentials, string domain)
        {
            var alertController = UIAlertController.Create(
                "Select Account",
                $"Choose an account for {domain}",
                UIAlertControllerStyle.ActionSheet
            );

            foreach (var credential in credentials.Take(10))
            {
                var action = UIAlertAction.Create(
                    $"{credential.Title}\n{credential.Username}",
                    UIAlertActionStyle.Default,
                    (obj) =>
                    {
                        // Require biometric/passcode authentication
                        AuthenticateAndProvideCredential(credential);
                    }
                );
                alertController.AddAction(action);
            }

            var cancelAction = UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, (obj) =>
            {
                CancelRequest(ASExtensionError.FromCode(ASExtensionErrorCode.UserCanceled));
            });
            alertController.AddAction(cancelAction);

            PresentViewController(alertController, true, null);
        }

        private void AuthenticateAndProvideCredential(Credential credential)
        {
            var context = new LocalAuthentication.LAContext();
            var error = new NSError();

            if (!context.CanEvaluatePolicy(LocalAuthentication.LAPolicy.DeviceOwnerAuthenticationWithBiometrics, out error))
            {
                // Fallback to passcode
                AuthenticateWithPasscode(credential);
                return;
            }

            context.EvaluatePolicy(
                LocalAuthentication.LAPolicy.DeviceOwnerAuthenticationWithBiometrics,
                "Authenticate to autofill password",
                (success, authError) =>
                {
                    InvokeOnMainThread(() =>
                    {
                        if (success)
                        {
                            var passwordCredential = new ASPasswordCredential(credential.Username, credential.Password);
                            ExtensionContext?.CompleteRequest(passwordCredential, null);
                        }
                        else
                        {
                            CancelRequest(ASExtensionError.FromCode(ASExtensionErrorCode.UserCanceled));
                        }
                    });
                }
            );
        }

        private List<Credential> GetMatchingCredentials(string domain)
        {
            if (_repository == null)
                return new List<Credential>();

            var allCredentials = _repository.GetAllCredentialsAsync().Result;

            // Extract base domain (remove www, protocol, etc.)
            var baseDomain = ExtractBaseDomain(domain);

            return allCredentials
                .Where(c => c.Url?.Contains(baseDomain, StringComparison.OrdinalIgnoreCase) == true)
                .OrderByDescending(c => c.LastUsedUtc)
                .ToList();
        }

        private string ExtractBaseDomain(string url)
        {
            try
            {
                var uri = new Uri(url.StartsWith("http") ? url : $"https://{url}");
                var host = uri.Host;

                // Remove www.
                if (host.StartsWith("www."))
                    host = host.Substring(4);

                return host;
            }
            catch
            {
                return url;
            }
        }

        private bool IsVaultUnlocked()
        {
            // Check shared keychain for unlock token
            var query = new SecRecord(SecKind.GenericPassword)
            {
                Service = "com.phantomvault.unlock",
                Account = "vault_unlock_token"
            };

            var result = SecKeyChain.QueryAsData(query);
            return result != null;
        }

        private Credential? GetCredentialById(string id)
        {
            if (_repository == null)
                return null;

            return _repository.GetCredentialByIdAsync(Guid.Parse(id)).Result;
        }

        private void CancelRequest(NSError error)
        {
            ExtensionContext?.CancelRequest(error);
        }
    }
}
```

**Info.plist** (for extension)
```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>NSExtension</key>
    <dict>
        <key>NSExtensionAttributes</key>
        <dict>
            <key>ASCredentialProviderExtensionCapabilities</key>
            <dict>
                <key>ProvidesPasswords</key>
                <true/>
                <key>ProvidesTextToInsert</key>
                <false/>
            </dict>
        </dict>
        <key>NSExtensionPointIdentifier</key>
        <string>com.apple.authentication-services-credential-provider-ui</string>
        <key>NSExtensionPrincipalClass</key>
        <string>CredentialProviderViewController</string>
    </dict>
</dict>
</plist>
```

### iOS Biometric Authentication (Face ID / Touch ID)

**BiometricAuthService.cs** (Platforms/iOS/)

```csharp
using LocalAuthentication;
using Foundation;
using Security;

namespace PhantomVault.Mobile.Platforms.iOS
{
    public class BiometricAuthService : IBiometricAuthService
    {
        public bool IsBiometricAvailable()
        {
            var context = new LAContext();
            NSError error;

            return context.CanEvaluatePolicy(
                LAPolicy.DeviceOwnerAuthenticationWithBiometrics,
                out error
            );
        }

        public string GetBiometricType()
        {
            var context = new LAContext();

            return context.BiometryType switch
            {
                LABiometryType.FaceId => "Face ID",
                LABiometryType.TouchId => "Touch ID",
                LABiometryType.OpticId => "Optic ID",
                _ => "Biometric"
            };
        }

        public async Task<BiometricAuthResult> AuthenticateAsync(string reason)
        {
            var context = new LAContext();
            var tcs = new TaskCompletionSource<BiometricAuthResult>();

            NSError error;
            if (!context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthenticationWithBiometrics, out error))
            {
                return new BiometricAuthResult
                {
                    Success = false,
                    ErrorMessage = error?.LocalizedDescription ?? "Biometric authentication not available"
                };
            }

            context.EvaluatePolicy(
                LAPolicy.DeviceOwnerAuthenticationWithBiometrics,
                reason,
                (success, authError) =>
                {
                    tcs.SetResult(new BiometricAuthResult
                    {
                        Success = success,
                        ErrorMessage = authError?.LocalizedDescription
                    });
                }
            );

            return await tcs.Task;
        }

        public async Task<bool> EnableBiometricUnlockAsync(byte[] masterKey)
        {
            // Store master key in Keychain with biometric protection
            var record = new SecRecord(SecKind.GenericPassword)
            {
                Service = "com.phantomvault.masterkey",
                Account = "vault_master_key",
                ValueData = NSData.FromArray(masterKey),
                Accessible = SecAccessible.WhenUnlockedThisDeviceOnly,
                UseOperationPrompt = "Authenticate to enable biometric unlock"
            };

            // Require biometric authentication to access
            if (OperatingSystem.IsIOSVersionAtLeast(11, 3))
            {
                var accessControl = SecAccessControl.Create(
                    SecAccessible.WhenUnlockedThisDeviceOnly,
                    SecAccessControlCreateFlags.BiometryCurrentSet
                );
                record.AccessControl = accessControl;
            }

            var result = SecKeyChain.Add(record);
            return result == SecStatusCode.Success;
        }

        public async Task<byte[]?> GetMasterKeyWithBiometricAsync()
        {
            var query = new SecRecord(SecKind.GenericPassword)
            {
                Service = "com.phantomvault.masterkey",
                Account = "vault_master_key",
                UseOperationPrompt = "Authenticate to unlock vault"
            };

            var result = SecKeyChain.QueryAsData(query);
            return result?.ToArray();
        }
    }
}
```

---

## Priority 4: QR Code Vault Unlock (Month 4)

### Desktop QR Code Generator

**QrVaultUnlockService.cs** (src/Core/Services/)

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QRCoder; // NuGet: QRCoder 1.6.0

namespace PhantomVault.Core.Services
{
    public class QrVaultUnlockService
    {
        private readonly VaultService _vaultService;
        private readonly byte[] _sessionKey;

        public QrVaultUnlockService(VaultService vaultService)
        {
            _vaultService = vaultService;
            _sessionKey = RandomNumberGenerator.GetBytes(32);
        }

        /// <summary>
        /// Generates a QR code containing encrypted vault unlock data
        /// Valid for 60 seconds
        /// </summary>
        public QrUnlockData GenerateUnlockQrCode()
        {
            var payload = new QrUnlockPayload
            {
                SessionId = Guid.NewGuid().ToString(),
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(60),
                EncryptedMasterKey = EncryptMasterKey(),
                VaultId = _vaultService.GetCurrentVaultId(),
                Version = 1
            };

            var json = JsonSerializer.Serialize(payload);
            var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(json, QRCodeGenerator.ECCLevel.H);
            var qrCode = new PngByteQRCode(qrCodeData);
            var qrCodeImage = qrCode.GetGraphic(20);

            return new QrUnlockData
            {
                SessionId = payload.SessionId,
                QrCodeImage = qrCodeImage,
                ExpiresAt = payload.ExpiresAt
            };
        }

        /// <summary>
        /// Validates and decrypts a QR unlock payload scanned from mobile
        /// </summary>
        public QrUnlockResult ValidateUnlockPayload(string qrCodeJson)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<QrUnlockPayload>(qrCodeJson);

                if (payload == null)
                    return QrUnlockResult.Failed("Invalid QR code data");

                if (payload.ExpiresAt < DateTimeOffset.UtcNow)
                    return QrUnlockResult.Failed("QR code expired");

                if (payload.Version != 1)
                    return QrUnlockResult.Failed("Unsupported QR code version");

                var masterKey = DecryptMasterKey(payload.EncryptedMasterKey);

                return QrUnlockResult.Success(masterKey, payload.VaultId);
            }
            catch (Exception ex)
            {
                return QrUnlockResult.Failed($"QR validation error: {ex.Message}");
            }
        }

        private byte[] EncryptMasterKey()
        {
            var masterKey = _vaultService.GetMasterKeyBytes();

            using var aes = new AesGcm(_sessionKey);
            var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
            var ciphertext = new byte[masterKey.Length];
            var tag = new byte[AesGcm.TagByteSizes.MaxSize];

            aes.Encrypt(nonce, masterKey, ciphertext, tag);

            // Combine nonce + tag + ciphertext
            var result = new byte[nonce.Length + tag.Length + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
            Buffer.BlockCopy(ciphertext, 0, result, nonce.Length + tag.Length, ciphertext.Length);

            return result;
        }

        private byte[] DecryptMasterKey(byte[] encryptedData)
        {
            var nonceSize = AesGcm.NonceByteSizes.MaxSize;
            var tagSize = AesGcm.TagByteSizes.MaxSize;

            var nonce = encryptedData[..nonceSize];
            var tag = encryptedData[nonceSize..(nonceSize + tagSize)];
            var ciphertext = encryptedData[(nonceSize + tagSize)..];

            using var aes = new AesGcm(_sessionKey);
            var plaintext = new byte[ciphertext.Length];

            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return plaintext;
        }
    }

    public class QrUnlockPayload
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
        public byte[] EncryptedMasterKey { get; set; } = Array.Empty<byte>();
        public string VaultId { get; set; } = string.Empty;
        public int Version { get; set; }
    }

    public class QrUnlockData
    {
        public string SessionId { get; set; } = string.Empty;
        public byte[] QrCodeImage { get; set; } = Array.Empty<byte>();
        public DateTimeOffset ExpiresAt { get; set; }
    }

    public class QrUnlockResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public byte[]? MasterKey { get; set; }
        public string? VaultId { get; set; }

        public static QrUnlockResult Success(byte[] masterKey, string vaultId) =>
            new() { Success = true, MasterKey = masterKey, VaultId = vaultId };

        public static QrUnlockResult Failed(string error) =>
            new() { Success = false, ErrorMessage = error };
    }
}
```

### Mobile QR Scanner

**QrScanPage.xaml**
```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:zxing="clr-namespace:ZXing.Net.Maui.Controls;assembly=ZXing.Net.MAUI"
             x:Class="PhantomVault.Mobile.Views.QrScanPage"
             BackgroundColor="{StaticResource ObscuraCanvasColor}">

    <Grid RowDefinitions="Auto,*,Auto">
        <!-- Header -->
        <Border Grid.Row="0"
                BackgroundColor="{StaticResource ObscuraSurfaceColor}"
                Padding="20,16">
            <Grid ColumnDefinitions="Auto,*,Auto">
                <Button Grid.Column="0"
                        Text="Cancel"
                        Command="{Binding CancelCommand}"
                        Style="{StaticResource SecondaryButton}"/>
                <Label Grid.Column="1"
                       Text="Scan QR Code"
                       FontSize="18"
                       FontFamily="InterSemiBold"
                       HorizontalOptions="Center"
                       VerticalOptions="Center"
                       TextColor="{StaticResource ObscuraTextPrimaryColor}"/>
            </Grid>
        </Border>

        <!-- Camera Preview -->
        <zxing:CameraBarcodeReaderView Grid.Row="1"
                                        x:Name="CameraView"
                                        IsDetecting="{Binding IsScanning}"
                                        BarcodesDetected="OnBarcodesDetected"
                                        CameraLocation="Rear"
                                        VibrationOnDetected="True"/>

        <!-- Overlay Guide -->
        <Grid Grid.Row="1" InputTransparent="True">
            <Frame WidthRequest="280"
                   HeightRequest="280"
                   BorderColor="{StaticResource ObscuraAccentColor}"
                   BackgroundColor="Transparent"
                   CornerRadius="16"
                   HasShadow="False"
                   HorizontalOptions="Center"
                   VerticalOptions="Center"/>
        </Grid>

        <!-- Instructions -->
        <Border Grid.Row="2"
                BackgroundColor="{StaticResource ObscuraSurfaceColor}"
                Padding="20,24">
            <VerticalStackLayout Spacing="12">
                <Label Text="Position the QR code within the frame"
                       FontSize="15"
                       TextColor="{StaticResource ObscuraTextPrimaryColor}"
                       HorizontalOptions="Center"/>
                <Label Text="{Binding StatusMessage}"
                       FontSize="13"
                       TextColor="{Binding StatusColor}"
                       HorizontalOptions="Center"
                       IsVisible="{Binding HasStatusMessage}"/>
            </VerticalStackLayout>
        </Border>
    </Grid>
</ContentPage>
```

**QrScanViewModel.cs**
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Services;

namespace PhantomVault.Mobile.ViewModels
{
    public partial class QrScanViewModel : ObservableObject
    {
        private readonly QrVaultUnlockService _unlockService;
        private readonly VaultService _vaultService;

        [ObservableProperty]
        private bool _isScanning = true;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private Color _statusColor = Colors.Gray;

        public QrScanViewModel(QrVaultUnlockService unlockService, VaultService vaultService)
        {
            _unlockService = unlockService;
            _vaultService = vaultService;
        }

        public async Task ProcessQrCodeAsync(string qrData)
        {
            IsScanning = false;
            StatusMessage = "Validating QR code...";
            StatusColor = Colors.Orange;

            var result = _unlockService.ValidateUnlockPayload(qrData);

            if (!result.Success)
            {
                StatusMessage = result.ErrorMessage ?? "Invalid QR code";
                StatusColor = Colors.Red;

                await Task.Delay(2000);

                // Resume scanning
                IsScanning = true;
                StatusMessage = string.Empty;
                return;
            }

            StatusMessage = "QR code validated! Unlocking vault...";
            StatusColor = Colors.Green;

            // Unlock vault with decrypted master key
            await _vaultService.UnlockVaultAsync(result.MasterKey!, result.VaultId!);

            // Navigate to vault page
            await Shell.Current.GoToAsync("//vault");
        }

        [RelayCommand]
        private async Task Cancel()
        {
            await Shell.Current.GoToAsync("..");
        }
    }
}
```

---

## Priority 5: USB Vault Portability (Month 5)

### Architecture: Portable Vault on USB Drive

PhantomVault is designed as an **offline-first, zero-knowledge system** where the vault database lives on a USB drive. Users plug the USB into any device (desktop, phone via USB-C/OTG) to access their vault. No cloud, no sync servers, complete privacy.

### USB Vault Structure

```
E:\ (USB Drive - "PhantomVault Key")
├── .phantom/
│   ├── vault.pvdb (encrypted vault database)
│   ├── vault.manifest (tamper detection)
│   ├── device-bindings.json (authorized device IDs)
│   └── backups/
│       ├── vault_backup_20250115_143022.pvbkp
│       └── vault_backup_20250114_091544.pvbkp
└── README.txt (instructions for users)
```

### Cross-Platform USB Access

**UsbVaultDetectionService.cs** (src/Core/Services/)

```csharp
using System.IO;
using System.Runtime.InteropServices;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Detects and monitors USB drives for PhantomVault databases
    /// Works on Windows, macOS, Linux, Android (USB OTG), iOS (USB-C/Lightning)
    /// </summary>
    public class UsbVaultDetectionService
    {
        private readonly List<string> _detectedVaultPaths = new();
        private FileSystemWatcher? _watcher;

        public event EventHandler<VaultDetectedEventArgs>? VaultDetected;
        public event EventHandler<VaultRemovedEventArgs>? VaultRemoved;

        /// <summary>
        /// Scans all removable drives for PhantomVault databases
        /// </summary>
        public List<UsbVaultInfo> ScanForVaults()
        {
            var vaults = new List<UsbVaultInfo>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                vaults.AddRange(ScanWindowsDrives());
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                vaults.AddRange(ScanMacVolumes());
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                vaults.AddRange(ScanLinuxMounts());
            }

            return vaults;
        }

        private List<UsbVaultInfo> ScanWindowsDrives()
        {
            var vaults = new List<UsbVaultInfo>();

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Removable && drive.IsReady)
                {
                    var vaultPath = Path.Combine(drive.RootDirectory.FullName, ".phantom", "vault.pvdb");

                    if (File.Exists(vaultPath))
                    {
                        vaults.Add(new UsbVaultInfo
                        {
                            VaultPath = vaultPath,
                            DriveLetter = drive.Name,
                            VolumeLabel = drive.VolumeLabel,
                            TotalSize = drive.TotalSize,
                            FreeSpace = drive.AvailableFreeSpace,
                            DriveFormat = drive.DriveFormat
                        });
                    }
                }
            }

            return vaults;
        }

        private List<UsbVaultInfo> ScanMacVolumes()
        {
            var vaults = new List<UsbVaultInfo>();
            var volumesPath = "/Volumes";

            if (!Directory.Exists(volumesPath))
                return vaults;

            foreach (var volumeDir in Directory.GetDirectories(volumesPath))
            {
                // Skip system volumes
                if (volumeDir.Contains("Macintosh HD"))
                    continue;

                var vaultPath = Path.Combine(volumeDir, ".phantom", "vault.pvdb");

                if (File.Exists(vaultPath))
                {
                    var driveInfo = new DriveInfo(volumeDir);

                    vaults.Add(new UsbVaultInfo
                    {
                        VaultPath = vaultPath,
                        DriveLetter = volumeDir,
                        VolumeLabel = Path.GetFileName(volumeDir),
                        TotalSize = driveInfo.TotalSize,
                        FreeSpace = driveInfo.AvailableFreeSpace,
                        DriveFormat = driveInfo.DriveFormat
                    });
                }
            }

            return vaults;
        }

        private List<UsbVaultInfo> ScanLinuxMounts()
        {
            var vaults = new List<UsbVaultInfo>();
            var mediaPath = $"/media/{Environment.UserName}";
            var mntPath = "/mnt";

            var searchPaths = new[] { mediaPath, mntPath };

            foreach (var basePath in searchPaths)
            {
                if (!Directory.Exists(basePath))
                    continue;

                foreach (var mountDir in Directory.GetDirectories(basePath))
                {
                    var vaultPath = Path.Combine(mountDir, ".phantom", "vault.pvdb");

                    if (File.Exists(vaultPath))
                    {
                        var driveInfo = new DriveInfo(mountDir);

                        vaults.Add(new UsbVaultInfo
                        {
                            VaultPath = vaultPath,
                            DriveLetter = mountDir,
                            VolumeLabel = Path.GetFileName(mountDir),
                            TotalSize = driveInfo.TotalSize,
                            FreeSpace = driveInfo.AvailableFreeSpace,
                            DriveFormat = driveInfo.DriveFormat
                        });
                    }
                }
            }

            return vaults;
        }

        /// <summary>
        /// Monitors for USB insertion/removal events
        /// </summary>
        public void StartMonitoring()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                MonitorWindowsDrives();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                MonitorMacVolumes();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                MonitorLinuxMounts();
            }
        }

        private void MonitorWindowsDrives()
        {
            // Use DriveInfo polling since FileSystemWatcher doesn't work well for drive letters
            Task.Run(async () =>
            {
                var previousDrives = new HashSet<string>(
                    DriveInfo.GetDrives()
                        .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                        .Select(d => d.Name)
                );

                while (true)
                {
                    await Task.Delay(2000); // Check every 2 seconds

                    var currentDrives = new HashSet<string>(
                        DriveInfo.GetDrives()
                            .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                            .Select(d => d.Name)
                    );

                    // Detect new drives
                    var newDrives = currentDrives.Except(previousDrives);
                    foreach (var drive in newDrives)
                    {
                        var vaultPath = Path.Combine(drive, ".phantom", "vault.pvdb");
                        if (File.Exists(vaultPath))
                        {
                            VaultDetected?.Invoke(this, new VaultDetectedEventArgs
                            {
                                VaultPath = vaultPath,
                                DriveLetter = drive
                            });
                        }
                    }

                    // Detect removed drives
                    var removedDrives = previousDrives.Except(currentDrives);
                    foreach (var drive in removedDrives)
                    {
                        VaultRemoved?.Invoke(this, new VaultRemovedEventArgs
                        {
                            DriveLetter = drive
                        });
                    }

                    previousDrives = currentDrives;
                }
            });
        }

        private void MonitorMacVolumes()
        {
            _watcher = new FileSystemWatcher("/Volumes")
            {
                NotifyFilter = NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _watcher.Created += (s, e) =>
            {
                var vaultPath = Path.Combine(e.FullPath, ".phantom", "vault.pvdb");
                if (File.Exists(vaultPath))
                {
                    VaultDetected?.Invoke(this, new VaultDetectedEventArgs
                    {
                        VaultPath = vaultPath,
                        DriveLetter = e.FullPath
                    });
                }
            };

            _watcher.Deleted += (s, e) =>
            {
                VaultRemoved?.Invoke(this, new VaultRemovedEventArgs
                {
                    DriveLetter = e.FullPath
                });
            };
        }

        private void MonitorLinuxMounts()
        {
            var mediaPath = $"/media/{Environment.UserName}";

            if (!Directory.Exists(mediaPath))
                return;

            _watcher = new FileSystemWatcher(mediaPath)
            {
                NotifyFilter = NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _watcher.Created += (s, e) =>
            {
                var vaultPath = Path.Combine(e.FullPath, ".phantom", "vault.pvdb");
                if (File.Exists(vaultPath))
                {
                    VaultDetected?.Invoke(this, new VaultDetectedEventArgs
                    {
                        VaultPath = vaultPath,
                        DriveLetter = e.FullPath
                    });
                }
            };

            _watcher.Deleted += (s, e) =>
            {
                VaultRemoved?.Invoke(this, new VaultRemovedEventArgs
                {
                    DriveLetter = e.FullPath
                });
            };
        }

        public void StopMonitoring()
        {
            _watcher?.Dispose();
        }
    }

    public class UsbVaultInfo
    {
        public string VaultPath { get; set; } = string.Empty;
        public string DriveLetter { get; set; } = string.Empty;
        public string VolumeLabel { get; set; } = string.Empty;
        public long TotalSize { get; set; }
        public long FreeSpace { get; set; }
        public string DriveFormat { get; set; } = string.Empty;
    }

    public class VaultDetectedEventArgs : EventArgs
    {
        public string VaultPath { get; set; } = string.Empty;
        public string DriveLetter { get; set; } = string.Empty;
    }

    public class VaultRemovedEventArgs : EventArgs
    {
        public string DriveLetter { get; set; } = string.Empty;
    }
}
```

### Mobile USB Support (Android & iOS)

**Android USB OTG Support**

Android devices with USB OTG can read USB drives via `/storage/` or `/mnt/media_rw/`.

**UsbVaultDetectionService.Android.cs** (Platforms/Android/)

```csharp
using Android.Content;
using Android.Hardware.Usb;
using Android.OS.Storage;

namespace PhantomVault.Mobile.Platforms.Android
{
    public class UsbVaultDetectionServiceAndroid
    {
        private readonly Context _context;
        private UsbManager? _usbManager;
        private StorageManager? _storageManager;

        public UsbVaultDetectionServiceAndroid(Context context)
        {
            _context = context;
            _usbManager = (UsbManager?)_context.GetSystemService(Context.UsbService);
            _storageManager = (StorageManager?)_context.GetSystemService(Context.StorageService);
        }

        public List<UsbVaultInfo> ScanForVaults()
        {
            var vaults = new List<UsbVaultInfo>();

            if (_storageManager == null)
                return vaults;

            // Android 7.0+ (API 24+)
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
            {
                var volumes = _storageManager.StorageVolumes;

                foreach (var volume in volumes)
                {
                    // Check if removable (USB drive, SD card)
                    if (!volume.IsRemovable)
                        continue;

                    // Get mount path
                    var mountPath = GetVolumePath(volume);
                    if (string.IsNullOrEmpty(mountPath))
                        continue;

                    var vaultPath = Path.Combine(mountPath, ".phantom", "vault.pvdb");

                    if (File.Exists(vaultPath))
                    {
                        vaults.Add(new UsbVaultInfo
                        {
                            VaultPath = vaultPath,
                            DriveLetter = mountPath,
                            VolumeLabel = volume.GetDescription(_context) ?? "USB Drive",
                            DriveFormat = "Unknown"
                        });
                    }
                }
            }
            else
            {
                // Fallback for older Android: check common USB mount points
                var commonPaths = new[]
                {
                    "/storage/usbotg",
                    "/storage/usb1",
                    "/mnt/usb_storage",
                    "/mnt/usbotg"
                };

                foreach (var basePath in commonPaths)
                {
                    if (!Directory.Exists(basePath))
                        continue;

                    var vaultPath = Path.Combine(basePath, ".phantom", "vault.pvdb");

                    if (File.Exists(vaultPath))
                    {
                        vaults.Add(new UsbVaultInfo
                        {
                            VaultPath = vaultPath,
                            DriveLetter = basePath,
                            VolumeLabel = "USB Drive",
                            DriveFormat = "Unknown"
                        });
                    }
                }
            }

            return vaults;
        }

        private string? GetVolumePath(StorageVolume volume)
        {
            try
            {
                // Use reflection to get path (not publicly exposed)
                var pathField = volume.Class?.GetDeclaredField("mPath");
                if (pathField != null)
                {
                    pathField.Accessible = true;
                    var pathObj = pathField.Get(volume);
                    return pathObj?.ToString();
                }
            }
            catch
            {
                // Fallback: try common patterns
            }

            return null;
        }

        public void RequestUsbPermission(UsbDevice device)
        {
            if (_usbManager == null)
                return;

            var permissionIntent = PendingIntent.GetBroadcast(
                _context,
                0,
                new Intent("com.phantomvault.USB_PERMISSION"),
                PendingIntentFlags.Immutable
            );

            _usbManager.RequestPermission(device, permissionIntent);
        }
    }
}
```

**iOS USB-C / Lightning Support**

iOS 13+ supports external drives via the Files app. PhantomVault can access USB drives connected via USB-C (iPad Pro, iPhone 15+) or Lightning to USB adapter.

**UsbVaultDetectionService.iOS.cs** (Platforms/iOS/)

```csharp
using Foundation;
using UIKit;

namespace PhantomVault.Mobile.Platforms.iOS
{
    public class UsbVaultDetectionServiceIOS
    {
        /// <summary>
        /// iOS external storage is accessed via NSFileManager
        /// Removable volumes appear under /private/var/mobile/Library/LiveFiles/
        /// </summary>
        public List<UsbVaultInfo> ScanForVaults()
        {
            var vaults = new List<UsbVaultInfo>();
            var fileManager = NSFileManager.DefaultManager;

            // iOS mounts external drives under /Volumes on some devices
            // or via NSFileProviderDomain for Files app integration
            var searchPaths = new[]
            {
                "/Volumes",
                "/private/var/mobile/Library/LiveFiles"
            };

            foreach (var basePath in searchPaths)
            {
                if (!Directory.Exists(basePath))
                    continue;

                try
                {
                    var subdirs = Directory.GetDirectories(basePath);

                    foreach (var dir in subdirs)
                    {
                        var vaultPath = Path.Combine(dir, ".phantom", "vault.pvdb");

                        if (File.Exists(vaultPath))
                        {
                            vaults.Add(new UsbVaultInfo
                            {
                                VaultPath = vaultPath,
                                DriveLetter = dir,
                                VolumeLabel = Path.GetFileName(dir),
                                DriveFormat = "Unknown"
                            });
                        }
                    }
                }
                catch
                {
                    // Permission denied or path not accessible
                }
            }

            return vaults;
        }

        /// <summary>
        /// Opens iOS Files app to let user select vault location
        /// Required for sandboxed apps
        /// </summary>
        public async Task<string?> PromptUserForVaultLocationAsync()
        {
            var documentPicker = new UIDocumentPickerViewController(
                new[] { "public.folder" },
                UIDocumentPickerMode.Open
            );

            var tcs = new TaskCompletionSource<string?>();

            documentPicker.DidPickDocument += (sender, args) =>
            {
                if (args.Url != null)
                {
                    // Start accessing security-scoped resource
                    args.Url.StartAccessingSecurityScopedResource();

                    var vaultPath = Path.Combine(args.Url.Path, ".phantom", "vault.pvdb");

                    if (File.Exists(vaultPath))
                    {
                        tcs.SetResult(vaultPath);
                    }
                    else
                    {
                        tcs.SetResult(null);
                    }

                    args.Url.StopAccessingSecurityScopedResource();
                }
                else
                {
                    tcs.SetResult(null);
                }
            };

            documentPicker.WasCancelled += (sender, args) =>
            {
                tcs.SetResult(null);
            };

            var viewController = GetTopViewController();
            viewController?.PresentViewController(documentPicker, true, null);

            return await tcs.Task;
        }

        private UIViewController? GetTopViewController()
        {
            var window = UIApplication.SharedApplication.KeyWindow;
            var rootViewController = window?.RootViewController;

            while (rootViewController?.PresentedViewController != null)
            {
                rootViewController = rootViewController.PresentedViewController;
            }

            return rootViewController;
        }
    }
}
```

### USB Vault Initialization Wizard

**CreateUsbVaultPage.xaml** (MAUI)

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="PhantomVault.Mobile.Views.CreateUsbVaultPage"
             BackgroundColor="{StaticResource ObscuraCanvasColor}">

    <ScrollView>
        <VerticalStackLayout Padding="32" Spacing="24">
            <!-- Header -->
            <VerticalStackLayout Spacing="8">
                <Label Text="Setup USB Vault"
                       FontSize="24"
                       FontFamily="InterBold"
                       TextColor="{StaticResource ObscuraTextPrimaryColor}"/>
                <Label Text="Initialize your USB drive as a PhantomVault key"
                       FontSize="14"
                       TextColor="{StaticResource ObscuraTextSecondaryColor}"/>
            </VerticalStackLayout>

            <!-- Step 1: Select USB Drive -->
            <Border BackgroundColor="{StaticResource ObscuraSurfaceColor}"
                    StrokeThickness="1"
                    Stroke="{StaticResource ObscuraBorderColor}"
                    Padding="20"
                    StrokeShape="RoundRectangle 16">
                <VerticalStackLayout Spacing="16">
                    <Label Text="Step 1: Select USB Drive"
                           FontSize="16"
                           FontFamily="InterSemiBold"
                           TextColor="{StaticResource ObscuraTextPrimaryColor}"/>

                    <Picker ItemsSource="{Binding AvailableDrives}"
                            SelectedItem="{Binding SelectedDrive}"
                            Title="Choose USB Drive"
                            TextColor="{StaticResource ObscuraTextPrimaryColor}"/>

                    <Label Text="{Binding SelectedDriveInfo}"
                           FontSize="13"
                           TextColor="{StaticResource ObscuraTextSecondaryColor}"
                           IsVisible="{Binding HasSelectedDrive}"/>

                    <Button Text="Scan for USB Drives"
                            Command="{Binding ScanDrivesCommand}"
                            Style="{StaticResource SecondaryButton}"/>
                </VerticalStackLayout>
            </Border>

            <!-- Step 2: Set Master Password -->
            <Border BackgroundColor="{StaticResource ObscuraSurfaceColor}"
                    StrokeThickness="1"
                    Stroke="{StaticResource ObscuraBorderColor}"
                    Padding="20"
                    StrokeShape="RoundRectangle 16">
                <VerticalStackLayout Spacing="16">
                    <Label Text="Step 2: Create Master Password"
                           FontSize="16"
                           FontFamily="InterSemiBold"
                           TextColor="{StaticResource ObscuraTextPrimaryColor}"/>

                    <Entry Placeholder="Master Password"
                           Text="{Binding MasterPassword}"
                           IsPassword="True"
                           Style="{StaticResource ObscuraEntry}"/>

                    <Entry Placeholder="Confirm Master Password"
                           Text="{Binding ConfirmPassword}"
                           IsPassword="True"
                           Style="{StaticResource ObscuraEntry}"/>

                    <!-- Password Strength Indicator -->
                    <ProgressBar Progress="{Binding PasswordStrength}"
                                 ProgressColor="{Binding PasswordStrengthColor}"
                                 HeightRequest="8"/>

                    <Label Text="{Binding PasswordStrengthText}"
                           FontSize="12"
                           TextColor="{Binding PasswordStrengthColor}"/>
                </VerticalStackLayout>
            </Border>

            <!-- Step 3: Device Binding (Optional) -->
            <Border BackgroundColor="{StaticResource ObscuraSurfaceColor}"
                    StrokeThickness="1"
                    Stroke="{StaticResource ObscuraBorderColor}"
                    Padding="20"
                    StrokeShape="RoundRectangle 16">
                <VerticalStackLayout Spacing="16">
                    <Label Text="Step 3: Device Binding (Optional)"
                           FontSize="16"
                           FontFamily="InterSemiBold"
                           TextColor="{StaticResource ObscuraTextPrimaryColor}"/>

                    <Label Text="Bind vault to this device's hardware ID for extra security"
                           FontSize="13"
                           TextColor="{StaticResource ObscuraTextSecondaryColor}"
                           TextType="Html"/>

                    <CheckBox IsChecked="{Binding EnableDeviceBinding}"
                              Color="{StaticResource ObscuraAccentColor}"/>

                    <Label Text="{Binding DeviceId}"
                           FontSize="12"
                           FontFamily="Consolas"
                           TextColor="{StaticResource ObscuraTextSecondaryColor}"
                           IsVisible="{Binding EnableDeviceBinding}"/>
                </VerticalStackLayout>
            </Border>

            <!-- Create Button -->
            <Button Text="Create USB Vault"
                    Command="{Binding CreateVaultCommand}"
                    Style="{StaticResource PrimaryButton}"
                    IsEnabled="{Binding CanCreateVault}"
                    HeightRequest="52"/>

            <!-- Warning -->
            <Border BackgroundColor="#2D1810"
                    StrokeThickness="1"
                    Stroke="#B45309"
                    Padding="16"
                    StrokeShape="RoundRectangle 12">
                <HorizontalStackLayout Spacing="12">
                    <Label Text="&#xf071;"
                           FontFamily="FontAwesome"
                           FontSize="20"
                           TextColor="#F59E0B"
                           VerticalOptions="Start"/>
                    <Label TextType="Html"
                           TextColor="#F59E0B"
                           FontSize="12"
                           LineBreakMode="WordWrap">
                        <Label.Text>
                            <![CDATA[<b>Important:</b> Your USB drive will be formatted. All existing data will be lost. Keep your USB drive safe - if you lose it, you lose access to your vault.]]>
                        </Label.Text>
                    </Label>
                </HorizontalStackLayout>
            </Border>
        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

---

## Timeline & Milestones

| Month | Milestone | Deliverables |
|-------|-----------|--------------|
| **1-2** | MAUI Project Resurrection | .NET 8 MAUI project, UI matching Obscura theme, core vault operations working |
| **2-3** | Android Implementation | Enhanced autofill service, biometric auth, USB OTG detection |
| **3-4** | iOS Implementation | Credential Provider extension, Face ID/Touch ID, USB-C/Lightning support |
| **4** | QR Code Unlock | Desktop QR generator, mobile scanner, encrypted session transfer |
| **5** | USB Vault Portability | Cross-platform USB detection, vault initialization wizard, automatic mounting |
| **6** | Testing & Launch | App Store submission, Google Play submission, documentation |

---

## Success Metrics

- **Android App**: Published on Google Play with 4.5+ rating
- **iOS App**: Published on App Store with 4.5+ rating
- **Autofill Success Rate**: >95% on top 100 websites/apps
- **USB Detection**: <2 seconds to detect vault after USB insertion
- **Biometric Auth**: <500ms authentication time
- **QR Unlock**: <2 second total unlock time
- **Crash Rate**: <0.1% (industry standard)
- **Downloads**: 10,000+ in first 3 months
- **USB Vault Portability**: Works across Windows, macOS, Linux, Android, iOS without modification

---

## Dependencies & Requirements

### NuGet Packages (Mobile)
```xml
<PackageReference Include="Microsoft.Maui.Controls" Version="8.0.90" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
<PackageReference Include="QRCoder" Version="1.6.0" />
<PackageReference Include="ZXing.Net.Maui" Version="0.4.0" />
<PackageReference Include="Xamarin.AndroidX.Biometric" Version="1.2.0-alpha05" />
```

### Platform Requirements
- **Android**: API 26+ (Android 8.0 Oreo), targetSdkVersion 34
- **iOS**: iOS 15.0+, Xcode 15+
- **MAUI**: .NET 8.0 SDK

### Permissions

**Android (AndroidManifest.xml)**:
```xml
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.CAMERA" />
<uses-permission android:name="android.permission.USE_BIOMETRIC" />
<uses-permission android:name="android.permission.USE_FINGERPRINT" />
```

**iOS (Info.plist)**:
```xml
<key>NSCameraUsageDescription</key>
<string>Camera access is required to scan QR codes for vault unlock</string>
<key>NSFaceIDUsageDescription</key>
<string>Face ID is used to securely unlock your vault</string>
```

---

## Risk Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| **App Store Rejection** | High | Follow all guidelines, prepare detailed privacy policy, implement in-app purchases for cloud sync (Apple requires monetization for password managers) |
| **Autofill API Changes** | Medium | Abstract autofill logic into interfaces, maintain compatibility layers for older OS versions |
| **Cloud Sync Conflicts** | Medium | Implement robust conflict detection with manual merge UI, local-first architecture |
| **Biometric Bypass** | High | Always encrypt master key in keychain, require recent biometric authentication (<5 minutes), implement fallback to master password |
| **QR Code Interception** | High | 60-second expiration, single-use tokens, encrypted payload with session key |

---

## Post-Launch Roadmap (Phase 2.5)

After initial mobile launch, consider:
1. **Passkey/WebAuthn Support** (iOS 17+, Android 14+)
2. **Wear OS / Apple Watch** quick access app for TOTP codes
3. **Multi-USB Vault Support** - switch between multiple USB vaults (personal, work, family)
4. **Emergency Access USB** - create recovery USB with time-delayed access
5. **USB Vault Cloning** - duplicate vault to backup USB drive
6. **Advanced Password Health** - reused password detection, weak password warnings
7. **Secure Notes** - encrypted text notes with markdown support
8. **Hardware Security Keys** - YubiKey, Titan Key integration for 2FA

---

## Conclusion

Phase 2 focuses on bringing PhantomVault to mobile platforms with feature parity to the desktop application. The mobile apps will leverage native platform capabilities (autofill, biometric auth) while maintaining the same zero-knowledge security architecture. **USB vault portability** ensures your encrypted vault stays with you on a physical USB drive - no cloud, no sync servers, complete offline privacy. QR unlock provides quick desktop-to-mobile authentication for convenience.

**Estimated Total Effort**: 4-6 months (1 senior developer full-time)
**Budget**: $80,000 - $120,000 (assuming $40-50/hour contractor rate)

**Next Steps**:
1. Begin MAUI project setup (Week 1)
2. Design mobile UI mockups in Figma
3. Set up CI/CD pipeline for mobile builds (GitHub Actions + Fastlane)
4. Create test Apple Developer + Google Play accounts
5. Start implementation following Priority 1 task breakdown
