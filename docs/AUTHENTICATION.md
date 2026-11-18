# AUTHENTICATION & SESSION SECURITY — TMUSIC V2

## 1. Chế độ khách và tài khoản

- Chế độ khách lưu album, bài hát và tệp audio trên thiết bị; dữ liệu khách mất khi gỡ ứng dụng nếu chưa đồng bộ vào tài khoản. Các endpoint dữ liệu tài khoản trên ASP.NET Core yêu cầu đăng nhập và kiểm tra chủ sở hữu; nhận diện/trích xuất dành cho khách dùng endpoint công khai riêng, có rate limit và xác nhận quyền tải.
- Mỗi người dùng có một định danh nội bộ duy nhất `Account.Id` (`Guid`). Nhiều phương thức đăng nhập (`password`, `google`, `apple`) có thể gắn vào cùng một tài khoản sau khi chứng minh quyền sở hữu.

---

## 2. Email & Password Authentication

- **Password Hashing:** Sử dụng `ASP.NET Core Identity` (`IPasswordHasher<Account>` PBKDF2-HMAC-SHA512 mặc định của .NET 10), tuyệt đối không tự cài đặt thuật toán băm riêng.
- **Chính sách mật khẩu:** Độ dài 12–128 ký tự, bắt buộc có chữ hoa, chữ thường, chữ số và ký tự đặc biệt.
- **Đăng ký tức thì (không yêu cầu xác minh email khi tạo tài khoản):** Người dùng đăng ký xong được cấp ngay phiên làm việc hợp lệ để sử dụng ứng dụng lập tức; chức năng gửi mã qua email chỉ dùng khi người dùng yêu cầu **Quên mật khẩu / Đặt lại mật khẩu**.
- **Chống dò tìm tài khoản & Timing Attack:**
  - `POST /auth/forgot-password` trả `202 Accepted` đồng nhất.
  - Khi đăng nhập với email không tồn tại, server vẫn thực hiện một phép băm mật khẩu giả lập để cân bằng thời gian phản hồi.
- **Chống Brute Force:** Rate limit theo IP (`auth` policy) kết hợp khóa tài khoản tạm thời 15 phút sau 5 lần nhập sai mật khẩu liên tiếp.
- **Đặt lại & Đổi mật khẩu:** Token có thời hạn 1 giờ (`DataProtectionTokenProviderOptions`), chỉ dùng được một lần; ngay khi đổi/đặt lại mật khẩu thành công, toàn bộ phiên đăng nhập hiện hành bị thu hồi trong cùng transaction.

---

## 3. Google Sign-In & Sign in with Apple

### 3.1. Google Sign-In
- Mobile dùng `google_sign_in` (`GoogleSignIn.instance.initialize` qua Android Credential Manager) lấy OIDC `idToken` (không bao giờ dùng Google access token làm session token nội bộ).
- Build Android thử nghiệm có Google: `./scripts/build-android.ps1`. Script đọc tên cấu hình hiện có, chỉ xuất API URL và client ID công khai vào `.local/artifacts/android-public-defines.json`, rồi tạo APK debug đã ký bằng chứng chỉ debug hiện có. Không đóng gói `.env`, client secret hoặc khóa backend. Dùng `-Mode release` chỉ khi đã cấu hình đủ keystore thật; đường dẫn keystore tương đối được tính từ thư mục gốc dự án.
- Build iOS trên GitHub Actions dùng `TMUSIC_GOOGLE_SERVER_CLIENT_ID` và `TMUSIC_GOOGLE_IOS_CLIENT_ID` đã lưu trong Actions secrets. `scripts/prepare_mobile_oauth.py` xác thực và chuẩn hóa ID, tạo public Dart defines và thêm URL scheme đảo ngược vào bản `Info.plist` dùng để build. File IPA chưa ký cần được ký/cài với bundle ID khớp đăng ký iOS trong Google Cloud.
- **Yêu cầu cấu hình trên Android:** Trên Google Cloud Console (cùng project với Web Client ID dùng cho `TMUSIC_GOOGLE_SERVER_CLIENT_ID`), bắt buộc phải đăng ký thêm một **OAuth 2.0 Client ID loại Android** khớp tên gói `com.tmusic.tmusic` và dấu vân tay chứng chỉ ký APK (`SHA-1`); trên Android chỉ truyền `serverClientId` (Web Client ID) vào `g.initialize`, không truyền `clientId`. Nếu chưa đăng ký cặp `package + SHA-1`, Google Play Services trả lỗi `UNREGISTERED_ON_API_CONSOLE`.
- Backend (`ExternalTokenVerifier`) tải JWKS chính thức qua HTTPS từ `https://accounts.google.com/.well-known/openid-configuration` (tắt auto-redirect & cookies), kiểm tra:
  - Chữ ký `RS256`
  - `iss` thuộc `https://accounts.google.com` hoặc `accounts.google.com`
  - `aud` khớp danh sách client ID được cấu hình (`Auth:Google:Audiences`, `TMUSIC_GOOGLE_SERVER_CLIENT_ID`, `TMUSIC_GOOGLE_WEB_CLIENT_ID`, `TMUSIC_GOOGLE_ANDROID_CLIENT_ID`, `TMUSIC_GOOGLE_IOS_CLIENT_ID`)
  - Hạn sử dụng (`exp`) với `ClockSkew` 15 giây
  - Tính xác thực của `email_verified` và miền quản quyền (`@gmail.com` hoặc Google Workspace `hd`).

### 3.2. Sign in with Apple
- Trước khi gọi SDK Apple, client xin `nonce` ngẫu nhiên 32 bytes từ `POST /api/v1/auth/apple/challenge` (hoặc `/auth/links/apple/challenge`). Server chỉ lưu SHA-256 của `nonce` kèm hạn 5 phút và xóa nguyên tử ngay khi đổi lần đầu (chống replay).
- Backend xác minh `idToken` với JWKS từ `https://appleid.apple.com/.well-known/openid-configuration`, hỗ trợ Apple Private Email Relay, không giả định Apple trả lại email/họ tên ở các lần đăng nhập tiếp theo (tra cứu theo `ProviderSubject` đã lưu).
- Đổi `authorizationCode` tại `https://appleid.apple.com/auth/token` bằng client secret `ES256` ký từ Apple `.p8` key, mã hóa Apple refresh token qua ASP.NET Core Data Protection, và gọi `https://appleid.apple.com/auth/revoke` khi người dùng xóa tài khoản.

### 3.3. Quy tắc chống chiếm đoạt qua liên kết tài khoản
- **Không tự động gộp tài khoản chỉ dựa trên email trùng.** Nếu một đăng nhập Google/Apple mới có email trùng với tài khoản đã tồn tại, server trả `409 ACCOUNT_LINK_PROOF_REQUIRED`.
- Người dùng phải đăng nhập vào tài khoản hiện có và gọi `POST /api/v1/auth/links/{provider}` kèm mật khẩu hiện tại để chứng minh quyền sở hữu.

---

## 4. Bảo Mật Phiên & Xoay Vòng Refresh Token

- **Access Token (JWT `HS256`):** Hiệu lực ngắn **10 phút**, chứa `sub` (`UserId`), `sid` (`FamilyId`), `jti` và `auth_time`. Trong sự kiện `OnTokenValidated`, server đối chiếu trực tiếp bảng `UserSessions` để đảm bảo phiên thiết bị chưa bị thu hồi hoặc hết hạn (cho phép đăng xuất/thu hồi tức thì).
- **Refresh Token (Opaque 48 bytes ngẫu nhiên mã hóa Base64Url):**
  - Hiệu lực 30 ngày; chỉ lưu bản băm SHA-256 (`RefreshTokenHash`) trong SQL Server.
  - Trên thiết bị di động, chỉ lưu `refreshToken` và `userId` trong `FlutterSecureStorage` (Android Keystore / iOS Keychain).
  - **Rotation & Reuse Detection:** Mỗi lần gọi `POST /api/v1/auth/refresh`, dòng phiên cũ được đánh dấu `RotatedAt` nguyên tử bằng `ExecuteUpdateAsync`. Nếu một refresh token đã xoay bị gửi lại lần thứ hai (`changed == 0`), hệ thống lập tức thu hồi toàn bộ chuỗi phiên `FamilyId` của thiết bị đó để chặn kẻ tấn công đánh cắp token.
