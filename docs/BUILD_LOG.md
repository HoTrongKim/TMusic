# Nhật Ký Build, Kiểm Thử & Cài Đặt Thiết Bị — TMusic V2

**Ngày thực hiện:** 17–18/11/2026 (Asia/Bangkok)

---

## 1. Xác Minh Bảo Toàn `.env` & Database Cũ

- File `.env` gốc được bảo toàn nguyên vẹn byte-for-byte:
  - Kích thước: `1.800 bytes`
  - SHA-256: `5EFA5C78DEEDF3910259D261FC039AFAAF72F86EA259D210C56BA015C7A420F7`
- Database cũ `TMusicDev` trên `lpc:.\SQLEXPRESS` không bị tác động. Toàn bộ migrations V2 được áp dụng riêng lên `TMusicV2Dev`.

---

## 2. Lệnh Thực Thi & Kết Quả Thực Tế

| Giai đoạn | Lệnh thực thi | Kết quả |
|---|---|---|
| **EF Core Migration** | `dotnet ef migrations add CompleteV2SchemaAndCovers --project backend/src/TMusic.Infrastructure --startup-project backend/src/TMusic.Api --output-dir Persistence/Migrations` | **Thành công** — Tạo migration `20261117165920_CompleteV2SchemaAndCovers`. |
| **Cập nhật DB Dev** | `dotnet ef database update --project backend/src/TMusic.Infrastructure --startup-project backend/src/TMusic.Api` (`TMusicV2Dev`) | **Thành công** — Áp dụng schema đầy đủ lên `TMusicV2Dev`. |
| **NuGet Security Audit** | `dotnet restore backend/TMusic.slnx -p:NuGetAuditMode=all -warnaserror:NU1901,NU1902,NU1903,NU1904` | **Thành công** — 0 lỗ hổng bảo mật đã biết. |
| **Backend Release Build** | `dotnet build backend/TMusic.slnx -c Release` | **Thành công** — 0 warnings, 0 errors. |
| **Backend Unit + SQL Integration + E2E Tests** | `$env:TMUSIC_SQL_TEST_SERVER="Server=lpc:.\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"; dotnet test backend/TMusic.slnx -c Release --no-build` | **Thành công — 91/91 passed, 0 failed, 0 skipped** (chạy trên SQL Server thật với database cô lập `TMusicV2Test_<guid>`). |
| **Flutter Static Analysis** | `flutter analyze --no-pub` | **Thành công — No issues found!** |
| **Flutter Unit & Widget Tests** | `flutter test --no-pub` | **Thành công — 19/19 passed!** (bao gồm kiểm chứng `320dp` & `430dp` ở tỷ lệ chữ `200%` Light/Dark, Đăng ký vào thẳng Trang chủ không cần xác minh email, Login, Tạo Album, Thêm Bài URL, Unified Search, Favorites, Optimistic Rollback, Edit Song, Delete Account). |
| **Android APK Build** | `flutter build apk --debug --no-pub` | **Thành công** — Sinh gói `mobile/build/app/outputs/flutter-apk/app-debug.apk`. |
| **Khởi chạy API & Worker thực tế** | `./scripts/dev.ps1 -Service api -Database TMusicV2Dev` & `-Service worker` | **Thành công** — `/health/ready` trả `Healthy`; `/api/v1/system/capabilities` xác nhận `emailAuthentication: true`, `googleSignIn: true`, `youtubeMetadata: true`, `tikTokLinks: true`. |
| **Cài đặt lên điện thoại thật** | `adb -s R7AL200102L reverse tcp:5078 tcp:5078` + `adb -s R7AL200102L install -r build\app\outputs\flutter-apk\app-debug.apk` + `adb -s R7AL200102L shell am start -n com.tmusic.tmusic/.MainActivity` | **Thành công (`Performing Streamed Install -> Success`)** — Đã cài và khởi chạy ứng dụng trên thiết bị vật lý Samsung `SM_A066B` (`R7AL200102L`). |
| **Chẩn đoán Google Sign-In thực tế trên thiết bị** | `adb -s R7AL200102L logcat -d` | **Xác định nguyên nhân ngoại vi:** Google Play Services trả `status=UNREGISTERED_ON_API_CONSOLE` vì project Google Cloud chưa tạo OAuth 2.0 Client ID loại **Android** cho package `com.tmusic.tmusic` với chứng chỉ debug `SHA-1: 64:2E:E3:D0:ED:B7:95:DE:51:DD:AC:C8:39:AE:D5:98:84:E0:7A:0A`. Đã chuẩn hóa `ExternalAuth` trên Android (chỉ truyền `serverClientId`, hiển thị thông báo hướng dẫn rõ ràng) và kiểm thử lại `flutter analyze` (0 lỗi) + `flutter test` (19/19 đạt). |
| **Tích hợp tải MP3 cá nhân (`yt-dlp` + `FFmpeg`) & Phát Album Offline** | `winget install yt-dlp.yt-dlp` + `dotnet ef migrations add AddAudioDownloads` + `dotnet test backend/TMusic.slnx -c Release` + `flutter analyze --no-pub` + `flutter test --no-pub` | **Thành công:** Migration `20261118084140_AddAudioDownloads` áp dụng lên `TMusicV2Dev`; **Backend 94/94 passed** (bao gồm `AudioDownloadTests` kiểm thử `yt-dlp` safe args, MP3 magic header, HTTP `206 Range` streaming và chống IDOR); **Flutter 0 lỗi analyze & 20/20 widget tests passed**. |
| **Nâng cấp V3 (Smart Download Home · 4-Tab Nav · Gỡ Embedded WebView · App Icon Concept C · Offline Cache Fix)** | `dotnet test backend/TMusic.slnx -c Release` + `flutter pub get` + `flutter analyze --no-pub` + `flutter test --no-pub` + `flutter build apk --debug --no-pub` + `adb install -r` | **Thành công:** Bảo toàn 100% dữ liệu `TMusicV2Dev` (`4` users, `1` album, `2` songs, `2` download jobs) & `.env` SHA-256; **Backend 94/94 passed**; **Flutter 0 lỗi analyze & 21/21 tests passed**; gỡ `webview_flutter` và `official_embed_player.dart` an toàn. |
| **Nâng cấp Trình phát nhạc chuyên nghiệp (Seek Timestamps · Lặp 1 bài/Danh sách · Lặp đoạn A–B · Tốc độ phát · Now Playing Sheet)** | `flutter analyze --no-pub` + `flutter test --no-pub` + `flutter build apk --debug --no-pub` + `adb -P 5044 -s R7AL200102L install -r` | **Thành công:** **Flutter 0 lỗi analyze & 22/22 tests passed** (bao gồm kiểm chứng `320dp` cho `MiniPlayerDock`, `NowPlayingSheet`, tua `±10s`, lặp 3 chế độ, chỉnh đoạn lặp A–B bằng `RangeSlider` & `Chấm đầu A`/`Chấm cuối B`, tốc độ `0.8x–1.5x`, và hàng đợi phát). Bảo toàn `.env` SHA-256 và toàn bộ dữ liệu trên thiết bị. |
| **Gỡ bỏ hoàn toàn nút & chức năng "Dán từ bộ nhớ tạm" (`Clipboard.getData` / `link-paste`) + Soạn `MASTER_UI_REDESIGN_PROMPT.md`** | `flutter analyze --no-pub` + `flutter test --no-pub` + `flutter build apk --debug --no-pub` + `adb -P 5044 -s R7AL200102L install -r` | **Thành công:** Đã xóa toàn bộ hàm `_pasteAndDetect`, quyền/lệnh đọc `Clipboard.getData`, nút `link-paste` và khóa ngôn ngữ `'paste'` trên `vi`/`en`; cập nhật test xác nhận không còn tồn tại nút dán clipboard; đóng gói và cài đặt lên `R7AL200102L`. |
| **Thiết kế lại toàn bộ hệ thống giao diện TMusic V4 ("Midnight Studio & Warm Vinyl") từ nghiên cứu 3 repo (`Cross` · `MusicRoom` · `Matchy`)** | 3 Sub-Agents phân tích `AdonisEnProvence/MusicRoom`, `Amineharrabi/Matchy`, `SWE-Team-9/Cross` + `flutter analyze --no-pub` + `flutter test --no-pub` + `flutter build apk --debug --no-pub` + `adb -P 5044 -s R7AL200102L install -r` | **Thành công:** Tái thiết kế đồng bộ toàn bộ 15 file giao diện (chế độ Studio Dark mặc định + Light Mode, Floating Capsule Mini-Player kèm vòng tròn tiến độ bao quanh nút Play đảo ngược tương phản, Now Playing với sóng âm 2 tầng `CustomPaint` tô sáng trực tiếp đoạn lặp A–B & cử chỉ vuốt ngang chuyển bài, Segmented Pill Selectors xóa bỏ mọi Dropdown thô, lưới Quick-Pick 2 cột, kệ Album bìa vuông, Album Hero Stage đánh số thứ tự bài kèm Equalizer đang phát). **Flutter 0 lỗi analyze, 22/22 tests passed**, cài cập nhật an toàn lên `R7AL200102L`. |
| **Nâng cấp Master V3 (SoundCloud · Guest Local Mode + Auto-sync · Album Sharing · Source Attribution · Spotify-Inspired Dark/Light UI/UX)** | `dotnet ef migrations add AddSoundCloudAndAlbumSharing` + `dotnet ef database update` + `dotnet test backend/TMusic.slnx -c Release` + Cập nhật Mobile Models/Repositories/Theme/Widgets/NowPlaying/SmartDownload/Home | **Backend 100/100 tests passed, 0 failed** (migration `20261118115706_AddSoundCloudAndAlbumSharing` áp dụng lên `TMusicV2Dev`, daemons API & Worker đang chạy nền). Mobile đã hoàn thiện tầng Core/Models/Theme/Home/NowPlaying; đã lập tài liệu bàn giao kỹ thuật chi tiết tại `docs/AGENT_HANDOVER_REPORT.md` để Agent tiếp theo hoàn thiện 4 màn hình Flutter còn lại, chạy test và build cài đặt lên Samsung A06 (`R7AL200102L`). Bảo toàn 100% file `.env` SHA-256 (`5EFA5C78DEEDF3910259D261FC039AFAAF72F86EA259D210C56BA015C7A420F7`). |


## 3. Hoàn tất bàn giao Flutter và cài Android ngày 18/11/2026

| Lệnh | Kết quả |
|---|---|
| `flutter analyze --no-pub` | Thành công, không phát hiện vấn đề. |
| `flutter test --no-pub` | 28/28 đạt. Hai kỳ vọng cũ về điều hướng khách và thông báo URL đã cập nhật theo yêu cầu hiện hành. |
| `dotnet test backend/TMusic.slnx -c Release --nologo --verbosity quiet` | 100 đạt, 0 lỗi, 0 bỏ qua. `TMUSIC_SQL_TEST_SERVER` trỏ tới SQL Server Express cục bộ với Integrated Security. Fixture tạo các database `TMusicV2Test_*` riêng. |
| `scripts/dev.ps1 -Service api -Database TMusicV2Dev` và `scripts/dev.ps1 -Service worker -Database TMusicV2Dev` | API và Worker chạy nền. `/health/ready` trả 200. API báo hỗ trợ email, Google, SoundCloud và tải audio. |
| `flutter build apk --debug --no-pub --dart-define=TMUSIC_API_URL=http://127.0.0.1:5078` | Thành công. Tham số build bổ sung dùng Client ID OAuth công khai sau khi chuẩn hóa bằng quy tắc có sẵn trong ứng dụng. Gradle báo cảnh báo native access của JVM. |
| `adb -P 5044 -s R7AL200102L reverse tcp:5078 tcp:5078` | Reverse hoạt động. Kiểm tra từ shell Android qua `toybox nc` nhận `HTTP/1.1 200 OK` từ `/health/ready`. |
| `adb -P 5044 -s R7AL200102L install -r mobile/build/app/outputs/flutter-apk/app-debug.apk` | `Performing Streamed Install` → `Success`. Package `com.tmusic.tmusic` phiên bản `2.0.0`, versionCode `1`. `firstInstallTime` vẫn là 17/11/2026 16:30:48. `lastUpdateTime` là 18/11/2026 19:39:15. |
| `adb -P 5044 -s R7AL200102L shell am start -W -n com.tmusic.tmusic/.MainActivity` | `Status: ok`, cold launch. UI hiện mục Bài hát yêu thích và nút nhận album tại Thư viện, mục Cài đặt tại Cá nhân. Đã quay về Trang chủ. `logcat` theo PID ứng dụng ghi 0 `FATAL EXCEPTION`. |
| `Get-FileHash -Algorithm SHA256 -LiteralPath .env` | Hash trước và sau trùng khớp `5EFA5C78DEEDF3910259D261FC039AFAAF72F86EA259D210C56BA015C7A420F7`. Môi trường phát triển dùng `TMusicV2Dev` và giữ nguyên `TMusicDev`. |

**APK** có kích thước 214.968.515 byte, SHA-256 `4FA40780E36A1CAF3F36DFFA3E0DF3315E5F1171BAD401E85E00007476E34F13`.

**Phạm vi thiết bị** gồm cài cập nhật, khởi chạy, điều hướng và kết nối API. Google Sign-In, phát/tải MP3 từ URL bên ngoài và đồng bộ tệp MP3 khách chưa được kiểm tra trực tiếp trên điện thoại. Đợt này chưa build iOS.

## 4. Rà soát và hoàn thiện chức năng ngày 18/11/2026

Đây là đợt sửa trực tiếp mã nguồn sau bàn giao. Thư mục không có Git metadata; không tạo commit. Không chạy migration hoặc chỉnh dữ liệu `TMusicV2Dev`/`TMusicDev`. SQL integration tests chỉ tạo/dọn database ngẫu nhiên `TMusicV2Test_*` theo fixture. Nội dung `.env` không được in hoặc đóng gói; SHA-256 trước/sau vẫn khớp bản gốc.

| Lệnh/kiểm tra thực tế | Kết quả |
|---|---|
| Baseline `dotnet test backend/TMusic.slnx -c Release --nologo --verbosity quiet`, với `TMUSIC_SQL_TEST_SERVER` dùng SQLEXPRESS Integrated Security | 100 đạt, 0 lỗi, 0 bỏ qua. |
| Baseline `flutter analyze --no-pub` + `flutter test --no-pub` | Không có vấn đề analyze; 28/28 test đạt. |
| Regression phiên/settings trước sửa | 7 ca đầu thất bại đúng triệu chứng; 2 ca bổ sung tái hiện late-login và GET settings cũ ghi đè PATCH mới. Sau sửa và kiểm tra shared authentication guard: `flutter test --no-pub test/session_and_settings_test.dart --reporter expanded` đạt 11/11. |
| Regression hủy tải backend trước/sau sửa | 3 SQL tests ban đầu thất bại do worker vẫn công bố MP3; sau sửa lease/transaction, các ca này đạt. |
| Regression chia sẻ album trước/sau sửa | `AlbumSharingCompletenessTests`: 2 ca ban đầu thất bại (cắt 201 xuống 200 và bỏ qua query pagination). Sau sửa, cả 2 đạt; kiểm tra đủ bài/thứ tự trên SQL và từ chối query không hợp lệ. |
| Regression khách/tệp cục bộ/chia sẻ Flutter | `flutter test --no-pub test/guest_download_management_test.dart test/guest_shared_album_regression_test.dart --reporter expanded`: 13/13 đạt sau các lượt RED/GREEN, gồm tệp thật trong thư mục tạm và fake SQLite boundary. |
| `dotnet restore backend/TMusic.slnx -p:NuGetAuditMode=all -warnaserror:NU1901,NU1902,NU1903,NU1904 --verbosity quiet` | Exit 0, không có cảnh báo audit. |
| `dotnet build backend/TMusic.slnx -c Release --no-restore --nologo --verbosity quiet` | Thành công, 0 warnings, 0 errors. |
| `dotnet test backend/TMusic.slnx -c Release --no-build --nologo --verbosity quiet`, có `TMUSIC_SQL_TEST_SERVER` | **138/138 đạt, 0 lỗi, 0 bỏ qua**, gồm SQL thật và FFmpeg thật với dữ liệu tổng hợp cục bộ. |
| `flutter test --no-pub test/guest_download_management_test.dart test/guest_shared_album_regression_test.dart test/download_account_isolation_test.dart --reporter expanded` | 16/16 focused tests đạt. Ba ca isolation tái hiện trước sửa: refresh A về muộn sau chuyển B, bytes về muộn sau logout, và polling cùng chủ không được hủy MP3 đang truyền. |
| Final `flutter analyze --no-pub` | Thành công, **No issues found**, trên mã đã đóng băng sau review. |
| Final `flutter test --no-pub --reporter expanded` | **55/55 đạt**, gồm toàn bộ baseline và các ca guest/sharing/session/settings/account-isolation mới. |
| Final `flutter build apk --debug --no-pub --dart-define=TMUSIC_API_URL=http://127.0.0.1:5078` | Exit 0, tạo `mobile/build/app/outputs/flutter-apk/app-debug.apk`, **214.978.670 bytes**. Có cảnh báo JVM/Gradle native-access nhưng không làm build thất bại. |

Connection test dùng tên máy chủ `lpc:.\SQLEXPRESS`, Integrated Security, Encrypt/TrustServerCertificate; không lấy/ghi mật khẩu từ `.env`. Một lần build Debug của tác vụ con gặp MSB3027/MSB3021 do daemon API đang giữ DLL; chuyển sang Release thành công, không dừng daemon của người dùng.

APK mới chỉ cấu hình API loopback cho môi trường Android có ADB reverse; không truyền OAuth client ID vào lệnh build này và không phải bản release. Binary chỉ được build, chưa cài lên thiết bị. Các lượt Flutter 51/54 test và APK trung gian được thay bằng lượt cuối **55 tests** trên toàn bộ mã đã hoàn tất; không coi các lượt chạy trong khi review còn sửa mã là kết quả cuối.

Các sửa đã được kiểm chứng trong backend: hủy QUEUED/RUNNING thu hồi lease; worker cũ không gắn lại audio đã xóa; xóa output bị bỏ; preview album theo trang 200 bài và nhập đủ tới 5000 bài; egress CONNECT với xác thực riêng, allowlist provider/CDN và DNS IPv4 công khai được pin. yt-dlp chỉ tải native, không postprocess/ffprobe; C# chuyển MP3 bằng FFmpeg với whitelist protocol/demuxer trước input. Hạn mức chung hai extraction đồng thời, không xếp hàng; subprocess có deadline, giới hạn stdout/stderr/line và kill/wait khi hủy. Kiểm thử thật gồm WAV/M4A -> MP3, từ chối playlist mạng/concat cục bộ, quá nhiều output và giải phóng handle sau hủy.

Docker API/Worker có tùy chọn `--build-arg INSTALL_AUDIO_TOOLS=true`: yt-dlp 2026.10.26 có checksum pin, Python3 và FFmpeg. Đã kiểm tra checksum tải từ release chính thức và cú pháp Bash; **chưa build image** do Docker Linux engine không chạy. Mặc định không cài audio tools; bật build-arg và runtime feature flag là hai bước riêng.

### Giới hạn xác minh của đợt này

- Không tải media thật từ YouTube/TikTok/SoundCloud, không thử live Google/Apple OAuth, không coi fixture ID3 hoặc widget tests là bằng chứng phát nhạc thật.
- Không cài cập nhật lên điện thoại, không thay phiên tài khoản trên thiết bị và không chạy `integration_test/` nhằm tránh ghi vào thư viện đang dùng. Không khởi động lại API/Worker đang chạy; binary daemon hiện có chưa được cập nhật bởi đợt sửa này.
- Chưa thử playback nền/màn hình khóa/audio-focus trên thiết bị, chưa build iOS, chưa ký release hoặc xác minh cấu hình OAuth theo chứng chỉ.
- Proxy có test từ chối host/port/DNS private/auth/header và converter có test an toàn tệp thật; kết nối tunnel công khai thành công và mọi giới hạn tài nguyên chưa được kiểm chứng toàn diện bằng integration tests mạng.
- Tệp người dùng tự nhập chỉ được sao chép sang vùng tài khoản trên cùng máy, không tự upload/đồng bộ byte media sang thiết bị khác. Màn tệp cục bộ chưa có đủ gán album/yêu thích/đổi tên; tải khách còn dùng endpoint đồng bộ, chưa có progress/cancel server chi tiết. Tài khoản chỉ có external provider vẫn cần mật khẩu để liên kết thêm provider; chưa bổ sung flow xác thực lại provider cho trường hợp này.

## 5. Thay biểu tượng app bằng ảnh người dùng cung cấp — 18/11/2026

- Lưu nguyên bản ảnh PNG RGB 1024×1024 vào `mobile/assets/branding/tmusic-app-icon.png`; SHA-256 khớp tệp đính kèm. Không vẽ lại hoặc thay nội dung ảnh gốc.
- Thay 5 icon Android legacy (48/72/96/144/192 px), foreground adaptive và monochrome 432×432; nền adaptive đổi sang `#110631` lấy từ ảnh. Thêm khoảng đệm để logo/chữ nằm trong vùng hiển thị khi launcher dùng mặt nạ tròn. Monochrome có alpha trong suốt, lấy từ logo mới.
- Thay đủ 15 PNG iOS AppIcon đúng kích thước hiện có, RGB không alpha; giữ nguyên tên và mọi tham chiếu `Contents.json`/Xcode. Không thay splash hoặc giao diện Flutter.
- Đã đọc lại kích thước/encoding của toàn bộ 22 PNG native và xem preview hình vuông/tròn/bo góc/monochrome tại `.local/app-icon-preview.png`. Các icon cũ được sao lưu riêng vào `.local/app-icons-before-provided-image.zip`.
- `flutter build apk --debug --no-pub --dart-define=TMUSIC_API_URL=http://127.0.0.1:5078`: **thành công, exit 0**. Gradle vẫn có cảnh báo JVM native-access như các lần trước.
- Đã mở APK và so sánh pixel của cả 7 PNG icon Android đóng gói với asset mới; khớp đầy đủ. Bản APK mới ở `mobile/build/app/outputs/flutter-apk/app-debug.apk`.
- Chưa cài APK lên điện thoại; chưa build iOS vì máy hiện tại là Windows. Không chạy lại test nghiệp vụ vì đợt này chỉ đổi asset native và màu nền icon. `.env` giữ nguyên SHA-256, không thao tác database.

## 6. Build và cài bản icon mới lên Samsung A06 — 18/11/2026

Theo yêu cầu cài trực tiếp của người dùng, đã build lại và cập nhật thiết bị `R7AL200102L` (`SM-A066B`):

| Lệnh/kiểm tra | Kết quả thực tế |
|---|---|
| Nạp cấu hình bằng `scripts/Load-Environment.ps1`, chỉ truyền OAuth client ID công khai được hỗ trợ vào Dart defines | Google server client đã có cấu hình. Không in giá trị `.env`, không ghi/bundle secrets. |
| `flutter build apk --debug --no-pub --dart-define=TMUSIC_API_URL=http://127.0.0.1:5078` kèm Dart defines Google công khai | Thành công, exit 0; Gradle có cảnh báo JVM native-access nhưng không lỗi. |
| `adb -P 5044 -s R7AL200102L reverse tcp:5078 tcp:5078` | Reverse được xác nhận trong danh sách. |
| `adb -P 5044 -s R7AL200102L install -r mobile/build/app/outputs/flutter-apk/app-debug.apk` | `Performing Streamed Install` → `Success`. Không uninstall hoặc clear data. |
| `adb -P 5044 -s R7AL200102L shell am start -W -n com.tmusic.tmusic/.MainActivity` | `Status: ok`, `LaunchState: COLD`, app mở thành công. |
| `dumpsys package com.tmusic.tmusic` | Version `2.0.0`, versionCode `1`; `firstInstallTime` giữ nguyên `2026-11-17 16:30:48`; `lastUpdateTime=2026-11-18 20:44:38`; dataDir giữ nguyên. |
| PID và AndroidRuntime logcat theo PID app sau mở | Tiến trình app đang chạy, 0 `FATAL EXCEPTION` trong lần kiểm tra. |
| SHA-256 APK cài trên thiết bị so với APK build trên máy | Khớp `382D0C0BD34EF7BE2F569F3B81593ECF86EB6E2848EDADD1196233F09577E014`; kích thước APK local 214.978.248 bytes. |
| HTTP từ shell điện thoại qua `toybox nc` và ADB reverse tới `/health/ready` | `HTTP/1.1 200 OK`, nội dung `Healthy`. Giữ stdin mở ngắn sau gửi request để netcat nhận phản hồi. |
| SHA-256 `.env` sau build/cài | Khớp bản gốc; không thao tác database. |

Đây là xác minh build/cài cập nhật/icon, khởi chạy và kết nối API. Không đăng nhập/đăng xuất, không thử tải media bên ngoài hoặc thay dữ liệu thư viện để kiểm thử. API/Worker hiện có không được khởi động lại trong đợt cài mobile này.

## 7. Checkbox thêm bài, điều hướng đăng nhập và sáng/tối — 18/11/2026

Hoàn thành theo bốn yêu cầu giao diện/lỗi đăng nhập của người dùng:

- Bảng **Thêm bài từ thư viện** có checkbox chọn nhiều bài, số lượng đã chọn và nút **Thêm N bài hát**. Bài đã thuộc album được đánh dấu và khóa; từng bài có trạng thái đang thêm/thành công/lỗi. Thêm tuần tự, lấy lại phiên bản album và thông tin bài trước mỗi lần ghi để giữ optimistic concurrency và các album khách khác. Thử lại chỉ xử lý bài còn lỗi; tải trang lỗi giữ lựa chọn và thử lại đúng trang. Vẫn hiện nguồn/creator và liên kết mở ứng dụng gốc.
- Tái hiện lỗi màn Login/Register được push từ Cá nhân đã lưu phiên nhưng chưa rời màn cũ. Google/email/đăng ký nay chủ động chọn Trang chủ chỉ sau khi phiên được lưu bền vững; hủy, lỗi, phiên cũ hoặc secure-storage thất bại không điều hướng thành công. Google bị gián đoạn có thông báo riêng; backend vẫn bắt buộc xác minh token thật.
- Form đăng nhập/đăng ký căn giữa trong cột tối đa 440dp, phân cấp nút chính/phụ, khoảng cách và Google action cân đối; cuộn được khi bàn phím mở hoặc chữ lớn. Bổ sung copy tiếng Việt/Anh cho các thay đổi.
- Trang chủ có nút mặt trời/mặt trăng; lưu thiết lập qua SettingsController và chờ tải preferences trước khi bật nút để không ghi đè ngôn ngữ đã lưu. Cài đặt nằm trong Cá nhân cho cả khách và tài khoản. Nội dung Trang chủ tải lại sau đổi danh tính và xóa snapshot chủ cũ khỏi giao diện.
- Review phát hiện request cũ của tài khoản A có thể retry bằng token B. Authorized API client nay gắn với chủ phiên, kiểm tra chủ/lifecycle trước và sau khi lấy hoặc refresh token; client cũ bị đóng. Same-owner refresh tiếp tục hoạt động.

| Lệnh/kiểm tra | Kết quả thực tế |
|---|---|
| `flutter test test/album_picker_test.dart --reporter expanded` trước triển khai | RED đúng triệu chứng thiếu checkbox. Sau sửa, toàn bộ 7 ca đạt trong focused/full suite; bao gồm chọn nhiều, xác nhận, phiên bản album mới, lỗi từng bài/thử lại, membership ngoài trang, pagination và guest store thật trong test. |
| `flutter test test/google_auth_navigation_test.dart --name 'public Profile\|pushed password' --reporter expanded` với handler cũ phục hồi tạm để đối chứng | RED: Google/email Login/Register từ màn push không hiện Home. Đã khôi phục handler sửa sau kiểm tra đối chứng. |
| `flutter test test/google_auth_navigation_test.dart test/session_and_settings_test.dart --reporter expanded` | 38/38 đạt: lưu phiên chậm/thất bại, hủy/gián đoạn SDK, backend 401/409/422/503, token thiếu, late/superseded attempt, điều hướng thật và layout 320dp/200% chữ/bàn phím cho cả hai theme. SDK/HTTP được thay boundary test, không dùng tài khoản Google thật. |
| `flutter test test/authorized_api_owner_test.dart --reporter expanded` trước/sau owner guard | RED 1 đạt/3 lỗi, trực tiếp quan sát request cũ gửi `access-a` rồi retry với `access-b` (token fixture chỉ trong test). GREEN 4/4: client cũ không lấy token chủ mới, B dùng client mới, refresh cùng chủ hoạt động, callback sau dispose trả null. Dùng ApiClient/Dio/SessionController thật với HTTP adapter giả. |
| Home/theme regressions | 6/6 đạt trong full suite: khách/tài khoản đổi và lưu theme, lỗi lưu giữ theme cũ, chờ preferences và giữ language=en, đăng nhập/đăng xuất làm mới nội dung, 320dp/200% chữ. Các ca chờ preferences/làm mới Home đã RED trước sửa. |
| Final `flutter analyze --no-pub` | **No issues found**, exit 0. Đã thay API semantics deprecated trong test bằng `flagsCollection`. |
| Final `flutter test --no-pub --reporter expanded` | **99/99 đạt**, exit 0, trên toàn bộ mã cuối. Test Register cũ đã bổ sung cuộn tới nút submit theo layout mới; assertion đăng ký vào Home giữ nguyên. Các runner Flutter chạy tuần tự sau một lỗi cache do chạy chồng trước đó; không xóa cache. |
| `flutter build apk --debug --no-pub --dart-define=TMUSIC_API_URL=http://127.0.0.1:5078` kèm Google Dart defines công khai | Thành công, exit 0, Gradle assembleDebug 26.9s. Nạp bằng `scripts/Load-Environment.ps1`, hỗ trợ tên server/web/android/iOS hiện có; không in giá trị cấu hình. Chỉ còn cảnh báo JVM native-access của Gradle. |
| `adb -P 5044 -s R7AL200102L install -r mobile/build/app/outputs/flutter-apk/app-debug.apk` | **Success**, cập nhật Samsung SM-A066B. Version 2.0.0/code 1, firstInstallTime vẫn `2026-11-17 16:30:48`, lastUpdateTime `2026-11-18 21:34:19`, dataDir giữ nguyên. Không uninstall/clear data. |
| `adb ... shell am force-stop com.tmusic.tmusic` rồi `shell am start -W -n com.tmusic.tmusic/.MainActivity` | **Status: ok, LaunchState: COLD**. Đánh thức màn hình ngủ trước khi kiểm tra; app khôi phục phiên hiện có và hiển thị Trang chủ. Không đăng xuất tài khoản đang dùng. |
| UI thật qua `uiautomator dump` và `screencap` | Nút Trang chủ đổi tối → sáng → tối, tooltip tương ứng, không gear Cài đặt. Cá nhân → Cài đặt mở đúng màn. Album hiện 2 checkbox 48dp, chọn 2 hiện **Đã chọn: 2 / Thêm 2 bài hát** và bật xác nhận; đóng bằng Hủy, không bấm thêm hoặc thay thư viện. Đã xem ảnh `.local/ui-home-dark.png`, `.local/ui-light.png`, `.local/ui-current.png`, `.local/ui-picker.png`, `.local/ui-selected.png`; kết thúc ở Trang chủ, chế độ tối. |
| `pm path` + `sha256sum` APK trên điện thoại so với `Get-FileHash` local | Khớp SHA-256 **D11202A318A74B6CBD7F1721EC2767F680AAC294925AF2574A3313129C130446**; APK local **215,001,426 bytes**. |
| `adb reverse tcp:5078 tcp:5078`, HTTP từ shell Android qua `toybox nc` tới `/health/ready` | **HTTP 200 Healthy** sau cập nhật. API capabilities có Google enabled; đây chỉ là cấu hình, không phải bằng chứng OAuth mới thành công. |
| `pidof` + logcat AndroidRuntime/flutter lọc PID sau UI QA | App đang chạy, **0 FATAL EXCEPTION**, **0 unhandled Flutter exception** trong lần kiểm tra. |
| `Get-FileHash .env -Algorithm SHA256` trước/sau | Khớp bản gốc **5EFA5C78DEEDF3910259D261FC039AFAAF72F86EA259D210C56BA015C7A420F7**. Không in/đổi/bundle secrets. |

**Giới hạn xác minh:** Thiết bị đang có phiên Google thật nên giữ phiên, không đăng xuất/đăng nhập mới hoặc chọn tài khoản Google thay người dùng. Chưa xác minh mới toàn bộ Google SDK → token → API bằng thông tin đăng nhập thật; lỗi điều hướng được tái hiện/sửa bằng regression tests dùng router và session thật. Layout Login được kiểm tra widget với cả theme/bàn phím/chữ lớn, chưa mở Login trên điện thoại đang đăng nhập. Không nhấn Thêm để thay album đang dùng; ghi album/partial retry được xác minh bằng tests. Không build iOS trên Windows. Backend không đổi, không chạy lại backend tests hoặc khởi động lại API/Worker. Không chạy lệnh SQL/migration/reset trên bất kỳ database nào; chỉ đổi theme qua app rồi trả về tối. Không tạo nội dung mẫu trên thiết bị.
# 2026-11-19 — Kiểm tra cấu hình Docker SQL cục bộ

- `docker compose config --quiet` với mật khẩu tạm chỉ trong biến môi trường: thành công; không thay đổi `.env`.
- Chưa chạy container hoặc kiểm tra kết nối database: Docker Desktop/Linux engine chưa được xác nhận sẵn sàng trong phiên này. File `.env` hiện không cung cấp giá trị `MSSQL_SA_PASSWORD` cho Compose.

## 2026-11-19 — Khởi động SQL Server Docker

- Docker Engine Linux 29.1.3 đã sẵn sàng; `docker compose up -d --quiet-pull db` tạo container SQL Server và volume riêng.
- Kết nối `sqlcmd` trong container thành công; migration vào database mới `TMusicV2Dev` trong container hoàn tất, kiểm tra thấy 4 migrations và 22 bảng người dùng.
- Mật khẩu ngẫu nhiên lưu bằng SecureString của Windows dưới `.local/`, không ghi vào `.env`. API hiện tại chưa được chuyển sang instance Docker; chưa kiểm tra luồng ứng dụng qua instance này.

## 2026-11-19 — Kiểm tra API với SQL Server Docker

- Khởi động một API thử nghiệm ở `127.0.0.1:5079` với kết nối tới `TMusicV2Dev` trong container; cấu hình chỉ trong tiến trình, không sửa `.env`.
- `GET http://127.0.0.1:5079/health/ready`: HTTP 200. Chưa kiểm tra đăng nhập, tải nhạc hoặc truy cập qua Internet; cấu hình này chỉ dành cho phát triển cục bộ.

## 2026-11-19 — Kiểm tra tunnel và cấu hình build iOS

- Tạo Cloudflare Quick Tunnel tạm tới API cục bộ, đặt origin Host là `localhost:5079`; kiểm tra HTTPS `/health/ready` bằng DNS-over-HTTPS và `curl --resolve`: HTTP 200.
- Kiểm tra cấu trúc workflow iOS bằng PyYAML: input `api_url` bắt buộc và được truyền làm `TMUSIC_API_URL`. Chưa kiểm tra cài đặt hoặc các chức năng trên iPhone; tunnel thử nghiệm không có địa chỉ ổn định.
