# UI Option A — Minimalist One-Handed Audio Utility (Sub-Agent B)

## 1. Triết Lý Thiết Kế
Loại bỏ hoàn toàn bước nhảy trung gian từ Trang chủ sang tab Thêm. Toàn bộ luồng **Dán link → Nhận diện → Chọn nơi lưu → Tải MP3 → Nghe ngay** được gom vào một **Main Download Card** duy nhất trên Trang chủ (`/`), sử dụng bề mặt đặc (`solid surface`) độ tương phản cao theo đúng bảng màu Navy & Off-White của `AGENTS.md`, không dùng `BackdropFilter` gây sụt giảm FPS trên GPU phổ thông (Mali-G57 trên Galaxy A06 5G).

---

## 2. Cấu Trúc Màn Hình Smart Download Home (`/`)

1. **Header gọn (tương thích 200% cỡ chữ):**
   - Cụm nhận diện trái: Biểu tượng chữ `T` cách điệu trên nền `primaryNavy` + tên **TMusic** + lời chào ngắn theo tên người dùng thật (`Xin chào, <Tên>` hoặc `Thư viện nhạc cá nhân` khi chưa đặt tên).
   - Cụm tiện ích phải (`48x48dp`): Nút **Avatar** mở Hồ sơ & nút **Cài đặt** (`/settings`).
2. **Main Download Card (4 trạng thái rõ ràng: `IDLE -> RESOLVING -> READY -> DOWNLOADING/COMPLETED`):**
   - **Ô nhập URL + Nút Xóa (`✕`):** Chỉ hiện nút xóa khi có nội dung; bàn phím tự thu gọn ngay khi dán hoặc bấm nhận diện.
   - **Auto-Detect thông minh không spam request:** Chỉ tự động gọi `/songs/resolve` khi người dùng bấm nút **Dán**, dán chuỗi URL hợp lệ (`>= 12` ký tự khớp allowlist YouTube/TikTok) với debounce `250ms`, hoặc nhận link từ Android Share Sheet — tuyệt đối không gọi API trên từng phím gõ.
   - **Song Preview trung thực:** Chỉ hiển thị thumbnail, tiêu đề gốc, tác giả gốc và thời lượng thật từ API; không sinh dữ liệu giả. Tên lưu riêng được điền sẵn và thu gọn sau nút **Đổi tên lưu riêng** để 90% thao tác không cần mở bàn phím.
   - **Album Selector một tay:** Hiển thị chip chọn nhanh **Gần nhất: `<Tên Album>`**, **Chưa phân loại**, và nút **Đổi album… / + Tạo album mới** mở Bottom Sheet ở vùng ngón cái dưới màn hình.
   - **Nút chính duy nhất `Tải MP3` (`54dp`):** Chỉ bật khi nguồn hợp lệ và hệ thống sẵn sàng tải MP3; hiển thị tiến độ thực `0–100%` ngay tại chỗ, không tạo bản ghi hay tiến trình trùng.
   - **Hành động sau khi tải xong:** **Phát MP3**, **Xem trong album** (hoặc **Gắn vào album** nếu đang ở Chưa phân loại), **Xem file đã lưu** (`/local`), và **Tải liên kết khác**.
3. **Khu vực thư viện gọn phía dưới:**
   - **Chưa phân loại** (chỉ hiện khi có bài chưa gắn album, tối đa 3 mục kèm nút gắn nhanh vào album).
   - **Mới lưu gần đây** (tối đa 4 mục kèm nút phát nhanh 1 chạm cho các bài đã có MP3).

---

## 3. Đếm Số Chạm Thực Tế (Golden Path)
- **Lưu vào Album gần nhất / Chưa phân loại:** `2 chạm` (`Dán` → tự nhận diện → `Tải MP3`).
- **Đổi sang Album khác qua Bottom Sheet:** `4 chạm` (`Dán` → `Đổi album…` → chọn Album → `Tải MP3`).
- **Chia sẻ trực tiếp từ YouTube / TikTok sang TMusic:** `1 chạm` (`Tải MP3`).

---

## 4. Phản Biện Đa Phương Án & Đánh Giá App Icon
- **Phản biện hiệu ứng kính mờ (Glassmorphism nặng):** Trên GPU Tile-Based (như Mali-G57 của Galaxy A06 5G), nhiều lớp `BackdropFilter` buộc flush bộ đệm tile ra RAM ngoài mỗi khung hình đúng lúc thanh tiến trình MP3 cập nhật liên tục và bàn phím trượt lên xuống, dễ gây rớt khung hình và giảm độ tương phản chữ trên ảnh bìa sáng màu.
- **Đánh giá 3 phương án App Icon:**
  - **Concept A (`T` + nốt nhạc rời):** Dễ hiểu nhưng tách làm 2 khối nhỏ trong vùng an toàn `66dp`, dễ dính nét ở kích thước `29pt`. (`7.5/10`)
  - **Concept B (Sóng âm tối giản):** Sạch nhưng dễ nhầm với ứng dụng Ghi âm mặc định, mất nhận diện chữ `T`. (`6.0/10`)
  - **Concept C (Chữ `T` cách điệu thành biểu tượng âm nhạc — Khuyên dùng):** Thanh ngang chữ `T` nghiêng nhẹ như dầm nốt móc đơn và chân chữ `T` kết thúc bằng bầu nốt nhạc tròn đặc trên nền `#17283D` Navy — 1 khối duy nhất, sắc nét ở mọi kích thước nhỏ. (`9.5/10`)
