# DEPLOYMENT & BUILD GUIDE — TMUSIC V2

## 1. Cấu Hình Môi Trường (`.env`)

Sao chép `.env.example` sang `.env` (nếu thiết lập máy mới; **không ghi đè `.env` đang có**) và điền các biến cần thiết:

```env
ConnectionStrings__TMusic=Server=lpc:.\SQLEXPRESS;Database=TMusicV2Dev;Integrated Security=True;Encrypt=True;TrustServerCertificate=True
Auth__SigningKey=<chuỗi ngẫu nhiên tối thiểu 32 ký tự UTF-8>
Auth__Issuer=TMusic
Auth__Audience=TMusic.Mobile

Mail__Host=127.0.0.1
Mail__Port=11025
Mail__UseTls=false
Mail__From=auth@tmusic.test

TMUSIC_GOOGLE_WEB_CLIENT_ID=
TMUSIC_GOOGLE_ANDROID_CLIENT_ID=
TMUSIC_GOOGLE_IOS_CLIENT_ID=

TMUSIC_ENABLE_APPLE_SIGN_IN=false
Auth__Apple__Audiences=

YouTube__ApiKey=
YouTube__DailyQuotaBudget=8000
```

Backend hỗ trợ song song cả tên biến hiện có (`ConnectionStrings__TMusic`, `Auth__SigningKey`, `Mail__*`) lẫn các bí danh V2 (`ConnectionStrings__DefaultConnection`, `Auth__Jwt__*`, `SMTP_*`).

---

## 2. Chạy Backend & Database Ở Môi Trường Phát Triển

`scripts/dev.ps1` tự động nạp `.env` vào tiến trình hiện tại, trỏ sang database riêng `TMusicV2Dev` (không chạm tới `TMusicDev` cũ) và chạy dịch vụ ở `http://127.0.0.1:5078`:

```powershell
# 1. Áp dụng migrations lên TMusicV2Dev
./scripts/dev.ps1 -Service migrate -Database TMusicV2Dev

# 2. Chạy API server
./scripts/dev.ps1 -Service api -Database TMusicV2Dev

# 3. (Tùy chọn) Chạy Worker nhập playlist & dọn cache hết hạn ở cửa sổ khác
./scripts/dev.ps1 -Service worker -Database TMusicV2Dev
```

Kiểm tra sức khỏe dịch vụ:
- Liveness: `GET http://127.0.0.1:5078/health`
- Readiness (SQL Server): `GET http://127.0.0.1:5078/health/ready`

---

## 3. Triển Khai Container / Production (`Docker`)

### Chạy SQL Server cục bộ bằng Docker Desktop

File `docker-compose.yml` đã có service `db` cho SQL Server 2022, chỉ lắng nghe tại `127.0.0.1:1433` và lưu dữ liệu trong Docker volume riêng `tmusic_v2_sql`. Khởi động Docker Desktop với Linux containers, sau đó mở PowerShell ở thư mục gốc dự án:

```powershell
$securePassword = Read-Host 'Nhập mật khẩu SQL Server sa' -AsSecureString
$env:MSSQL_SA_PASSWORD = (New-Object System.Net.NetworkCredential('', $securePassword)).Password
docker compose up -d db
Remove-Item Env:MSSQL_SA_PASSWORD
```

Chọn mật khẩu mạnh theo yêu cầu của SQL Server và lưu tại nơi an toàn. Không đưa mật khẩu vào mã nguồn hoặc gửi qua chat. Docker Compose dùng mật khẩu để khởi tạo instance mới; nếu volume đã có dữ liệu, thay đổi biến môi trường không thay đổi mật khẩu `sa` đã thiết lập. Để chạy lại lệnh Compose khi biến chưa được cấu hình, nhập lại mật khẩu đã dùng. Không dùng `docker compose down -v` nếu cần giữ dữ liệu.

Lệnh này chỉ khởi động SQL Server: nó không tạo database `TMusicV2Dev`, không chuyển dữ liệu và không tự đổi cấu hình kết nối của API. Database V2 và cấu hình API phải được thiết lập riêng trước khi sử dụng; không chạy migration lên `TMusicDev` cũ. Nếu cổng 1433 đã được dịch vụ khác sử dụng, cần chọn một cổng máy chủ còn trống và cập nhật kết nối API tương ứng.

### Tự chạy SQL Server, API và worker trong Docker

Sau khi instance SQL Docker đã được khởi tạo, migration V2 đã được áp dụng và `.local/sql-docker-password.xml` đã được tạo bằng tài khoản Windows hiện tại, chạy một lần:

```powershell
./scripts/start-docker-stack.ps1
```

Script dùng file `docker-compose.autostart.yml` cùng file Compose gốc, đọc mật khẩu SQL được Windows mã hóa và giữ nguyên `.env`. API và worker nhận cấu hình từ `.env` lúc tạo container, dùng volume Docker chung cho audio, ảnh bìa và khóa Data Protection, và được cấu hình `restart: unless-stopped` cùng database. Dữ liệu cấu hình được truyền vào môi trường container; không đưa `.env` hoặc mật khẩu vào Docker image hay Git. Chỉ dùng trên máy tin cậy và bảo vệ quyền truy cập Docker. API local thử nghiệm ở `http://127.0.0.1:5080/health/ready`.

Sau khi các container được tạo thành công, Docker sẽ thử khởi động lại chúng khi Docker Engine hoạt động trở lại, miễn là chúng không bị dừng thủ công. Máy vẫn cần bật và có mạng; mở Docker không tự bật máy. Không dùng `docker compose down -v` nếu cần giữ database và file. Mật khẩu trong `.local/sql-docker-password.xml` gắn với tài khoản Windows đã tạo nó; cần có cách sao lưu thông tin truy cập phù hợp trước khi chuyển máy.

Profile `internet` dành cho Cloudflare Tunnel **đã được cấu hình riêng** và chưa tự hoạt động nếu thiếu token/tunnel và tên miền. Thử nghiệm hiện dùng Quick Tunnel riêng trỏ tới API Docker cổng 5080 và Worker gateway có địa chỉ cố định. Sau khi khởi động lại máy/tunnel, URL Quick Tunnel có thể đổi; cần cập nhật secret upstream của Worker, gateway không tự phát hiện URL mới. API trong stack đang ở chế độ phát triển; không coi đây là cấu hình production. Cần tunnel ổn định và cấu hình triển khai phù hợp trước khi phục vụ lâu dài.

```powershell
# Khởi chạy SQL Server 2022 cục bộ và Mailpit (chỉ dành cho dev)
docker compose --profile dev-mail up -d

# Build production image cho API & Worker
docker build -f backend/Dockerfile -t tmusic-api:v2 .
docker build -f backend/Worker.Dockerfile -t tmusic-worker:v2 .

# Chỉ build biến thể không xử lý MP3 khi chủ đích cần image nhỏ hơn
docker build --build-arg INSTALL_AUDIO_TOOLS=false -f backend/Dockerfile -t tmusic-api:no-audio .
docker build --build-arg INSTALL_AUDIO_TOOLS=false -f backend/Worker.Dockerfile -t tmusic-worker:no-audio .
```

### Lưu ý vận hành Production
1. Bắt buộc chạy sau reverse proxy TLS (`HTTPS`) — bản build Release của Flutter từ chối `TMUSIC_API_URL` không dùng `https://`.
2. Gắn volume bền vững, writable cho runtime user, cho `DataProtection:KeyPath`, `Storage:CoversPath` và `Storage:AudioPath`. API/Worker tách dịch vụ phải dùng cùng audio storage. Compose gốc định nghĩa SQL Server/Mailpit; overlay `docker-compose.autostart.yml` ghép API và worker với cùng volume `/data`.
3. Cung cấp khóa ký JWT, mật khẩu SMTP và API keys từ Secret Manager của hạ tầng triển khai.
4. API và Worker Dockerfile mặc định cài Python 3, FFmpeg, Deno 2.9.8 và yt-dlp 2026.10.26; tải yt-dlp/Deno từ upstream release có SHA-256 kiểm tra. Runtime kiểm tra `yt-dlp --version` khớp bản trong image và chạy thử FFmpeg; runtime vẫn cần `Features__EnableAudioDownloads=true`. Có thể build image không có audio tools bằng `--build-arg INSTALL_AUDIO_TOOLS=false`, nhưng service tải MP3 không được dùng image đó. Khi nâng yt-dlp phải cập nhật version/hash đã review và kiểm thử lại.

### Download queue và storage trên Railway

- Queue SQL vẫn dùng các bảng hiện tại, lease/rowversion, tối đa 3 tổng lượt chạy, exponential backoff/jitter và timeout. `AudioDownload__Concurrency` bị chặn tối đa 2 job đồng thời **mỗi process**; nếu chạy API + Worker hoặc nhiều replicas, giới hạn tổng là số process nhân concurrency. Khi tách Worker, đặt `AudioDownload__RunInApiProcess=false` cho API và chỉ chạy một cấu hình `deploy/railway/worker.toml` cho service Worker.
- Biến queue tùy chọn: `AudioDownload__Concurrency=2`, `AudioDownload__MaxAttempts=3`, `AudioDownload__TimeoutSeconds=180`, `AudioDownload__LeaseSeconds=480`, `AudioDownload__MaxBackoffSeconds=60`. Mã đăng nhập/challenge/403/nội dung giới hạn không retry tự động; lỗi mạng/429 và lỗi object store tạm thời mới retry hữu hạn. Lease tự tăng đủ bao phủ thời gian extraction + conversion.
- `Storage:Provider` mặc định `local` để giữ dữ liệu cũ. Railway filesystem không bền vững nếu không có volume. Để dùng S3/R2, cấu hình Variables riêng trên API và Worker: `Storage__Provider=s3`, `Storage__Object__Endpoint=https://...`, `Storage__Object__Bucket=...`, `Storage__Object__Region=...`, `Storage__Object__AccessKey` và `Storage__Object__SecretKey` (hai giá trị credential chỉ nhập ở Railway Variables/secret manager, không commit hoặc đưa vào build args). Tùy chọn: `Storage__Object__Prefix=tmusic/audio`, `Storage__Object__PathStyle=true`, `Storage__Object__TimeoutSeconds=30`.
- Khi bật S3, MP3 mới được lưu vào object storage; adapter đọc từ S3 trước rồi fallback sang file local hiện có, hỗ trợ byte-range cho player, và xóa song song ở hai nơi. Giữ volume `Storage__AudioPath` cũ gắn với API/Worker trong giai đoạn đọc dữ liệu legacy. Chưa tự động di chuyển tệp local lên bucket; kiểm kê và backup bucket/volume trước khi tháo volume. Bucket private, TLS bắt buộc bên ngoài loopback; không cấu hình public ACL.
- API Railway hiện có `deploy/railway/api.toml`. API đọc biến `PORT` Railway và bind HTTP trên `0.0.0.0`; Worker không cần cổng HTTP. Chọn `deploy/railway/worker.toml` trong cấu hình Railway của service Worker để dùng `backend/Worker.Dockerfile`. Bật `Features__EnableAudioDownloads=true` chỉ sau khi đã cấu hình quyền tải, storage bền vững và secrets phù hợp.
- Kiểm tra vận hành: deployment log phải xác nhận startup validation yt-dlp/FFmpeg; gọi `/api/v1/system/capabilities` để xem feature/binary; dùng tài khoản test và URL audio công khai được phép tải, chọn bitrate, theo dõi `/api/v1/downloads/{id}`, stream byte range, hủy job, rồi redeploy và đọc lại file để xác nhận storage bền vững. Ghi nhận `SOURCE_ACCESS_CHECK_REQUIRED`, `SOURCE_AUTHENTICATION_REQUIRED`, `SOURCE_ACCESS_FORBIDDEN`, `SOURCE_RESTRICTED_OR_UNSUPPORTED`, `SOURCE_NETWORK_ERROR` và `AUDIO_CONVERSION_FAILED` riêng. Không dùng nguồn yêu cầu login/anti-bot trong bài kiểm thử; chỉ retry lỗi mạng/429 hoặc storage tạm thời.

---

## 4. Hướng Dẫn Build Android

Yêu cầu: Flutter `3.47+`, JDK 17+, Android SDK (`API 36`).

```powershell
cd mobile
flutter pub get

# Chạy trên Android Emulator (10.0.2.2 trỏ về 127.0.0.1 của máy chủ dev)
flutter run --dart-define=TMUSIC_API_URL=http://10.0.2.2:5078

# Build APK Debug kiểm thử
flutter build apk --debug

# Build App Bundle Release cho Google Play (bắt buộc HTTPS API)
flutter build appbundle --release `
  --dart-define=TMUSIC_API_URL=https://api.yourdomain.com `
  --dart-define=TMUSIC_GOOGLE_WEB_CLIENT_ID=<web-client-id> `
  --dart-define=TMUSIC_GOOGLE_ANDROID_CLIENT_ID=<android-client-id>
```

---

## 5. Hướng Dẫn Cấu Hình & Build iOS (trên macOS / Xcode)

> Docker và Windows không thể thay thế macOS + Xcode để biên dịch và ký ứng dụng iOS.

Dự án đã có sẵn cấu hình `mobile/ios/Runner`, `Runner.entitlements` (App Group `group.com.tmusic.tmusic` + Sign in with Apple) và `ShareExtension` (`ShareViewController.swift`). Khi build trên máy Mac có Xcode:

1. Mở `mobile/ios/Runner.xcworkspace` trong Xcode, cấu hình **Development Team**, Bundle Identifier cho cả `Runner` và `ShareExtension`, bật capability **App Groups** (`group.com.tmusic.tmusic`) và **Sign in with Apple**.
2. Nếu dùng Google Sign-In trên iOS, thêm URL scheme đảo ngược của `TMUSIC_GOOGLE_IOS_CLIENT_ID` vào `Info.plist`.
3. Chạy lệnh build IPA:
   ```bash
   cd mobile
   flutter pub get
   flutter build ipa --release \
     --dart-define=TMUSIC_API_URL=https://api.yourdomain.com \
     --dart-define=TMUSIC_GOOGLE_WEB_CLIENT_ID=<web-client-id> \
     --dart-define=TMUSIC_GOOGLE_IOS_CLIENT_ID=<ios-client-id> \
     --dart-define=TMUSIC_ENABLE_APPLE_SIGN_IN=true
   ```
