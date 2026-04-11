# Obscura.Keysmith Review

## Overview

`Obscura.Keysmith` is an internal operational utility for generating root keys, generating self-signed certificates, signing policies, and verifying signatures.

## Tech Stack

- `.NET`
- C#
- Certificate and signing helpers

## Architecture

- Utility-style console tool
- Focused on repo trust material and policy signing workflows

## Strengths

- Useful operational tool for an app with signed-policy ambitions
- Helps keep trust tooling close to the code that consumes it

## Competitor Comparison

Score: `6/10`

Why:

- Good internal utility concept
- Operational hygiene needs to be much tighter before it supports a mature release process

## Production Readiness

Score: `5/10`

Blockers:

- Sensitive certificate material is currently present in-tree under `certs/`
- Certificate loading uses obsolete API paths
- Tooling and repo hygiene are not yet what you want around release-signing material

## Outstanding TODOs And Stubs

- Migrate certificate loading to modern APIs
- Move operational secrets/certs out of normal source control
- Clarify whether this tool is dev-only, build-pipeline tooling, or long-term release infrastructure

## Broken Or Risky Areas

- Sensitive `.crt` and `.pfx` material in the repo is a release-process smell even if the files are test/dev material
- The project file copies cert files into output, which increases the chance of accidental leakage or misuse

## Security And Privacy Notes

- The tool is valuable, but only if its operational handling is disciplined
- Trust tooling must be held to a higher standard than normal utility code

## Unused Or Unnecessary Files

- `certs/obscura_root.crt`
- `certs/obscura_root.pfx`

These may be operationally useful, but they should not live casually in the repository.

## Improvement Ideas

- Externalize cert material into secure secrets storage
- Make the tool pipeline-friendly and explicit about environment expectations
- Add verification tests around policy signing and schema/version compatibility

## Next Steps

1. Remove cert material from source control
2. Modernize certificate-loading APIs
3. Define the exact operational role of Keysmith in build and release flows
