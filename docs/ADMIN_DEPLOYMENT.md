# TMusic V4 — Hướng dẫn Triển khai & Vận hành Hệ thống Admin

## 1. Kiến trúc triển khai

```
Flutter Android/iOS ──┐
                      ├──► Cloudflare Gateway / HTTPS ──► ASP.NET Core API (TMusic.Api)
TMusic Admin Web ─────┘                                         │
                                                                ├── SQL Server (TMusicV2Dev / Prod DB)
                                                                ├── Server Storage (MP3 + Covers + _quarantine)
                                                                └── Background Download Runner / Worker
```

- **Backend duy nhất:** `TMusic.Api` phục vụ cả `/api/v1/*` (Flutter mobile) và `/api/admin/*` (TMusic Admin Web) với chính sách xác thực, phân quyền RBAC/Permission, MFA bắt buộc, CSRF và rate-limit tách biệt hoàn toàn.
- **Database an toàn:** Tái sử dụng bảng ASP.NET Core Identity (`AspNetRoles`, `AspNetUserRoles`, `AspNetRoleClaims`) kết hợp các bảng quản trị mới (`AccountSuspensions`, `AdminInvitations`, `AuditLogs`, `SystemEvents`, `SystemSettings`, `ContentModerationFlags`, `AdminMfaChallenges`). **Tuyệt đối không áp dụng migration lên database `TMusicDev` cũ.**

---

## 2. Chiến lược Migration, Sao lưu & Rollback Database

1. **Sao lưu trước khi chạy migration (Bắt buộc trên Production):**
   ```sql
   BACKUP DATABASE [TMusicV2Dev]
   TO DISK = N'/var/opt/mssql/backup/TMusicV2Dev_pre_v4_admin.bak'
   WITH COMPRESSION, CHECKSUM, INIT;
   ```
2. **Áp dụng migration (chỉ lên database xác nhận của V2/V4):**
   ```powershell
   $env:ConnectionStrings__DefaultConnection = "<SQL_SERVER_CONNECTION_STRING>"
   dotnet ef database update --project backend/src/TMusic.Infrastructure --startup-project backend/src/TMusic.Api
   ```
3. **Kế hoạch Rollback:**
   - Migration `20261119171815_AddAdminRbacAndManagement` hoàn toàn mang tính cộng thêm (không xóa cột/bảng hiện hữu).
   - Nếu cần hoàn tác về phiên bản trước:
     ```powershell
     dotnet ef database update 20261119161219_AddAudioBitrateOption --project backend/src/TMusic.Infrastructure --startup-project backend/src/TMusic.Api
     ```
   - Hoặc khôi phục trực tiếp từ bản sao lưu `.bak` đã tạo ở bước 1.

---

## 3. Khởi tạo SuperAdmin đầu tiên (An toàn, không mật khẩu mặc định)

TMusic **không** tạo tài khoản admin mặc định và **không** mở endpoint HTTP công khai để tự đăng ký Admin. Người triển khai có quyền truy cập server chạy CLI một lần:

```powershell
# Chế độ tương tác (mật khẩu được ẩn khi gõ, không in ra console)
$env:ConnectionStrings__DefaultConnection = "<SQL_SERVER_CONNECTION_STRING>"
dotnet run --project backend/src/TMusic.AdminCli -- create-superadmin
```

Hoặc chạy không tương tác trong quy trình triển khai (đọc mật khẩu từ STDIN, không truyền qua tham số dòng lệnh):

```bash
printf '%s\n' "$SUPERADMIN_PASSWORD" | \
  ConnectionStrings__DefaultConnection="$SQL_CONN" \
  dotnet run --project backend/src/TMusic.AdminCli -- create-superadmin \
    --email "owner@domain.com" \
    --display-name "TMusic SuperAdmin" \
    --password-stdin
```

Kiểm tra trạng thái (chỉ đọc, không lộ bí mật):

```powershell
dotnet run --project backend/src/TMusic.AdminCli -- status
```

Đặc tính an toàn của CLI:
- Từ chối chạy nếu `InitialCatalog` là `TMusicDev`.
- Yêu cầu email hợp lệ và mật khẩu mạnh (`12–128` ký tự gồm chữ hoa, chữ thường, chữ số, ký tự đặc biệt).
- Tính idempotent: nếu đã tồn tại SuperAdmin, lệnh tự dừng an toàn, không tạo thêm và không đặt lại mật khẩu.
- Ghi bản ghi `SUPERADMIN_BOOTSTRAPPED` vào bảng `AuditLogs`.

---

## 4. Hướng dẫn đăng nhập quản trị và bật MFA bắt buộc

1. Truy cập **TMusic Admin Web** (`https://admin.<domain-tmusic>` hoặc `http://localhost:5173` khi chạy dev).
2. Nhập email và mật khẩu SuperAdmin đã khởi tạo qua CLI.
3. Ở lần đăng nhập đầu tiên (`MFA_SETUP_REQUIRED`):
   - Quét mã QR hiển thị trên màn hình bằng ứng dụng Authenticator (Google Authenticator, 1Password, Microsoft Authenticator, Authy…) hoặc nhập khóa thủ công.
   - Nhập mã 6 chữ số TOTP để kích hoạt MFA.
   - **Lưu lại 10 mã khôi phục dùng một lần (Recovery Codes)** ở nơi an toàn (chỉ hiển thị một lần duy nhất).
4. Từ các lần đăng nhập tiếp theo (`MFA_REQUIRED`): nhập email + mật khẩu + mã TOTP 6 chữ số (hoặc mã khôi phục dùng một lần).
5. **Mời Admin/Moderator mới:**
   - SuperAdmin vào mục **Quản trị viên → Mời quản trị viên**, nhập email và vai trò (`Admin` hoặc `Moderator`).
   - Hệ thống tạo token ngẫu nhiên dùng một lần (hết hạn sau 24 giờ, chỉ lưu SHA-256 hash trên DB) và gửi qua SMTP nếu đã cấu hình (hoặc hiển thị đường dẫn `/accept-invite?token=...` một lần duy nhất).
   - Người được mời mở đường dẫn, tạo/xác nhận mật khẩu và bắt buộc hoàn tất thiết lập TOTP MFA trước khi được cấp quyền.

---

## 5. Triển khai Admin Web lên Production

### Cấu hình biến môi trường Backend (`TMusic.Api`)

Thêm origin của Admin Web vào biến môi trường CORS (tuyệt đối không dùng `*`):

```env
Cors__AdminOrigins=https://admin.your-domain.com
```

Các secrets (`Auth__Jwt__SigningKey`, `ConnectionStrings__DefaultConnection`, `Mail__Password`) tiếp tục quản lý tuyệt đối qua biến môi trường của Railway/VPS, không bao giờ lộ trên dashboard.

### Build & Deploy bằng Docker (Railway / VPS)

```bash
docker build -f admin-web/Dockerfile \
  --build-arg VITE_API_BASE_URL=https://tmusic-api-gateway.trongkim3008.workers.dev \
  -t tmusic-admin-web:latest .

docker run -d --name tmusic-admin-web -p 8080:8080 tmusic-admin-web:latest
```

`admin-web/nginx.conf` đã tích hợp sẵn:
- `Content-Security-Policy` nghiêm ngặt (`frame-ancestors 'none'`, `script-src 'self'`).
- `Strict-Transport-Security`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Permissions-Policy`.
- SPA history fallback cho `/accept-invite` và các route client.

### Chạy cục bộ (Development)

```bash
# Terminal 1: Backend API
dotnet run --project backend/src/TMusic.Api

# Terminal 2: Admin Web (mặc định proxy /api -> http://localhost:5000)
cd admin-web
npm install
npm run dev
```
