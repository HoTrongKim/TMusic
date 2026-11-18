# MASTER PROMPT — TMUSIC V2
## FULL PROJECT AUDIT, REFACTOR, IMPLEMENTATION & PRODUCTION READINESS

### VAI TRÒ

Bạn là Principal Software Engineer, Mobile Architect, Backend Architect, Database Engineer, UI/UX Designer, Security Engineer và QA Lead.

Nhiệm vụ của bạn là trực tiếp kiểm tra, sửa chữa, tái cấu trúc và hoàn thiện toàn bộ source code dự án **TMusic**, một ứng dụng quản lý thư viện âm nhạc cá nhân trên Android và iOS.

**KHÔNG chỉ đưa ra tư vấn. KHÔNG chỉ tạo tài liệu. PHẢI trực tiếp chỉnh sửa code, chạy kiểm thử và hoàn thiện chức năng trong repository hiện tại.**

Ngôn ngữ giao diện mặc định: Tiếng Việt.

Ngôn ngữ code, tên class, biến, database, API: Tiếng Anh.

---

# PHẦN 1 — PHÂN TÍCH DỰ ÁN HIỆN TẠI

Trước khi viết bất kỳ chức năng mới nào:

1. Đọc toàn bộ cấu trúc repository.
2. Xác định frontend, backend, database, infrastructure và các file cấu hình.
3. Đọc README, Docker, migrations, environment templates, tests và các tài liệu kỹ thuật hiện có.
4. Liệt kê những chức năng đã hoàn thiện, chưa hoàn thiện, lỗi và thiếu sót.
5. Xác định các thư viện, package và công nghệ đang được sử dụng.
6. Kiểm tra khả năng tương thích Android và iOS.
7. Kiểm tra bảo mật, API keys, secrets và authentication.
8. Phát hiện code trùng lặp, dead code, unused dependencies, mock data, UI giả và API chưa kết nối.

Tạo tài liệu `docs/PROJECT_AUDIT.md` mô tả kết quả.

### Quy tắc bảo toàn dự án

- Không xóa dữ liệu người dùng hiện tại.
- Không xóa migrations cũ một cách tùy tiện.
- Không xóa các module đang hoạt động tốt.
- Không thay toàn bộ công nghệ nếu không có lý do kỹ thuật rõ ràng.
- Ưu tiên sửa chữa và nâng cấp thay vì viết lại không cần thiết.
- Đối với thay đổi database, phải có migration an toàn.
- Trước thao tác phá hủy dữ liệu, phải có phương án backup/rollback.
- Tuyệt đối không commit API key hoặc mật khẩu thật.
- Nếu phát hiện secrets bị lộ, ghi rõ biện pháp thu hồi và thay mới.

Nếu repository đã có Flutter, ASP.NET Core và SQL Server, ưu tiên giữ nguyên stack đó.

Nếu công nghệ khác đang được sử dụng, phải phân tích chi phí chuyển đổi trước khi quyết định.

---

# PHẦN 2 — TẦM NHÌN SẢN PHẨM

## 2.1. TMusic là gì?

TMusic là ứng dụng cho phép người dùng:

- Đăng ký và đăng nhập tài khoản.
- Sao chép link bài hát yêu thích từ YouTube hoặc TikTok.
- Dán hoặc chia sẻ link vào TMusic.
- Tự động nhận diện thông tin nội dung khi API cho phép.
- Tự đặt tên bài hát theo sở thích.
- Tạo album cá nhân.
- Lưu bài hát vào một hoặc nhiều album.
- Sắp xếp thư viện nhạc.
- Tìm kiếm bài hát và album.
- Mở nội dung đã lưu bằng phương thức phát hợp lệ.
- Đồng bộ album trên nhiều thiết bị.
- Khôi phục thư viện khi đổi điện thoại.

TMusic phải tạo cảm giác đơn giản như ứng dụng ghi chú, nhưng được thiết kế chuyên biệt cho việc quản lý bộ sưu tập âm nhạc.

### Triết lý sản phẩm

**COPY LINK → ADD TO TMUSIC → NAME → CHOOSE ALBUM → SAVE.**

Người dùng không nên phải thực hiện những thao tác không cần thiết.

## 2.2. Nguyên tắc sản phẩm

- Account-first.
- Mobile-first.
- Fast interaction.
- Minimal cognitive load.
- Privacy by default.
- Accessibility.
- Reliable synchronization.
- Graceful error handling.
- Platform policy compliance.
- No fake functionality.

---

# PHẦN 3 — AUTHENTICATION & ACCOUNT MANAGEMENT

Đây là yêu cầu BẮT BUỘC.

Người dùng phải có tài khoản và đăng nhập trước khi sử dụng thư viện cá nhân.

Không triển khai guest mode trong phiên bản này.

## 3.1. Các phương thức xác thực

Triển khai:

1. Email + Password.
2. Google Sign-In.
3. Sign in with Apple.

Các phương thức phải được liên kết với một user identity nội bộ thống nhất.

Một tài khoản có thể liên kết nhiều nhà cung cấp đăng nhập, nhưng tuyệt đối không tự liên kết chỉ dựa trên email chưa được xác minh.

### Email Authentication

- Đăng ký.
- Đăng nhập.
- Xác minh email.
- Gửi lại email xác minh.
- Quên mật khẩu.
- Reset password bằng token có thời hạn.
- Đổi mật khẩu.
- Đăng xuất.
- Đăng xuất khỏi tất cả thiết bị.
- Xóa tài khoản.

Sử dụng password hashing an toàn do framework cung cấp, ưu tiên ASP.NET Core Identity nếu phù hợp.

Không tự xây dựng thuật toán hashing.

### Google Authentication

- Sử dụng Google Sign-In SDK phù hợp với Flutter.
- Backend xác minh ID token.
- Kiểm tra issuer, audience, expiry và chữ ký.
- Tạo hoặc truy xuất tài khoản nội bộ.
- Không sử dụng Google access token như session token nội bộ.

### Apple Authentication

- Sử dụng Sign in with Apple.
- Xác minh identity token phía backend.
- Hỗ trợ email relay.
- Không giả định Apple luôn trả lại họ tên và email trong những lần đăng nhập tiếp theo.
- Hỗ trợ thu hồi hoặc xử lý liên kết tài khoản theo yêu cầu hiện hành.

### Session Security

Sử dụng:

- Short-lived access token.
- Refresh token rotation.
- Refresh token revocation.
- Secure storage trên thiết bị.
- Session expiration.
- Rate limiting.
- Account lockout hoặc cơ chế chống brute force.
- Phát hiện refresh token reuse.
- Quản lý phiên theo thiết bị.

Không lưu mật khẩu hoặc refresh token dạng plaintext trong database.

## 3.2. Account Data Isolation

Mỗi user có một định danh nội bộ duy nhất.

Toàn bộ albums, library entries, favorites, settings và dữ liệu cá nhân phải gắn với user.

Backend phải lấy user ID từ authenticated principal, không tin tưởng user ID do client tự gửi.

Mỗi truy vấn, cập nhật hoặc xóa dữ liệu cá nhân phải thực thi authorization ở server.

Không được xảy ra lỗi IDOR cho phép user A truy cập album riêng tư của user B.

---

# PHẦN 4 — YOUTUBE & TIKTOK LINK INTEGRATION

## 4.1. URL Input

Hỗ trợ nhận diện:

YouTube:
- youtube.com/watch?v=
- youtu.be/
- youtube.com/shorts/
- m.youtube.com/
- Các dạng URL chia sẻ hợp lệ khác.

TikTok:
- tiktok.com/@username/video/
- vt.tiktok.com/
- vm.tiktok.com/
- Các URL chia sẻ TikTok hợp lệ khác.

Thiết kế URL parser theo adapter pattern để có thể bổ sung nền tảng mới trong tương lai.

### URL Processing Pipeline

1. Receive URL.
2. Trim và validate.
3. Detect provider.
4. Normalize URL.
5. Resolve supported redirects safely.
6. Extract content ID.
7. Check duplicate.
8. Fetch permitted metadata.
9. Return metadata preview.
10. Allow user modifications.
11. Save to account.

Không dựa vào regex đơn giản làm cơ chế bảo mật duy nhất.

### SSRF Protection

- Chỉ cho phép provider domain được định nghĩa.
- Xác minh hostname chuẩn xác.
- Giới hạn redirect.
- Không follow redirect đến private IP hoặc địa chỉ nội bộ.
- Giới hạn timeout và response size.
- Không fetch arbitrary URLs.
- Không cho user điều khiển destination request headers.

## 4.2. Metadata

Cố gắng lấy:

- Original title.
- Creator/channel.
- Thumbnail.
- Provider.
- Original URL.
- Canonical URL.
- External content ID.
- Duration nếu API cung cấp.
- Embed availability nếu xác định được.
- Metadata retrieval timestamp.

Các trường không có dữ liệu phải nullable.

Người dùng được phép đổi tên bài hát độc lập với original title.

Ví dụ:

Original title: Original Video Title

Custom title: Nhạc chill mỗi tối

Không ghi đè original metadata khi user chỉnh sửa.

## 4.3. YouTube Integration

Ưu tiên YouTube Data API v3 cho metadata.

Sử dụng YouTube IFrame Player hoặc cơ chế nhúng chính thức được phép.

Phải:

- Kiểm tra khả năng nhúng.
- Tôn trọng giới hạn API.
- Xử lý video private, deleted, blocked và unavailable.
- Hiển thị nguồn YouTube.
- Giữ nguyên các thành phần bắt buộc của player.
- Không che quảng cáo hoặc thông tin bản quyền.
- Không tuyên bố hỗ trợ background audio nếu nền tảng không cho phép.

## 4.4. TikTok Integration

Ưu tiên TikTok oEmbed và Embed Player theo tài liệu chính thức.

Phải:

- Resolve supported share links.
- Extract TikTok content ID.
- Fetch available metadata.
- Display creator attribution.
- Support official embed khi khả dụng.
- Fallback sang Open in TikTok khi không thể embed.
- Cho phép user đặt tên thủ công khi không lấy được metadata.
- Xử lý video bị xóa, riêng tư hoặc bị hạn chế.

Không sử dụng scraper nhằm vượt qua quyền truy cập hay hạn chế nội dung.

## 4.5. Copyright & Playback Compliance

TMusic V2 là **personal music-link library**, không phải công cụ tải nhạc trái phép.

Không được:

- Tự động tách MP3 từ YouTube hoặc TikTok.
- Tải và lưu bản sao audio/video không được cấp phép.
- Vượt DRM.
- Bypass paywall.
- Bypass anti-bot hoặc access restrictions.
- Giả lập background playback trái chính sách.
- Ẩn player bắt buộc của nền tảng.
- Sử dụng các dịch vụ extractor không chính thức để vượt hạn chế.

Thiết kế playback capability theo từng provider.

Ví dụ:

- EMBED_SUPPORTED
- EXTERNAL_OPEN_ONLY
- UNAVAILABLE
- UNKNOWN

Giao diện phải phản ánh chính xác khả năng thực tế.

Nếu một nguồn không cho phép phát trực tiếp, hiển thị nút mở trong ứng dụng gốc hoặc trình duyệt.

---

# PHẦN 5 — ALBUM MANAGEMENT

## 5.1. Album CRUD

Cho phép người dùng:

- Tạo album.
- Đặt tên.
- Chỉnh sửa tên.
- Đặt mô tả.
- Chọn ảnh bìa.
- Thay đổi ảnh bìa.
- Xóa album.
- Sắp xếp album.
- Tìm kiếm album.

Album mặc định riêng tư.

## 5.2. Album Content

Mỗi album có:

- ID.
- Owner ID.
- Name.
- Description.
- Cover image.
- Created at.
- Updated at.
- Song count.
- Display order.

Một bài hát có thể nằm trong nhiều album.

Dùng bảng quan hệ AlbumSong.

## 5.3. Song Management

Người dùng có thể:

- Add song.
- Rename song.
- Edit metadata.
- Add to album.
- Remove from album.
- Delete from personal library.
- Favorite/unfavorite.
- Reorder song.
- Search song.
- Filter by provider.

Phân biệt rõ:

**Remove from album** không đồng nghĩa **delete from library**.

Khi xóa album, không tự động xóa tất cả bài hát khỏi thư viện cá nhân.

## 5.4. Duplicate Detection

Nhận diện trùng theo provider + content ID.

Nếu bài đã tồn tại:

- Hiển thị bài hát hiện có.
- Cho phép thêm bài đó vào album khác.
- Không tạo record trùng không cần thiết.
- Đảm bảo database unique constraints phù hợp.

Một video YouTube và một TikTok có nội dung giống nhau nhưng external ID khác nhau vẫn được xem là hai nguồn riêng biệt.

Không tự động gộp chúng nếu không có bằng chứng chắc chắn.

---

# PHẦN 6 — UI/UX DESIGN SYSTEM

## 6.1. Design Philosophy

Thiết kế TMusic với chất lượng của một ứng dụng mobile thương mại.

Không tạo giao diện giống dashboard quản trị.

Không sử dụng phong cách AI-generated generic.

Tránh:

- Gradient tím-hồng rực rỡ.
- Glassmorphism quá mức.
- Card lồng card liên tục.
- Icon không đồng nhất.
- Layout chật chội.
- Text quá nhỏ.
- Quá nhiều nút cùng mức độ ưu tiên.
- Animation không có mục đích.
- Placeholder xuất hiện như tính năng thật.

### Visual Direction

Phong cách:

**Premium Minimal Music Library**

Bảng màu tham khảo:

- Primary Navy: `#17283D`
- Deep Navy: `#101B2A`
- Soft Blue: `#477FA8`
- Background Light: `#F7F7F3`
- Surface: `#FFFFFF`
- Main Text: `#18212B`
- Secondary Text: `#687483`
- Success: `#23836B`
- Error: `#B44343`

Hỗ trợ Light Mode và Dark Mode.

Màu có thể được tinh chỉnh để đạt tương phản WCAG AA.

### Typography

Ưu tiên typography dễ đọc, hỗ trợ đầy đủ tiếng Việt.

Có thể dùng Inter hoặc font hệ thống phù hợp.

Sử dụng typography scale nhất quán.

### Spacing

Sử dụng hệ thống spacing dựa trên bội số của 4 hoặc 8.

Bo góc vừa phải, không lạm dụng rounded corners.

### Accessibility

- Touch target đủ lớn.
- Hỗ trợ screen reader.
- Dynamic text scaling.
- Semantic labels.
- Visible focus states.
- Reduced motion.
- Proper contrast.
- Không chỉ dùng màu để thể hiện trạng thái.

## 6.2. Design Reference Research

Nếu có quyền truy cập Internet:

Nghiên cứu các pattern UI/UX phù hợp từ:

- Apple Human Interface Guidelines.
- Material Design 3.
- Mobbin.
- Spotify.
- Apple Music.
- YouTube Music.
- Các ứng dụng quản lý bookmark hiện đại.

Chỉ tham khảo luồng tương tác, typography và cấu trúc thông tin.

Không sao chép nguyên bản giao diện, hình ảnh, icon có bản quyền hoặc nhận diện thương hiệu.

Tạo design system riêng của TMusic.

---

# PHẦN 7 — REQUIRED MOBILE SCREENS

Triển khai đầy đủ:

### Authentication

1. Splash Screen.
2. Welcome Screen.
3. Login Screen.
4. Register Screen.
5. Email Verification Screen.
6. Forgot Password Screen.
7. Reset Password Flow.

### Main Application

8. Home Screen.
9. Library Screen.
10. Album Detail Screen.
11. Create Album Screen.
12. Edit Album Screen.
13. Add Song by URL Screen.
14. Song Metadata Preview Screen.
15. Edit Song Screen.
16. Search Screen.
17. Favorites Screen.
18. Playback/Content Viewer Screen.
19. Profile Screen.
20. Settings Screen.
21. Account Security Screen.
22. Manage Sessions Screen.
23. Delete Account Flow.

## 7.1. Navigation

Bottom navigation ưu tiên:

- Home.
- Library.
- Add.
- Profile.

Add có thể thiết kế như central primary action nếu UX testing cho thấy phù hợp.

Search phải dễ truy cập từ Home hoặc Library.

Không bắt người dùng đi qua nhiều màn hình chỉ để thêm một bài.

## 7.2. Home Screen

Hiển thị:

- Greeting.
- Quick Add URL.
- Recently Added.
- Favorite Albums.
- Recently Opened Albums.
- Continue Organizing.

Không hiển thị các recommendation giả.

## 7.3. Library Screen

Hai phân mục:

- Albums.
- Songs.

Có:

- Search.
- Sort.
- Filter.
- Grid/List toggle nếu hữu ích.
- Empty state.
- Loading state.
- Error state.

## 7.4. Add Song Screen

Trọng tâm của ứng dụng.

UI gồm:

- URL input.
- Paste from clipboard.
- Detect link button.
- Metadata preview.
- Editable custom title.
- Artist/creator.
- Album selector.
- Create new album.
- Save button.

Khi người dùng lưu thành công, hiển thị feedback rõ ràng và đường dẫn đến album.

Hỗ trợ nhận dữ liệu từ Android Share Intent và iOS Share Extension hoặc cơ chế native sharing phù hợp.

## 7.5. Playback Screen

Không giả định rằng mọi nguồn đều hỗ trợ audio-only playback.

Nếu provider hỗ trợ embed hợp lệ:

- Hiển thị embedded player.
- Hiển thị song title.
- Hiển thị source.
- Hiển thị album membership.
- Cho phép mở nguồn gốc.

Nếu provider không hỗ trợ:

- Hiển thị content preview.
- Nút Open in YouTube/TikTok.
- Không hiển thị nút playback không hoạt động.

Các chức năng như next, previous, autoplay, background playback chỉ được bật khi provider thực sự cho phép và hỗ trợ.

---

# PHẦN 8 — DATABASE DESIGN

Nếu sử dụng SQL Server, thiết kế tối thiểu:

### Users
- Id
- DisplayName
- Email
- EmailVerified
- AvatarUrl
- CreatedAt
- UpdatedAt
- DeletedAt

### UserAuthProviders
- Id
- UserId
- Provider
- ProviderSubject
- LinkedAt

### UserSessions
- Id
- UserId
- DeviceName
- RefreshTokenHash
- CreatedAt
- ExpiresAt
- RevokedAt

### Songs
- Id
- Provider
- ProviderContentId
- CanonicalUrl
- OriginalTitle
- OriginalCreator
- ThumbnailUrl
- DurationSeconds
- MetadataFetchedAt
- CreatedAt

### UserLibrarySongs
- Id
- UserId
- SongId
- CustomTitle
- CustomArtist
- IsFavorite
- CreatedAt
- UpdatedAt

### Albums
- Id
- UserId
- Name
- Description
- CoverImageUrl
- SortOrder
- CreatedAt
- UpdatedAt
- DeletedAt

### AlbumSongs
- Id
- AlbumId
- UserLibrarySongId
- Position
- AddedAt

### UserSettings
- UserId
- Theme
- Language
- NotificationsEnabled
- UpdatedAt

Có thể bổ sung các bảng phục vụ audit, email verification, password reset và security.

### Database Requirements

- Primary keys.
- Foreign keys.
- Unique indexes.
- Cascading rules rõ ràng.
- Concurrency control.
- Appropriate indexes.
- UTC timestamps.
- Transaction handling.
- Migrations.
- Pagination support.

Sử dụng EF Core nếu backend là ASP.NET Core.

Không lưu các thông tin nhạy cảm trong log.

Đảm bảo một UserLibrarySong không thể bị thêm vào album của user khác.

Xem xét composite constraints hoặc kiểm tra ownership trong transaction để bảo vệ tính toàn vẹn quan hệ.

---

# PHẦN 9 — BACKEND API

Triển khai versioned REST API.

Base path:

`/api/v1`

### Authentication

- POST /auth/register
- POST /auth/login
- POST /auth/google
- POST /auth/apple
- POST /auth/refresh
- POST /auth/logout
- POST /auth/forgot-password
- POST /auth/reset-password
- POST /auth/verify-email
- GET /auth/sessions
- DELETE /auth/sessions/{id}

### Users

- GET /users/me
- PATCH /users/me
- DELETE /users/me

### Songs

- POST /songs/resolve
- GET /library/songs
- POST /library/songs
- GET /library/songs/{id}
- PATCH /library/songs/{id}
- DELETE /library/songs/{id}
- PATCH /library/songs/{id}/favorite

### Albums

- GET /albums
- POST /albums
- GET /albums/{id}
- PATCH /albums/{id}
- DELETE /albums/{id}
- POST /albums/{id}/songs
- DELETE /albums/{id}/songs/{songId}
- PATCH /albums/{id}/songs/order

### Search

- GET /search?q=
- GET /library/songs?provider=
- GET /albums?sort=

### API Requirements

- OpenAPI/Swagger.
- Input validation.
- Standard error responses.
- Appropriate HTTP status codes.
- Authentication/authorization.
- Rate limiting.
- Pagination.
- Cancellation tokens.
- Structured logging.
- Correlation IDs.
- Health checks.
- Idempotency where relevant.
- No sensitive information leakage.

Ưu tiên RFC 7807 Problem Details cho lỗi HTTP.

---

# PHẦN 10 — MOBILE ARCHITECTURE

Ưu tiên Flutter và kiến trúc feature-first.

Ví dụ cấu trúc:

```text
mobile/
  lib/
    app/
      app.dart
      router/
      theme/
    core/
      api/
      auth/
      errors/
      storage/
      network/
      widgets/
    features/
      onboarding/
      authentication/
      home/
      library/
      albums/
      songs/
      link_import/
      playback/
      search/
      profile/
      settings/
    shared/
      models/
      utils/
  test/
  integration_test/
  android/
  ios/
```

Có thể dùng Riverpod hoặc state management đã hoạt động tốt trong repository.

Áp dụng:

- Dependency injection.
- Repository pattern khi có lợi ích thực tế.
- Immutable state.
- Typed navigation.
- Typed API clients.
- Clear loading/error/success states.
- Centralized theme.
- Responsive mobile layouts.
- Secure token storage.

Không over-engineering.

Không tạo quá nhiều abstraction không có giá trị.

---

# PHẦN 11 — SYNCHRONIZATION

Tất cả album và thư viện phải có dữ liệu chuẩn trên backend.

### Expected Behavior

Thiết bị A:

1. User đăng nhập.
2. Tạo album.
3. Thêm bài hát.
4. Backend lưu thành công.

Thiết bị B:

1. User đăng nhập cùng tài khoản.
2. Album được lấy từ backend.
3. Bài hát xuất hiện với tên đã chỉnh sửa.
4. Không tạo dữ liệu trùng.

### Synchronization Strategy

V1/V2 có thể sử dụng:

- Server as source of truth.
- Local cache để tăng tốc UI.
- Refresh on foreground.
- Refresh on navigation.
- Optimistic updates có rollback.
- Retry policy với backoff.
- Conflict handling theo version hoặc UpdatedAt.

Không tuyên bố hỗ trợ offline editing đầy đủ nếu chưa có conflict resolution.

Nếu offline, phải hiển thị rõ trạng thái kết nối.

---

# PHẦN 12 — SECURITY REQUIREMENTS

Kiểm tra và xử lý:

- Broken authentication.
- Broken object-level authorization.
- SQL injection.
- SSRF.
- XSS trong nội dung WebView.
- Insecure token storage.
- Hardcoded secrets.
- Excessive data exposure.
- Weak password reset.
- OAuth misconfiguration.
- Unvalidated redirects.
- Unsafe file upload.
- Missing rate limits.

Bảo mật ảnh bìa upload:

- Validate file type thực tế.
- Validate file size.
- Generate safe filenames.
- Strip unsafe metadata khi phù hợp.
- Không cho phép thực thi file upload.

Backend không được tin tưởng dữ liệu client.

Secrets phải đến từ environment hoặc secret manager.

---

# PHẦN 13 — PERFORMANCE

Các mục tiêu kỹ thuật ban đầu:

- UI đạt cảm giác mượt ở 60 FPS trên thiết bị mục tiêu.
- Danh sách album có thể cuộn ổn định.
- Không block main thread khi gọi API.
- Lazy load ảnh.
- Cache thumbnail hợp lý.
- Debounce search.
- Pagination cho thư viện lớn.
- Database queries tránh N+1.
- Không tải lại dữ liệu không cần thiết.

Các chỉ số phải được đo trên môi trường phù hợp; không tuyên bố đạt nếu chưa benchmark.

---

# PHẦN 14 — TESTING STRATEGY

### Backend Unit Tests

Kiểm thử:

- Authentication services.
- Token rotation.
- Album ownership.
- Song deduplication.
- URL normalization.
- Provider detection.
- Metadata mapping.
- Album reorder.

### Backend Integration Tests

- Register → Login.
- Login → Create Album.
- Add Song → Album.
- Duplicate Song.
- Delete Album.
- Cross-user access denied.
- Refresh token rotation.
- Invalid provider token.
- Concurrent updates.

### Flutter Tests

- Widget tests.
- Navigation.
- Form validation.
- Loading states.
- Error states.
- Login flow.
- Add Song flow.
- Album creation flow.

### End-to-End

Bắt buộc kiểm chứng ít nhất:

1. Đăng ký tài khoản.
2. Xác minh email.
3. Đăng nhập.
4. Tạo album.
5. Dán link YouTube.
6. Lưu bài hát.
7. Dán link TikTok.
8. Lưu bài hát.
9. Đăng xuất.
10. Đăng nhập lại.
11. Kiểm tra dữ liệu được bảo toàn.
12. User khác không thể xem album riêng tư.
13. Xóa album không xóa bài khỏi library.
14. Khôi phục từ lỗi mạng.

Sử dụng mock provider ở test environment, không phụ thuộc hoàn toàn vào API bên ngoài.

---

# PHẦN 15 — DEVOPS & CONFIGURATION

Cung cấp:

- `.env.example`
- Backend configuration samples.
- Dockerfile cho backend.
- Docker Compose cho các service phù hợp.
- Database migrations.
- Development setup guide.
- Production configuration guide.
- Android build guide.
- iOS build guide.
- Test commands.
- Health check configuration.

Không cho rằng Docker có thể thay thế hoàn toàn công cụ native để build và ký ứng dụng iOS.

### Configuration

Hỗ trợ các cấu hình liên quan:

```env
TMUSIC_GOOGLE_WEB_CLIENT_ID=
TMUSIC_GOOGLE_ANDROID_CLIENT_ID=
TMUSIC_GOOGLE_IOS_CLIENT_ID=

TMUSIC_ENABLE_APPLE_SIGN_IN=false
Auth__Apple__Audiences=

YouTube__ApiKey=
YouTube__DailyQuotaBudget=8000

ConnectionStrings__DefaultConnection=

Auth__Jwt__Issuer=
Auth__Jwt__Audience=
Auth__Jwt__SigningKey=

SMTP_HOST=
SMTP_PORT=
SMTP_USERNAME=
SMTP_PASSWORD=
SMTP_FROM_EMAIL=
```

Đây chỉ là template, không được sử dụng làm giá trị thật.

Ưu tiên tương thích tên biến đang tồn tại trong repository.

Không đổi configuration keys tùy tiện gây hỏng môi trường hiện tại.

---

# PHẦN 16 — REQUIRED DOCUMENTATION

Sau khi triển khai, tạo hoặc cập nhật:

```text
README.md
docs/
  PROJECT_AUDIT.md
  ARCHITECTURE.md
  DATABASE.md
  API.md
  AUTHENTICATION.md
  PROVIDER_INTEGRATION.md
  UI_DESIGN_SYSTEM.md
  TESTING.md
  DEPLOYMENT.md
  SECURITY.md
  IMPLEMENTATION_REPORT.md
```

Các file phải phản ánh code thật.

Không ghi rằng một tính năng đã hoàn thành nếu code chưa hoạt động.

---

# PHẦN 17 — IMPLEMENTATION ORDER

Thực hiện theo trình tự:

### Phase 0: Audit

- Inspect repository.
- Identify existing architecture.
- Run available checks.
- Create implementation plan.
- Identify regression risks.

### Phase 1: Backend Foundation

- Fix backend architecture.
- Configure database.
- Implement authentication.
- Implement account isolation.
- Implement migrations.

### Phase 2: Core Domain

- Songs.
- Albums.
- AlbumSongs.
- Favorites.
- Search.
- Duplicate handling.

### Phase 3: Providers

- YouTube link parsing.
- YouTube metadata.
- TikTok link parsing.
- TikTok metadata.
- Official embed integration.
- Playback capability fallbacks.

### Phase 4: Flutter UI

- Design system.
- Authentication screens.
- Home.
- Library.
- Albums.
- Add Song.
- Search.
- Profile.
- Settings.

### Phase 5: Integration

- Connect Flutter to backend.
- Secure session handling.
- Google/Apple integrations.
- Cross-device synchronization.
- Share intents.
- Error handling.

### Phase 6: Quality

- Unit tests.
- Integration tests.
- Widget tests.
- Security audit.
- Performance checks.
- Accessibility checks.

### Phase 7: Release Preparation

- Android build.
- iOS build configuration.
- Environment documentation.
- Deployment configuration.
- Final implementation report.

**Không dừng lại sau Phase 0 hoặc Phase 1. Tiếp tục triển khai toàn bộ phạm vi có thể hoàn thành trong môi trường hiện tại.**

---

# PHẦN 18 — ACCEPTANCE CRITERIA

Dự án chỉ được đánh giá hoàn thiện V2 khi:

- Người dùng có thể đăng ký tài khoản.
- Người dùng có thể đăng nhập.
- Google Sign-In hoạt động khi có cấu hình hợp lệ.
- Apple Sign-In hoạt động trên môi trường iOS được cấu hình phù hợp.
- Người chưa đăng nhập không truy cập được library.
- User A không truy cập được dữ liệu riêng của User B.
- Người dùng tạo được album.
- Người dùng sửa/xóa album.
- Người dùng thêm được link YouTube.
- Người dùng thêm được link TikTok.
- Có thể đặt custom song title.
- Có thể lưu một bài vào nhiều album.
- Duplicate links được xử lý đúng.
- Dữ liệu tồn tại sau khi đăng xuất/đăng nhập.
- Dữ liệu đồng bộ giữa hai thiết bị.
- UI không chứa nút giả hoặc trang rỗng không giải thích.
- Trình phát tôn trọng giới hạn từng provider.
- Không có API secrets trong source.
- Core tests chạy thành công.
- Backend build thành công.
- Flutter analyze thành công.
- Android build thành công trong môi trường có Android SDK.
- iOS build được kiểm chứng trong môi trường macOS/Xcode phù hợp.
- Có hướng dẫn triển khai đầy đủ.

Nếu không có credentials, máy Mac hoặc dịch vụ bên ngoài, không được giả vờ đã kiểm thử thành công.

Phải ghi rõ phần nào đã triển khai, phần nào đã kiểm thử và phần nào đang bị chặn bởi môi trường.

---

# PHẦN 19 — WORKING RULES FOR CODEX

1. Luôn kiểm tra file hiện có trước khi chỉnh sửa.
2. Ưu tiên dùng code production-ready.
3. Không chỉ xuất pseudocode.
4. Không bỏ trống các hàm quan trọng bằng TODO.
5. Không sử dụng fake authentication để thay cho real auth.
6. Không đưa dữ liệu mẫu vào production database.
7. Không hardcode API keys.
8. Không phá vỡ backward compatibility không cần thiết.
9. Cài đặt dependencies thật khi môi trường cho phép.
10. Chạy build, lint và tests sau những thay đổi lớn.
11. Tự sửa các lỗi do code mới gây ra.
12. Tuân thủ cấu trúc repository hiện có khi hợp lý.
13. Không tự ý loại bỏ tính năng đang hoạt động tốt.
14. Không tuyên bố triển khai xong khi chưa kiểm chứng.
15. Nếu thiếu API credentials, triển khai interface, adapter, validation và configuration thật; dùng mock chỉ trong test.
16. Nếu một API không hỗ trợ tính năng mong muốn, dùng phương án chính thức phù hợp thay vì bypass.
17. Không tạo thêm một project rời rạc khi mục tiêu là nâng cấp repository đang mở.
18. Mọi thay đổi cần có lý do kỹ thuật và không làm mất dữ liệu hiện tại.

---

# PHẦN 20 — FINAL OUTPUT

Sau khi trực tiếp hoàn thiện code, xuất báo cáo:

## 1. Project Audit
Những vấn đề đã phát hiện.

## 2. Architecture Changes
Những thay đổi kiến trúc thực tế.

## 3. Implemented Features
Danh sách chức năng đã có code hoạt động.

## 4. Modified Files
Những file được tạo, sửa hoặc xóa và lý do.

## 5. Database Changes
Migrations và dữ liệu bị ảnh hưởng.

## 6. Tests & Builds
Lệnh đã chạy, kết quả và lỗi còn lại.

## 7. Environment Requirements
Các API keys, credentials và service người dùng cần cung cấp.

## 8. Deployment Instructions
Lệnh chính xác để chạy backend, Android và iOS.

## 9. Remaining Limitations
Những giới hạn do API, bản quyền, thiết bị hoặc môi trường.

## 10. Completion Assessment
Đánh giá chính xác mức độ đáp ứng acceptance criteria.

---

# FINAL EXECUTION COMMAND

**BEGIN IMPLEMENTATION NOW.**

Hãy bắt đầu bằng cách đọc repository TMusic hiện tại và thực hiện comprehensive code audit.

Sau đó trực tiếp refactor, implement, integrate, test và sửa lỗi cho đến khi đạt các acceptance criteria có thể thực hiện trong môi trường hiện tại.

Không dừng ở việc viết kế hoạch.

Không yêu cầu người dùng lựa chọn những phương án kỹ thuật nhỏ khi có thể tự đưa ra quyết định hợp lý.

Ưu tiên một sản phẩm thực sự chạy được, bảo mật, đơn giản, đẹp và dễ sử dụng hơn một dự án có quá nhiều tính năng nhưng chưa hoàn thiện.

Mục tiêu cuối cùng:

**TMUSIC — A BEAUTIFUL, SECURE, ACCOUNT-BASED PERSONAL MUSIC LIBRARY FOR ANDROID AND IOS.**