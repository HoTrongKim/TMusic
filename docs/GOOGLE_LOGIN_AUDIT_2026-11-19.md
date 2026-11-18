# Google login and eight-agent project audit — 2026-11-19

The user requested Google login, an Android APK, updated Docker, an optional updated IPA, and eight collaborating agents reviewing the project. The host permits four concurrent agents including root, so assignments ran in batches. Root integrates changes, runs combined checks and builds, and records exact results in `BUILD_LOG.md`.

| Agent assignment | Evidence / result |
|---|---|
| 1. Flutter Google login | Existing SDK/token/session/Home flow already implemented. 38 focused tests and analyze passed; no speculative source changes. Native build configuration was the activation gap. |
| 2. Backend Google identity | No confirmed production defect in signature/issuer/audience/expiry checks or explicit account linking. Added algorithm/time-claim crypto regressions; 13 focused checks passed. |
| 3. Native configuration and CI | Added allowlisted public build settings helper and iOS Google callback preparation. 8 tests passed. Android requires registered package plus certificate SHA-1; no Android clientId is passed to the SDK. |
| 4. Provider/download/storage | Fixed failed replacement download hiding an intact prior MP3 and failure handling overwriting a cancellation/removal. Isolated SQL regression verifies unchanged bytes and HTTP 200 playback; deterministic race tests verify cancellation wins. 10 focused tests passed. No confirmed SSRF or IDOR regression in reviewed paths. |
| 5. Guest/library/sharing | Fixed guest album song order lost during account claim. Preserves existing account memberships and keeps guest data on ordering/session/revision failure; 6 focused regressions passed. Reviewed ownership filters/share revocation/private-media separation; no confirmed bypass found. |
| 6. Local audio runtime | Fixed MPEG-TS conversion and invalid executable startup mapping with red/green regressions; 9 local audio tests passed with real FFmpeg. An earlier Windows cleanup-lock failure did not reproduce in focused runs; no unsupported cancellation fix was made. |
| 7. Deployment/build | Fixed relative release keystore path resolution. Identified existing operational limit: anonymous/auth rate limits see the shared tunnel connector IP, so users can share the request budget. No untrusted forwarding headers were enabled. |
| 8. Independent final review | Reviewed combined OAuth/build, downloader and guest-order changes; identified failure/cancel publication race, then reviewed the implemented transaction/write-fence fix. No additional material blocker found in reviewed changes; device/credential gaps remain. |

Final local Flutter analyze passed and all 105 Flutter tests passed. Full backend tests run with isolated SQL; final counts, Docker smoke checks and artifact verification are recorded in `BUILD_LOG.md`.

## Verification boundaries

- Passing tests and a static review cannot prove that no defects will ever occur.
- SQL integration tests use generated `TMusicV2Test_*` databases; no migrations/reset/deletion of legacy `TMusicDev` or live `TMusicV2Dev` are part of this work.
- Google SDK UI and signed token verification are separate evidence. Synthetic crypto tests and a configured capability do not establish a fresh real Google login. Account choice/consent requires the user's real account and matching provider registration.
- The Android test artifact uses the existing debug signing certificate. A production release needs the user's distribution keystore and matching Google registration.
- iOS builds use macOS CI and produce an unsigned IPA. Signing, bundle registration and playback/background behavior on the user's iPhone need device validation.
- The Docker/Quick Tunnel/Worker gateway setup remains the existing experiment. The computer and tunnel must stay online. API development mode and shared anonymous-IP limits remain deployment constraints, not a claim of production readiness.
