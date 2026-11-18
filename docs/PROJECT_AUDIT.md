# PROJECT AUDIT — TMUSIC V2

## Đối chiếu bổ sung ngày 18/11/2026

Phần audit ngày 17/11 bên dưới là lịch sử; yêu cầu hiện hành lấy từ `AGENTS.md`, bao gồm Guest Local Mode, SoundCloud, chia sẻ album và tải MP3 có xác nhận quyền.

Đợt đọc mã hiện tại tái hiện và sửa các lỗi: hủy tác vụ đang chạy vẫn công bố MP3; yt-dlp chưa có egress gate; guest không thấy/quản lý được tải lỗi; tệp nhập trên máy không được claim khi đăng nhập; gộp nhầm ID YouTube khác hoa/thường; album chia sẻ bị cắt ở 200 bài; snapshot guest báo trang kế không tồn tại; phản hồi refresh/login cũ ghi đè phiên mới; settings khách không bền vững và bỏ qua lựa chọn theme hệ thống. Các lỗi phản hồi bất đồng bộ được kiểm tra thêm bằng yêu cầu tạm dừng có điều khiển.

Backend mới dùng CONNECT gate kiểm tra provider/CDN và DNS công khai, pin địa chỉ mỗi kết nối; yt-dlp chỉ tải bằng native downloader, FFmpeg được gọi riêng với whitelist protocol/demuxer cho tệp cục bộ. Có hạn mức hai extraction đồng thời, deadline, giới hạn output/bytes và kiểm thử FFmpeg thật bằng WAV/M4A tổng hợp. Không có xác nhận rằng tải media ngoài thực tế hoặc OAuth trên thiết bị đã được thử trong đợt này.

Build/test và giới hạn còn lại phải đọc tại phần mới nhất của `BUILD_LOG.md`. Không coi các số đếm hoặc tuyên bố hoàn tất lịch sử bên dưới là kết quả của revision hiện hành. `.env` giữ nguyên; không áp dụng migration lên database dev/legacy trong đợt này, SQL tests chỉ dùng `TMusicV2Test_*`.

---

**Ngày thực hiện audit:** 17/11/2026 (Asia/Bangkok)  
**Workspace hiện tại:** `E:\PROJECT\TMusic_Starter`  
**Bản sao lưu dự án cũ (V1):** `E:\PROJECT\TMusic_Starter_backup_20261117_before_v2`

---

## 1. Bảo toàn dữ liệu, `.env` và cơ sở dữ liệu cũ

- Yêu cầu trực tiếp của người dùng là viết lại dự án sang TMusic V2, **chỉ giữ nguyên file `.env` của dự án cũ**.
- File `.env` tại thư mục gốc được bảo toàn **byte-for-byte** (1.800 bytes, SHA-256: `5EFA5C78DEEDF3910259D261FC039AFAAF72F86EA259D210C56BA015C7A420F7`). Không có giá trị secret nào được in ra log, tài liệu hoặc mã nguồn.
- Trên SQL Server cục bộ (`lpc:.\SQLEXPRESS`), database cũ `TMusicDev` được giữ nguyên hoàn toàn (không migrate đè, không xóa, không reset).
- TMusic V2 chỉ làm việc trên database riêng `TMusicV2Dev` (môi trường dev) và các database cô lập `TMusicV2Test_<guid>` (môi trường integration test, tự động dọn dẹp có kiểm tra tên chính xác sau khi chạy xong).

---

## 2. Cấu trúc Repository & Stack Công Nghệ

### 2.1. Cấu trúc tổng thể

- **`backend/`** — Giải pháp `.NET 10` (`TMusic.slnx`) theo kiến trúc phân tầng thực dụng:
  - `src/TMusic.Domain` — Thực thể nghiệp vụ (`Song`, `UserLibrarySong`, `Album`, `AlbumSong`, `ImportPreview`, `ImportCandidate`, `ImportJob`, `ProviderQuotaWindow`).
  - `src/TMusic.Application` — Hợp đồng dịch vụ (`ISourceResolver`, `IYouTubePlaylist`, `IAccountMailSender`) và bộ phân tích URL an toàn (`SourceUrl`).
  - `src/TMusic.Infrastructure` — `TMusicDb` (`IdentityDbContext<Account, IdentityRole<Guid>, Guid>` trên SQL Server), migrations EF Core, `SmtpAccountMailSender`, `SafeHttpTransport` (chống SSRF + pin DNS công khai), `YouTubeApi`, `TikTokRedirectResolver`, `TikTokMetadata`, `ProviderQuota`, `ImportProcessor`.
  - `src/TMusic.Api` — ASP.NET Core Minimal API (`/api/v1`), JWT ngắn hạn + xoay vòng refresh token, xác minh Google/Apple OIDC ID token, rate limiting, RFC 7807 Problem Details, OpenAPI, health checks (`/health`, `/health/ready`).
  - `src/TMusic.Worker` — Background worker xử lý hàng đợi import playlist YouTube và dọn dẹp metadata hết hạn mà vẫn bảo toàn bookmark/custom title của người dùng.
  - `tests/TMusic.Tests` — Unit tests và SQL Server integration tests thực tế (`WebApplicationFactory<Program>`).
- **`mobile/`** — Ứng dụng Flutter (`Flutter 3.47.0`, `Dart 3.13.0`) tổ chức theo hướng feature-first:
  - `lib/app/` — Cấu hình `MaterialApp.router`, `GoRouter` guard bắt buộc đăng nhập, design tokens `TMusicTheme`.
  - `lib/core/` — `ApiClient` (`Dio`), `SessionController` (`FlutterSecureStorage`), `ExternalAuth` (`google_sign_in`, `sign_in_with_apple`), đa ngôn ngữ (`vi`/`en`), thành phần UI dùng chung.
  - `lib/features/` — `authentication`, `home`, `library`, `albums`, `songs`, `link_import`, `playback`, `profile`, `settings`, `share`.
  - `android/` — Android native (`Kotlin`, `AudioServiceActivity`, `ACTION_SEND` / `ACTION_SEND_MULTIPLE` share intent).
  - `ios/` — iOS native (`Swift`, `AppDelegate`, `ShareExtension` qua App Group `group.com.tmusic.tmusic`, Sign in with Apple entitlements).
- **Hạ tầng & Công cụ:**
  - `Dockerfile`, `Worker.Dockerfile`, `docker-compose.yml` (SQL Server 2022 + Mailpit profile `dev-mail`).
  - `.github/workflows/ci.yml` — Pipeline CI cho backend (.NET 10 + SQL Server container + NuGet audit) và mobile (Flutter analyze, test, debug APK).
  - `scripts/Load-Environment.ps1` & `scripts/dev.ps1` — Script nạp `.env` an toàn (không `Invoke-Expression`, không in secret) và chuyển process sang `TMusicV2Dev`.

### 2.2. Thư viện & Phiên bản chính

- **Backend:** `.NET SDK 10.0.300`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.*`, `Microsoft.EntityFrameworkCore.SqlServer 10.0.*`, `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.*`, `Microsoft.IdentityModel.Protocols.OpenIdConnect`, `xUnit v3`.
- **Mobile:** `flutter_riverpod ^3.5.0`, `dio ^5.11.1`, `go_router ^18.1.4`, `flutter_secure_storage ^11.2.0`, `google_sign_in ^7.2.0`, `sign_in_with_apple ^8.2.0`, `url_launcher ^6.3.3`, `file_selector ^1.1.0`, `path_provider ^2.1.6`, `sqflite ^2.4.6`, `just_audio ^0.10.6`, `audio_service ^0.18.19`, `audio_session ^0.2.4`.

---

## 3. Kết quả Chạy Kiểm Tra Baseline Trước Khi Hoàn Thiện

| Hạng mục kiểm tra | Lệnh thực thi | Kết quả thực tế |
|---|---|---|
| Build Backend | `dotnet build backend/TMusic.slnx -c Release` | **Thành công** (0 warnings, 0 errors) |
| Backend Unit + SQL Tests | `dotnet test backend/TMusic.slnx -c Release --no-build` (với `TMUSIC_SQL_TEST_SERVER`) | **83/83 passed** trên database test cô lập `TMusicV2Test_*` |
| Phân tích tĩnh Flutter | `flutter analyze` | **1 lint issue** tại `test/widget_test.dart:35:7` (`curly_braces_in_flow_control_structures`) |
| Kiểm thử Widget Flutter | `flutter test` | **10 passed, 2 FAILED**: lỗi tràn bố cục `RenderFlex overflowed by 9.2 pixels on the right` tại tab `library` ở màn hình hẹp `320dp` với tỷ lệ chữ `200%` (cả Light & Dark) |
| Môi trường Android | `flutter doctor -v` & `adb devices -l` | Có Android SDK 36.0.0 (`E:\Android\Sdk`), JDK 25, máy ảo `emulator-5554` (Android 16 API 36) đang hoạt động; cần xác nhận giấy phép SDK nếu Gradle yêu cầu |
| Môi trường iOS | Kiểm tra hệ điều hành Windows 11 | Đã có cấu hình Xcode project & `ShareExtension`, nhưng không thể biên dịch/ký native iOS trên Windows (cần macOS + Xcode) |

---

## 4. Đánh Giá Chi Tiết: Chức Năng Đã Có vs Lỗi, Thiếu Sót & Rủi Ro

### 4.1. Những phần nền tảng đã hoạt động tốt (giữ lại và củng cố)

1. **Xác thực cốt lõi & bảo mật phiên:** ASP.NET Core Identity PBKDF2 password hashing, khóa tài khoản tạm thời khi sai mật khẩu nhiều lần, JWT ngắn hạn (10 phút) kiểm tra phiên sống theo `FamilyId`, xoay vòng refresh token SHA-256 kèm phát hiện dùng lại (reuse detection thu hồi toàn bộ chuỗi phiên), xác minh chữ ký/issuer/audience/nonce cho Google & Apple, chặn tự động gộp tài khoản qua email chưa chứng minh mật khẩu.
2. **Cô lập dữ liệu tài khoản & chống IDOR:** Composite foreign key `(AlbumId, UserId)` và `(UserLibrarySongId, UserId)` ở tầng SQL Server ngăn gắn bài hát của User A vào album của User B ngay cả ở mức cơ sở dữ liệu; optimistic concurrency qua `rowversion` + `If-Match` (`428` / `409`).
3. **Chống SSRF & bảo vệ vòng đời bookmark:** `SafeHttpTransport` chỉ kết nối IPv4 công khai trên cổng `443` với allowlist hostname cố định, kiểm tra từng bước redirect của TikTok shortlink (tối đa 5 bước), giới hạn kích thước JSON trả về, và tách `UserLibrarySong.CustomTitle` khỏi metadata có thời hạn của `Song`.

### 4.2. Các lỗi thực tế và chức năng còn thiếu cần khắc phục ngay

1. **Lỗi giao diện tràn khung hình (`RenderFlex overflow`) trên Flutter:**
   - Màn hình `LibraryScreen` bị tràn `9.2px` bên phải ở độ rộng `320dp` khi phóng chữ `200%`.
   - Nguyên nhân: Các điều khiển lọc/tạo mới chưa giới hạn co giãn văn bản (`Flexible` / `TextOverflow.ellipsis`) trên màn hình nhỏ.
2. **Thiếu chuẩn hóa đầy đủ Schema Database theo Phần 8:**
   - Bảng `Users` (`Account`) chưa có `AvatarUrl` và `DeletedAt`.
   - Chưa có bảng `UserAuthProviders` lưu tường minh `Id`, `UserId`, `Provider`, `ProviderSubject`, `LinkedAt` (hiện mới chỉ dựa vào `AspNetUserLogins` không có mốc thời gian liên kết `LinkedAt`).
3. **Thiếu Upload & Bảo mật Ảnh Bìa Album (Phần 5.1 & Phần 12):**
   - Backend mới chỉ cho phép 4 chuỗi màu tĩnh (`cover:navy`, `cover:blue`, `cover:green`, `cover:terracotta`), chưa có API upload ảnh bìa thật với kiểm tra magic bytes (JPEG/PNG/WebP), giới hạn dung lượng, loại bỏ metadata không an toàn (EXIF/XMP/comment chunks), đặt tên file ngẫu nhiên an toàn và phân quyền truy cập theo chủ sở hữu.
4. **Thiếu sót ở Tích hợp Provider & Cấu hình (Phần 4 & Phần 15):**
   - `ExternalTokenVerifier` chưa đọc các biến chuẩn Phần 15: `TMUSIC_GOOGLE_WEB_CLIENT_ID`, `TMUSIC_GOOGLE_ANDROID_CLIENT_ID`, `TMUSIC_GOOGLE_IOS_CLIENT_ID`.
   - `TikTokMetadata` bỏ qua `thumbnail_url` từ phản hồi TikTok oEmbed (luôn gán `null`).
   - Endpoint `POST /api/v1/songs/resolve` mới trả `existingLibrarySongId`, chưa trả chi tiết bài hát đã tồn tại (`customTitle`, `customArtist`, `albumIds`) để màn hình Thêm bài hiển thị đầy đủ bài hiện có khi trùng link (Phần 5.4).
   - Script `scripts/dev.ps1` đọc `.env` cũ (vốn đặt `Features__EnableTikTokVideoImport=false` từ bản V1) khiến môi trường dev V2 mặc định từ chối link TikTok nếu không ghi đè biến môi trường trong tiến trình.
5. **Thiếu sót ở Kiến trúc Màn hình Mobile, Embed Player & Đồng bộ (Phần 6, 7, 10, 11):**
   - **Design System (`TMusicTheme`):** `surface` và `scaffoldBackgroundColor` ở chế độ sáng đang dùng chung `#F7F7F3`, chưa tách nền ấm `#F7F7F3` với bề mặt thẻ `#FFFFFF`, chữ chính `#18212B` và chữ phụ `#687483` đúng định hướng *Premium Minimal Music Library*.
   - **Playback / Content Viewer Screen (Phần 4.3, 4.4, 7.5):** Chưa tích hợp trình phát nhúng chính thức (YouTube IFrame Player & TikTok Embed Player v2 qua WebView khóa chặt origin chống XSS) khi `playbackCapability == 'EMBED_SUPPORTED'`, và chưa hiển thị danh sách tên các album chứa bài hát kèm điều hướng trực tiếp.
   - **Home Screen (Phần 7.2):** Thiếu lời chào cá nhân hóa theo tên tài khoản, thiếu ô nhập nhanh URL ngay tại Trang chủ, thiếu mục **Album mở gần đây (Recently Opened Albums)** và mục **Tiếp tục sắp xếp (Continue Organizing)** cho các bài chưa gắn album.
   - **Library Screen (Phần 7.3) & Album Sort (Phần 5.1):** Thiếu chuyển đổi chế độ **Grid/List**, thiếu sắp xếp thứ tự album tùy chỉnh (`PATCH /api/v1/albums/order`).
   - **Các màn hình chuyên biệt trong 23 màn hình bắt buộc (Phần 7):**
     - `Search Screen` (`/search`) đang tái sử dụng `LibraryScreen` nên chỉ tìm từng tab riêng lẻ thay vì gọi `GET /api/v1/search?q=` để hiển thị đồng thời cả Album và Bài hát.
     - `Favorites Screen` (`/favorites`) chưa hiển thị đồng thời cả Album yêu thích và Bài hát yêu thích.
     - `Edit Song Screen` (`/songs/:id/edit`), `Song Metadata Preview` rõ ràng và `Delete Account Flow` (`/account/delete`) cần tách thành luồng/màn hình hoàn chỉnh, dễ thao tác; sau khi lưu bài hát thành công cần có nút mở trực tiếp album vừa lưu (Phần 7.4).
   - **Đồng bộ & Bộ nhớ đệm cục bộ (Phần 11):** `sqflite` đã được khai báo trong `pubspec.yaml` nhưng chỉ mới dùng cho nhạc nội bộ trên máy; thư viện server chưa có read-through cache cục bộ theo user, chưa có retry backoff cho truy vấn đọc, chưa có optimistic toggle yêu thích kèm rollback, và chưa hiển thị thanh trạng thái ngoại tuyến rõ ràng khi mất kết nối.
6. **Thiếu tài liệu kỹ thuật bắt buộc (Phần 16):**
   - Chưa có `README.md` ở gốc dự án và thiếu 9 tài liệu bắt buộc trong `docs/`: `ARCHITECTURE.md`, `DATABASE.md`, `API.md`, `AUTHENTICATION.md`, `PROVIDER_INTEGRATION.md`, `TESTING.md`, `DEPLOYMENT.md`, `SECURITY.md`, `IMPLEMENTATION_REPORT.md`.

---

## 5. Kiểm Tra Bảo Mật, Dead Code & Dữ Liệu Giả

- **Secrets:** Không có API key, mật khẩu SMTP hay JWT signing key nào bị hardcode trong source code backend hoặc mobile. Các khóa trong `TMusic.Tests` chỉ là hằng số kiểm thử cô lập trong bộ nhớ.
- **Mock data / Fake UI:** Không có dữ liệu mẫu hay gợi ý bài hát giả trong mã chạy thật. Mọi tính năng chưa có cấu hình (Google Sign-In, Apple Sign-In, YouTube Data API) đều được kiểm tra động qua `/api/v1/system/capabilities` và trả về fallback trung thực (`EXTERNAL_OPEN_ONLY`).
- **Dependencies dư thừa:** Không có package thừa; `sqflite` sẽ được tận dụng trọn vẹn cho cả bộ nhớ đệm đọc thư viện theo tài khoản và chỉ mục nhạc cục bộ trên thiết bị; bổ sung `webview_flutter` chính chủ cho trình phát nhúng hợp lệ của YouTube/TikTok.
