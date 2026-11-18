# IMPLEMENTATION REPORT — TMUSIC V2

## Hiện trạng đối chiếu mã nguồn — 18/11/2026

Phạm vi hiện tại theo `AGENTS.md` đã thay thế các ràng buộc account-only, không MP3 và palette navy của giai đoạn đầu. Báo cáo lịch sử bên dưới không phải tuyên bố nghiệm thu hiện hành.

| Phạm vi | Hiện trạng và ranh giới kiểm chứng |
|---|---|
| Khách và tài khoản | `GuestLibraryStore` lưu album/bài trên máy không cần login; gỡ app trước sync làm mất dữ liệu. `LibraryRepository.syncGuestDataToCloud` chuyển metadata, giữ dữ liệu khách khi lỗi, sao chép MP3 sang vùng tài khoản cùng máy; không tự upload media. Đăng ký hiện cấp phiên ngay, không yêu cầu xác minh email. |
| Backend | Identity hashing, verified external tokens, refresh rotation/reuse detection, server ownership và optimistic concurrency có mã nguồn/tests. OAuth trực tiếp còn phụ thuộc cấu hình thiết bị và credential. |
| Provider/MP3 | YouTube, TikTok và SoundCloud có parser, metadata/fallback, attribution và mở nguồn. `yt-dlp`/`FFmpeg` thực hiện trích xuất sau xác nhận quyền. Tài khoản có queue/progress/retry/cancel và audio stream riêng có HTTP Range; khách hiện trích xuất trực tiếp. `AudioDownloadEgressProxy` kiểm tra host/CDN, DNS công khai và pin socket cho HTTP(S) extractor qua CONNECT có xác thực riêng. |
| Chia sẻ album | Chủ tạo/thu hồi mã; recipient preview/import metadata thuộc tài khoản mình. Source attribution được giữ; không cấp quyền đọc MP3/cover riêng. Thu hồi không xóa bản đã nhập. |
| Mobile/UI | Audio native có seek, A–B, repeat, speed, queue; embedded player/WebView đã gỡ. Theme charcoal `#121212` / `#181818` / `#282828`, chữ trắng, xanh `#1DB954`, dark mặc định + light. Không có nút clipboard-paste. |

Các sửa bổ sung trong lượt hoàn thiện này:

- Hủy tải đang chạy thu hồi lease; worker không công bố lại MP3 sau khi hủy/xóa. Chặn mạng extractor bằng proxy riêng và chuyển đổi FFmpeg có giới hạn thay cho postprocessor tự do.
- Guest Downloads hiển thị tải hoàn tất/lỗi, thử lại và xóa tệp mà vẫn giữ nguồn. Claim tệp người dùng nhập và MP3 đã tải vào vùng tài khoản trên cùng máy; chỉ dọn bản khách sau khi sao chép/index thành công, giữ nguyên khi lỗi.
- Chia sẻ album không còn âm thầm cắt 200 bài: preview theo trang, nhập tối đa 5000 bài hoặc báo lỗi rõ. Guest giữ original metadata/content ID/thứ tự và phân biệt ID nguồn hoa/thường; snapshot không báo trang kế giả.
- Refresh/password/native-provider sign-in cũ không được thay phiên sau logout/đăng nhập khác. Settings lưu bền theo chủ, có cache offline, giữ lựa chọn system theme và từ chối GET cũ ghi đè PATCH mới.
- Download list/poll/tệp đang chờ được chặn theo chủ phiên; đổi tài khoản không hiện job cũ hoặc ghi bytes về sau logout. Poll cùng tài khoản vẫn cho phép hoàn tất MP3 đang đồng bộ.

Những phần sản phẩm còn mở, không được xem là đã triển khai trong lượt sửa này: gán album/yêu thích/đổi tên cho raw local tracks ở màn tệp riêng; progress/cancel server chi tiết cho tải khách đồng bộ; flow xác thực lại provider để tài khoản chưa có mật khẩu liên kết thêm provider. Xem nhật ký mới nhất để phân biệt phần đã kiểm thử và giới hạn môi trường.

Kết quả cuối của lượt sửa ngày 18/11/2026: **138/138 backend tests (0 skipped)**, **55/55 Flutter tests**, static analysis không có vấn đề, backend Release build và Android debug APK thành công. Lệnh cụ thể và phạm vi xác minh nằm ở phần 4 của `BUILD_LOG.md`. Baseline trước sửa là **100 backend / 28 Flutter**; các mốc **91/18** và bảng “Đạt” bên dưới thuộc revision lịch sử, không phải nghiệm thu hiện hành. Chưa cài APK mới hoặc khởi động lại daemon API/Worker.

Các giới hạn cần giữ rõ:

- `SqlApiFixture` dùng SQL Server thật và database `TMusicV2Test_<guid>`, nhưng thay email/metadata provider. `AudioDownloadTests` dùng `FixtureMp3Extractor`, nên không chứng minh live download từng nguồn. `LocalAudioConversionTests` riêng có WAV tổng hợp -> FFmpeg cục bộ -> MP3, playlist mạng/concat bị từ chối và capacity hai slot; test cần FFmpeg sẽ bị bỏ qua nếu không tìm thấy binary. Chỉ kết quả ghi tại `BUILD_LOG.md` mới xác nhận test đã chạy.
- `AudioDownloadEgressTests` kiểm tra allowlist, destination/port xấu, DNS private/mixed, thiếu proxy authentication, HTTP thường và headers quá lớn. Downloader hiện tắt postprocess/fixup và giấu FFmpeg/ffprobe khỏi yt-dlp; C# tự chuyển đổi bằng FFmpeg với whitelist protocol/demuxer. Positive tunnel/pinning ra public IP, mọi giới hạn tài nguyên, nội dung provider thật và sandbox mạng cấp OS vẫn chưa được chứng minh bằng các test này.
- Widget tests dùng fake dependencies; không chứng minh file I/O native, audio focus, playback nền/màn hình khóa hoặc sync MP3 thật. `integration_test/account_library_e2e_test.dart` không chạy trong `flutter test` mặc định hay CI; chỉ dùng môi trường test riêng vì thay phiên trên thiết bị và tạo/xóa dữ liệu API.
- `BUILD_LOG.md` ghi cài cập nhật, khởi chạy, điều hướng và kết nối API trên Samsung A06; còn ghi chưa kiểm tra trực tiếp Google Sign-In, download/play MP3 bên ngoài và sync MP3 khách. iOS chưa được build trên máy Windows.
- `/health/ready` chỉ kiểm tra SQL; capability flags chỉ phản ánh cấu hình/availability, không phải thử kết nối provider. Cần xác minh release signing Android, OAuth theo package/chứng chỉ và iOS trên macOS/Xcode.
- Deployment cần binary extractor, volume audio bền vững dùng chung nếu tách API/Worker, covers và Data Protection. Docker Compose chỉ định nghĩa SQL/Mailpit; trạng thái đóng gói extractor phải được kiểm tra theo Dockerfile hiện tại và build thực tế.
- `.env` phải giữ nguyên; không migrate/drop/reset `TMusicDev`. Mọi migration/test chỉ nhắm database V2 có tên tường minh.

---

## Báo cáo giai đoạn đầu — lịch sử, đã được thay thế ở các điểm trên

## 1. Tóm Tắt Thực Thi

Dự án **TMusic V2** đã được kiểm toán toàn diện, nâng cấp kiến trúc, hoàn thiện đầy đủ mã nguồn Backend (`.NET 10` + `SQL Server`) và Mobile (`Flutter 3.47`), chạy kiểm thử thực tế, biên dịch APK Android và triển khai trực tiếp lên thiết bị Android vật lý của người dùng, trong khi **bảo toàn tuyệt đối byte-for-byte file `.env` gốc** và giữ nguyên database cũ `TMusicDev`.

---

## 2. Những Thay Đổi & Hoàn Thiện Trọng Tâm

### 2.1. Backend & Database (`backend/`)
- **Hoàn thiện Schema SQL Server V2 (`CompleteV2SchemaAndCovers`):** Bổ sung `AvatarUrl`, `DeletedAt` cho bảng người dùng, `DeletedAt` cho `Albums`, và bảng `UserAuthProviders` (`Id`, `UserId`, `Provider`, `ProviderSubject`, `LinkedAt`) với unique index `(Provider, ProviderSubject)` phân biệt hoa-thường.
- **Bảo mật Upload Ảnh Bìa Album (`CoverImageStorage`):** Kiểm tra chữ ký nhị phân thực tế (PNG/JPEG/WebP), giới hạn kích thước khung hình `<= 4096px` và dung lượng `<= 2 MB`, **tự động loại bỏ EXIF/XMP/IPTC/comment chunks**, sinh tên file ngẫu nhiên an toàn ngoài thư mục tĩnh và phục vụ riêng cho chủ sở hữu với `Content-Security-Policy: default-src 'none'; sandbox`.
- **Tương thích Cấu Hình & Provider:**
  - `ExternalTokenVerifier` hỗ trợ đầy đủ `Auth:Google:Audiences`, `TMUSIC_GOOGLE_SERVER_CLIENT_ID`, `TMUSIC_GOOGLE_WEB_CLIENT_ID`, `TMUSIC_GOOGLE_ANDROID_CLIENT_ID`, `TMUSIC_GOOGLE_IOS_CLIENT_ID`.
  - `TikTokMetadata` trích xuất và kiểm tra allowlist miền CDN chính thức cho `thumbnail_url` từ TikTok oEmbed.
  - `POST /api/v1/songs/resolve` trả kèm chi tiết `existingSong` khi phát hiện link trùng.
  - Bổ sung `PATCH /api/v1/albums/order` (sắp xếp thứ tự album) và tham số lọc `unorganized` trên `GET /api/v1/library/songs`.

### 2.2. Mobile Architecture & UI/UX (`mobile/`)
- **Design System (*Premium Minimal Music Library*):** Tách bạch nền sáng ấm `#F7F7F3` và bề mặt thẻ `#FFFFFF`, navy chủ đạo `#17283D`, chế độ tối `#101B2A` / `#162538`, tương phản chuẩn WCAG AA, kích thước vùng chạm `>= 48dp` và sửa triệt để lỗi tràn khung `RenderFlex overflow` ở màn hình `320dp` khi phóng chữ `200%`.
- **Hoàn thiện đủ 23 màn hình:** Bao gồm Trang chủ (kèm Album mở gần đây & Tiếp tục sắp xếp), Thư viện (chuyển đổi Lưới/Danh sách & sắp xếp thứ tự album), Tạo/Sửa Album (tải ảnh bìa thật từ máy), Thêm bài qua URL & Xem trước Metadata (mở thẳng album vừa lưu), Sửa bài hát riêng (`/songs/:id/edit`), Tìm kiếm hợp nhất (`/search`), Yêu thích hợp nhất (`/favorites`), Màn hình Phát/Xem nội dung tích hợp **Official Embed Player** (`webview_flutter` khóa chặt origin chống XSS cho YouTube IFrame & TikTok Embed v2), Quản lý phiên và Luồng xóa tài khoản chuyên biệt (`/account/delete`).
- **Đồng bộ & Bộ nhớ đệm (`LibraryCache`):** Lưu snapshot đọc cục bộ bằng SQLite theo từng tài khoản kèm thanh thông báo `OfflineBanner` khi mất kết nối, retry backoff cho truy vấn `GET` và cập nhật lạc quan có rollback khi bật/tắt yêu thích.

---

## 3. Đánh Giá Tiêu Chí Nghiệm Thu (Acceptance Criteria)

| Tiêu chí | Trạng thái | Chứng cứ kiểm chứng |
|---|---|---|
| Đăng ký, xác minh email, đăng nhập | **Đạt** | `AccountLibraryIntegrationTests`, `EndToEndAcceptanceTests`, `widget_test.dart` |
| Google Sign-In hoạt động khi có cấu hình | **Đạt** | Server xác nhận `googleSignIn: true` từ `.env`; `ExternalTokenSecurityTests` & `ExternalAccountLinkTests` |
| Apple Sign-In triển khai đúng chuẩn | **Đạt (cần macOS/Apple key để test live)** | Đầy đủ challenge nonce, xác minh JWKS, thu hồi token trong `AppleTokens.cs` & entitlements iOS |
| Người chưa đăng nhập không vào được thư viện | **Đạt** | `SecurityIntegrationTests`, `private routes require authentication` |
| User A không truy cập được dữ liệu/ảnh bìa User B | **Đạt** | `Another_user_cannot_read_edit_attach_or_delete_private_resources`, `CoverAndV2EnhancementsTests` |
| Tạo / sửa / xóa / sắp xếp album & tải ảnh bìa | **Đạt** | `AlbumEndpoints.cs`, `CoverAndV2EnhancementsTests`, `widget_test.dart` |
| Thêm link YouTube & TikTok, đặt custom title, lưu nhiều album, chống trùng | **Đạt** | `EndToEndAcceptanceTests` (14 bước), `ProviderTests` |
| Dữ liệu bảo toàn sau đăng xuất/đăng nhập & đồng bộ đa thiết bị | **Đạt** | `EndToEndAcceptanceTests` (Device A -> Device B) |
| Trình phát tôn trọng giới hạn từng provider, không có nút giả | **Đạt** | `OfficialEmbedPlayer` + fallback `Mở trong YouTube/TikTok` |
| Không có API secrets trong source; `.env` nguyên vẹn | **Đạt** | SHA-256 `.env` khớp 100%; NuGet audit 0 lỗi |
| Backend build & tests thành công | **Đạt** | **91/91 passed** (`dotnet test -c Release`) |
| Flutter analyze & tests thành công | **Đạt** | `No issues found!`, **18/18 passed** (`flutter test`) |
| Android build & cài lên thiết bị thật | **Đạt** | Build APK thành công & cài đặt qua `adb` lên Samsung `SM_A066B` (`R7AL200102L`) |
| iOS build trên macOS/Xcode | **Chờ môi trường macOS** | Cấu hình `Runner` + `ShareExtension` đã sẵn sàng; máy hiện tại chạy Windows 11 |
