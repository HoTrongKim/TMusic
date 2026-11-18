# Báo Cáo Kiểm Toán Hệ Thống, Bảo Toàn Backend & Bảo Mật — TMusic V2 → V3
**Thực hiện:** Independent Sub-Agent A (System Auditor) · Sub-Agent E (Backend Preservation) · Sub-Agent F (QA & Security)

---

## 1. Baseline Thực Tế Trước Khi Nâng Cấp (Phase 0)

- **Bảo toàn `.env`:** Nguyên vẹn tuyệt đối (`SHA-256: 5EFA5C78DEEDF3910259D261FC039AFAAF72F86EA259D210C56BA015C7A420F7`).
- **Database `TMusicV2Dev` (trước nâng cấp):**
  - `AspNetUsers`: `4` | `Albums`: `1` | `Songs`: `2` | `UserLibrarySongs`: `2` | `AlbumSongs`: `2` | `AudioDownloadJobs`: `2` | `__EFMigrationsHistory`: `3` (`InitialV2`, `CompleteV2SchemaAndCovers`, `AddAudioDownloads`).
- **Backend + SQL Server Baseline Tests:** **`94/94 passed`** (`0 failed`, `0 skipped`).
- **Flutter Baseline Tests:** `flutter analyze --no-pub` **`0 issues`**, `flutter test --no-pub` **`20/20 passed`**.
- **Engine xử lý âm thanh:** `yt-dlp 2026.10.26`, `FFmpeg 9.0.2`, `Deno 2.9.8` đã xác minh hoạt động.

---

## 2. Phân Loại Thành Phần Embedded Player vs Shared / Native MP3 Player

| Vị trí | Thành phần | Phân loại | Quyết định xử lý |
|---|---|---|---|
| `mobile/pubspec.yaml` | `webview_flutter: ^4.14.1` | **Embedded-only** | Loại bỏ khỏi `pubspec.yaml` để giảm kích thước APK và bề mặt WebView không cần thiết. |
| `mobile/lib/features/playback/official_embed_player.dart` | `OfficialEmbedPlayer` | **Embedded-only** | Xóa file sau khi gỡ tham chiếu ở `song_screen.dart` và `widget_test.dart`. |
| `mobile/lib/features/songs/song_screen.dart` | `_showEmbed`, nút `toggle-embed-player`, nhánh render `OfficialEmbedPlayer` | **Embedded-only** | Gỡ bỏ khỏi giao diện bài hát; giữ nguyên ảnh bìa `Cover`, trình phát `LocalPlayerControls`, nút `Tải MP3` / `Phát MP3`, và nút `Mở trong YouTube/TikTok`. |
| `mobile/lib/core/widgets/common.dart` & `app_copy.dart` | `CapabilityBadge` & chuỗi `sourceNotice` | **Shared widget** | Giữ widget để cảnh báo khi nguồn `UNAVAILABLE`, cập nhật nhãn từ *"Hỗ trợ phát nhúng"* sang trạng thái nguồn hợp lệ hỗ trợ tải/mở gốc trung thực. |
| `mobile/lib/features/playback/local_audio.dart` & `local_audio_screen.dart` | `just_audio`, `audio_service`, `audio_session`, `LocalLibrary`, `LocalAudio`, `LocalPlayerControls` | **Native MP3 (Bắt buộc giữ)** | Bảo toàn 100% logic phát offline, phát nền khi tắt màn hình, phát cả Album, Shuffle, Repeat, Seek và Media Notification. |

---

## 3. Kiểm Chứng "Chưa Phân Loại", Chống Trùng Lặp & Bảo Mật Anti-IDOR

1. **Hỗ trợ "Chưa phân loại" (`albumIds: []`) trên Backend:**
   - Đã hoạt động chính thức trong `SongEndpoints.Create` và `GET /api/v1/library/songs?unorganized=true` (`!x.AlbumSongs.Any()`) mà **không cần tạo album giả** và **không cần sửa schema database**.
   - File `.mp3` chỉ lưu **1 bản duy nhất** gắn với `UserLibrarySong(UserId, SongId)` trên server và `song_<id>.mp3` trên điện thoại; thêm/bớt/xóa Album chỉ tác động bảng nối `AlbumSongs`, không nhân bản hay làm mất file MP3.
2. **Chống tạo tiến trình tải trùng & Anti-IDOR:**
   - `SongEndpoints.Create` (`Serializable` transaction) và `AudioDownloadEndpoints.Enqueue` đã kiểm tra job `QUEUED`/`RUNNING` hiện hành và `RowVersion`.
   - *Phát hiện 1 điểm cải thiện nhỏ ở Backend:* Trong `AudioDownloadEndpoints.Enqueue`, nếu bài hát đã `READY` và file thực tế vẫn tồn tại trên ổ đĩa nhưng người dùng đã xóa lịch sử job cũ khỏi `/downloads`, API nên trả ngay `200 OK` (thông tin file sẵn sàng) thay vì tạo job tải lại.
   - `/api/v1/library/songs/{id}/audio` kiểm tra chặt `UserId == sub`, chống path traversal bằng regex `\A[0-9a-f]{32}\.mp3\z`, hỗ trợ `HTTP 206 Partial Content` (`Range`).
3. **Phát hiện 3 điểm nghẽn Offline trên Mobile cần khắc phục trong V3 (để đạt trọn vẹn `TEST 02` & `TEST 08`):**
   - Khi mở app lúc mất mạng hoàn toàn, `SessionController.build()` ném lỗi mạng làm kẹt ở màn hình `/loading` thay vì cho phép vào chế độ ngoại tuyến đọc cache và phát MP3 đã tải.
   - `LibraryRepository.song(id)` và `album(id)` chưa có fallback đọc từ `LibraryCache` khi mất mạng, khiến mở chi tiết bài hát hoặc album lúc ngắt mạng báo lỗi kết nối dù file `song_<id>.mp3` đã nằm sẵn trên máy.
   - Khi xóa bài hát hoặc gỡ bản MP3, cần gọi thêm `LocalLibrary.removeSongTrack(songId)` để dọn sạch file cục bộ tương ứng.

---

## 4. Kiểm Tra Xung Đột Quy Tắc (`AGENTS.md` vs Prompt V3 Mục 7.4)

- **Phát hiện xung đột:** `AGENTS.md` dòng 14 quy định bắt buộc: *"Keep design tokens consistent: navy, off-white, accessible touch targets, readable text. No neon gradients, glassmorphism or generic dashboard UI"*, trong khi Mục 7.4 của Prompt V3 đề cập *"Glassmorphism"*.
- **Nguyên tắc xử lý:** Tuân thủ ưu tiên cao nhất của `AGENTS.md` đồng thời đáp ứng yêu cầu hiệu năng cho Galaxy A06 5G ở Mục 7.4 & 8.2: **không dùng `BackdropFilter` làm mờ tốn GPU hoặc gradient neon**, thay bằng hệ thống phân lớp bề mặt Material 3 Navy & Off-White sắc nét (`surface`, `surfaceContainerLow`, `surfaceContainer`, viền mảnh `1dp` `outlineVariant`, đổ bóng siêu nhẹ và chuyển trạng thái mượt hỗ trợ `Reduce Motion`).
