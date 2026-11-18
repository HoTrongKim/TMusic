# MASTER PROMPT: THIẾT KẾ LẠI TOÀN DIỆN GIAO DIỆN (UI/UX OVERHAUL) CHO TMUSIC V2

> **Hướng dẫn sử dụng:** Bạn có thể sao chép toàn bộ nội dung file này và gửi trực tiếp cho bất kỳ AI chuyên thiết kế/lập trình nào (Claude Opus/Sonnet, ChatGPT, Cursor, Windsurf, Codex...). Prompt đã đính kèm đầy đủ kiến trúc kỹ thuật thực tế của dự án để AI mới **chỉ tập trung lột xác 100% giao diện sang đẳng cấp ứng dụng nghe nhạc thương mại** mà không làm hỏng Backend, Database, Google Sign-In hay bộ máy tải/phát MP3 hiện có.

---

```markdown
# VAI TRÒ & NHIỆM VỤ TỐI THƯỢNG

Bạn là **Principal Consumer Mobile Product Designer (Apple Design Award / Spotify / Apple Music / Tidal / SoundCloud)** kiêm **Staff Flutter UI Engineer**.

Nhiệm vụ của bạn là **THIẾT KẾ LẠI HOÀN TOÀN HỆ THỐNG GIAO DIỆN (UI/UX VISUAL OVERHAUL)** của ứng dụng nghe nhạc cá nhân **TMusic V2** tại thư mục `mobile/` (`Flutter Android/iOS`), biến nó từ giao diện Material 3 mặc định cứng nhắc hiện tại thành một **ứng dụng nghe nhạc cá nhân hiện đại, giàu cảm xúc, tinh xảo và mang đẳng cấp thương mại thực thụ**.

---

# 1. VÌ SAO GIAO DIỆN CŨ BỊ LOẠI BỎ (PHẢI KHẮC PHỤC TRIỆT ĐỂ)

Giao diện hiện tại bị đánh giá là **xấu, thô và giống form quản trị (admin CRUD)** vì các lỗi thiết kế sau — bạn **TUYỆT ĐỐI KHÔNG ĐƯỢC LẶP LẠI**:

1. **Lạm dụng widget Material 3 mặc định thô cứng:** Dùng quá nhiều `OutlinedButton`, `FilterChip`, `ChoiceChip`, `ListTile` mặc định, `Divider` cắt ngang vụn vặt và các khung viền chữ nhật đơn điệu.
2. **Bảng màu cũ quá cứng và thiếu chiều sâu âm nhạc:** Ràng buộc cũ trong `AGENTS.md` (`navy, off-white, no gradients/glassmorphism`) khiến giao diện phẳng lì, nhợt nhạt như ứng dụng văn phòng. **Chủ dự án chính thức cấp quyền cho bạn thay thế hoàn toàn hệ màu, typography, độ bo góc, ánh sáng, chuyển động và cập nhật lại dòng quy tắc màu sắc trong `AGENTS.md`** để đạt thẩm mỹ ứng dụng âm nhạc đỉnh cao.
3. **Trình phát nhạc (Now Playing & Mini Player) chưa có hồn:** Chưa có hiệu ứng chuyển màu nền động theo ảnh bìa bài hát (Dynamic Artwork Ambient Palette), thanh kéo thời gian & khung cắt/lặp đoạn A–B chưa được thiết kế riêng dạng custom timeline/waveform mượt mà, bố cục nút bấm còn giống thanh công cụ kỹ thuật thay vì trình phát nhạc cao cấp.
4. **Thiếu nhịp điệu thị giác (Visual Hierarchy & Spatial Rhythm):** Màn hình Trang chủ, Album và Chi tiết bài hát xếp chồng nhiều khối chữ/nhãn kỹ thuật (`ProviderBadge`, `CapabilityBadge`, `Notice` khung viền dày) gây rối mắt.

---

# 2. ĐỊNH HƯỚNG THẨM MỸ MỚI CẦN ĐẠT

Hãy thiết kế một ngôn ngữ thị giác **Custom Music Design System** hoàn toàn mới (hỗ trợ cả **Dark Mode chủ đạo kiểu phòng thu/Spotify/Apple Music** và **Light Mode tinh tế kiểu Editorial Vinyl**):

- **Bố cục giàu tính âm nhạc:** Ảnh bìa bài hát/album là trung tâm thị giác (bo góc mềm mại, đổ bóng sâu tự nhiên, trích xuất màu chủ đạo từ ảnh bìa cho nền trình phát Now Playing).
- **Custom Components 100% thay cho chip/button mặc định:**
  - **Floating Capsule Mini-Player** tinh gọn nằm lơ lửng phía trên thanh điều hướng đáy, hiển thị ảnh bìa xoay/bo tròn, thanh tiến độ mượt và vuốt lên để mở trình phát toàn màn hình.
  - **Full-Screen Gesture Now Playing Screen:** Vuốt xuống để thu nhỏ; khu vực ảnh bìa lớn; thanh scrub thời gian thiết kế riêng cực mượt kèm bộ chọn đoạn lặp **A–B Custom Timeline Handle** trực quan (như trình cắt đoạn nhạc trên TikTok/CapCut/Apple Music); cụm nút điều khiển trung tâm cân đối hoàn hảo (`Trộn bài`, `Bài trước`, `Phát/Tạm dừng`, `Bài tiếp`, `Lặp 1 bài / Lặp danh sách`); menu gọn đẹp cho `Tốc độ phát (0.8x–1.5x)` và `Danh sách đang phát (Up Next Queue)`.
  - **Smart Link Input Bar hiện đại trên Trang chủ:** Thiết kế như một thanh tìm kiếm/nhập link tối giản, sang trọng. Khi người dùng nhập hoặc chia sẻ link YouTube/TikTok vào, hiển thị một **Compact Music Card** đẹp mắt để chọn Album & bấm **Tải MP3** chỉ trong 1 chạm.
  - **TUYỆT ĐỐI KHÔNG CÓ nút hay chức năng "Dán từ bộ nhớ tạm" (`Clipboard.getData` / Paste button):** Chủ dự án đã yêu cầu xóa vĩnh viễn cả giao diện lẫn chức năng đọc clipboard này. Chỉ nhận URL khi người dùng tự nhập/dán bằng bàn phím hệ thống hoặc chia sẻ (`Share Intent`) từ YouTube/TikTok sang ứng dụng.

---

# 3. KIẾN TRÚC KỸ THUẬT & HỢP ĐỒNG DỮ LIỆU PHẢI BẢO TOÀN 100%

Hệ thống Backend (`.NET 10` + `SQL Server TMusicV2Dev` + `yt-dlp` + `FFmpeg`) và tầng Logic/Audio của Flutter **ĐANG HOẠT ĐỘNG HOÀN HẢO**. Bạn **chỉ thiết kế lại tầng UI/Theme/Widgets/Screens**, **KHÔNG ĐƯỢC phá vỡ** các thành phần lõi sau:

## 3.1. Quy tắc an toàn bất khả xâm phạm
1. **Bảo toàn nguyên vẹn file `.env` byte-for-byte** (`SHA-256: 5EFA5C78DEEDF3910259D261FC039AFAAF72F86EA259D210C56BA015C7A420F7`); không in, không sửa, không commit secrets.
2. **Không đụng vào database cũ `TMusicDev`.** Môi trường dev chỉ dùng `TMusicV2Dev` (API chạy ở cổng `http://127.0.0.1:5078`).
3. **Không xóa dữ liệu người dùng trên điện thoại:** Khi cài APK lên thiết bị vật lý, luôn dùng `adb install -r` (tuyệt đối không chạy `adb uninstall`).
4. **Không tạo dữ liệu giả (fake/mock songs, demo login, placeholder controls không hoạt động).**
5. **Ngôn ngữ hiển thị mặc định:** Tiếng Việt tự nhiên, ngắn gọn, tinh tế (hỗ trợ đa ngôn ngữ `vi`/`en` qua `mobile/lib/core/localization/app_copy.dart`).

## 3.2. Tầng Core, Repository & Audio Engine có sẵn trong `mobile/lib/` (Giữ nguyên API logic, nối vào UI mới)
- **Xác thực & Phiên đăng nhập:**
  - `core/auth/session.dart` (`sessionProvider` — quản lý JWT + Refresh Token + phục hồi phiên offline).
  - `core/auth/external_auth.dart` (`externalAuthProvider`, `capabilitiesProvider` — Google Sign-In với `TMUSIC_GOOGLE_SERVER_CLIENT_ID` & Apple Sign-In).
- **Kho nhạc, Album & Tải MP3:**
  - `features/library/library_repository.dart` (`libraryRepositoryProvider`, `libraryRevisionProvider`, `linkDraftProvider`):
    - `resolve(url)` → nhận diện metadata link YouTube/TikTok.
    - `saveLink(url, title, artist, albumIds, downloadMp3: ..., rightsConfirmed: ...)` → lưu bài & kích hoạt job tải MP3.
    - `albums(...)`, `album(id)`, `saveAlbum(...)`, `deleteAlbum(...)`, `order(album, songIds)`, `uploadAlbumCover(...)`.
    - `songs(...)`, `song(id)`, `editSong(...)`, `deleteSong(...)`, `favorite(...)`, `addToAlbum(...)`, `removeFromAlbum(...)`.
    - `startSongDownload(songId)`, `downloadJobs()`, `downloadJob(id)`, `fetchSongAudioBytes(songId)`.
  - `features/playback/downloads_screen.dart` (`downloadJobsProvider`, `activeDownloadCountProvider`, `songDownloadJobProvider` — theo dõi tiến trình tải MP3 0–100% thời gian thực).
- **Bộ máy phát nhạc nội bộ & chạy nền (`just_audio` + `audio_service`):**
  - `features/playback/local_audio.dart` (`localLibraryProvider`, `localAudioProvider`, class `LocalAudio`):
    - Lưu và phát MP3 offline riêng cho từng `userId`.
    - Streams & State có sẵn: `player.mediaItem`, `player.playbackState`, `player.queue`, `player.position`, `player.duration`, `player.abLoop` (`ValueNotifier<({Duration start, Duration end})?>`), `player.speed` (`ValueNotifier<double>`).
    - Các hàm điều khiển có sẵn: `start(tracks, index, {albumName})`, `play()`, `pause()`, `stop()`, `seek(pos)`, `seekRelative(delta)`, `skipToNext()`, `skipToPrevious()`, `skipToQueueItem(index)`, `cycleRepeatMode()` (`none -> one -> all`), `setShuffleMode(...)`, `setAbLoop(start, end)`, `clearAbLoop()`, `changeSpeed(rate)`, và constructor `LocalAudio.forTest(...)` phục vụ widget test.

---

# 4. DANH SÁCH ĐẦY ĐỦ CÁC MÀN HÌNH CẦN THIẾT KẾ LẠI

1. **Welcome & Auth (`features/authentication/auth_screens.dart`):**
   - Màn hình chào mừng giàu cảm xúc âm nhạc, Đăng nhập, Đăng ký (vào thẳng ứng dụng), Quên/Đặt lại mật khẩu, nút Tiếp tục với Google chuẩn chỉnh.
2. **App Shell & Navigation (`app/tmusic_app.dart` + Floating Mini-Player):**
   - Thanh điều hướng 4 tab hiện đại (`Trang chủ`, `Thư viện / Album`, `Tải xuống` có badge tiến trình, `Cá nhân`) kết hợp **Floating Mini-Player** tinh tế.
3. **Trang chủ — Smart Music Hub (`features/home/home_screen.dart` & `smart_download_card.dart`):**
   - Lời chào tinh gọn + nút Tìm kiếm nhanh & Yêu thích.
   - Khung nhập link YouTube/TikTok sang trọng (**không có nút dán bộ nhớ tạm**), tự nhận diện khi người dùng dán/nhập URL đầy đủ hoặc bấm Nhận diện → hiển thị thẻ Preview gọn đẹp → chọn Album lưu (hoặc Chưa phân loại) → nút chính **Tải MP3** → hiển thị thanh tiến trình mượt và nút **Phát ngay** khi hoàn tất.
   - Các kệ ngang/dọc thẩm mỹ cao: *Album mở gần đây* (bìa vuông lớn), *Bài hát vừa thêm* (bấm phát ngay nếu đã có MP3).
4. **Thư viện & Album (`features/library/library_screen.dart`, `features/albums/album_screens.dart`):**
   - Chuyển đổi mượt giữa *Album* (Grid bìa đĩa sang trọng / List) và *Bài hát* / *Chưa phân loại*, bộ lọc tinh gọn không chiếm diện tích.
   - **Màn hình Chi tiết Album:** Header nghệ thuật với ảnh bìa lớn, nút nổi bật **Phát toàn bộ Album** & **Trộn bài**, danh sách track đánh số thứ tự rõ ràng như Spotify/Apple Music, vuốt/menu gọn nhẹ để sắp xếp hoặc gỡ bài.
   - **Màn hình Tạo/Sửa Album:** Tải ảnh bìa riêng hoặc chọn bảng màu nghệ thuật.
5. **Trình phát nhạc Đang phát (`NowPlayingSheet` / `NowPlayingScreen`) & Chi tiết bài hát (`features/songs/song_screen.dart`):**
   - Giao diện trình phát toàn màn hình đẳng cấp: Ảnh bìa lớn, tiêu đề & nghệ sĩ, thanh kéo thời gian mượt kèm mốc `mm:ss`, tua `±10s`, lặp 3 chế độ (`Tắt lặp` / `Lặp 1 bài` / `Lặp danh sách`), trộn bài, **bộ chỉnh đoạn lặp A–B trực quan trên thanh timeline** (`Chấm A`, `Chấm B`, kéo 2 đầu mút, `Bỏ lặp`), chọn tốc độ `0.8x–1.5x`, và danh sách bài tiếp theo (`Queue`).
   - Giữ nút phụ mở video gốc trên ứng dụng YouTube/TikTok khi người dùng cần xem nguồn.
6. **Trung tâm Tải xuống (`features/playback/downloads_screen.dart`) & File trên thiết bị (`local_audio_screen.dart`):**
   - Hiển thị hàng đợi đang tải (phần trăm + thanh tiến độ sống động) và danh sách MP3 đã hoàn tất với thao tác chạm để phát ngay.
7. **Tìm kiếm hợp nhất (`features/search/search_screen.dart`), Yêu thích (`FavoritesScreen`), và Cá nhân / Cài đặt / Bảo mật (`features/profile/profile_screens.dart`):**
   - Đồng bộ hoàn toàn với ngôn ngữ thiết kế mới (giao diện Sáng/Tối/Hệ thống, Ngôn ngữ, Phiên thiết bị, Đổi mật khẩu, Xóa bộ nhớ đệm, Xóa tài khoản).

---

# 5. QUY TRÌNH THỰC HIỆN & TIÊU CHUẨN NGHIỆM THU

1. **Bước 1 — Khảo sát & Đề xuất Concept Thiết kế:**
   - Đọc toàn bộ `mobile/lib/` để nắm rõ các route, key kiểm thử và provider hiện có.
   - Thiết lập hệ thống Design Tokens mới trong `mobile/lib/app/theme/tmusic_theme.dart` (bảng màu, font chữ/type scale, spacing, shadows, surfaces, custom sliders, animations tôn trọng `MediaQuery.disableAnimationsOf`).
2. **Bước 2 — Tái cấu trúc toàn bộ UI Screens & Custom Widgets:**
   - Thay thế triệt để giao diện cũ trên tất cả màn hình mục 4.
   - Đảm bảo không lỗi tràn khung (`RenderFlex overflow`) trên cả màn hình nhỏ `320dp` lẫn màn hình lớn `430dp`, kể cả khi phóng cỡ chữ hệ thống lên `200%`.
3. **Bước 3 — Kiểm thử & Cài đặt thực tế:**
   - Cập nhật các widget test trong `mobile/test/` tương ứng với UI mới (đảm bảo kiểm chứng rõ: không tồn tại nút dán clipboard, luồng đăng nhập, tạo album, lưu link tải MP3, tìm kiếm, trình phát nhạc tua/lặp/A–B loop/tốc độ, và responsive `320dp`/`430dp`).
   - Chạy `flutter analyze --no-pub` (**bắt buộc 0 lỗi, 0 cảnh báo**) và `flutter test --no-pub` (**100% pass**).
   - Chạy kiểm tra Backend `dotnet test backend/TMusic.slnx -c Release` để chắc chắn không ảnh hưởng API.
   - Build APK debug (nạp biến môi trường từ `scripts/Load-Environment.ps1` để giữ `TMUSIC_GOOGLE_SERVER_CLIENT_ID` và `TMUSIC_API_URL=http://127.0.0.1:5078`), cài cập nhật trực tiếp qua `adb install -r` lên điện thoại đang kết nối, và ghi kết quả vào `docs/BUILD_LOG.md`.
```
