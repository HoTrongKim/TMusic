# PROVIDER INTEGRATION & AUDIO — TMUSIC V2

## 1. Nguyên Tắc Tuân Thủ Bản Quyền & Chính Sách Nền Tảng

Phạm vi hiện hành ngày 18/11/2026 theo `AGENTS.md`: TMusic lưu liên kết **YouTube, TikTok và SoundCloud**, giữ source attribution và hỗ trợ audio native. Đây là mô tả ràng buộc sản phẩm/mã nguồn, không phải xác nhận mọi URL hoặc phương thức tải đều được điều khoản provider cho phép.

- Hỗ trợ trích xuất MP3 bằng `yt-dlp` + `FFmpeg` sau xác nhận quyền, chỉ cho nội dung người dùng có quyền hoặc được phép tải. Quyết định “không tách MP3” của giai đoạn đầu đã được thay thế.
- **Không** vượt DRM, paywall, anti-bot hay quyền truy cập riêng tư.
- **Không** giả lập phát nhạc nền (background audio) cho nguồn nhúng không cho phép.
- Bỏ HTML oEmbed; mobile hiện không có embedded WebView. `OfficialEmbedPlayer`/`webview_flutter` đã gỡ. Liên kết mở trong ứng dụng/trình duyệt ngoài; `playbackCapability` còn trong hợp đồng metadata, không bật một player nhúng trong app.
- Khi metadata nguồn hết hạn (sau 29 ngày theo chính sách bộ nhớ đệm) hoặc API chưa được cấu hình, hệ thống tự động xóa metadata tạm thời nhưng **giữ nguyên vĩnh viễn liên kết, tên bài hát tùy chỉnh (`CustomTitle`), ghi chú nghệ sĩ (`CustomArtist`), trạng thái yêu thích và danh sách album của người dùng**.

---

## 2. Quy Trình Xử Lý Liên Kết (URL Processing Pipeline)

1. **Nhận & Kiểm tra sơ bộ (`SourceUrl.TryHttps`):** Cắt khoảng trắng, giới hạn tối đa 2.048 ký tự, bắt buộc giao thức `https://`, cổng mặc định `443`, không chứa thông tin đăng nhập (`UserInfo`), không chứa ký tự điều khiển hoặc punycode bất thường.
2. **Nhận diện nguồn & Chuẩn hóa (`SourceUrl.ParseVideo`):**
   - **YouTube:** Hỗ trợ `youtube.com/watch?v=`, `m.youtube.com`, `music.youtube.com`, `youtu.be/<id>`, `youtube.com/shorts/<id>`, `youtube.com/live/<id>`. Kiểm tra video ID khớp chính xác biểu thức `^[A-Za-z0-9_-]{11}$` (không backtracking) và chuẩn hóa về `https://www.youtube.com/watch?v=<id>` (loại bỏ các tham số tracking như `si=`).
   - **TikTok:** Hỗ trợ bài video `tiktok.com/@<user>/video/<id>` và bài ảnh có nhạc `tiktok.com/@<user>/photo/<id>`, trên các host `www.tiktok.com`, `tiktok.com`, `m.tiktok.com` cùng các link rút gọn hợp lệ `vt.tiktok.com/<code/>`, `vm.tiktok.com/<code/>`, `tiktok.com/t/<code/>`. Giữ nguyên loại `/video/` hoặc `/photo/` trong canonical URL và attribution.
   - **SoundCloud:** Hỗ trợ `soundcloud.com`, `www.soundcloud.com`, `m.soundcloud.com` với `/artist/track`, loại các trang/chuyên mục dành riêng; `on.soundcloud.com` qua resolver. ID track được chuẩn hóa chữ thường.
3. **Giải mã Redirect an toàn cho TikTok Shortlink (`TikTokRedirectResolver`):**
   - Tối đa 5 bước chuyển hướng (`301/302/303/307/308`), phát hiện vòng lặp ngay lập tức.
   - Chỉ đọc header phản hồi (`ResponseHeadersRead`), không tải nội dung trang bài đăng.
   - Mỗi bước chuyển hướng đều được kiểm tra lại với allowlist tên miền TikTok.
   - `SoundCloudRedirectResolver` áp dụng cùng kiểu deadline, giới hạn vòng lặp, phát hiện chu kỳ và kiểm tra allowlist trên mỗi Location cho shortlink SoundCloud.
4. **Bảo vệ chống SSRF ở tầng Socket (`SafeHttpTransport`):**
   - Tắt auto-redirect, proxy và cookies.
   - Trong `ConnectCallback`, kiểm tra hostname theo tập host riêng của từng client trong `ProviderServices.cs` và cổng `443`, phân giải IPv4, chặn địa chỉ không công khai, rồi kết nối đúng IP đã kiểm tra; TLS vẫn xác minh hostname gốc. IPv6 bị từ chối. SoundCloud bổ sung các host cụ thể `on.soundcloud.com`, `soundcloud.com`, `www.soundcloud.com`, `m.soundcloud.com` cho resolver và `soundcloud.com` cho oEmbed.
   - Giới hạn JSON `128 KB` cho TikTok/SoundCloud oEmbed, `1 MB` cho YouTube Data API kể cả thiếu `Content-Length`; timeout và headers được chặn ở client.
   - HTTP extractor có lớp riêng `AudioDownloadEgressProxy`: mỗi lần trích xuất tạo proxy loopback có credential ngẫu nhiên; chỉ chấp nhận CONNECT tới cổng 443, xác thực trước DNS, kiểm tra allowlist theo provider và biên tên miền CDN, từ chối mọi câu trả lời DNS không công khai rồi kết nối IP đã kiểm tra. Redirect sang host khác cần CONNECT mới; HTTP thường bị chặn.
   - Egress gate giới hạn từng lần trích xuất: 64 kết nối tổng, 16 tunnel đồng thời, 256 MiB tổng hai chiều, headers 16 KiB, deadline ba phút, header/connect 10 giây và idle I/O 20 giây. `AudioExtractionCapacity` bổ sung tối đa hai extractor đồng thời trong mỗi process; request tiếp theo không xếp hàng, nhận `AUDIO_DOWNLOAD_BUSY` có thể thử lại. Đây không phải hạn mức dùng chung toàn cluster nếu có nhiều API/Worker.

---

## 3. Metadata & Khả Năng Phát (`PlaybackCapability`)

### 3.1. YouTube (`YouTubeApi`)
- Sử dụng **YouTube Data API v3** (`videos?part=snippet,contentDetails,status`) với quản lý ngân sách quota hàng ngày trong SQL Server (`ProviderQuotaWindows`, cấu hình qua `YouTube:DailyQuotaBudget`).
- Kiểm tra `privacyStatus` (`public` / `unlisted`), `uploadStatus`, và cờ `status.embeddable`:
  - Không tìm thấy video -> `UNAVAILABLE`.
  - `embeddable == true` -> `EMBED_SUPPORTED`.
  - `embeddable == false` hoặc chưa cấu hình `YouTube__ApiKey` -> `EXTERNAL_OPEN_ONLY` (người dùng vẫn lưu được liên kết và tự đặt tên bài hát).
- Ảnh thumbnail chỉ chấp nhận từ `i.ytimg.com` hoặc `img.youtube.com` qua HTTPS.

### 3.2. TikTok (`TikTokMetadata`)
- Gọi endpoint chính thức `https://www.tiktok.com/oembed?url=...`.
- Lấy `title`, `author_name`, kiểm tra `thumbnail_url` thuộc miền CDN chính thức (`*.tiktokcdn.com`, `*.tiktokcdn-us.com`, `*.tiktokcdn-eu.com`).
- Loại bỏ hoàn toàn trường `html` trả về từ oEmbed; chỉ đánh dấu `EMBED_SUPPORTED` khi `type == "video"` và `embed_product_id` khớp chính xác ID video đã xác thực, ngược lại dùng `EXTERNAL_OPEN_ONLY`.

### 3.3. SoundCloud (`SoundCloudMetadata`)
- oEmbed cố định `https://soundcloud.com/oembed?format=json&url=...`, kiểm tra `provider_name`, chỉ đọc title/author/thumbnail, không chạy HTML.
- Thumbnail HTTPS chỉ nhận subdomain `.sndcdn.com`; metadata thiếu/lỗi thì fallback nullable, capability `EXTERNAL_OPEN_ONLY`.

### 3.4. Audio native, download và chia sẻ
- Audio native từ tệp hợp lệ trên thiết bị dùng `just_audio`/`audio_service`: seek, A–B, repeat, speed, queue. Background/audio focus/màn hình khóa cần kiểm tra thực trên Android/iOS.
- `YtDlpAudioExtractor` dùng `ProcessStartInfo.ArgumentList`, không chạy shell; bắt buộc proxy CONNECT, native downloader/HLS, tắt config/plugins/remote components/cookies/cache và xóa proxy env kế thừa. yt-dlp chỉ tải `bestaudio/best` vào `source.media`, có sentinel `--`, `100M`, retries hữu hạn và socket timeout 20 giây. `--fixup never`, không `-x`/postprocessor và `--ffmpeg-location` trỏ thư mục rỗng để yt-dlp không gọi FFmpeg/ffprobe tự do.
- C# `LocalMp3Converter` tự chạy FFmpeg cho tệp cục bộ với `-protocol_whitelist file,pipe` và demuxer whitelist `mov,matroska,webm,mp3,aac,ogg,flac,wav,mpegts` trước `-i`; không gọi ffprobe. MPEG-TS hỗ trợ tệp được native HLS downloader lưu cục bộ; không mở thêm giao thức mạng. Bỏ video/subtitle/data/metadata/chapter, giới hạn allocation/probe/thread và output 100 MiB. `BoundedAudioProcess` giới hạn stdout/stderr 128 KiB mỗi luồng, dòng progress 16 KiB, deadline/kill process tree và cleanup khi lỗi; executable không chạy được trả mã đã làm sạch `AUDIO_ENGINE_UNAVAILABLE`. Đây là kiểm soát invocation/parser, không phải sandbox OS chống mọi lỗ hổng native codec.
- Toàn bộ extraction có deadline ba phút. `AudioStorage` tiếp tục kiểm tra kích thước/header ID3/MPEG; riêng header không thay kiểm tra decode đầy đủ hoặc quyền tải nội dung nguồn.
- Tài khoản có hàng đợi SQL theo chủ sở hữu, retry/cancel, audio stream riêng với HTTP Range. API có runner mặc định; có thể tách Worker bằng `AudioDownload__RunInApiProcess=false`. `Storage__AudioPath` phải bền vững và dùng chung khi tách API/Worker.
- Khách hiện dùng `/guest/songs/extract-mp3` trả bytes trực tiếp; chưa tương đương queue/progress/retry/cancel bền vững của tài khoản. Login chuyển metadata và sao chép MP3 vào vùng tài khoản trên cùng máy, không tự upload media cá nhân.
- Chia sẻ album chuyển metadata/source attribution; không chia sẻ quyền đọc tệp audio/cover riêng. Thu hồi mã chặn truy cập/nhập mới, không xóa bản đã nhập.
- UI hiển thị `Nguồn: YouTube / TikTok / SoundCloud`, creator/channel khi có và external-open. Metadata không có không được thay bằng thông tin tác giả giả.

## 4. Cấu hình và giới hạn xác minh

Các tên cấu hình hiện có được giữ: `YouTube__ApiKey`, `Features__EnableTikTokVideoImport`, `Features__EnableTikTokMetadata`, `Features__EnableSoundCloudImport`, `Features__EnableAudioDownloads`, `AudioDownload__YtDlpPath`, `AudioDownload__FfmpegPath`, `AudioDownload__RunInApiProcess`, `Storage__AudioPath`. `scripts/dev.ps1` bật các tính năng V2 trong tiến trình dev; không in/sửa `.env` để kiểm tra.

`/system/capabilities` không gọi thử provider: `youtubeMetadata` kiểm tra key không rỗng, `licensedAudioDownload` kiểm tra feature flag và binary tìm thấy. Cờ `true` chưa chứng minh thực thi, quyền tải, output giải mã được hoặc live OAuth.

`ProviderTests` dùng HTTP handlers giả lập; `AudioDownloadTests` dùng `FixtureMp3Extractor` cho hành trình download. `AudioDownloadEgressTests` kiểm tra host/CDN allowlist, destination/port xấu, DNS private/mixed, authentication bắt buộc, chặn HTTP và headers quá lớn. `LocalAudioConversionTests` kiểm tra capacity hai slot, WAV tổng hợp chuyển MP3 bằng FFmpeg thực và từ chối playlist mạng/concat; các test cần FFmpeg bị bỏ qua nếu thiếu binary. Các test này chưa mở positive tunnel ra public IP hoặc chứng minh mọi byte/tunnel/timeout limit; không thay live extraction từng provider. Kết quả đã thực thi, audio nền/offline và thiết bị phải ghi riêng trong `BUILD_LOG.md`. CI hiện build/test backend và APK debug, chưa chạy extraction provider thật, E2E thiết bị hoặc iOS.

### Runtime container tùy chọn

API và Worker dùng image `.NET 10.0-noble` (Ubuntu 24.04). `--build-arg INSTALL_AUDIO_TOOLS=true` cài Python 3 và FFmpeg từ kho Noble, tải executable zipimport `yt-dlp` `2026.10.26` từ release chính chủ và kiểm tra SHA-256 `e51e69e89489f199530f13d32a4342da3ddb2cf34842f364603a066db05afbf3`. Build mặc định không cài audio tools; `Features__EnableAudioDownloads` vẫn là gate runtime riêng. Hai image có audio tools đã build và chạy trên Docker Linux ngày 19/11/2026; kết quả tải thực tế được ghi riêng trong `BUILD_LOG.md`.

Bundle đã kiểm tra được giữ tại `/usr/local/lib/tmusic/yt-dlp.pyz`; executable `/usr/local/bin/yt-dlp` là launcher `backend/tools/yt_dlp_safe.py`. Riêng TikTok dùng User-Agent minh bạch `TMusic/2.0`, tắt browser impersonation và header ngẫu nhiên. Launcher chặn hàm giải challenge của upstream trước khi chạy hoặc lấy cookie; trang yêu cầu xác minh không được xử lý tiếp. Khi không có dữ liệu video và không xác định được nguyên nhân, trả lỗi phản hồi không được hỗ trợ thay vì suy đoán là kiểm tra truy cập. C# phân biệt `SOURCE_ACCESS_CHECK_REQUIRED`/`SOURCE_RESPONSE_UNSUPPORTED` và không tự retry các lỗi này; lỗi mạng tạm thời vẫn giữ chính sách retry hữu hạn.

Adapter kiểm tra đúng phiên bản và chữ ký các hàm nội bộ đã review, dừng nếu không tương thích. Khi nâng yt-dlp cần cập nhật version/SHA, review lại các hook và chạy `python3 -m unittest discover -s backend/tools/tests -v` trong môi trường có bundle đã xác minh. Test dùng phản hồi HTTP cục bộ giả lập trên extractor thật, kiểm tra trang công khai, trang lỗi/login/challenge, không gọi solver/cookie/nhận diện trình duyệt và giữ nguyên hành vi request của provider khác. Không coi bộ test offline này là bằng chứng mọi URL TikTok sẽ tải được.

Nguồn đối chiếu cho lựa chọn runtime: Microsoft Learn `Default .NET images use Ubuntu`, Ubuntu Packages `noble/python3` và `noble/ffmpeg`, release/API `yt-dlp/yt-dlp` tag `2026.10.26`. Python 3.12 của Noble đáp ứng yêu cầu Python >=3.11 của executable zipimport. Distro packages tiếp tục nhận bản vá qua repository, không được khóa chính xác phiên bản như binary yt-dlp. Chưa đóng gói JavaScript runtime tùy chọn của yt-dlp; các nguồn cần runtime đó có thể không trích xuất được. Cần kiểm thử nội dung được phép tải và giữ hành vi fail-closed khi nguồn chặn.
