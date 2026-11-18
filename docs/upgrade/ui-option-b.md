# UI Option B — Premium Modern Audio App + Native Motion (Sub-Agent C & D)

## 1. Hướng Thiết Kế: "Warm Studio Acoustic" (Tuân Thủ Tuyệt Đối `AGENTS.md`)
Nâng tầm TMusic thành ứng dụng âm nhạc cá nhân hoàn thiện thương mại bằng hệ thống **3 tầng bề mặt Tonal Navy & Warm Off-White** (`scaffoldBackground` → `surfaceContainerLow` → `surfaceLowest`), viền mảnh `1dp` `outlineVariant`, số liệu thời lượng/phần trăm cố định độ rộng (`tabularFigures`), phản hồi xúc giác `HapticFeedback` tinh tế và chuyển động mượt dựa trên Flutter gốc (tự động tắt hoàn toàn khi hệ điều hành bật **Reduce Motion** `MediaQuery.disableAnimationsOf(context)`).

Tuyệt đối **không dùng `BackdropFilter` mờ đục tốn GPU** và **không dùng gradient neon** theo quy tắc bắt buộc của `AGENTS.md`.

---

## 2. Hệ Thống Token & Microinteractions

1. **Phân tầng màu sắc (Light / Dark / System):**
   - **Light:** Nền ấm `#F7F7F3`, khung nhóm `#F2F3EE`, thẻ chính `#FFFFFF`, chữ chính `#16202A` (`15.4:1`), nút chính Navy `#17283D` (`14.5:1`).
   - **Dark:** Nền sâu `#101B2A`, khung nhóm `#1A2B40`, thẻ chính `#162538`, chữ chính `#EEF2F6` (`15.1:1`), nút chính Ice Blue `#A8CBEA` (`10.1:1`).
   - Mở rộng `ThemeExtension<TMusicTokens>` cho màu `success`, `warning`, viền và bóng đổ siêu nhẹ để mọi màn hình, Dialog và Bottom Sheet chuyển đổi tức thời theo Light / Dark / System.
2. **Microinteractions & Native Motion (`motion.dart`):**
   - `PressableScale`: Nhấn nút chính co nhẹ `0.975` (`100ms easeOutCubic`), nhả ra `1.0` (`150ms`), giữ nguyên vùng chạm `>= 48x48dp`.
   - `AnimatedSize` (`220ms`): Mở rộng mượt thẻ `SmartDownloadCard` khi chuyển từ nhập link sang xem trước và tải MP3, xóa bỏ hiện tượng giật cục `350dp`.
   - `TweenAnimationBuilder`: Nội suy mượt thanh tiến độ `0–100%` giữa các nhịp cập nhật từ server.
   - `MiniPlayerDock`: Thanh điều khiển nhạc MP3 gọn ngay phía trên Bottom Navigation khi `LocalAudio` đang phát nhạc nền.

---

## 3. Kiến Trúc Riverpod 3 Tái Sử Dụng Không Nhân Đôi Logic

1. **`DownloadJobsController` (`downloadJobsProvider`):**
   - Gom toàn bộ bộ đếm thời gian (`Timer` `1.5s`) từ 3 màn hình rời rạc về **1 nguồn sự thật duy nhất**.
   - Khóa chống ghi trùng (`_syncingSongIds`) đảm bảo khi job đạt `COMPLETE`, file `song_<id>.mp3` chỉ được tải và lưu xuống điện thoại đúng 1 lần, đồng thời cấp số lượng job đang chạy cho `Badge` trên tab **Tải xuống**.
2. **`SmartDownloadController` + `SmartDownloadCard`:**
   - Tái sử dụng trọn vẹn pipeline nhận diện URL, kiểm tra trùng, chọn Album / Chưa phân loại và kích hoạt tải MP3 ngay trên `HomeScreen`.
   - Xử lý mượt cả liên kết chia sẻ từ ngoài ứng dụng (`Android Share Intent` → tự động nhận diện ngay trên Trang chủ) lẫn liên kết thêm vào album cụ thể (`/?album=<id>`).
3. **Cấu trúc 4 Tab mới (`StatefulShellRoute.indexedStack`):**
   - `0`: **Trang chủ** (`/`) — Trung tâm nhận diện URL & tải MP3 thông minh.
   - `1`: **Album** (`/albums`, kèm redirect tương thích `/library`) — Quản lý Album, Bài hát & mục Chưa phân loại.
   - `2`: **Tải xuống** (`/downloads`) — Hàng đợi, tiến độ, thử lại/hủy và truy cập file đã lưu trên máy.
   - `3`: **Cá nhân** (`/profile`) — Hồ sơ, Yêu thích, Cài đặt giao diện/ngôn ngữ và Bảo mật (loại bỏ mục Tải xuống trùng lặp).
