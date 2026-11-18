# TMusic iOS IPA Build Plan

**Goal:** Build and retrieve one real unsigned iPhone IPA using the locally cloned flutter-app-template macOS Actions workflow.

**Architecture:** Export only Flutter source and the adapted iOS workflow into a private GitHub build repository. Preserve the existing app, icons, configuration names and source template. Build profile AOT for the user-selected LAN HTTP API; prepare iOS local-network and public Google configuration only in the build copy. Package the actual arm64 Runner.app under Payload and verify the downloaded archive.

**Tech stack:** Flutter 3.47.0/Dart 3.13, iOS 15+, Xcode on standard macOS GitHub Actions runner, Python plist preparation, unsigned IPA for later local signing.

**Spec:** User requests an IPA based on the cloned template and confirms API on the existing machine over the same Wi-Fi.

## Constraints and verification

- Preserve `.env` byte-for-byte; export only supported public OAuth IDs, never backend/secrets, keys, generated host paths, local user data or credentials.
- Do not publish the source publicly, change GitHub billing, touch SQL or replace running API/Worker. Do not upload an Apple certificate/account or claim unsigned output is directly installable.
- Keep existing Google token verification and release HTTPS enforcement. HTTP LAN configuration belongs only to this explicit internal profile build.
- Validate safe source inventory, analyze/full Flutter tests on macOS, native compile, IPA Payload/plist/arm64 executable, size/hash, artifact download and API LAN reachability. Device signing/playback/Google remain separate from compilation.

## Tasks

- [x] Read both local template clones, iOS source, build prerequisites and current API networking.
- [ ] Prepare and test iOS build-copy plist/Google/keychain configuration; adapt template workflow and IPA packaging.
- [ ] Stage sanitized Flutter-only source, review inventory, create/use private build repository and dispatch macOS build.
- [ ] Enable a bounded LAN forward to the existing loopback API without restarting services; verify health through the selected address.
- [ ] Follow the actual build to completion, repair build failures, download and inspect IPA; record commands/outcomes and signing limitations in BUILD_LOG.
