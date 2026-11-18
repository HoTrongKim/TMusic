# Quyết định triển khai V2

## Quyết định hiện hành — 18/11/2026

`AGENTS.md` hiện tại có ưu tiên hơn `codex/MASTER_PROMPT_TMUSIC.md` và quyết định lịch sử bên dưới.

- Giữ Flutter Android/iOS, ASP.NET Core .NET 10, EF Core SQL Server. `.env` nguyên vẹn byte-for-byte; chỉ `TMusicV2Dev`/`TMusicV2Test_*`, không migrate/drop/reset `TMusicDev`.
- Có Khách cục bộ lẫn tài khoản. Khách tạo album/quản lý liên kết không cần login; gỡ app trước sync làm mất dữ liệu. Login thử chuyển metadata và sao chép MP3 sang vùng tài khoản cùng thiết bị; giữ dữ liệu khách khi sync lỗi, không tự upload tệp cá nhân.
- Identity hashing, verified external tokens, refresh rotation/reuse detection, quyền sở hữu server và optimistic concurrency; đăng ký hiện cấp phiên ngay, không bắt buộc xác minh email để dùng thư viện. Capability flags không thay live OAuth test.
- Ba nguồn YouTube/TikTok/SoundCloud có attribution, creator/channel khi metadata có và mở nguồn gốc. MP3 có xác nhận quyền dùng `yt-dlp`/`FFmpeg`, không cookies/DRM/login/anti-bot bypass. Tài khoản có queue/progress/retry/cancel; khách hiện trích xuất trực tiếp. `AudioDownloadEgressProxy` kiểm tra CONNECT/host/CDN/DNS và pin IP cho extractor; không coi các đối số CLI là sandbox mạng cấp OS của mọi subprocess.
- Audio native từ tệp chọn trên máy/MP3 hợp lệ với seek, A–B, repeat, speed, queue; `OfficialEmbedPlayer` và `webview_flutter` đã gỡ. Cần kiểm thử audio nền, khóa màn hình và gián đoạn bằng thiết bị thực.
- Chia sẻ album bằng mã có thể thu hồi; recipient nhập metadata có source attribution vào thư viện riêng. Không cấp quyền đọc tệp audio/cover riêng; thu hồi chỉ chặn truy cập/nhập mới.
- UI dark charcoal `#121212` / `#181818` / `#282828`, chữ trắng, xanh `#1DB954`, light mode tùy chọn; không có nút clipboard-paste.
- Kết quả có ngày/lệnh/phạm vi trong `BUILD_LOG.md`. Provider/extractor test doubles không chứng minh live success. Runtime extractor và volume phải được kiểm tra riêng khi triển khai container.

## Lịch sử giai đoạn đầu — các điểm xung đột đã được thay thế ở trên

### Phạm vi và thứ tự ban đầu

Yêu cầu trực tiếp viết lại được thực hiện bằng sao lưu ngoài workspace rồi tạo source mới. Stack Flutter / ASP.NET Core .NET 10 / EF Core SQL Server giữ nguyên. Prompt V2 mới ưu tiên thư viện tài khoản và palette navy; các ràng buộc an toàn của prompt cũ tiếp tục áp dụng. Audit và baseline trước, foundation/auth/ownership trước domain, provider, mobile integration và verification.

### Ranh giới thiết kế ban đầu

- Domain chứa thực thể; Application chứa DTO/contract; Infrastructure chứa SQL/Identity/provider; API là nơi ghép DI và REST; Worker sử dụng cùng persistence và xử lý import.
- Song chứa danh tính nguồn và metadata có hạn; UserLibrarySong chứa tên riêng/favorite; AlbumSong biểu diễn quan hệ nhiều album. Không dùng tên video để dedupe; ID nguồn dùng collation binary.
- Mobile dùng feature-first, API typed, secure storage, guard trước mọi route cá nhân. Không dùng guest mode V2. Không nhét provider secrets vào Dart defines.
- SQL Server là dữ liệu chuẩn. Không chuyển dữ liệu cũ tự động: schema mới và mới tạo DB có tên riêng. Migration legacy cần mapping và diễn tập trên bản restore trước.
- Xác minh provider token phía server. Email trùng không tự động nối tài khoản. Refresh token lưu hash và xoay trong transaction.
- Link là tham chiếu, không phải quyền sở hữu âm thanh. Metadata không có được giữ nullable; mở nguồn là phương án đúng khi nhúng chưa xác định.
- TikTok shortlink chỉ được tải qua allowlist, DNS công khai được pin khi kết nối và kiểm tra lại từng redirect. API metadata cố định host, timeout và giới hạn bytes.

## Lịch sử nghiên cứu nguồn chính chủ, 17/11/2026

Đã đọc metadata repository và README qua GitHub API; web search trong phiên trả kết quả rỗng. Không clone/import template, chạy remote installer hoặc sao chép source vào TMusic.

| Nguồn | Điều học và áp dụng | License được API báo tại lần xem |
|---|---|---|
| nextlevelbuilder/ui-ux-pro-max-skill | Ghi token, phân cấp typography và trạng thái trước UI | MIT |
| superdesigndev/superdesign-prompts | Spec rõ spacing/interaction thay vì lựa chọn màu tùy tiện | MIT |
| RubenGlez/mobile-design | Bốn trạng thái loading/content/empty/error, native touch/safe area | MIT |
| MiguelCovarrubiasdaiz/flutter-structure | Feature-first, route guard, Dio typed failures, Riverpod DI | MIT |
| anu0501/flutter_clean_starter | Tham khảo module/state boundaries; bỏ Firebase, tenancy, codegen không cần | License không xác định; không sao chép mã |
| ardalis/CleanArchitecture | Hướng phụ thuộc vào domain, composition root | MIT |
| jasontaylordev/CleanArchitecture | Tách infrastructure/use case/test; không nhận DB mặc định của template | MIT |

Các URL gốc: `https://github.com/<owner>/<repo>` theo tên trong bảng. Phiên bản SDK/package của TMusic được quyết định từ toolchain có sẵn và restore/build, không lấy badge phiên bản của repository làm chuẩn.

Đây là ghi chép nghiên cứu tại thời điểm đó, không phải chứng nhận tuân thủ mọi điều khoản provider. Không xem pattern tham khảo là bằng chứng hoàn tất kiểm thử. Phạm vi mã nguồn hiện tại và các ranh giới cần xác minh ở `PROVIDER_INTEGRATION.md`.

## Công cụ kiểm thử cục bộ

Mailpit v1.27.8 lấy từ release chính chủ axllent/mailpit; ZIP Windows SHA-256 kiểm chứng khớp release digest `429adff7d7cfe982c43f0a8e90c54dfba23f838a91d90be6464441e862eb1fe8`. Binary và log ở `.local/` được ignore. Chỉ bind loopback SMTP11025 và HTTP18025, không gửi email ra ngoài. CI actions được pin SHA commit lấy từ refs API chính chủ.
