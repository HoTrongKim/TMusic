# SECURITY ARCHITECTURE — TMUSIC V2

## 1. Quản Lý Bí Mật (Secrets Management)

- File `.env` gốc của người dùng được bảo toàn nguyên vẹn byte-for-byte và nằm trong `.gitignore` / `.dockerignore`.
- Không có API key, chuỗi kết nối chứa mật khẩu hay khóa ký JWT nào xuất hiện trong mã nguồn hoặc cấu hình đóng gói ứng dụng di động.
- Nạp biến môi trường trong `scripts/Load-Environment.ps1` dùng trình phân tích dòng `KEY=value` thuần, không dùng `Invoke-Expression` và không in giá trị ra terminal.

---

## 2. Xác Thực & Phân Quyền Mức Đối Tượng (Anti-IDOR / BOLA)

- **Identity & JWT:** Mật khẩu băm bằng ASP.NET Core Identity PBKDF2; JWT `HS256` ngắn hạn 10 phút kiểm tra phiên hoạt động trực tiếp trên SQL Server; refresh token 48 bytes ngẫu nhiên chỉ lưu SHA-256 kèm cơ chế phát hiện dùng lại (reuse detection) thu hồi toàn bộ chuỗi phiên thiết bị.
- **Chống IDOR:**
  - Server luôn đọc `UserId` từ claim `sub` đã xác thực (`ApiSupport.UserId`), không bao giờ nhận `userId` từ body/query của client cho dữ liệu cá nhân.
  - Mọi truy vấn `Albums`, `UserLibrarySongs`, `AlbumSongs`, `UserSessions`, `ImportPreviews`, `ImportJobs` và file ảnh bìa `/api/v1/covers/{userId}/{fileName}` đều kiểm tra quyền sở hữu. Truy cập chéo giữa hai tài khoản trả `404 NotFound` để không tiết lộ sự tồn tại của tài nguyên.
  - Ràng buộc khóa ngoại kép ở tầng SQL Server (`(AlbumId, UserId)` và `(UserLibrarySongId, UserId)`) chặn mọi khả năng gắn nhầm dữ liệu chéo tài khoản ngay cả khi có lỗi logic ứng dụng.

---

## 3. Chống SSRF, Unvalidated Redirects & SQL Injection

- **SQL Injection:** Toàn bộ truy vấn dữ liệu dùng LINQ tham số hóa qua Entity Framework Core; không nối chuỗi SQL thủ công.
- **SSRF & DNS Rebinding (`SafeHttpTransport`):**
  - Chỉ cho phép kết nối HTTPS cổng `443` tới danh sách trắng tên miền chính xác (`www.googleapis.com`, `vt.tiktok.com`, `vm.tiktok.com`, `www.tiktok.com`, `tiktok.com`).
  - Phân giải DNS một lần, kiểm tra toàn bộ địa chỉ IPv4 phải là địa chỉ công khai (chặn loopback, RFC1918, link-local `169.254.0.0/16`, CGNAT `100.64.0.0/10`, multicast, documentation, IPv6/mapped), rồi mở socket thẳng tới IP đã xác thực trong khi TLS vẫn kiểm tra SNI/chứng chỉ gốc.
  - Chuyển hướng TikTok shortlink bị giới hạn tối đa 5 bước, chống vòng lặp và kiểm tra lại allowlist ở từng bước.
  - Phản hồi JSON bị giới hạn dung lượng đọc thực tế (`128 KB` – `1 MB`) và độ sâu JSON (`MaxDepth = 32`).

---

## 4. Chống XSS Trong Trình Phát WebView

- Backend loại bỏ hoàn toàn trường `html` từ phản hồi TikTok oEmbed.
- Mobile (`OfficialEmbedPlayer`) chỉ dựng URL nhúng HTTPS từ `providerContentId` đã khớp biểu thức chính quy chặt (`^[A-Za-z0-9_-]{11}$` cho YouTube, `^[0-9]{1,24}$` cho TikTok) và chặn mọi điều hướng ra ngoài miền nhúng chính thức qua `NavigationDelegate`.

---

## 5. Bảo Mật Upload Ảnh Bìa Album (`CoverImageStorage`)

- **Giới hạn kích thước:** Mặc định Kestrel giới hạn body `128 KB` cho toàn bộ JSON API; riêng endpoint upload ảnh bìa giới hạn cứng `2 MB` (`2.097.152 bytes`).
- **Kiểm tra định dạng thực tế (Magic Bytes & Container Parsing):** Không tin `Content-Type` hay phần mở rộng tên file do client gửi. Chỉ chấp nhận PNG, JPEG hoặc WebP có cấu trúc nhị phân hợp lệ và kích thước khung hình `1–4096px` (từ chối SVG, HTML, script hoặc file hỏng CRC).
- **Loại bỏ Metadata Nhạy Cảm:**
  - **PNG:** Chỉ giữ các chunk hiển thị thiết yếu (`IHDR`, `PLTE`, `tRNS`, `IDAT`, `IEND`) có kiểm tra lại CRC-32; gỡ bỏ hoàn toàn `eXIf`, `tEXt`, `zTXt`, `iTXt`, `tIME`, `iCCP`.
  - **JPEG:** Gỡ bỏ toàn bộ các phân đoạn `APP1`–`APP15` (EXIF GPS, XMP, IPTC) và `COM` comment.
  - **WebP:** Gỡ bỏ các chunk `EXIF`, `XMP `, `ICCP` và xóa cờ metadata trong `VP8X`.
- **Lưu trữ & Phục vụ An Toàn:** Tên file sinh ngẫu nhiên `{Guid:N}.{png|jpg|webp}`, lưu ngoài thư mục web tĩnh (`App_Data/covers/{userId:N}/`), không có quyền thực thi, và chỉ trả về qua endpoint yêu cầu xác thực kèm `Content-Security-Policy: default-src 'none'; sandbox` + `X-Content-Type-Options: nosniff`.
