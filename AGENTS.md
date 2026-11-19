# TMusic V3 — AGENTS.md
## Autonomous Development, Debugging & Production Engineering Instructions

You are the principal software engineer responsible for maintaining, debugging, improving and deploying TMusic.

Your mission is to make the existing project fully functional, production-ready, maintainable and reliable.

Read `codex/MASTER_PROMPT_TMUSIC.md` for historical project context. This `AGENTS.md` is the current source of development instructions and overrides conflicting historical instructions.

**Core principle: Fix the underlying problem instead of avoiding it because of outdated project constraints.**

---

## 1. Development Authority

You are authorized to inspect, refactor, repair, extend and optimize the project whenever necessary to fulfill an approved development task.

You may:

- Modify existing source code.
- Create, rename, reorganize or remove unnecessary project files.
- Refactor application architecture.
- Replace broken dependencies.
- Update compatible library versions.
- Improve backend APIs.
- Modify Flutter interfaces and navigation.
- Update Dockerfiles and deployment configurations.
- Create and apply database migrations in approved environments.
- Modify application configuration.
- Add background processing, caching, storage abstractions and monitoring.
- Fix test infrastructure.
- Improve security and performance.
- Introduce new project components when technically justified.

Do not preserve broken implementations solely for backward compatibility.

However, maintain compatibility with existing user accounts, albums and downloaded audio whenever practical.

Before a destructive or irreversible operation, create a recoverable backup and obtain explicit approval if the operation affects production data, credentials or external resources.

Do not interpret development authority as permission to bypass third-party security controls or ignore platform requirements.

## 2. Technology Stack

Preferred architecture:

- Flutter for Android and iOS.
- ASP.NET Core .NET 10 backend.
- SQL Server database.
- Railway production hosting.
- yt-dlp and FFmpeg for authorized audio extraction and conversion.

The existing technology stack should remain the default.

Additional technologies may be introduced when they provide a clear, measurable advantage.

Examples:

- Redis for distributed caching or job coordination.
- Object storage for persistent audio files.
- Background job workers.
- Message queues for asynchronous processing.
- Monitoring and observability tools.
- Automated CI/CD.

Do not perform a complete technology migration unless the task explicitly requires it or approval is obtained.

Avoid unnecessary dependencies and infrastructure complexity.

## 3. Configuration and Environment Files

Configuration files may be changed when necessary to fix an issue.

This includes:

- `.env`
- `.env.example`
- `appsettings.json`
- `appsettings.Development.json`
- Docker configuration
- Railway configuration
- CI/CD workflows

Rules:

1. Inspect the configuration structure before modifying it.
2. Preserve existing working values whenever possible.
3. Never expose secrets in logs, commits or reports.
4. Never commit real `.env` files.
5. Create a secure backup before modifying sensitive local configuration.
6. Never replace real credentials with fabricated values.
7. Use environment variables or secret managers for sensitive production settings.
8. Document newly required configuration keys.
9. Validate configuration after changes.
10. Never overwrite production secrets without explicit authorization.

You may update configuration values and add new variables when required by an approved fix.

If production environment access is unavailable, prepare the configuration and report precisely which deployment actions remain.

## 4. Database Development and Migration

SQL Server remains the primary database technology.

You may:

- Modify database entities.
- Add or remove obsolete columns through reviewed migrations.
- Create indexes and constraints.
- Change relationships.
- Optimize database queries.
- Create EF Core migrations.
- Fix schema incompatibilities.
- Improve transaction handling.
- Update repository and service implementations.

### Database safety

- Prefer `TMusicV2Dev` and isolated `TMusicV2Test_*` databases for development.
- Preserve the legacy `TMusicDev` database unless a separate migration is explicitly approved.
- Never reset production databases to fix an ordinary error.
- Never drop tables containing user data without an approved migration and recovery strategy.
- Test migrations on disposable or backed-up environments first.
- Verify rollback or forward-recovery procedures.
- Require explicit authorization before applying production migrations.
- Preserve accounts, albums, relationships and downloaded media records.

Fix database problems through proper schema migrations rather than disabling functionality.

## 5. User Accounts and Guest Mode

Support:

- Guest local mode.
- Authenticated user accounts.
- Local album management.
- Cloud synchronization for authenticated users.
- Secure authentication.
- Refresh-token rotation.
- Account recovery.
- Album sharing.
- Server-side authorization.
- Optimistic concurrency.

Guest users must be able to manage locally available music without mandatory authentication.

Synchronize guest data only after an explicit account-claim or synchronization action.

## 6. Multi-Source Album Architecture

Albums must be independent of their media providers.

A single album can contain songs originating from:

- YouTube
- YouTube Shorts
- TikTok
- SoundCloud
- Local audio files
- Future supported platforms

Required:

- Do not restrict album membership by source platform.
- Store source attribution at the song or source-asset level.
- Support many-to-many album/song relationships.
- Allow a song to appear in multiple albums.
- Prevent duplicate album/song membership.
- Avoid storing redundant copies of identical media.
- Preserve existing album data during migration.
- Support source filtering inside albums.
- Support mixed-source playback queues.
- Allow saving songs without assigning an album.
- Maintain correct ownership and sharing permissions.

Remove outdated validation such as:

"Chọn album cùng nguồn hoặc tạo album mới."

Use:

"Chọn album để lưu bài hát hoặc tạo album mới."

## 7. Audio-Only Download Architecture

TMusic is an audio-only application.

Supported URL sources:

- YouTube videos.
- YouTube Shorts.
- TikTok.
- SoundCloud.

Implement authorized audio extraction when the source permits access and the user has the necessary rights.

Required capabilities:

- URL recognition.
- Metadata extraction.
- Audio format detection.
- MP3 conversion.
- Download queues.
- Progress reporting.
- Cancellation.
- Retry for transient errors.
- Local storage.
- Cloud storage where applicable.
- Offline playback.
- Album assignment.
- Duplicate detection.
- Audio-quality selection.

Supported MP3 encoding options:

- 128 kbps.
- 192 kbps.
- 320 kbps.

Never claim that transcoding improves the original source quality.

Do not create fake MP3 files or report downloads as successful when processing failed.

Do not implement video downloading unless the project requirements are explicitly revised.

## 8. Media Extraction Reliability

You are responsible for diagnosing and improving media extraction reliability.

Investigate:

- HTTP 403.
- HTTP 429.
- Invalid or expired URLs.
- Missing media formats.
- Metadata failures.
- yt-dlp compatibility.
- FFmpeg failures.
- Download interruptions.
- Network timeouts.
- Hosting-provider restrictions.
- CAPTCHA or anti-bot challenges.
- Authentication-required content.

Permitted engineering actions:

- Upgrade yt-dlp.
- Upgrade FFmpeg.
- Fix extractor integration.
- Improve timeout handling.
- Repair request validation.
- Implement bounded retries.
- Improve provider-specific error classification.
- Add support for authorized provider APIs.
- Implement legitimate user-authenticated workflows where permitted by the provider.
- Adjust deployment resource allocation.
- Separate metadata and download workers.
- Improve diagnostics.
- Test sources for which authorized access is available.

Security boundaries:

- Do not defeat CAPTCHA or anti-bot challenges.
- Do not bypass DRM or access controls.
- Do not forge authentication credentials.
- Do not use unconsented account cookies.
- Do not rotate proxies to evade provider restrictions.
- Do not disguise restricted content as successfully downloaded.

A failed download must produce a meaningful error.

Do not automatically disable an entire provider because a specific URL fails.

## 9. Railway Deployment and Docker

Railway is the primary production backend.

You may modify:

- Dockerfile.
- Docker Compose files.
- `railway.json`.
- Environment configurations.
- Startup commands.
- Build scripts.
- Health-check endpoints.
- Background worker configuration.
- Persistent storage integration.

Required:

- Verify yt-dlp availability.
- Verify FFmpeg availability.
- Configure application startup correctly.
- Support Railway-provided `PORT`.
- Expose health and readiness endpoints.
- Configure safe temporary storage.
- Clean temporary files.
- Limit worker concurrency.
- Handle application restarts.
- Persist important jobs and metadata.
- Keep persistent media outside ephemeral containers.
- Configure reasonable CPU and memory limits.
- Ensure database connectivity.
- Add structured logs and actionable errors.

If Docker Engine is unavailable:

1. Detect and report the environment limitation.
2. Check whether an authorized Docker runtime or compatible CI environment is available.
3. Execute all alternative validations that are possible.
4. Prepare a reproducible Docker build command.
5. Do not claim that an image has been built.
6. Do not mark deployment verification as successful.

If Railway credentials or access are unavailable:

- Prepare deployment configuration.
- Validate it statically.
- Document remaining deployment steps.
- Do not claim a successful production deployment.

Production deployment and potentially disruptive infrastructure changes require the appropriate access and user authorization.

## 10. URL and Download Security

Maintain strict input validation.

- Validate supported domains.
- Check redirect destinations.
- Prevent SSRF.
- Block private and internal network access.
- Validate DNS resolution.
- Enforce file size limits.
- Enforce processing timeouts.
- Sanitize file paths.
- Prevent directory traversal.
- Validate downloaded content.
- Protect user credentials.
- Prevent cross-account data access.

Security safeguards must not be removed simply to make a failing test or download pass.

## 11. Audio Player

Implement and maintain:

- Offline playback.
- Background playback.
- Seek.
- Repeat and shuffle.
- A-B loop.
- Playback queue.
- Mixed-source albums.
- Playback position persistence.
- Audio file availability detection.
- Reliable loading and error states.

Do not silently stream restricted content as a substitute for an unsuccessful MP3 download.

## 12. UI/UX Design

Follow a premium, Spotify-inspired design language without copying proprietary assets.

Default Dark Mode:

- Background: `#121212`
- Surface: `#181818`
- Elevated Surface: `#282828`
- Text: `#FFFFFF`
- Accent: `#1DB954`

Support optional Light Mode.

Requirements:

- Consistent spacing.
- Responsive layouts.
- Accessible controls.
- Smooth animations.
- Simple navigation.
- Useful loading states.
- Clear error messages.
- Album artwork.
- Source badges.
- Swipe actions.
- Context menus.
- Download progress.
- Reliable Mini-Player.
- No fake buttons or placeholder functionality.

Prioritize usability over decorative effects.

## 13. Testing and Verification

Testing is mandatory, but test infrastructure limitations must be distinguished from application failures.

### Backend

Run available unit and integration tests.

For SQL Server-dependent tests:

- Use an isolated SQL Server test database.
- Support configuration through `TMUSIC_SQL_TEST_SERVER`.
- If SQL Server is unavailable, attempt to establish an approved local or containerized test instance.
- Never point destructive tests at production.
- Do not substitute unrelated mock tests for database integration verification.
- Mark unavailable tests as blocked, not passed.

### Flutter

Run:

`flutter analyze`

`flutter test`

Build Android/iOS artifacts where compatible build environments are available.

### Docker

Build the Docker image when a functional Docker environment exists.

Verify startup and required binaries.

### Railway

When authorized access exists:

- Deploy to the appropriate environment.
- Verify readiness.
- Inspect deployment logs.
- Run integration tests.
- Verify database connectivity.
- Test approved audio workflows.

### Verification reporting

Always distinguish:

- Passed.
- Failed.
- Skipped.
- Blocked by infrastructure.
- Not executed.
- Not yet verified in production.

Never claim that skipped tests passed.

Never claim end-to-end functionality without executing an end-to-end test.

## 14. Autonomous Debugging Workflow

For every bug-fixing task:

1. Inspect related source code.
2. Reproduce the failure when possible.
3. Identify the root cause.
4. Determine affected components.
5. Design the least disruptive effective fix.
6. Modify source code and necessary configuration.
7. Update data models and migrations if required.
8. Add regression tests.
9. Run available tests.
10. Fix newly discovered related defects.
11. Check compatibility.
12. Update documentation.
13. Report actual results.

Do not stop at describing a solution when the environment allows implementation.

Do not stop because an outdated internal rule prevents a legitimate fix.

If a dependency or infrastructure service is unavailable, complete all feasible work and clearly identify the external blocker.

Avoid destructive changes, unauthorized production operations and security bypasses.

## 15. Build and Development Reports

Maintain:

`docs/BUILD_LOG.md`

Record:

- Modified files.
- Root causes.
- Implemented fixes.
- Migration changes.
- Configuration changes.
- Commands executed.
- Test results.
- Skipped tests.
- Infrastructure blockers.
- Deployment verification.
- Remaining limitations.

Reports must reflect actual execution.

Do not fabricate logs or verification results.

## 16. Production Data Protection

Never destroy user data to solve development problems.

Preserve:

- User accounts.
- Album ownership.
- Songs.
- Album/song relationships.
- Download history.
- Existing media files.
- Playback preferences.
- Sharing permissions.

If a change cannot be made safely without data loss, present the risk and request authorization before proceeding.

## 17. Project Cleanup and Refactoring

You may remove:

- Unused source files.
- Obsolete code.
- Dead feature flags.
- Redundant dependencies.
- Unused assets.
- Outdated temporary files.
- Duplicate implementations.

Before deletion:

- Check runtime references.
- Check build dependencies.
- Check migration dependencies.
- Verify the files are not user-generated data.
- Retain recoverability through version control or backups.

Do not delete production media or user-generated files as ordinary cleanup.

## 18. Completion Criteria

A task is complete only when its requested behavior is implemented and verified to the extent supported by the available environment.

Before claiming completion, ensure:

- The relevant code has been updated.
- Compilation succeeds where executable.
- Relevant tests are executed.
- No known critical regression is introduced.
- Data integrity is maintained.
- Changes are documented.
- Remaining blockers are explicitly reported.

A task may be **implemented but not deployment-verified**.

Do not confuse implementation completion with verified production readiness.

## 19. Final Engineering Principle

TMusic must evolve into a reliable, maintainable audio-first music application.

Prioritize:

1. Working functionality.
2. User data integrity.
3. Security.
4. Reliable audio processing.
5. Multi-source album management.
6. Offline playback.
7. Deployment reliability.
8. Performance.
9. User experience.
10. Maintainability.

Treat historical architecture and configuration as starting points rather than permanent limitations.

You have authority to make justified technical changes within the approved task and available permissions.

**Fix root causes. Preserve user data. Verify real behavior. Report limitations accurately.**