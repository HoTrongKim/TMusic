# TMusic V2 — Mobile Client (Flutter)

Ứng dụng Flutter **TMusic V2** cho Android/iOS, mặc định **Tiếng Việt** (hỗ trợ **English**), có Khách cục bộ và tài khoản. UI **Premium Spotify-Inspired** dùng dark charcoal `#121212` / `#181818` / `#282828`, chữ trắng, xanh `#1DB954`, kèm light mode. Hỗ trợ YouTube/TikTok/SoundCloud và audio native từ tệp hợp lệ trên thiết bị; embedded WebView đã gỡ.

## Cấu Trúc Thư Mục

- `lib/app/` — `TMusicApp`, định tuyến `GoRouter` có bảo vệ xác thực và `TMusicTheme`.
- `lib/core/` — `ApiClient` (`Dio`), `SessionController` (`FlutterSecureStorage`), `ExternalAuth` (Google & Apple), `LibraryCache` (`sqflite`), bộ từ điển `AppCopy` và các widget dùng chung.
- `lib/features/` — Các tính năng `authentication`, `home`, `library`, `albums`, `link_import`, `songs`, `playback`, `search`, `profile`, `settings`, `share`; bốn tab Trang chủ/Thư viện/Tải xuống/Cá nhân.
- `test/` — Bộ kiểm thử đơn vị và widget (bao gồm kiểm chứng màn hình `320dp` ở tỷ lệ chữ `200%` cho Light & Dark mode).
- `integration_test/` — E2E API/SQL/Mailpit chỉ dùng app riêng trên emulator/thiết bị test với database `TMusicV2Test_<id>`. Không chạy vào `TMusicDev`, `TMusicV2Dev` có dữ liệu hoặc điện thoại người dùng vì bộ test thay phiên lưu và tạo/xóa dữ liệu.

## Lệnh Phát Triển & Kiểm Thử

```powershell
flutter pub get
flutter analyze --no-pub
flutter test --no-pub
flutter build apk --debug --no-pub
```

Xem hướng dẫn chi tiết cho Android và iOS tại [../docs/DEPLOYMENT.md](../docs/DEPLOYMENT.md).

`flutter test` mặc định không chạy `integration_test/`. Kết quả/lệnh đã chạy và giới hạn live OAuth/audio nền/device được ghi tại [../docs/BUILD_LOG.md](../docs/BUILD_LOG.md); widget tests với fake dependencies không chứng minh playback MP3 thực tế. Khách mất dữ liệu khi gỡ app nếu chưa đồng bộ; login chuyển metadata và sao chép MP3 trên cùng thiết bị, không tự upload tệp cá nhân.
