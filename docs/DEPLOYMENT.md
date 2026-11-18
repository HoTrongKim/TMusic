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

# Runtime audio tùy chọn (bật riêng khi cần MP3)
docker build --build-arg INSTALL_AUDIO_TOOLS=true -f backend/Dockerfile -t tmusic-api-audio:v2 .
docker build --build-arg INSTALL_AUDIO_TOOLS=true -f backend/Worker.Dockerfile -t tmusic-worker-audio:v2 .
```

### Lưu ý vận hành Production
1. Bắt buộc chạy sau reverse proxy TLS (`HTTPS`) — bản build Release của Flutter từ chối `TMUSIC_API_URL` không dùng `https://`.
2. Gắn volume bền vững, writable cho runtime user, cho `DataProtection:KeyPath`, `Storage:CoversPath` và `Storage:AudioPath`. API/Worker tách dịch vụ phải dùng cùng audio storage. Compose gốc định nghĩa SQL Server/Mailpit; overlay `docker-compose.autostart.yml` ghép API và worker với cùng volume `/data`.
3. Cung cấp khóa ký JWT, mật khẩu SMTP và API keys từ Secret Manager của hạ tầng triển khai.
4. Image cơ bản không cài audio tools. Opt-in build cài Python3/FFmpeg và yt-dlp2026.10.26 có SHA256 pin; runtime vẫn cần `Features__EnableAudioDownloads=true`. Launcher TikTok chỉ xử lý trang công khai, tắt browser impersonation/header ngẫu nhiên và không chạy trình giải challenge upstream. Hai image audio đã được build trên Docker Linux; xem `BUILD_LOG.md` cho kết quả tải thực tế và `PROVIDER_INTEGRATION.md` cho giới hạn/runtime egress. Khi nâng yt-dlp phải review và kiểm thử lại adapter gắn với phiên bản đã pin.

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
