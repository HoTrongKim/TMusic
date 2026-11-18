# UI/UX DESIGN SYSTEM — TMUSIC V2

## 1. Triết Lý & Định Hướng Thị Giác

TMusic hiện dùng **Premium Spotify-Inspired**: dark charcoal mặc định, xanh accent, quick-pick grid, carousel và album header có sắc độ từ bìa; light mode tùy chọn. `mobile/lib/app/theme/tmusic_theme.dart` và `AGENTS.md` là tham chiếu hiện hành; palette navy giai đoạn đầu đã được thay thế.

- Glassmorphism nhẹ chỉ dành cho mini-player, thanh điều hướng và sheet nổi. Không hiển thị gợi ý giả hoặc nút chức năng chưa kết nối; không có nút clipboard-paste.
- **Triết lý thao tác cốt lõi:**
  `COPY LINK -> ADD TO TMUSIC -> NAME -> CHOOSE ALBUM -> SAVE`

---

## 2. Hệ Thống Màu Sắc Hiện Hành (Light & Dark Mode)

| Vai trò | Light Mode | Dark Mode | Mục đích sử dụng |
|---|---|---|---|
| **Accent** | `#1DB954` | `#1DB954` | Hành động chính và trạng thái thành công |
| **Background** | `#F6F6F6` | `#121212` | Nền toàn màn hình |
| **Surface** | `#FFFFFF` | `#181818` | Bề mặt nội dung |
| **Card** | `#EBEBEB` | `#282828` | Thẻ nội dung |
| **Main Text** | `#121212` | `#FFFFFF` | Tiêu đề, tên bài/album |
| **Secondary Text** | `#5E5E5E` | `#B3B3B3` | Nghệ sĩ, metadata phụ |
| **Error** | `#D32F2F` | `#FF6B6B` | Lỗi và hành động xóa |

Mục tiêu accessibility là tương phản đọc được và touch target >=48dp; cần kiểm tra từng tổ hợp màu thực tế, không suy ra chứng nhận WCAG cho toàn bộ ứng dụng chỉ từ bảng token.

---

## 3. Typography, Spacing & Khả Năng Tiếp Cận (Accessibility)

- **Typography:** Hỗ trợ đầy đủ dấu tiếng Việt với chiều cao dòng rộng (`1.2` – `1.5`), thang kích thước rõ ràng (`28 / 22 / 18 / 16 / 15 / 13sp`).
- **Spacing & Bo góc:** Lưới khoảng cách bội số của `4` và `8` (`4, 8, 12, 16, 20, 24, 32dp`); bo góc vừa phải (`12dp` cho nút/ô nhập/thẻ, `8dp` cho ảnh bìa).
- **Touch Targets & Screen Reader:**
  - Mọi nút bấm (`FilledButton`, `OutlinedButton`, `TextButton`, `IconButton`) có kích thước chạm tối thiểu `>= 48x48dp`.
  - Hỗ trợ phóng to chữ động (`Dynamic Text Scaling`) lên tới **200%** ngay cả trên màn hình hẹp **320dp** mà không bị tràn khung (`RenderFlex overflow`) — được kiểm chứng tự động trong `mobile/test/widget_test.dart`.
  - Các thông báo lỗi/trạng thái dùng `Semantics(liveRegion: true)` kết hợp biểu tượng + văn bản (không bao giờ chỉ dùng màu sắc đơn lẻ để truyền tải trạng thái).

---

## 4. Luồng Điều Hướng Và Danh Mục Màn Hình

Điều hướng hiện tại: **Trang chủ** (`/`) · **Thư viện** (`/albums`) · **Tải xuống** (`/downloads`) · **Cá nhân** (`/profile`). `/library` và `/add` là route tương thích chuyển về thư viện/trang chủ. Khách dùng nội dung local; các route bảo mật/phiên/xóa tài khoản cần đăng nhập. Danh mục đánh số dưới đây bắt nguồn từ bản thiết kế đầu, không phải cam kết số màn hình độc lập hiện tại.

### Nhóm Xác Thực (1–7)
1. **Splash Screen** (`/loading`) — Khôi phục phiên từ `FlutterSecureStorage` kèm giao diện thử lại nếu mất mạng.
2. **Welcome Screen** (`/welcome`) — Giới thiệu giá trị cốt lõi, dẫn tới Đăng nhập hoặc Tạo tài khoản.
3. **Login Screen** (`/auth/login`) — Email/mật khẩu (có nút ẩn/hiện), đăng nhập Google/Apple khi server bật khả năng tương ứng.
4. **Register Screen** (`/auth/register`) — Tên hiển thị, email, mật khẩu kèm hướng dẫn độ mạnh rõ ràng.
5. **Email Verification Screen** (`/auth/verify`) — Nhập mã xác minh từ email và gửi lại mã.
6. **Forgot Password Screen** (`/auth/forgot`) — Yêu cầu mã khôi phục mật khẩu.
7. **Reset Password Flow** (`/auth/reset`) — Đặt mật khẩu mới và tự động thu hồi các phiên cũ.

### Nhóm Ứng Dụng Chính (8–23)
Trang chủ tích hợp nhập URL; tab Tải xuống quản lý công việc tải và audio đã lưu.

8. **Home Screen** (`/`) — Lời chào cá nhân hóa, khung dán nhanh URL, Bài mới thêm, Album yêu thích, Album mở gần đây, và mục Tiếp tục sắp xếp (các bài chưa gắn album).
9. **Library Screen** (`/library`) — Chuyển đổi Album / Bài hát, tìm kiếm tức thì, sắp xếp (Mới nhất / Theo tên / Thứ tự riêng), lọc nguồn (YouTube / TikTok), chuyển đổi Lưới/Danh sách (`Grid/List`) và sắp xếp lại thứ tự album.
10. **Album Detail Screen** (`/albums/:id`) — Ảnh bìa, mô tả, thêm link mới vào album, chọn bài đã có trong thư viện, sắp xếp thứ tự bài, bỏ bài khỏi album (tách biệt rõ với xóa khỏi thư viện), xóa album.
11. **Create Album Screen** (`/albums/new`) — Đặt tên, mô tả, chọn 6 màu bìa chuẩn hoặc tải ảnh bìa từ máy.
12. **Edit Album Screen** (`/albums/:id/edit`) — Đổi tên, mô tả, thay đổi hoặc gỡ ảnh bìa tải lên.
13. **Nhập bài qua URL** (Trang chủ, `/add` chuyển hướng) — Nhập URL bằng bàn phím/menu hệ điều hành hoặc nhận Android Share Intent / iOS Share Extension; không có nút ứng dụng đọc/dán clipboard.
14. **Song Metadata Preview** (`SongMetadataPreviewCard` trong `/add`) — Hiển thị nguồn, khả năng phát, ảnh thu nhỏ, tiêu đề gốc, phát hiện bài trùng kèm tên đã lưu, cho phép sửa tên riêng, chọn nhiều album hoặc tạo album mới tại chỗ, và mở thẳng album vừa lưu.
15. **Edit Song Screen** (`/songs/:id/edit`) — Chỉnh sửa tên tùy chỉnh, nghệ sĩ/ghi chú và cập nhật danh sách album chứa bài hát.
16. **Search Screen** (`/search`) — Tìm kiếm hợp nhất đồng thời cả Album và Bài hát qua `/api/v1/search`.
17. **Favorites Screen** (`/favorites`) — Hiển thị tập trung cả Album yêu thích và Bài hát yêu thích.
18. **Song / Playback** (`/songs/:id`, `/playback/:id`) — Thông tin bài, source attribution và external-open cho YouTube/TikTok/SoundCloud; audio native từ tệp trên thiết bị/MP3 hợp lệ đã tải. Embedded player/WebView đã gỡ.
19. **Profile Screen** (`/profile`) — Thông tin tài khoản, ảnh đại diện màu, các nhà cung cấp đăng nhập đã liên kết, lối tắt tới Yêu thích, Nhạc trên thiết bị (`/local`), Cài đặt, Bảo mật, Phiên thiết bị, Quyền riêng tư và Đăng xuất.
20. **Settings Screen** (`/settings`) — Chế độ Sáng/Tối/Hệ thống, Ngôn ngữ Tiếng Việt/English, xóa bộ nhớ đệm cục bộ.
21. **Account Security Screen** (`/security`) — Đổi mật khẩu, liên kết Google/Apple có xác minh mật khẩu, đăng xuất mọi thiết bị.
22. **Manage Sessions Screen** (`/sessions`) — Danh sách phiên thiết bị đang hoạt động và thu hồi từng thiết bị.
23. **Delete Account Flow** (`/account/delete`) — Màn hình xác nhận xóa vĩnh viễn tài khoản với xác thực lại mật khẩu và hộp kiểm đồng ý rõ ràng.
