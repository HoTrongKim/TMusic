# TESTING STRATEGY & VERIFICATION — TMUSIC V2

## 1. Cấu Trúc Kiểm Thử

### 1.1. Backend Unit & SQL Integration Tests (`backend/tests/TMusic.Tests`)
Chạy trên `.NET 10` (`xUnit 2.9.3`) kết hợp SQL Server thật qua database cô lập `TMusicV2Test_<guid>`. Thiếu `TMUSIC_SQL_TEST_SERVER` thì các test `[SqlFact]` bị bỏ qua, không được báo là đã kiểm chứng SQL:
- **`ProviderTests`:** Chuẩn hóa URL YouTube/TikTok/SoundCloud, từ chối link độc hại/mơ hồ, chặn dải IP riêng tư/nội bộ (SSRF), giới hạn redirect shortlink, phân trang YouTube playlist 50 mục, loại bỏ HTML oEmbed, fallback trung thực khi thiếu API key. HTTP provider được giả lập trong các test này.
- **`ExternalTokenSecurityTests` & `ExternalAccountLinkTests`:** Kiểm tra chữ ký RSA, issuer, audience, hạn token, từ chối token không ký, yêu cầu chứng minh mật khẩu khi trùng email và ngăn liên kết 1 tài khoản ngoài cho 2 user.
- **`AccountLibraryIntegrationTests`:** Đăng ký cấp phiên ngay không bắt buộc xác minh email, băm mật khẩu Identity, xoay vòng refresh token & phát hiện dùng lại, khôi phục mật khẩu một lần, xóa tài khoản dọn sạch dữ liệu riêng tư, xung đột đồng thời `If-Match` (`428` / `409`).
- **`LibraryOrderingTests` & `SecurityIntegrationTests`:** Sắp xếp bài hát, xóa khỏi album khác xóa khỏi thư viện, khóa tài khoản khi nhập sai mật khẩu liên tiếp, chống IDOR chéo tài khoản ở cả API và khóa ngoại SQL Server, idempotency tạo album.
- **`ImportTests`:** Hàng đợi nhập playlist có lease/checkpoint, hủy ngang chừng, thử lại, kiểm tra toàn vẹn SHA-256 của bản xem trước, hạn mức quota YouTube và hết hạn metadata nguồn mà vẫn giữ nguyên tên bài của người dùng.
- **`CoverAndV2EnhancementsTests`:** Gỡ bỏ EXIF/tEXt khỏi ảnh bìa PNG/JPEG, từ chối SVG giả mạo, chống IDOR ảnh bìa riêng tư, sắp xếp thứ tự album, lọc bài chưa vào album, trả chi tiết bài trùng từ `/songs/resolve`.
- **`EndToEndAcceptanceTests`:** Hành trình API/SQL từ đăng ký, đăng nhập, tạo album, thêm YouTube/TikTok, đổi phiên và kiểm tra dữ liệu/ownership/idempotency. Các client mô phỏng thiết bị; tên “14 bước” không chứng minh chạy trên điện thoại hoặc provider thật.
- **`AudioDownloadTests` / `AudioDownloadEgressTests`:** API download/Range/ownership dùng extractor giả lập; kiểm tra arguments và các trường hợp proxy từ chối host/DNS/auth/header. Không suy ra live extraction hoặc playback thật từ các test double.
- **`LocalAudioConversionTests`:** Capacity hai slot không xếp hàng, WAV tổng hợp chuyển MP3 bằng FFmpeg thực, từ chối playlist truy cập mạng và concat tệp phụ. `[FfmpegFact]` bỏ qua nếu thiếu FFmpeg; kiểm tra skipped count trước báo hoàn tất. Các test này dùng dữ liệu tổng hợp cục bộ, không chứng minh tải thật từng provider hoặc phát nền trên điện thoại.

### 1.2. Flutter Unit & Widget Tests (`mobile/test/`)
- Kiểm tra bộ lọc link `SharedLinks` cho ba provider; embedded player và dependency WebView đã gỡ.
- Kiểm tra route cần tài khoản (bảo mật/phiên/xóa tài khoản) trong khi khách vẫn dùng thư viện cục bộ.
- Kiểm tra xác thực biểu mẫu và luồng đăng nhập tới Trang chủ.
- Kiểm tra luồng tạo album và nhập URL -> nhận diện -> đặt tên riêng -> chọn album -> lưu; không có nút đọc/dán clipboard. `screens_and_sync_test.dart` có sharing, attribution, sync khách và thao tác player với fake dependencies.
- Kiểm tra khả năng đáp ứng giao diện trên màn hình `320dp` và `430dp`, cả `Light` và `Dark`, ở mức phóng chữ **200%** cho toàn bộ 4 tab chính và màn hình xác thực.
- Kiểm tra trạng thái lỗi mạng và khôi phục khi bấm `Thử lại`.

### 1.3. Flutter Live Device / Emulator E2E (`mobile/integration_test/account_library_e2e_test.dart`)
- Bộ test này kết nối API/SQL/Mailpit đã cấu hình; **không được chạy vào `TMusicDev`, `TMusicV2Dev` đang có dữ liệu hoặc điện thoại người dùng**. Nó thay phiên secure storage, tạo/xóa tài khoản và dùng source IDs kiểm thử, nên cần app cài riêng trên emulator/thiết bị test và API trỏ database `TMusicV2Test_<id>` riêng.
- Chuẩn bị database V2 test mới bằng `scripts/dev.ps1 -Service migrate -Database <tên-test>` rồi chạy API/Worker dành riêng cho database đó, dùng cổng riêng nếu dev API đang hoạt động. Chỉ đặt `TMUSIC_API_URL` / `TMUSIC_E2E_MAIL_URL` sau khi xác minh API, Mailpit và ứng dụng đều thuộc môi trường test. Không tự chạy lệnh E2E trên thiết bị đang lưu dữ liệu thật.
- `flutter test --no-pub` mặc định chỉ chạy unit/widget trong `test/`, không chạy `integration_test/`. CI hiện chưa chạy E2E thiết bị, OAuth trực tiếp hoặc build iOS. Ghi riêng kết quả/lệnh/phạm vi tại `BUILD_LOG.md`.

---

## 2. Lệnh Chạy Kiểm Thử Chuẩn

### Backend (kèm SQL Server Integration Tests)
```powershell
$env:TMUSIC_SQL_TEST_SERVER = "Server=lpc:.\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
dotnet restore backend/TMusic.slnx -p:NuGetAuditMode=all
dotnet build backend/TMusic.slnx -c Release --no-restore
dotnet test backend/TMusic.slnx -c Release --no-build
```

### Mobile (Static Analysis & Widget Tests)
```powershell
cd mobile
flutter pub get
flutter analyze --no-pub
flutter test --no-pub
```
