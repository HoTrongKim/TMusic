# TMusic completion and reliability plan

**Goal:** Complete broken existing user flows against the current `AGENTS.md` without redesigning the application.
**Spec:** `AGENTS.md`; the master prompt and older reports are historical where they conflict.
**Architecture:** Retain Flutter/Riverpod, ASP.NET Core .NET 10, and SQL Server. Fix each boundary in its current layer, with regression tests before production changes.

## Constraints

- Preserve `.env` byte-for-byte; never read its values into output or bundle it.
- No access, migration, reset or deletion of legacy `TMusicDev`; SQL tests use generated `TMusicV2Test_*` only.
- No media uploads or authentication/DRM/anti-bot bypass; keep source attribution.
- This folder has no Git metadata. Work in place; no fabricated commits/worktrees.

## Tasks and ownership

- [x] Inspect code and current instructions; delegate independent backend/mobile/infrastructure audits.
- [x] Backend: regression tests for cancel/remove while extraction is running; revoke lease and prevent stale publication; enforce extractor egress allowlists/public DNS pinning with bounded transport.
- [x] Mobile library: show and manage guest download jobs, surface extraction failures with retry; claim device-imported files without uploading them or discarding originals on failure.
- [x] Mobile session/settings: regressions for refresh completing after logout/account switch, corrupt stored session, settings persistence and system theme restoration; fix within existing controllers.
- [x] Documentation/runtime packaging: align current behavior with README/provider/implementation docs; verify container tool prerequisites without claiming an unrun Docker build.
- [x] Run combined backend SQL tests, Flutter analyze/tests and Android debug build; review changes and append exact results/remaining device and credential limits to `docs/BUILD_LOG.md`.

## Review focus

- Late asynchronous responses must not restore a logged-out session or erase a newly signed-in account.
- Cancellation during extraction/publication must not restore deleted media.
- Guest failures must remain visible and retryable; failed claim must retain local data.
- Settings must survive controller/application recreation and offline use.
- Metadata safety must not be misrepresented as extraction-network safety; external service/device tests must be distinguished from fakes.

## Baseline (18 November 2026)

Baseline passed: Flutter static analysis, 28 Flutter tests, and 100 real-SQL backend tests. Final combined verification passed 138 backend tests with zero skipped and 55 Flutter tests. Flutter analysis, backend Release build/NuGet audit and final Android debug APK succeeded. Exact commands/results are in BUILD_LOG. Original `.env` hash is unchanged; its content was never printed.

## Follow-up review

- [x] Shared album preview pagination/import completeness beyond 200 songs, preserving source IDs and case-sensitive guest identity.
- [x] Separate local-only FFmpeg conversion from download-only yt-dlp to prevent unguarded ffprobe input; exercise real WAV/M4A conversion and malicious playlists.
- [x] Fence download refresh/poll/device-copy callbacks across account changes; preserve same-owner MP3 transfers during polling. Three isolation regressions and final full Flutter suite/build passed.

## Verification and product limits

No real provider URL was downloaded; OAuth credentials/device behavior, Android background/lock-screen playback, iOS builds and Docker image builds remain unverified. Raw imported local files have a separate device library; full album/favorite/rename integration is not added in this repair pass. Guest extraction still uses the synchronous endpoint and does not offer granular server-side progress/cancellation. External-provider-only accounts still need a password to link another provider; supporting provider reauthentication for this requires a separate credential-linking flow.
