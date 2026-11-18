# TMusic V2 — Báo cáo bàn giao

**Cập nhật ngày 18/11/2026 (GMT+7)**

Bốn màn hình Flutter trong đợt bàn giao đã hoàn tất. APK debug đã được cài cập nhật lên Samsung Galaxy A06 5G (`SM-A066B`, `R7AL200102L`) qua ADB cổng `5044`. Kết quả kiểm tra và lệnh chạy nằm trong [BUILD_LOG.md](BUILD_LOG.md).

## Phần đã hoàn tất

| Màn hình | Kết quả |
|---|---|
| Thư viện | Có thanh tìm kiếm, sắp xếp, đổi lưới/danh sách album, lối vào Bài hát yêu thích và nút nhận album chia sẻ. Bộ lọc gồm Album, Bài hát, MP3 lưu trên máy, YouTube, TikTok, SoundCloud và Chưa phân loại. Dòng bài hát ghi nguồn và đánh dấu bài đang phát. |
| Chi tiết album | Có bìa vuông 196 dp, nền theo bìa, nhãn lưu trên máy và các thao tác yêu thích, thêm link, chia sẻ, thêm bài có sẵn, trộn và phát. Nút phát 56 dp dùng màu xanh `#1DB954`; album khách lấy MP3 từ bộ nhớ máy. |
| Chi tiết bài hát | Có thẻ nguồn gốc với kênh/nghệ sĩ, tiêu đề và URL khi có dữ liệu, cùng nút mở YouTube, TikTok hoặc SoundCloud. Bài hát khách phát bản lưu trên máy. Thao tác tải từ màn hình này yêu cầu xác nhận quyền tải. |
| Cá nhân và cài đặt khách | Có nút đăng nhập, tạo tài khoản, lối tới yêu thích, nhạc trên thiết bị, cài đặt và quyền riêng tư. Phiên khách lưu lựa chọn giao diện và ngôn ngữ trong bộ nhớ của phiên ứng dụng. Các màn bảo mật và phiên đăng nhập yêu cầu tài khoản. Màn chào có nút vào ứng dụng ngay. |

Chia sẻ album hỗ trợ tạo, sao chép và thu hồi mã, xem trước nguồn bài hát, rồi lưu vào thư viện. Mở màn nhận album qua biểu tượng liên kết ở Thư viện; mở màn chia sẻ qua nút chia sẻ trong chi tiết album.

### Đồng bộ Chế độ Khách

Đã sửa lỗi xóa thư viện khách khi yêu cầu đồng bộ thất bại. Thư viện cục bộ chỉ được dọn sau khi đồng bộ thành công và dữ liệu vẫn khớp với bản đã đồng bộ. Lần thử lại dùng cùng khóa tạo album để tránh tạo bản trùng. Với MP3 đã tải, ứng dụng sao chép tệp sang kho của tài khoản trên cùng điện thoại. Máy chủ nhận siêu dữ liệu bài hát và liên kết album.

## Backend và cấu hình phát triển

- API và Worker đã được khởi động lại bằng `scripts/dev.ps1` với `-Database TMusicV2Dev`. API nghe trên `127.0.0.1:5078` và `/health/ready` trả HTTP 200 từ máy phát triển và từ Android qua ADB reverse.
- Migration `20261118115706_AddSoundCloudAndAlbumSharing` đã được áp dụng cho `TMusicV2Dev` ở đợt trước. Đợt này chạy lại bộ test .NET Release trên SQL Server với các database cô lập `TMusicV2Test_*`.
- File `.env` giữ nguyên SHA-256 `5EFA5C78DEEDF3910259D261FC039AFAAF72F86EA259D210C56BA015C7A420F7`. Database cũ `TMusicDev` được bảo toàn.

## Kiểm chứng ngày 18/11/2026

- **Backend .NET 10** đạt 100/100 test, 0 bỏ qua.
- **Flutter** không có lỗi phân tích tĩnh, đạt 28/28 test. Các ca mới bao gồm lọc SoundCloud và MP3 trên máy qua nhiều trang, nguồn bài hát, tạo/thu hồi mã, nhận album, tạo album khách, cài đặt khách và giữ dữ liệu khi đồng bộ lỗi.
- **Android** đã cài `com.tmusic.tmusic` phiên bản `2.0.0` bằng `install -r`. `firstInstallTime` giữ nguyên. Đã khởi chạy MainActivity, mở Thư viện và Cá nhân, rồi quay về Trang chủ. Tiến trình ứng dụng đang chạy với 0 bản ghi `FATAL EXCEPTION` trong lần kiểm tra.
- Gói APK nằm tại `mobile/build/app/outputs/flutter-apk/app-debug.apk`.

## Phạm vi xác minh trên thiết bị

Lần kiểm tra điện thoại xác nhận cập nhật ứng dụng, điều hướng và kết nối API. Các luồng tạo/nhận mã chia sẻ, đồng bộ khách và nguồn SoundCloud được kiểm tra bằng test tự động. Chưa xác minh trên điện thoại việc đăng nhập Google, phát/tải MP3 từ URL bên ngoài, hoặc đồng bộ tệp MP3 khi chuyển sang tài khoản. Google Sign-In từng báo `UNREGISTERED_ON_API_CONSOLE` với package và chứng chỉ debug ở đợt trước. iOS chưa được build hoặc kiểm tra trên thiết bị trong đợt này.
