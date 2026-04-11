# PhantomVault.Core.Tests Review

## Overview

`PhantomVault.Core.Tests` is the primary verification project for Obscura. It covers large parts of the core and crypto-adjacent behavior and is far more meaningful than the UI test project.

## Tech Stack

- xUnit
- .NET test project
- References into `src/Core`

## Architecture

- Broad test surface spanning manifests, policies, USB behavior, imports, TOTP, passkeys, vault lifecycle, and security flows
- Mostly unit and integration-style tests mixed in one project

## Strengths

- Breadth is good
- It covers important security-facing areas instead of only trivial helpers
- There is enough here to become a strong regression suite

## Competitor Comparison

Score: `7/10`

Why:

- Better than many small security-product test suites
- Still behind mature products because several tests labelled as integrations are placeholders rather than fixture-backed reality

## Production Readiness

Score: `6/10`

Blockers:

- KeePass integration tests are still mostly specifications written as placeholder asserts
- Current test invocation reliability was not clean in this session due to stale file-lock issues

## Outstanding TODOs And Stubs

- Convert placeholder KeePass tests into real `.kdbx` fixture-backed tests
- Separate true integration tests from lighter unit coverage if the suite keeps growing
- Tighten build/test workflow so stale `testhost` locks are less disruptive

## Broken Or Risky Areas

- Placeholder assertions can create a false sense of coverage
- The main solution/test split makes drift easier if contributors assume the solution alone represents the repo health

## Security And Privacy Notes

- This project is one of the most important readiness multipliers in the repo
- The fastest way to improve production confidence is to turn the placeholder tests into real negative and corruption-path coverage

## Unused Or Unnecessary Files

- No broad dead-tree problem surfaced in the live test folder during this pass

## Improvement Ideas

- Add real corruption, forgery, rollback, and USB-mismatch fixtures
- Add manifest/schema mismatch tests for policy artifacts
- Add strong unlock-failure tests once the core unlock path is made authoritative

## Next Steps

1. Replace KeePass placeholder tests with fixture-backed ones
2. Add explicit regression tests for the current placeholder-backed core risks
3. Tighten CI and local test workflow reliability
