# Google Login, Mobile Builds and Eight-Agent Audit

**Goal:** Enable the existing verified Google login on Android/iOS builds, update Docker, and assess project regressions with evidence.

**Architecture:** Keep the native Google SDK → verified backend ID token → existing app session flow. Inject only validated public OAuth client IDs into mobile builds. Preserve the current gateway URL, Docker SQL/audio volumes, TikTok launcher, and byte-for-byte `.env`.

**Execution:** The user has authorized implementation without more routine confirmations. Eight separate agent assignments run in batches under the four-concurrent-agent limit, with root integrating/building/deploying. No claim of absolute defect freedom.

## Work assignments

- [ ] 1. Flutter Google flow: inspect SDK/session/navigation/error paths; repair proven defects with focused regressions.
- [ ] 2. Backend Google identity: validate signature/issuer/audience/expiry/email and safe account linking; test proven defects.
- [ ] 3. Android/iOS/CI: validate native Google package/callback and inject OAuth IDs into iOS workflow.
- [ ] 4. Provider/download audit: review redirects/egress/extraction/storage without bypassing source gates.
- [ ] 5. Guest/library/sharing audit: review ownership, synchronization and data preservation.
- [ ] 6. Session/security audit: review token rotation/concurrency/linking authorization boundaries.
- [ ] 7. Docker/deployment audit: inspect shared storage, persistent keys, startup and gateway configuration.
- [ ] 8. Final independent review: inspect combined changes, coverage and remaining device/credential gaps.

## Integration and verification

- [ ] Add a repeatable Android build script that reads existing environment names, validates and passes only public client IDs, uses the existing HTTPS gateway, and leaves `.env` untouched.
- [ ] Run Flutter analyze/full tests serially, backend full tests with isolated `TMusicV2Test_*` SQL, Python TikTok and gateway tests.
- [ ] Build an installable Android APK, inspect signing/package/API/OAuth configuration and provide a stable artifact path; update a connected device only when safe and available.
- [ ] Rebuild/recreate Docker API and worker without resetting production data, verify readiness/capabilities/invalid-token rejection and live TikTok extraction.
- [ ] If mobile iOS config changes, push only authorized project files and run the existing macOS workflow to obtain a new unsigned IPA; inspect the artifact and report sideload signing limitations accurately.
- [ ] Record commands/results, eight-agent findings and unresolved limits in BUILD_LOG and an audit report.
