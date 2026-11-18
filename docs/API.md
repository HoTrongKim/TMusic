# API SPECIFICATION — TMUSIC V2

- **Base Path:** `/api/v1`
- **OpenAPI JSON (Development / Testing):** `GET /openapi/v1.json`
- **Health Checks:** `GET /health` (liveness), `GET /health/ready` (kiểm tra kết nối SQL Server)
- **Định dạng lỗi chuẩn:** RFC 7807 `application/problem+json` kèm trường mở rộng `code` và `traceId` (đồng bộ với header `X-Correlation-ID`).

---

## 1. Hệ Thống & Khả Năng Môi Trường

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| `GET` | `/api/v1/system/capabilities` | Public | Trả về trạng thái cấu hình thật: `emailAuthentication`, `googleSignIn`, `appleSignIn`, `youtubeMetadata`, `tikTokLinks`, `multiPlaylistMerge`, `crossProviderMixedCollections`, `licensedAudioDownload`. |

---

## 2. Authentication & Sessions (`/api/v1/auth`)

Áp dụng rate-limit policy `auth`.

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| `POST` | `/auth/register` | Public | Đăng ký bằng `email`, `password` (12–128 ký tự đủ hoa/thường/số/ký hiệu), `displayName` và cấp ngay phiên đăng nhập (`201 Created` `{ accessToken, refreshToken, expiresAt, userId }`) mà không bắt buộc xác minh email. |
| `POST` | `/auth/login` | Public | Đăng nhập bằng `{ email, password, deviceName }` -> `200 OK` `{ accessToken, refreshToken, expiresAt, userId }`. Chống timing-attack và tự khóa sau 5 lần sai liên tiếp. |
| `POST` | `/auth/google` | Public | Đăng nhập bằng Google OIDC `idToken` -> xác minh chữ ký/issuer/audience/expiry -> `200 OK` hoặc `409 ACCOUNT_LINK_PROOF_REQUIRED` nếu trùng email tài khoản hiện có. |
| `POST` | `/auth/apple/challenge` | Public | Cấp `nonce` ngẫu nhiên dùng một lần (hiệu lực 5 phút) cho Sign in with Apple. |
| `POST` | `/auth/apple` | Public | Đăng nhập bằng Apple `idToken` + `authorizationCode` (xác minh nonce & lưu refresh token mã hóa DataProtection phục vụ thu hồi). |
| `POST` | `/auth/links/apple/challenge` | Bearer | Cấp `nonce` gắn với `userId` hiện tại để liên kết Apple. |
| `POST` | `/auth/links/{provider}` | Bearer | Liên kết `google` hoặc `apple` vào tài khoản đang đăng nhập (bắt buộc xác nhận `password` hiện tại). |
| `POST` | `/auth/refresh` | Public | Xoay vòng `refreshToken` một lần -> cấp cặp token mới; nếu phát hiện dùng lại token cũ sẽ thu hồi toàn bộ chuỗi phiên thiết bị (`FamilyId`). |
| `POST` | `/auth/forgot-password` | Public | Gửi mã đặt lại mật khẩu qua SMTP -> `202 Accepted`. |
| `POST` | `/auth/reset-password` | Public | Đặt lại mật khẩu bằng `{ email, code, password }` và thu hồi toàn bộ phiên cũ -> `204 No Content`. |
| `POST` | `/auth/change-password` | Bearer | Đổi mật khẩu bằng `{ currentPassword, newPassword }` và thu hồi các phiên -> `204 No Content`. |
| `POST` | `/auth/logout` | Bearer | Thu hồi phiên thiết bị hiện tại -> `204 No Content`. |
| `POST` | `/auth/logout-all` | Bearer | Thu hồi toàn bộ phiên của tài khoản trên mọi thiết bị -> `204 No Content`. |
| `GET` | `/auth/sessions` | Bearer | Liệt kê các phiên thiết bị đang hoạt động (`id`, `deviceName`, `createdAt`, `expiresAt`, `isCurrent`). |
| `DELETE` | `/auth/sessions/{id}` | Bearer | Thu hồi một phiên thiết bị cụ thể theo `FamilyId` -> `204 No Content`. |

---

## 3. Người Dùng & Cài Đặt (`/api/v1/users/me`)

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| `GET` | `/users/me` | Bearer | Trả hồ sơ cá nhân: `id`, `email`, `displayName`, `avatarUrl`, `emailVerified`, `createdAt`, `authProviders`, `linkedProviders`. |
| `PATCH` | `/users/me` | Bearer | Cập nhật `displayName` và `avatarUrl` (`avatar:navy`, `avatar:blue`, `avatar:green`, `avatar:terracotta`, `avatar:slate`, `avatar:amber` hoặc rỗng). |
| `DELETE` | `/users/me` | Bearer | Xóa vĩnh viễn tài khoản, phiên, album, bài hát cá nhân và ảnh bìa tải lên (yêu cầu xác thực lại bằng `password` hoặc phiên OAuth vừa xác thực trong 10 phút; thu hồi Apple token nếu có). |
| `GET` | `/users/me/settings` | Bearer | Lấy cài đặt `{ theme, language, notificationsEnabled }`. |
| `PATCH` | `/users/me/settings` | Bearer | Lưu cài đặt giao diện (`system` \| `light` \| `dark`) và ngôn ngữ (`vi` \| `en`). |

---

## 4. Nhận Diện Link & Thư Viện Bài Hát

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| `POST` | `/songs/resolve` | Bearer | Chuẩn hóa URL YouTube/TikTok/SoundCloud, xử lý shortlink an toàn, lấy metadata được phép và kiểm tra trùng theo `(provider, providerContentId)`. Trả kèm `existingLibrarySongId` và `existingSong` nếu đã có trong thư viện. |
| `GET` | `/library/songs` | Bearer | Phân trang danh sách bài hát cá nhân (`page`, `pageSize`, `q`, `provider=youtube\|tiktok\|soundcloud`, `favorite`, `unorganized`, `sort=recent\|name`). |
| `POST` | `/library/songs` | Bearer | Lưu bài hát vào thư viện cá nhân với `{ url, customTitle, customArtist, albumIds, downloadMp3, rightsConfirmed }` (nếu bật `downloadMp3: true` kèm `rightsConfirmed: true`, hệ thống tạo ngay `AudioDownloadJob` để trích xuất file `.mp3` riêng tư vào Album). |
| `GET` | `/library/songs/{id}` | Bearer | Lấy chi tiết bài hát cá nhân kèm `audioReady`, `audioStatus`, `audioStreamUrl` và header `ETag`. |
| `POST` | `/library/songs/{id}/download` | Bearer | Đưa bài hát vào hàng đợi xử lý trích xuất MP3 (`yt-dlp` + `FFmpeg`) khi người dùng xác nhận quyền (`{ rightsConfirmed: true }`) -> `202 Accepted`. |
| `GET` | `/library/songs/{id}/audio` | Bearer | Phát / tải luồng `audio/mpeg` riêng tư chỉ dành cho đúng chủ sở hữu bài hát (`Range` `206 Partial Content` hỗ trợ tua nhạc & chống IDOR). |
| `DELETE` | `/library/songs/{id}/audio` | Bearer | Xóa file MP3 riêng của bài hát nhưng vẫn giữ nguyên liên kết & tên tùy chỉnh trong Album. |
| `GET` | `/downloads` | Bearer | Danh sách các tiến trình tải MP3 (`status`, `progressPercent`, `songTitle`, `audioStreamUrl`). |
| `GET` | `/downloads/{id}` | Bearer | Xem tiến độ trực tiếp (`0–100%`) của một tiến trình tải MP3. |
| `DELETE` | `/downloads/{id}` | Bearer | Hủy tiến trình QUEUED/RUNNING và thu hồi lease để worker cũ không công bố MP3; với tác vụ đã kết thúc thì dọn lịch sử. Không xóa liên kết bài hát. |
| `POST` | `/downloads/{id}/retry` | Bearer | Thử lại tác vụ FAILED/CANCELED của chính chủ; tôn trọng xác nhận quyền đã lưu. |
| `PATCH` | `/library/songs/{id}` | Bearer + `If-Match` | Đổi `customTitle` và `customArtist` (không ghi đè metadata gốc). |
| `PATCH` | `/library/songs/{id}/favorite` | Bearer + `If-Match` | Bật/tắt `isFavorite`. |
| `DELETE` | `/library/songs/{id}` | Bearer + `If-Match` | Xóa bài hát khỏi thư viện cá nhân và gỡ khỏi mọi album của user. |

---

## 5. Quản Lý Album & Ảnh Bìa (`/api/v1/albums`)

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| `GET` | `/albums` | Bearer | Phân trang danh sách album (`page`, `pageSize`, `q`, `sort=recent\|name\|order`, `favorite`). |
| `POST` | `/albums` | Bearer | Tạo album riêng tư mới (hỗ trợ header `Idempotency-Key`). |
| `PATCH` | `/albums/order` | Bearer | Sắp xếp thứ tự danh sách album bằng `{ albumIds }`. |
| `GET` | `/albums/{id}` | Bearer | Lấy chi tiết album kèm `songCount` và header `ETag`. |
| `PATCH` | `/albums/{id}` | Bearer + `If-Match` | Cập nhật tên, mô tả, màu bìa preset, trạng thái yêu thích. |
| `DELETE` | `/albums/{id}` | Bearer + `If-Match` | Xóa album (không xóa các bài hát khỏi thư viện cá nhân; tự dọn file ảnh bìa nếu có). |
| `POST` | `/albums/{id}/cover` | Bearer + `If-Match` | Upload `multipart/form-data` (`file` PNG/JPEG/WebP <= 2 MB). Kiểm tra magic bytes, loại bỏ EXIF/XMP/comment chunks, lưu tên file ngẫu nhiên an toàn. |
| `DELETE` | `/albums/{id}/cover` | Bearer + `If-Match` | Xóa ảnh bìa của album. |
| `GET` | `/covers/{userId}/{fileName}` | Bearer | Phục vụ ảnh bìa riêng tư chỉ cho đúng chủ sở hữu (`userId == sub`) với `Content-Security-Policy: default-src 'none'; sandbox` và `X-Content-Type-Options: nosniff`. |
| `GET` | `/albums/{id}/songs` | Bearer | Phân trang danh sách bài hát trong album theo thứ tự `Position`. |
| `POST` | `/albums/{id}/songs` | Bearer + `If-Match` | Thêm bài hát đã có trong thư viện (`userLibrarySongId`) vào album. |
| `DELETE` | `/albums/{id}/songs/{songId}` | Bearer + `If-Match` | Bỏ bài hát khỏi album (vẫn giữ bài hát trong thư viện cá nhân). |
| `PATCH` | `/albums/{id}/songs/order` | Bearer + `If-Match` | Sắp xếp lại toàn bộ thứ tự bài hát trong album (`{ songIds }`). |

---

## 6. Tìm Kiếm Hợp Nhất & Nhập Playlist

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| `GET` | `/search?q=` | Bearer | Tìm kiếm đồng thời cả `albums` và `songs` của người dùng hiện tại. |
| `POST` | `/imports/previews` | Bearer | Tạo bản xem trước bất đồng bộ cho một YouTube playlist hợp lệ. |
| `GET` | `/imports/previews/{id}` | Bearer | Xem trạng thái và danh sách ứng viên của bản xem trước playlist. |
| `POST` | `/imports` | Bearer | Khởi chạy job nhập playlist vào album (bắt buộc `Idempotency-Key`). |
| `GET` | `/imports` / `/imports/{id}` | Bearer | Liệt kê và theo dõi tiến độ nhập playlist; hỗ trợ `/cancel` và `/retry`. |

## 7. Khách và chia sẻ album

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| `POST` | `/guest/songs/resolve` | Không | Nhận `{ url }`, trả metadata nullable/fallback. Album/bài khách vẫn được lưu trong ứng dụng trên máy. |
| `POST` | `/guest/songs/extract-mp3` | Không | Nhận `{ url, rightsConfirmed: true }`, trả MP3 sau xử lý đồng bộ có rate limit và hạn mức extraction chung. Không cung cấp tiến độ chi tiết/hủy job khách qua API hiện tại. |
| `GET` / `POST` | `/albums/{id}/share` | Bearer | Đọc hoặc tạo mã chia sẻ album thuộc chính chủ. |
| `DELETE` | `/albums/{id}/share` | Bearer | Thu hồi mã đang hoạt động. Bản đã nhập vào thư viện người nhận vẫn thuộc người nhận. |
| `GET` | `/shared-albums/{code}` | Không | Preview metadata có rate limit: `page` mặc định 1, hợp lệ 1–10000; `pageSize` mặc định 200, hợp lệ 1–200. Trả `songCount` tổng, `page`, `pageSize`, `hasMore`, `songs` của trang và thông tin album/người chia sẻ. Mã đã thu hồi trả 404. |
| `POST` | `/shared-albums/{code}/import` | Bearer | Sao chép đủ metadata và thứ tự vào album mới của người nhận; dùng lại bài nguồn đã có, không chia sẻ quyền đọc MP3/cover riêng. Hỗ trợ tối đa 5000 bài mỗi album, vượt giới hạn trả 409 `SHARED_ALBUM_TOO_LARGE` trước khi tạo album. |

Client khách tải và kiểm tra toàn bộ preview theo trang trước khi lưu album cục bộ, giữ original title/creator/content ID tách khỏi tên tùy chỉnh. Khi các trang không còn nhất quán, client trả `SHARED_ALBUM_CHANGED` để người dùng thử lại.
