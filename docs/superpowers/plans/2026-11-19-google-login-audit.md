# Google Login, Mobile Builds and Eight-Agent Audit

**Goal:** Enable the existing verified Google login on Android/iOS builds, update Docker, and assess project regressions with evidence.

**Architecture:** Keep the native Google SDK → verified backend ID token → existing app session flow. Inject only validated public OAuth client IDs into mobile builds. Preserve the current gateway URL, Docker SQL/audio volumes, TikTok launcher, and byte-for-byte `.env`.

**Execution:** The user has authorized implementation without more routine confirmations. Eight separate agent assignments run in batches under the four-concurrent-agent limit, with root integrating/building/deploying. No claim of absolute defect freedom.

## Work assignments

- [x] 1. Flutter Google flow: inspect SDK/session/navigation/error paths; repair proven defects with focused regressions.
- [x] 2. Backend Google identity: validate signature/issuer/audience/expiry/email and safe account linking; test proven defects.
- [x] 3. Android/iOS/CI: validate native Google package/callback and inject OAuth IDs into iOS workflow.
- [x] 4. Provider/download audit: review redirects/egress/extraction/storage without bypassing source gates.
- [x] 5. Guest/library/sharing audit: review ownership, synchronization and data preservation.
- [x] 6. Audio runtime: reproduce conversion/process failures and verify bounded local decoding; identity/session checks also covered by assignments 1, 2 and 7.
- [x] 7. Docker/deployment audit: inspect shared storage, persistent keys, startup and gateway configuration.
- [x] 8. Final independent review: inspect combined changes, coverage and remaining device/credential gaps.

## Integration and verification

- [x] Add a repeatable Android build script that reads existing environment names, validates and passes only public client IDs, uses the existing HTTPS gateway, and leaves `.env` untouched.
- [x] Run Flutter analyze/full tests serially, backend full tests with isolated `TMusicV2Test_*` SQL, Python TikTok and gateway tests.
- [x] Build an installable Android APK, inspect signing/package/API/OAuth configuration and provide a stable artifact path; update a connected device only when safe and available.
- [x] Rebuild/recreate Docker API and worker without resetting production data, verify readiness/capabilities/invalid-token rejection and live TikTok extraction.
- [x] If mobile iOS config changes, push only authorized project files and run the existing macOS workflow to obtain a new unsigned IPA; inspect the artifact and report sideload signing limitations accurately.
- [x] Record commands/results, eight-agent findings and unresolved limits in BUILD_LOG and an audit report.
