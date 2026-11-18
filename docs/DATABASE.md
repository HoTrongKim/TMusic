# DATABASE DESIGN — TMUSIC V2

## 1. Hệ Quản Trị & Quy Tắc Cô Lập Môi Trường

- **DBMS:** Microsoft SQL Server (`Microsoft.EntityFrameworkCore.SqlServer` trên `.NET 10`).
- **Bảo toàn database cũ:** Database cũ `TMusicDev` được giữ nguyên tuyệt đối. TMusic V2 chỉ sử dụng:
  - `TMusicV2Dev` cho môi trường phát triển cục bộ (`scripts/dev.ps1`).
  - `TMusicV2Test_<guid>` cho từng phiên kiểm thử tích hợp (`SqlApiFixture`), tự động hủy sau khi test kết thúc với kiểm tra kép tên database.
- **Collation cho định danh nhạy cảm:** Các cột `Provider`, `ProviderContentId`, `RefreshTokenHash`, `ProviderSubject`, `CreateKey` sử dụng `Latin1_General_100_BIN2` để phân biệt chính xác chữ hoa/thường (ví dụ YouTube video ID `AbCdEfGhI02` khác `abcdEfGhI02`).
- **Thời gian:** Toàn bộ mốc thời gian lưu dạng `datetimeoffset` theo chuẩn UTC (`TimeProvider`).

---

## 2. Sơ Đồ Các Bảng Chính

### 2.1. `AspNetUsers` (`Account : IdentityUser<Guid>`)
Lưu định danh nội bộ thống nhất của người dùng:
- `Id` (`uniqueidentifier`, PK)
- `Email` / `NormalizedEmail` (Unique index `EmailIndex`)
- `EmailConfirmed` (`bit` — tương đương `EmailVerified`)
- `PasswordHash` (`nvarchar(max)` — PBKDF2 của ASP.NET Core Identity, nullable với tài khoản chỉ dùng OAuth)
- `DisplayName` (`nvarchar(100)`)
- `AvatarUrl` (`nvarchar(512)`, nullable — preset an toàn `avatar:*`)
- `LockoutEnd`, `AccessFailedCount`, `SecurityStamp`, `ConcurrencyStamp`
- `CreatedAt` (`datetimeoffset`)
- `UpdatedAt` (`datetimeoffset`)
- `DeletedAt` (`datetimeoffset`, nullable)

### 2.2. `UserAuthProviders` & `AspNetUserLogins`
Theo dõi các phương thức đăng nhập ngoài (`google`, `apple`) gắn với tài khoản nội bộ:
- `Id` (`uniqueidentifier`, PK)
- `UserId` (`uniqueidentifier`, FK -> `AspNetUsers.Id` `ON DELETE CASCADE`)
- `Provider` (`varchar(32)`, binary collation)
- `ProviderSubject` (`varchar(128)`, binary collation)
- `LinkedAt` (`datetimeoffset`)
- **Indexes:** Unique `(Provider, ProviderSubject)`, Index `(UserId, Provider)`.

### 2.3. `UserSessions`
Quản lý phiên đăng nhập theo thiết bị và chuỗi xoay vòng refresh token:
- `Id` (`uniqueidentifier`, PK)
- `UserId` (`uniqueidentifier`, FK -> `AspNetUsers.Id` `ON DELETE CASCADE`)
- `FamilyId` (`uniqueidentifier` — định danh phiên thiết bị xuyên suốt các lần xoay token)
- `DeviceName` (`nvarchar(100)`)
- `RefreshTokenHash` (`varchar(64)`, SHA-256 hex, Unique index, không bao giờ lưu plaintext)
- `CreatedAt`, `AuthenticatedAt`, `ExpiresAt` (`datetimeoffset`)
- `RotatedAt`, `RevokedAt` (`datetimeoffset`, nullable)
- `Version` (`rowversion`)
- **Indexes:** Unique `(RefreshTokenHash)`, Index `(UserId, FamilyId)`.

### 2.4. `UserSettings`
- `UserId` (`uniqueidentifier`, PK & FK -> `AspNetUsers.Id` `ON DELETE CASCADE`)
- `Theme` (`nvarchar(16)`: `system` | `light` | `dark`)
- `Language` (`nvarchar(10)`: `vi` | `en`)
- `NotificationsEnabled` (`bit`)
- `UpdatedAt` (`datetimeoffset`)

### 2.5. `Songs`
Danh tính nguồn bên ngoài và bộ nhớ đệm metadata có thời hạn:
- `Id` (`uniqueidentifier`, PK)
- `Provider` (`varchar(16)`: `youtube` | `tiktok`, binary collation)
- `ProviderContentId` (`varchar(128)`, binary collation)
- `CanonicalUrl` (`nvarchar(512)`)
- `OriginalUrl` (`nvarchar(2048)`)
- `OriginalTitle` (`nvarchar(1000)`, nullable)
- `OriginalCreator` (`nvarchar(300)`, nullable)
- `ThumbnailUrl` (`nvarchar(2048)`, nullable)
- `DurationSeconds` (`int`, nullable)
- `PlaybackCapability` (`varchar(32)`: `EMBED_SUPPORTED` | `EXTERNAL_OPEN_ONLY` | `UNAVAILABLE` | `UNKNOWN`)
- `MetadataFetchedAt`, `MetadataExpiresAt` (`datetimeoffset`, nullable)
- `CreatedAt` (`datetimeoffset`)
- **Indexes:** Unique `(Provider, ProviderContentId)`, Index `(MetadataExpiresAt)`.

### 2.6. `UserLibrarySongs`
Bản lưu bài hát cá nhân của từng người dùng (tồn tại độc lập kể cả khi metadata nguồn hết hạn):
- `Id` (`uniqueidentifier`, PK; Alternate Key `(Id, UserId)`)
- `UserId` (`uniqueidentifier`, FK -> `AspNetUsers.Id` `ON DELETE CASCADE`)
- `SongId` (`uniqueidentifier`, FK -> `Songs.Id` `ON DELETE RESTRICT`)
- `CustomTitle` (`nvarchar(200)`)
- `CustomArtist` (`nvarchar(200)`, nullable)
- `OriginalUrl` (`nvarchar(2048)`, nullable — giữ riêng theo user để không rò rỉ tham số chia sẻ giữa các tài khoản)
- `IsFavorite` (`bit`)
- `CreatedAt`, `UpdatedAt` (`datetimeoffset`)
- `Version` (`rowversion`)
- **Indexes:** Unique `(UserId, SongId)`, Index `(UserId, CreatedAt)`.

### 2.7. `Albums`
- `Id` (`uniqueidentifier`, PK; Alternate Key `(Id, OwnerId)`)
- `OwnerId` (`uniqueidentifier`, FK -> `AspNetUsers.Id` `ON DELETE CASCADE`)
- `Name` (`nvarchar(160)`)
- `Description` (`nvarchar(1000)`, nullable)
- `CoverImageUrl` (`nvarchar(512)`, nullable — `cover:*` hoặc `/api/v1/covers/{ownerId}/{file}`)
- `SortOrder` (`int`)
- `IsFavorite` (`bit`)
- `SourceBoundary` (`varchar(160)`, nullable — bảo vệ ranh giới nguồn theo chính sách)
- `CreateKey`, `CreateFingerprint` (phục vụ `Idempotency-Key`)
- `CreatedAt`, `UpdatedAt`, `DeletedAt` (`datetimeoffset`)
- `Version` (`rowversion`)
- **Indexes:** Unique filtered `(OwnerId, CreateKey) WHERE [CreateKey] IS NOT NULL`, Index `(OwnerId, SortOrder, Id)`.

### 2.8. `AlbumSongs`
Quan hệ nhiều-nhiều giữa `Albums` và `UserLibrarySongs` với bảo vệ quyền sở hữu bằng khóa ngoại kép:
- `Id` (`uniqueidentifier`, PK)
- `UserId` (`uniqueidentifier`)
- `AlbumId` (`uniqueidentifier`)
- `UserLibrarySongId` (`uniqueidentifier`)
- `Position` (`bigint`)
- `AddedAt` (`datetimeoffset`)
- **Composite Foreign Keys:**
  - `(AlbumId, UserId) -> Albums(Id, OwnerId)` `ON DELETE CASCADE`
  - `(UserLibrarySongId, UserId) -> UserLibrarySongs(Id, UserId)` `ON DELETE NO ACTION` (xử lý xóa tường minh trong transaction khi xóa bài khỏi thư viện hoặc xóa tài khoản, tránh lỗi multiple cascade paths của SQL Server)
- **Indexes:** Unique `(AlbumId, UserLibrarySongId)`, Index `(AlbumId, Position, Id)`.

### 2.9. Các bảng phụ trợ bảo mật & hàng đợi
- `ExternalChallenges`: lưu nonce một lần có thời hạn 5 phút cho Sign in with Apple (`Id` SHA-256, `UserId`, `ExpiresAt`).
- `ProviderQuotaWindows`: theo dõi hạn mức YouTube Data API theo ngày (`Day`, `Used`).
- `ImportPreviews`, `ImportCandidates`, `ImportJobs`: lưu trạng thái xem trước và tiến trình nhập playlist YouTube theo lô có kiểm tra toàn vẹn SHA-256 (`Checksum`).

---

## 3. Lịch Sử Migrations (`backend/src/TMusic.Infrastructure/Persistence/Migrations`)

1. `20261117143231_InitialV2` — Khởi tạo toàn bộ bảng Identity, `Songs`, `UserLibrarySongs`, `Albums`, `AlbumSongs`, `UserSessions`, `UserSettings`, `ExternalChallenges`, `Import*`, `ProviderQuotaWindows`.
2. `20261117165920_CompleteV2SchemaAndCovers` — Bổ sung `AvatarUrl`, `DeletedAt` trên `AspNetUsers`, `DeletedAt` trên `Albums` và bảng `UserAuthProviders` với unique index `(Provider, ProviderSubject)`.

### Lệnh cập nhật migration an toàn

```powershell
./scripts/dev.ps1 -Service migrate -Database TMusicV2Dev
```
