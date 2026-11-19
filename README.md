# TMusic V2

**TMusic V2** là ứng dụng Flutter dành cho **Android** và **iOS**, giúp người dùng quản lý liên kết **YouTube, TikTok, SoundCloud**, album và audio lưu trên thiết bị. Có thể dùng **Khách cục bộ** hoặc đăng nhập để đồng bộ thư viện tài khoản. `AGENTS.md` mô tả phạm vi hiện hành; `codex/MASTER_PROMPT_TMUSIC.md` là lịch sử.

> **Triết lý sản phẩm:** `COPY LINK → ADD TO TMUSIC → NAME → CHOOSE ALBUM → SAVE`

---

## Tính Năng Chính

- **Khách cục bộ và tài khoản:**
  - Khách tạo album, lưu và quản lý liên kết trên thiết bị không cần đăng nhập. Dữ liệu khách mất khi gỡ ứng dụng nếu chưa đồng bộ. Khi đăng nhập/đăng ký, ứng dụng thử chuyển metadata sang tài khoản; MP3 khách được sao chép vào vùng tài khoản trên cùng máy, không tự upload tệp cá nhân.
  - Đăng ký cấp phiên ngay; xác minh email hiện không bắt buộc để vào thư viện. Có đăng nhập, quên/đặt lại mật khẩu, đổi mật khẩu, quản lý phiên và xóa tài khoản.
  - Hỗ trợ **Google Sign-In** và **Sign in with Apple** xác minh chữ ký OIDC phía server (chặn tự động gộp tài khoản qua email chưa chứng minh mật khẩu).
  - Access token ngắn hạn (10 phút), xoay vòng refresh token kèm phát hiện dùng lại (reuse detection) và quản lý phiên theo từng thiết bị.
- **Nguồn nhạc và MP3 cá nhân:**
  - Chuẩn hóa URL YouTube, TikTok và SoundCloud bằng allowlist cụ thể. HTTP metadata/shortlink trong .NET kiểm tra redirect, pin IP công khai và giới hạn response. Extractor buộc HTTP(S) qua proxy CONNECT xác thực riêng từng lần tải, kiểm tra host/CDN và pin IP công khai; phạm vi subprocess và các phần chưa kiểm chứng được ghi tại `docs/PROVIDER_INTEGRATION.md`.
  - Lấy metadata khi provider/cấu hình cho phép; giữ `CustomTitle` khi metadata thiếu/hết hạn. Hiển thị nguồn, creator/channel khi có và liên kết mở nguồn gốc, không tạo metadata giả.
  - Backend dùng `yt-dlp` + `FFmpeg` sau xác nhận quyền tải, chỉ cho nội dung người dùng có quyền hoặc được phép tải; không vượt DRM, login wall hay anti-bot. Tài khoản có queue/progress/retry/cancel; khách hiện dùng yêu cầu trích xuất trực tiếp.
  - Phát audio native từ tệp được chọn và MP3 lưu trên máy với seek, lặp bài/danh sách, A–B, tốc độ và hàng đợi. `OfficialEmbedPlayer`/`webview_flutter` đã gỡ; nguồn ngoài mở bằng ứng dụng/trình duyệt ngoài. Không có nút clipboard-paste.
- **Quản lý Album & Thư viện cá nhân:**
  - Tạo, sửa, xóa, sắp xếp thứ tự album; chọn màu bìa chuẩn hoặc tải ảnh bìa riêng (tự động kiểm tra magic bytes PNG/JPEG/WebP và gỡ bỏ EXIF/metadata nhạy cảm).
  - Một bài hát có thể thuộc nhiều album; bỏ bài khỏi album không xóa bài khỏi thư viện cá nhân; xóa album không làm mất bài hát đã lưu.
  - Chủ album có thể tạo/thu hồi mã chia sẻ; người nhận xem trước và nhập metadata vào thư viện của mình, giữ source attribution. Thu hồi chặn truy cập/nhập mới, không xóa bản đã nhập và không chia sẻ quyền đọc tệp MP3/cover riêng.
  - Tìm kiếm hợp nhất (`/api/v1/search`), lọc theo nguồn, đánh dấu yêu thích, nhận link trực tiếp từ **Android Share Intent** / **iOS Share Extension**, phát file nhạc hợp pháp do người dùng tự chọn trên thiết bị và nhập playlist YouTube theo lô bất đồng bộ.
- **Giao diện Premium Spotify-Inspired:**
  - Dark mode mặc định: `#121212` / `#181818` / `#282828`, chữ `#FFFFFF`, xanh `#1DB954`; có light mode, quick-pick grid, carousel, album header và mini-player.
  - Ngôn ngữ mặc định **Tiếng Việt** (hỗ trợ chuyển sang **English**), chế độ **Sáng / Tối / Theo hệ thống**, hỗ trợ phóng chữ động tới **200%** không vỡ bố cục.

---

## Công Nghệ Sử Dụng

| Thành phần | Công nghệ |
|---|---|
| **Mobile** | Flutter `3.47` (`Dart 3.13`), Riverpod, GoRouter, Dio, FlutterSecureStorage, sqflite, just_audio, audio_service |
| **Backend** | ASP.NET Core `.NET 10` Minimal API, ASP.NET Core Identity, Entity Framework Core `10` |
| **Database** | Microsoft SQL Server (`TMusicV2Dev` cho môi trường phát triển, tách biệt hoàn toàn với `TMusicDev` cũ) |

---

## Khởi Chạy Nhanh

```powershell
# 1. Cập nhật schema database TMusicV2Dev (file .env hiện có được giữ nguyên)
./scripts/dev.ps1 -Service migrate -Database TMusicV2Dev

# 2. Khởi chạy Backend API tại http://127.0.0.1:5078
./scripts/dev.ps1 -Service api -Database TMusicV2Dev

# Cửa sổ riêng nếu cần Worker nhập playlist/maintenance
./scripts/dev.ps1 -Service worker -Database TMusicV2Dev

# 3. Chạy ứng dụng Flutter trên Android Emulator
cd mobile
flutter pub get
flutter run --dart-define=TMUSIC_API_URL=http://10.0.2.2:5078
```

---

## Xác Minh Và Điều Kiện Chạy

`/health/ready` kiểm tra SQL; `/api/v1/system/capabilities` phản ánh cấu hình và binary tìm thấy, không chứng minh OAuth/provider đã hoạt động trên thiết bị. MP3 cần `Features__EnableAudioDownloads=true`, `yt-dlp`, `FFmpeg`; API mặc định cũng xử lý download, hoặc tách Worker bằng `AudioDownload__RunInApiProcess=false`.

Hai Dockerfile mặc định cài Python 3, FFmpeg, Deno `2.9.8` và `yt-dlp` `2026.10.26` có kiểm tra SHA-256; dùng `--build-arg INSTALL_AUDIO_TOOLS=false` chỉ khi chủ đích build image không xử lý MP3. Runtime vẫn cần `Features__EnableAudioDownloads=true`. Queue có concurrency cấu hình giới hạn 2/process; S3-compatible storage tùy chọn dùng `Storage__Provider=s3`, cấu hình chi tiết ở `docs/DEPLOYMENT.md`. Docker Linux engine hiện chưa chạy nên chưa build lại image trong lượt này; không suy ra live extraction từ các test mock/local.

Chạy backend test với `TMUSIC_SQL_TEST_SERVER` trỏ tới SQL Server cho phép tạo database cô lập `TMusicV2Test_<guid>`; thiếu biến này sẽ bỏ qua SQL integration tests. `flutter test --no-pub` mặc định không chạy `integration_test/`. Bộ E2E thiết bị thay phiên lưu và ghi dữ liệu vào API đích, chỉ dùng app/database kiểm thử riêng.

Kết quả đã chạy và giới hạn thiết bị/credential nằm trong [docs/BUILD_LOG.md](docs/BUILD_LOG.md). Backend API download tests dùng extractor giả lập; test converter riêng dùng WAV tổng hợp với FFmpeg cục bộ khi có binary, không gọi provider. Widget tests không thay kiểm thử download thật, audio nền/màn hình khóa hoặc live OAuth. iOS cần macOS/Xcode/signing; APK debug không chứng minh sẵn sàng phát hành.

---

## Tài Liệu Kỹ Thuật Chi Tiết

- [docs/PROJECT_AUDIT.md](docs/PROJECT_AUDIT.md) — Kết quả kiểm toán mã nguồn và phương án bảo toàn
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — Kiến trúc phân tầng Backend, Mobile và chiến lược đồng bộ
- [docs/DATABASE.md](docs/DATABASE.md) — Thiết kế bảng SQL Server, khóa ngoại kép và migrations
- [docs/API.md](docs/API.md) — Đặc tả toàn bộ REST API `/api/v1`
- [docs/AUTHENTICATION.md](docs/AUTHENTICATION.md) — Cơ chế xác thực Email, Google, Apple và xoay vòng phiên
- [docs/PROVIDER_INTEGRATION.md](docs/PROVIDER_INTEGRATION.md) — Ba provider, MP3 và ranh giới bảo mật/kiểm chứng
- [docs/UI_DESIGN_SYSTEM.md](docs/UI_DESIGN_SYSTEM.md) — Hệ thống thiết kế và danh mục 23 màn hình
- [docs/SECURITY.md](docs/SECURITY.md) — Kiểm soát bảo mật, chống IDOR/XSS và xử lý ảnh bìa an toàn
- [docs/TESTING.md](docs/TESTING.md) — Chiến lược kiểm thử Unit, SQL Integration, Widget & E2E
- [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) — Hướng dẫn triển khai Backend, Docker, Android và iOS
- [docs/BUILD_LOG.md](docs/BUILD_LOG.md) — Nhật ký lệnh build/test thực tế và kết quả
- [docs/IMPLEMENTATION_REPORT.md](docs/IMPLEMENTATION_REPORT.md) — Báo cáo hoàn thiện tổng thể TMusic V2
