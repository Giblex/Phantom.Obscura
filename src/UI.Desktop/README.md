# PhantomVault.UI Review

## Overview

`PhantomVault.UI` is the Avalonia desktop application for Obscura. It is the visible product surface: onboarding, vault views, categories, dashboard, password tools, import/export, themeing, autofill prompts, recovery windows, and settings.

## Tech Stack

- `.NET 9` targeting `net9.0-windows10.0.19041.0`
- Avalonia `11.3.9`
- ReactiveUI
- CommunityToolkit.Mvvm
- SkiaSharp
- ZXing
- Serilog
- References to core, platform, crypto, autofill, and AssetShield

## Architecture

- Large DI registration in `App.axaml.cs`
- MVVM with many view models and windows
- Rich resource/theme system under `Assets`, `Styles`, and `Resources`
- Desktop app currently acts as the composition root for many backend services
- Legacy recovery integration is excluded and replaced with stubs

## Strengths

- Visually ambitious compared with most security desktop apps
- Large UX surface already exists rather than being only a prototype shell
- Good theming depth and a broad set of desktop workflows
- Desktop startup path shows strong intent around integrity, policy verification, and structured logging

## Competitor Comparison

Score: `7/10`

Why:

- Better visual ambition than KeePassXC-class tooling
- Stronger security framing than many indie Avalonia apps
- Still behind mature products because several user-facing options are placeholders or shims

## Production Readiness

Score: `6/10`

Blockers:

- Recovery is still shimmed through stub classes when the external recovery module is absent
- Several converters still throw on `ConvertBack`
- Sign-in dialog still shows placeholder secondary auth buttons
- UI test coverage is much thinner than the breadth of the desktop surface
- The app still carries developer-mode hooks and bypass-oriented workflows

## Outstanding TODOs And Stubs

- `SignInDialog.axaml` still exposes Windows Hello / Google / Microsoft sign-in as placeholders
- `Services/IntegratedRecoveryServiceStub.cs`
- `Services/RecoveryDeveloperModeStub.cs`
- `Views/RecoveryPanelStub.cs`
- `ViewModels/DashboardViewModel.cs` still contains placeholder security dashboard logic
- `ViewModels/PasswordSecurityViewModel.cs` still contains placeholder comments

## Broken Or Risky Areas

- The converter set under `Converters/` still includes multiple `ConvertBack` methods that throw `NotImplementedException`
- Desktop docs and feature messaging still overstate some security surfaces
- `App.axaml.cs` enables `RecoveryDeveloperMode.IsEnabled = true` in DEBUG, which is acceptable for development but should stay carefully bounded
- The desktop app composes many responsibilities directly, which raises maintenance and regression risk

## Security And Privacy Notes

- Startup sequence is thoughtful: crash-dump suppression, build integrity, policy verification, structured logging
- Privacy-shield behavior exists and is a nice differentiator
- The user-facing security story should be tightened so placeholder options do not appear production-ready

## Unused Or Unnecessary Files

- Large `Legacy/` exclusion in the project file suggests churn and old snapshots that should either be archived cleanly or removed
- Stub recovery files are useful only as temporary compatibility shims and should not become permanent architecture

## Improvement Ideas

- Replace throwing converters with safe `BindingOperations.DoNothing` or equivalent non-throwing behavior
- Move recovery integration behind a real feature boundary instead of compile-time stubs
- Decompose oversized composition logic in `App.axaml.cs`
- Audit all visible dialogs for placeholder copy and disable unfinished actions in release builds
- Add focused UI tests for onboarding, unlock, settings, categories, and import/export flows

## Next Steps

1. Remove or gate placeholder sign-in options
2. Replace recovery stubs with real integration or explicit feature unavailability
3. Eliminate converter throw paths
4. Expand UI test coverage around the main user journeys
5. Reduce composition-root sprawl in `App.axaml.cs`
