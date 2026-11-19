# TMusic V2 Working Instructions

Read `codex/MASTER_PROMPT_TMUSIC.md` as historical context. Instructions in this `AGENTS.md` override older constraints where updated below.

## 1. Technology Stack

- Flutter Android/iOS.
- ASP.NET Core .NET 10 backend.
- SQL Server database only.
- Vietnamese UI, localization-ready code.
- Railway is the primary production backend.
- Preserve `.env` byte-for-byte; never print, commit, or bundle secrets.
- Support existing configuration names.
- Never migrate, drop, or reset the legacy `TMusicDev` database.
- Use `TMusicV2Dev` or isolated `TMusicV2Test_*` databases.

## 2. User Accounts and Albums

- Support Guest local mode without requiring login.
- Guest users can create albums, download audio and manage local music.
- Guest data remains local unless claimed or synchronized to an authenticated account.
- Support authenticated accounts with secure Identity password hashing, verified external tokens, refresh-token rotation and reuse detection.
- Enforce server-side ownership and optimistic concurrency.
- Support album sharing through revocable share codes or links.
- Preserve source attribution and authorization.

## 3. Audio-Only Download Policy

TMusic is an **audio-only music application**.

The application must support extracting and downloading MP3 audio from supported URLs where the user has the necessary rights or the download is legally permitted.

Supported sources:
- YouTube videos.
- YouTube Shorts.
- TikTok.
- SoundCloud.

Required behavior:
- Accept valid supported URLs.
- Retrieve actual metadata when accessible.
- Display title, creator, thumbnail and duration.
- Extract audio using yt-dlp and FFmpeg when permitted and technically accessible.
- Convert supported audio streams to MP3.
- Provide 128 kbps, 192 kbps and 320 kbps encoding options where appropriate.
- Never claim that transcoding improves original audio quality.
- Allow saving MP3 files to a selected album or outside albums.
- Support audio playback offline.
- Display download progress, errors, cancellation and retry options.
- No video download feature.
- No MP4 download option.
- No embedded YouTube video player.
- No unnecessary video-processing UI.

## 4. Railway Production Compatibility

The backend must remain deployable on Railway and support audio download jobs.

Required:
- Install compatible yt-dlp and FFmpeg versions in the deployment image.
- Validate binary availability during startup.
- Use asynchronous download jobs.
- Implement bounded concurrency and queue processing.
- Use safe temporary directories.
- Clean temporary files after processing.
- Handle application restarts and interrupted jobs.
- Avoid blocking HTTP request threads during media processing.
- Configure appropriate download timeouts and resource limits.
- Do not assume Railway's local filesystem provides persistent storage.
- Use durable storage where persistent server-side files are required.

### YouTube Download Reliability

Investigate and correctly handle:
- HTTP 403.
- HTTP 429.
- Metadata extraction failures.
- Unsupported or unavailable formats.
- yt-dlp compatibility issues.
- FFmpeg conversion failures.
- `Sign in to confirm you're not a bot`.
- Restrictions associated with hosting-provider IP addresses.

**Implementation rules:**

- Do not automatically disable YouTube downloads simply because the backend runs on Railway.
- Attempt standard, authorized extraction when the source is accessible.
- Do not bypass DRM, authentication requirements, access controls or anti-bot challenges.
- Do not use stolen or unconsented cookies, forged authentication or proxy rotation intended to evade restrictions.
- Do not present an anti-bot failure as a successful download.
- When YouTube blocks extraction, report the actual failure clearly.
- Offer retry only for potentially transient failures.
- Keep TikTok, SoundCloud and local audio functionality independent of YouTube failures.
- Do not silently replace an unsuccessful download with a fake MP3.

## 5. URL and Download Security

- Use strict provider allowlists: YouTube, TikTok and SoundCloud.
- Validate all redirects.
- Resolve and pin public DNS addresses where applicable.
- Prevent SSRF and access to internal network resources.
- Bound request sizes, processing time and download sizes.
- Sanitize filenames and metadata.
- Reject unsupported or unsafe URLs.
- Preserve original source attribution.
- Show `Nguồn: YouTube / TikTok / SoundCloud`, creator/channel and an external-open link.

## 6. Audio Player

Support:
- User-selected local audio files.
- Legitimately downloaded MP3 files.
- Offline playback.
- Background playback.
- Seek and A-B loop.
- Playback queue.
- Album organization.
- Download-progress tracking.
- Accurate file availability states.

Never upload local user media to third parties without explicit authorization.

## 7. UI/UX Design

Use a Premium Spotify-Inspired UI/UX.

**Dark Mode**
- Background: `#121212`.
- Surface: `#181818`.
- Elevated surface: `#282828`.
- Text: `#FFFFFF`.
- Accent: `#1DB954`.

Also support optional Light Mode.

Design requirements:
- Bento quick-pick grid.
- Horizontal carousels.
- Dynamic album-toned ambient headers.
- Accessible touch targets.
- Smooth, subtle animations.
- Glassmorphism reserved for the Mini-Player, navigation bar and floating sheets.
- Clear loading, success and failure states.
- Prioritize usability and balanced layouts.
- Do not include a clipboard-paste button.
- No fake controls or placeholder actions.

## 8. Data Integrity and Production Rules

- No demo credentials.
- No fake metadata.
- No sample content in production.
- No unsupported claims about provider capabilities.
- No unauthorized redistribution of copyrighted media.
- Never expose secrets through logs or API responses.
- Maintain compatibility with existing accounts and albums.
- Preserve previously downloaded audio files and their ownership records.

## 9. Testing and Verification

Before reporting completion:

1. Inspect the existing download implementation.
2. Run relevant backend and Flutter tests.
3. Verify the yt-dlp and FFmpeg integration.
4. Test MP3 downloads from authorized, accessible sources.
5. Test album assignment and local playback.
6. Test failures, cancellation and retry behavior.
7. Verify Railway deployment configuration.
8. Confirm existing functionality remains intact.
9. Record executed commands and results in `docs/BUILD_LOG.md`.

Report actual results and remaining limitations. Never claim a successful Railway YouTube download without a verified end-to-end test.

## 10. Final Development Principle

**TMusic V2 must remain an audio-only application.**

Prioritize reliable MP3 extraction, simple album management, offline playback, secure accounts and production stability.

Do not implement video download features.

Do not unnecessarily restrict permitted audio functionality because of hosting limitations.

Do not implement techniques intended to defeat YouTube access restrictions.

When a source cannot be accessed legitimately, explain the limitation accurately and preserve all unrelated application functionality.
