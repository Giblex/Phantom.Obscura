# PhantomVault.Autofill Review

## Overview

`PhantomVault.Autofill` is the backend side of Obscura's autofill system. It contains native messaging host logic, field detection, ranking, capture flows, and provider abstractions for desktop and mobile autofill scenarios.

## Tech Stack

- `.NET 9`
- C#
- Core project reference
- Windows and mobile-oriented code paths

## Architecture

- Native messaging host service for browser/native-host communication
- Provider-style desktop/mobile services
- Credential matching and detection support classes
- Security checks around unlocked-vault state and origin allowlisting

## Strengths

- Better backend design than the older docs imply
- Native host is fail-closed around origin allowlisting
- Credential operations are gated on unlocked-vault state and manifest autofill settings
- The intended architecture is sound

## Competitor Comparison

Score: `5/10`

Why:

- Promising backend ideas
- Not competitive with real browser-integrated autofill products until the extension and deployment surface exist

## Production Readiness

Score: `4/10`

Blockers:

- Browser extension layer is not present in the current tree
- Windows autofill service is not a complete fill engine
- In-memory repository is not a production persistence strategy
- Mobile support is structurally hinted at but not actually targeted by current TFMs

## Outstanding TODOs And Stubs

- Missing browser extension implementation
- Missing native-host registration/deployment assets
- `InMemoryCredentialRepository` is explicitly non-persistent and unencrypted
- Project targets do not match the cross-platform/mobile story the code suggests

## Broken Or Risky Areas

- `WindowsAutofillService` proves credential existence but does not deliver a full browser fill path by itself
- Repo docs can overstate autofill readiness relative to the code

## Security And Privacy Notes

- Security posture is directionally good where the native host is concerned
- Trust posture falls apart if the missing extension surface is assumed rather than implemented

## Unused Or Unnecessary Files

- None obviously dead inside this project, but repo-level docs and scripts still reference absent browser-extension assets

## Improvement Ideas

- Build the browser extension first, then validate the end-to-end contract
- Replace in-memory-only storage assumptions with explicit vault-backed retrieval
- Decide whether mobile autofill is truly in scope; if yes, retarget project TFMs accordingly

## Next Steps

1. Build the missing extension and native-host registration assets
2. Finish a real end-to-end Windows/browser fill flow
3. Remove or replace non-production repository implementations
4. Align project targets with the intended platform story
