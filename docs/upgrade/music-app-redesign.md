# TMusic V4 — Thiết Kế Lại Toàn Bộ Giao Diện Chuẩn Ứng Dụng Nghe Nhạc Thương Mại

Tài liệu tổng hợp nghiên cứu chuyên sâu từ 3 mã nguồn tham chiếu thông qua 3 Sub-Agent độc lập:
1. **`AdonisEnProvence/MusicRoom`** (React Native + Dripsy + Moti + XState)
2. **`Amineharrabi/Matchy`** (React Native + Expo + Custom Component System)
3. **`SWE-Team-9/Cross`** (Flutter + GoRouter + `just_audio` + `audio_service` + `CustomPaint` Waveform)

---

## 1. Đối Chiếu & Chọn Lọc Tinh Hoa UI/UX Từ 3 Repository

| Thành phần | Học từ `Matchy` | Học từ `MusicRoom` | Học từ `Cross` (Flutter) | Áp dụng vào `TMusic` |
|---|---|---|---|---|
| **Chế độ màu mặc định** | Mở mặc định ở **Dark Mode** sâu, tách 3 bậc nền độ tương phản thấp, dùng **Alpha-Tint `12%`** cho huy hiệu icon tròn | Nền tối `#121212` / `#212121` / `#2A2A2C`, danh sách phẳng không viền | OLED/Studio Dark + 3 bậc surface + giữ hỗ trợ chuyển đổi Sáng/Tối | **Mặc định Studio Midnight Dark (`#090E17` / `#111927` / `#182438`)**, đồng thời hỗ trợ **Warm Vinyl Light (`#F6F6F2`)** qua Segmented Selector trong Cài đặt. |
| **Triệt tiêu cảm giác Form Quản Trị** | Xóa bỏ hoàn toàn `Dropdown`, bảng kẻ ô; thay bằng **Segmented Pill Selector** & **Horizontal Pill Chips** | Lược bỏ viền hộp thừa, dùng khoảng thở hệ `4/8px` | Thanh Search & Input dạng **Full Pill (`radius: 24`) không viền thô** | Xóa toàn bộ `DropdownButtonFormField` và khung `Notice` thừa; dùng Pill Input, Segmented Tabs và Tinted Icon Badges `40x40`. |
| **Mini-Player** | — | Giữ sống state trình phát, chạm mở toàn màn hình, hỗ trợ Back thu gọn | **Floating Capsule Mini-Player** nổi phía trên Bottom Nav + **Vòng tròn tiến độ (`CircularProgressIndicator`) bao quanh nút Play** | **Floating Capsule Mini-Player** (`radius: 28`, đổ bóng mềm, không dùng blur nặng GPU) + ảnh bìa tròn + vòng tiến độ bao quanh nút Play/Pause + nút đổi chế độ lặp nhanh. |
| **Full-Screen Now Playing** | Ảnh bìa trung tâm đổ bóng sâu + nền phủ chuyển sắc êm (`alpha 0.25 -> 0`) | **Nút Play tròn đặc đảo ngược tương phản (`#FFFFFF` / Navy)** + **căn tâm quang học `+2px`** | **CustomPaint Waveform 2 tầng có bóng gương**, vuốt ngang ảnh bìa chuyển bài, `AnimatedScale` ảnh bìa khi Pause | Kết hợp cả 3: Ảnh bìa lớn co giãn theo Play/Pause + vuốt ngang chuyển bài + **CustomPaint Studio Waveform hiển thị trực tiếp vùng lặp `A–B`** + cụm 5 nút chuẩn Spotify/MusicRoom. |
| **Trang chủ (Home)** | Header gọn + Avatar tròn viền nhấn + **Kệ cuộn ngang bìa vuông `144x144`** | Tách khối rõ ràng, không dữ liệu giả | **Lưới Quick-Pick 2 cột** cho Album gần đây + Khối bài hát đóng khung bo `18px` | Thanh nhập link YouTube/TikTok dạng **Studio Capsule gọn** (**đã xóa hoàn toàn nút/chức năng Dán bộ nhớ tạm**) + Quick-Pick 2 cột + Kệ Album ngang + Khối bài mới phát 1 chạm. |
| **Chi tiết Album & Track Row** | Header bìa lớn + danh sách bo góc trên `24px` | Nút sắp xếp gọn | **Thanh tác vụ bất đối xứng** (trái: tim/thêm/menu; phải: Shuffle + nút Play tròn lớn `56dp`) + **Equalizer badge** cho bài đang phát | Áp dụng đầy đủ: Hero Album Stage + cặp nút Shuffle & Play tròn lớn + Track Row đánh số thứ tự `01, 02...` tự sáng icon sóng nhạc khi đang phát. |
