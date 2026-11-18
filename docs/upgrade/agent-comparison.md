# So Sánh Đa Tác Tử (Multi-Agent Tournament) — TMusic V2 → V3
**Điều phối & Chấm điểm cuối cùng:** Agent H (Independent Technical Judge)

---

## 1. Tóm Tắt Các Vòng Phản Biện (Rounds 1–4)

- **Round 1 (Phân tích độc lập):**
  - **Sub-Agent A/E/F** kiểm toán toàn bộ mã nguồn C# & Dart, phân tách chính xác các điểm phụ thuộc `OfficialEmbedPlayer`/`webview_flutter`, xác nhận Backend đã hỗ trợ sẵn `Chưa phân loại` (`albumIds: []`) và phát hiện **3 lỗi phát nhạc Offline thực tế** khi khởi động app lúc mất mạng.
  - **Sub-Agent B** thiết kế **Option A (Minimalist Utility)** tối ưu thao tác 1 tay **2 chạm** (`Dán` → tự nhận diện → `Tải MP3`), thu gọn ô đổi tên khi đã có metadata gốc và chọn nhanh *Album gần nhất / Chưa phân loại / Bottom Sheet*.
  - **Sub-Agent C/D** thiết kế **Option B (Warm Studio Acoustic + Riverpod Architecture)** giải quyết triệt để lỗi 3 bộ `Timer` chạy chồng chéo bằng `DownloadJobsController` tập trung, bổ sung `MiniPlayerDock`, `Badge` tiến trình tải trên thanh điều hướng và hệ thống microinteractions hỗ trợ `Reduce Motion`.
  - **Agent G (Repository Researcher)** kiểm tra trực tiếp HTTP & GitHub REST API của cả 8 repository ứng viên.
- **Round 2 (Phản biện chéo):**
  - *Agent B phản biện C:* Nếu dùng `BackdropFilter` kính mờ hoặc mở bung toàn bộ ô nhập tên bài hát/nghệ sĩ mọi lúc sẽ gây rớt khung hình trên GPU Mali-G57 (Galaxy A06 5G), vi phạm `AGENTS.md` dòng 14 và đẩy nút `Tải MP3` xuống dưới bàn phím. -> **Chấp thuận:** Loại bỏ hoàn toàn `BackdropFilter`; dùng phân tầng màu đặc Tonal Navy/Off-White và thu gọn ô đổi tên sau nút *Đổi tên lưu riêng* khi nguồn đã có tiêu đề.
  - *Agent C/D phản biện B:* Nếu giữ cách quản lý state cục bộ bằng `setState` và `Timer` riêng trong từng màn hình như Option A thuần túy sẽ gây giật khung giao diện khi mở preview và có nguy cơ 2 màn hình cùng ghi đè file `song_<id>.mp3`. -> **Chấp thuận:** Dùng kiến trúc `DownloadJobsController` + `SmartDownloadController` + `AnimatedSize` (tự tắt khi bật Reduce Motion).

---

## 2. Bảng Đánh Giá 8 Repository GitHub Ứng Viên (Agent G)

| Repository | HTTP / Stars / Pushed | License | Đánh giá kỹ thuật & Quyết định |
|---|---|---|---|
| `rrousselGit/riverpod` | `200` · `7,425★` · `2026-11-17` | `MIT` | **GIỮ NGUYÊN (`flutter_riverpod: ^3.5.0`)** — Đang dùng ổn định, cập nhật tích cực. |
| `cfug/dio` | `200` · `12,844★` · `2026-11-15` | `MIT` | **GIỮ NGUYÊN (`dio: ^5.11.1`)** — Xử lý tốt auth interceptor, timeout và tải luồng MP3. |
| `BluFin-Designs/liser` | `200` · `17★` · `2026-08-20` | **`GPL-3.0`** | **LOẠI BỎ** — Giấy phép `GPL-3.0` lây nhiễm copyleft, không phù hợp ứng dụng thương mại. |
| `gskinner/flutter_animate` | `200` · `1,120★` · `2024-11-25` | `BSD-3-Clause` | **KHÔNG CÀI THÊM** — Nhánh chính ngừng cập nhật từ 11/2024; `AnimatedScale`, `AnimatedSize`, `TweenAnimationBuilder` của Flutter SDK đáp ứng 100% mà không tăng dung lượng APK. |
| `rydmike/flex_color_scheme` | `200` · `1,199★` · `2026-09-15` | `BSD-3-Clause` | **KHÔNG CÀI THÊM** — `TMusicTheme` + `ThemeExtension<TMusicTokens>` đã kiểm soát chính xác bảng màu Navy/Off-White của `AGENTS.md`. |
| `781flyingdutchman/background_downloader` | `200` · `241★` · `2026-11-06` | `BSD-3-Clause` | **KHÔNG CÀI THÊM** — Quá trình tách/chuyển đổi MP3 diễn ra ở Backend (`yt-dlp` + `FFmpeg`); đồng bộ file `.mp3` hoàn tất về máy qua `Dio` vào `LocalLibrary` gọn nhẹ và không sinh thêm quyền Foreground Service trùng với `audio_service`. |
| `fluttercommunity/flutter_downloader` | `200` · `938★` · `2026-08-22` | `BSD-3-Clause` | **KHÔNG CÀI THÊM** — Tương tự trên; phụ thuộc Android `DownloadManager` khó gửi kèm header JWT xoay vòng nội bộ hơn `Dio`. |
| `fluttercommunity/flutter_launcher_icons` | `200` · `2,123★` · `2025-06-10` | `MIT` | **THAM KHẢO QUY CHUẨN KÍCH THƯỚC** — Sinh trực tiếp bộ icon PNG vector đa mật độ + Android Adaptive Icon XML (`mipmap-anydpi-v26`) & iOS `AppIcon.appiconset` bằng script tái lập, không thêm runtime dependency. |

---

## 3. Bảng Chấm Điểm Phương Án Giao Diện & Kiến Trúc (Thang 0–10)

*Điều kiện tiên quyết bắt buộc (Pass/Fail):* Bảo mật Anti-IDOR, không mất dữ liệu, tuân thủ `AGENTS.md` (không neon gradient, không glassmorphism mờ đục) -> **ĐẠT**.

| Tiêu chí | Trọng số | Option A Thuần (Minimalist) | Option B Thuần (Premium Studio) | **Phương Án Tổng Hợp B+A (Được chọn)** |
|---|---:|---:|---:|---:|
| **Usability (Dễ dùng, ít chạm)** | 30% | 9.5 | 8.6 | **9.6** |
| **Reliability (Ổn định, chống race/offline)** | 25% | 8.0 | 9.5 | **9.6** |
| **Maintainability (Dễ bảo trì, không lặp code)** | 15% | 8.0 | 9.4 | **9.4** |
| **Performance (FPS trên Galaxy A06 5G)** | 15% | 9.6 | 9.0 | **9.5** |
| **Visual Quality (Thẩm mỹ thương mại)** | 10% | 7.6 | 9.4 | **9.3** |
| **Implementation Cost (Chi phí/rủi ro)** | 5% | 9.0 | 8.2 | **8.8** |
| **TỔNG ĐIỂM CÓ TRỌNG SỐ** | **100%** | **8.67 / 10** | **9.05 / 10** | **9.50 / 10** |

---

## 4. Bảng Chấm Điểm 3 Phương Án App Icon

| Phương án | Nhận diện âm nhạc + TMusic | Độ nét ở `29pt` / `48dp` | Tương phản Sáng & Tối | Tổng điểm | Quyết định |
|---|---|---:|---:|---:|---|
| **Concept A:** Chữ `T` + nốt nhạc tách rời | Rõ nghĩa nhưng 2 chi tiết tranh chấp vùng `66dp` | 7.5 | 8.5 | **7.9 / 10** | Á quân |
| **Concept B:** Sóng âm tối giản | Dễ nhầm app Ghi âm, thiếu chữ `T` | 9.0 | 8.8 | **7.2 / 10** | Loại |
| **Concept C:** Chữ `T` cách điệu thành nốt nhạc âm thanh | Khối thống nhất: thanh ngang `T` như dầm khuông nhạc + bầu nốt nhạc đặc ở chân + vạch cộng hưởng xanh băng `#A8CBEA` trên nền Navy `#17283D` có viền nội `#294260` | **9.6** | **9.5** | **9.5 / 10** | **CHỌN TRIỂN KHAI** |
