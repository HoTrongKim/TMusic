# Quyết Định Kiến Trúc & Kỹ Thuật Cuối Cùng — TMusic V3

## 1. Giải Quyết Xung Đột Quy Tắc (`AGENTS.md` vs Prompt V3)
- **Xung đột:** `AGENTS.md` dòng 14 cấm *glassmorphism* và *neon gradients*, trong khi Prompt V3 Mục 7.4 yêu cầu hiệu ứng trong suốt có kiểm soát kèm fallback khi giảm FPS.
- **Quyết định:** Đặt quy tắc bảo vệ thiết kế trong `AGENTS.md` lên cao nhất: **không sử dụng `BackdropFilter` / `ImageFilter.blur` hay gradient neon**. Thay vào đó sử dụng phân lớp bề mặt đặc Material 3 Tonal (`#F7F7F3` / `#F2F3EE` / `#FFFFFF` ở Light; `#101B2A` / `#1A2B40` / `#162538` ở Dark) kết hợp viền mảnh `1dp` `outlineVariant`. Cách này vừa đẹp chuẩn thương mại, vừa đảm bảo độ tương phản WCAG AA/AAA, vừa giữ tốc độ khung hình 60/90 FPS tuyệt đối trên Samsung Galaxy A06 5G.

## 2. Bảo Toàn Toàn Bộ Backend & Dữ Liệu
- Giữ nguyên 100% engine `yt-dlp` + `FFmpeg`, `AudioDownloadProcessor`, `AudioStorage`, các migration đã áp dụng và toàn bộ dữ liệu thực tế trong `TMusicV2Dev` (`4` users, `1` album, `2` bài hát, `2` download jobs).
- Chỉ bổ sung 1 bản vá an toàn nhỏ trong `AudioDownloadEndpoints.Enqueue`: nếu bài hát đã ở trạng thái `READY` và file vật lý thực sự tồn tại trong `AudioStorage` (kể cả khi người dùng đã xóa lịch sử job cũ), trả ngay `200 OK` thay vì tạo job tải lại dư thừa.

## 3. Cải Tiến Kiến Trúc Flutter & UX
1. **Smart Download Home (`/`):** Gom toàn bộ luồng dán URL, tự động nhận diện có kiểm soát (không spam theo từng phím gõ), xem trước trung thực, chọn **Album gần nhất / Chưa phân loại / Album khác / Tạo album mới**, nút chính **Tải MP3**, tiến độ `0–100%` và bộ hành động sau tải (**Phát MP3**, **Xem trong album**, **Xem file đã lưu**) ngay trên Trang chủ.
2. **Đơn nhất hóa quản lý tiến trình (`DownloadJobsController`):** Một bộ theo dõi tiến độ duy nhất kèm khóa chống ghi trùng file cục bộ, cung cấp `Badge` số tiến trình đang chạy trên tab **Tải xuống**.
3. **Điều hướng 4 Tab mới:** `Trang chủ` (`/`) · `Album` (`/albums`, hỗ trợ lọc cả Bài hát & Chưa phân loại) · `Tải xuống` (`/downloads`) · `Cá nhân` (`/profile`), kèm các route redirect tương thích ngược (`/library` -> `/albums`, `/add` -> `/`) và hỗ trợ Android Share Intent trực tiếp vào Trang chủ.
4. **Loại bỏ an toàn Embedded Player:** Gỡ `official_embed_player.dart` và dependency `webview_flutter` (chỉ phục vụ nhúng WebView), bảo toàn tuyệt đối `just_audio`, `audio_service`, `LocalAudio`, `LocalLibrary` và nút mở ứng dụng gốc khi nguồn không cho phép tải.
5. **Khắc phục triệt để Offline Playback (`TEST 02` & `TEST 08`):**
   - Cho phép khôi phục phiên đọc cục bộ khi mở app lúc mất mạng (`status == null` trên `/auth/refresh` không làm kẹt ở màn hình `/loading`).
   - Bổ sung đọc/ghi `LibraryCache` cho chi tiết từng bài hát (`song(id)`) và chi tiết album (`album(id)`), ưu tiên kiểm tra file cục bộ trong `LocalLibrary` trước khi gọi mạng để phát offline tức thì không cần kết nối API.
6. **App Icon mới (Concept C):** Tạo bộ icon cả 3 phương án để đối chiếu tại `docs/upgrade/icons/`, triển khai chính thức **Concept C (Chữ `T` cách điệu thành nốt nhạc âm thanh trên nền Navy `#17283D`)** cho Android Adaptive Icon (`mipmap-anydpi-v26` + `drawable` + `mipmap-*`) và iOS (`AppIcon.appiconset`), giữ nguyên `applicationId = com.tmusic.tmusic` để cập nhật không mất dữ liệu.
