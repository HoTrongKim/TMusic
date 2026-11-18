# ARCHITECTURE — TMUSIC V2

## 1. Tổng Quan Kiến Trúc

TMusic V2 là ứng dụng quản lý thư viện nhạc cá nhân trên Android/iOS với **Khách cục bộ và tài khoản**, nhận liên kết YouTube/TikTok/SoundCloud và phát audio hợp lệ lưu trên máy, được xây dựng với 3 thành phần chính:

1. **Mobile Client (`mobile/`):** Flutter `3.47` (`Dart 3.13`), kiến trúc feature-first, quản lý trạng thái bằng `Riverpod`, điều hướng có bảo vệ phiên bằng `GoRouter`, lưu trữ khóa phiên bằng `FlutterSecureStorage` và bộ nhớ đệm đọc cục bộ bằng `sqflite`.
2. **Backend REST API (`backend/src/TMusic.Api`):** ASP.NET Core `.NET 10` Minimal API (`/api/v1`), tích hợp ASP.NET Core Identity, JWT ngắn hạn kèm xoay vòng refresh token, xác minh Google/Apple OIDC ID token, rate limiting, RFC 7807 Problem Details, OpenAPI và health checks.
3. **Background Worker (`backend/src/TMusic.Worker`) & SQL Server:** Xử lý playlist YouTube có checkpoint/lease, download MP3 theo quyền người dùng và dọn metadata hết hạn. API cũng có download runner mặc định; có thể tách Worker bằng cấu hình. Metadata/album/ownership thuộc SQL, audio nằm ở storage riêng.

---

## 2. Phân Tầng Backend (`backend/`)

```text
backend/
  TMusic.slnx
  src/
    TMusic.Domain/            # Thực thể nghiệp vụ thuần: Song, UserLibrarySong, Album, AlbumSong, Import*
    TMusic.Application/       # Hợp đồng dịch vụ (ISourceResolver, IYouTubePlaylist, IAccountMailSender) & SourceUrl
    TMusic.Infrastructure/    # TMusicDb (EF Core SQL Server), Migrations, SmtpAccountMailSender,
                              # SafeHttpTransport, YouTube/TikTok/SoundCloud adapters,
                              # CoverImageStorage, AudioStorage, AudioDownloadProcessor/EgressProxy, ImportProcessor
    TMusic.Api/               # Composition root, JWT/SessionService, ExternalTokenVerifier, AppleTokens,
                              # REST endpoints (/auth, /users/me, /albums, /library/songs, /songs/resolve, /search, /imports)
    TMusic.Worker/            # ImportProcessor, AudioDownloadProcessor, maintenance
  tests/
    TMusic.Tests/             # Unit, isolated SQL integration, API journey; số/kết quả theo BUILD_LOG
```

### Nguyên tắc ranh giới

- **Tài khoản có server-authoritative metadata:** Album, bài cá nhân và quyền truy cập thuộc `UserId` từ JWT claim `sub`. Khách dùng `GuestLibraryStore` trong sandbox, không cần tài khoản; cài đặt và media local được tách theo owner trên thiết bị. Gỡ app trước khi sync làm mất dữ liệu khách.
- **Chia sẻ có kiểm soát:** Mã chia sẻ do chủ album tạo/thu hồi cho phép preview/import metadata. Người nhận có album riêng và source attribution; mã không cấp quyền đọc tệp audio/cover riêng của chủ nguồn.
- **Tách biệt danh tính nguồn và dữ liệu người dùng:**
  - `Song` biểu diễn nguồn nội dung bên ngoài (`Provider` + `ProviderContentId` theo collation nhị phân phân biệt hoa-thường `Latin1_General_100_BIN2`) cùng metadata tạm thời có thời hạn (`MetadataExpiresAt`).
  - `UserLibrarySong` biểu diễn bản lưu cá nhân của từng người dùng (`CustomTitle`, `CustomArtist`, `IsFavorite`, `OriginalUrl`, `Version`). Khi metadata nguồn hết hạn hoặc video bị ẩn, `CustomTitle` và liên kết của người dùng vẫn được giữ nguyên.
  - `AlbumSong` biểu diễn quan hệ nhiều-nhiều có thứ tự (`Position`) và khóa ngoại phức hợp đảm bảo bài hát của User A không bao giờ gắn được vào album của User B.

---

## 3. Kiến Trúc Mobile (`mobile/lib/`)

```text
mobile/lib/
  main.dart
  app/
    tmusic_app.dart           # GoRouter (23 màn hình), auth redirect guard, lifecycle sync, share channel
    theme/
      tmusic_theme.dart       # Spotify-inspired charcoal/green (Dark mặc định + Light)
  core/
    api/api_client.dart       # Dio client, GET retry backoff, multipart upload, private bytes fetch
    auth/
      session.dart            # SessionController, FlutterSecureStorage, single-flight token rotation
      external_auth.dart      # Google Sign-In & Sign in with Apple client adapters
    localization/app_copy.dart# Đa ngôn ngữ tập trung (Tiếng Việt mặc định + Tiếng Anh)
    storage/library_cache.dart# SQLite read-through snapshot cache & Recently Opened Albums theo user
    storage/guest_library_store.dart # Guest metadata local; claim/sync khi đăng nhập
    widgets/common.dart       # Cover, ProviderBadge, CapabilityBadge, OfflineBanner, Notice, EmptyView
  features/
    authentication/           # Splash, Welcome, Login, Register, Verify Email, Forgot & Reset Password
    home/                     # Greeting, Quick Add URL, Recently Added, Favorite/Recent Albums, Continue Organizing
    library/                  # LibraryRepository + LibraryScreen (Albums/Songs, Grid/List, Sort, Filter, Reorder)
    albums/                   # AlbumEditor (Create/Edit + Cover Upload), AlbumDetailScreen, ExistingSongPicker
    link_import/              # AddLinkScreen, SongMetadataPreviewCard, Playlist ImportsScreen & Status
    songs/                    # SongScreen (Playback/Content Viewer) & EditSongScreen
    playback/                 # LocalLibrary/LocalAudio (just_audio + audio_service), downloads, native player; không WebView
    search/                   # Unified SearchScreen (/api/v1/search) & FavoritesScreen
    profile/                  # Profile, Settings, Account Security, Manage Sessions, Delete Account Flow, Privacy
    settings/                 # SettingsController đồng bộ theme & ngôn ngữ với server
    share/                    # SharedLinks parser & MethodChannel cho Android Intent / iOS ShareExtension
  shared/
    models/library_models.dart# PageResult, Album, Song, LinkPreview, SearchResults
```

---

## 4. Chiến Lược Đồng Bộ Đa Thiết Bị (Synchronization)

1. **Server-authoritative account writes:** Các thao tác thư viện tài khoản xác nhận SQL; khách ghi local. Login thử chuyển metadata khách và sao chép MP3 sang vùng tài khoản trên cùng máy, giữ dữ liệu khách khi sync lỗi; không tự upload media cá nhân.
2. **Optimistic Concurrency (`ETag` / `If-Match`):** Các bản ghi `Album` và `UserLibrarySong` dùng `rowversion` (`Version`). Mọi yêu cầu `PATCH` / `DELETE` bắt buộc gửi `If-Match`; nếu thiếu trả `428 Precondition Required`, nếu xung đột giữa hai thiết bị trả `409 Conflict`.
3. **Idempotency:** Tạo album (`POST /api/v1/albums`) và gửi tác vụ nhập playlist (`POST /api/v1/imports`) hỗ trợ `Idempotency-Key` kèm kiểm tra SHA-256 fingerprint của payload.
4. **Refresh on Foreground & Navigation:** Khi ứng dụng quay lại foreground (`AppLifecycleState.resumed`), `libraryRevisionProvider` kích hoạt tải mới dữ liệu từ server.
5. **Read-through Local Cache & Offline Notice:** Trang đầu của danh sách album/bài hát được lưu snapshot vào SQLite riêng của từng `userId`. Nếu mất kết nối mạng, ứng dụng chỉ hiển thị bản lưu gần nhất ở chế độ chỉ đọc kèm thanh trạng thái **`OfflineBanner`** rõ ràng; khi đăng xuất hoặc xóa tài khoản, phiên và bộ nhớ đệm được dọn sạch.
