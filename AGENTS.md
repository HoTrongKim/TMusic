# TMusic V2 working instructions

Read `codex/MASTER_PROMPT_TMUSIC.md` as historical context; instructions in this `AGENTS.md` override older constraints where updated below.

- Flutter Android/iOS, ASP.NET Core .NET 10, SQL Server only; Vietnamese UI, localization-ready code.
- Preserve `.env` byte-for-byte; never print, commit, or bundle secrets. Support the existing configuration names.
- Never migrate, drop, or reset the legacy `TMusicDev` database. Use `TMusicV2Dev` or isolated `TMusicV2Test_*` databases.
- Support both Guest local mode (users can create albums and manage music locally on device without login; local-only guest data is lost on app uninstall unless the user logs in to sync/claim it to their account) and authenticated accounts (Identity password hashing, verified external tokens, refresh rotation/reuse detection, server-side ownership, optimistic concurrency).
- Support album sharing between TMusic users via revocable share codes/links while preserving server-side authorization and source attribution.
- No fake demo credentials, fake metadata, sample content in production, placeholder controls, or claims unsupported by tests. Do not include a clipboard-paste button.
- Resolve exact provider allowlists (YouTube, TikTok, SoundCloud), validate every redirect, pin public DNS addresses, bound requests, and clearly display original video/audio source attribution (`Nguồn: YouTube / TikTok / SoundCloud`, creator/channel, and external-open link).
- Enable MP3 audio extraction and downloads from supported YouTube, TikTok, and SoundCloud URLs where the user holds the necessary rights or where legally permitted, using `yt-dlp` and `FFmpeg` without bypassing DRM, login walls, or anti-bot gates. Support managing download jobs and saving downloaded audio to albums.
- Native audio supports user-selected local audio files and legitimately downloaded MP3 files with offline playback, background playback, seek/A-B loop, and download progress tracking. Never upload user media to third parties without explicit authorization.
- Use a Premium Spotify-Inspired UI/UX: Dark Mode (`#121212` / `#181818` / `#282828` charcoal surfaces, crisp `#FFFFFF` text, `#1DB954` green accent) + optional Light Mode, Bento quick-pick grid, horizontal carousels, dynamic album-toned ambient headers, accessible touch targets, and subtle glassmorphism reserved only for the Mini-Player, navigation bar, and floating sheets.
- Run meaningful tests/builds, record commands/results in `docs/BUILD_LOG.md`, and report device/credential gaps accurately.