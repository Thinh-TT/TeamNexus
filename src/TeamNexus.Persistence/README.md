# TeamNexus.Persistence — Tầng Lưu trữ Dữ liệu & EF Core Migrations

Phân hệ sở hữu **`TeamNexusDbContext` duy nhất**, toàn bộ các thực thể miền (Entities), cấu hình ánh xạ Fluent API (`IEntityTypeConfiguration`) và **chuỗi 16 EF Core Migrations tuyến tính** quản lý **35 bảng vật lý** trên PostgreSQL 18 + `pgvector` (chi tiết đầy đủ tại [`project-docs/04-database-design.md`](../../project-docs/04-database-design.md)).

---

## 1. Cấu trúc Thư mục

- `Data/TeamNexusDbContext.cs`: DbContext trung tâm dùng chung cho cả 4 phân hệ nghiệp vụ (`Auth`, `Board`, `Ai`, `Reporting`) và triển khai `IDataProtectionKeyContext` (`DataProtectionKeys`).
- `Data/Entities/`: Định nghĩa toàn bộ 35 thực thể bảng (7 bảng Identity, 27 bảng nghiệp vụ và 1 bảng `data_protection_keys`) cùng các hằng số hệ thống (`SystemUsers.GitHubBotUserId`).
- `Data/Configurations/`: Các lớp `IEntityTypeConfiguration<T>` quy định tên bảng/cột chuẩn `snake_case`, khóa ngoại, ràng buộc `CHECK`, chỉ mục một phần (Partial Indexes), chỉ mục vector **HNSW (`vector_cosine_ops`)** và bộ lọc truy vấn toàn cục (`HasQueryFilter(x => x.DeletedAt == null)`).
- `Data/IdentityRoles.cs`: Khai báo 2 vai trò toàn cục chuẩn (`Admin` và `User`) với GUID cố định.
- `Migrations/`: Chuỗi 16 migrations từ `InitialSchema` đến `Phase15DataProtectionKeyRing`.

---

## 2. Các Quy tắc Bất biến khi Làm việc với Persistence

1. **Image PostgreSQL bắt buộc có `pgvector`:**
   Migration #14 (`Phase147KnowledgeBase`) kích hoạt `CREATE EXTENSION IF NOT EXISTS vector;` và sử dụng kiểu `vector(1536)` cùng `UseVector()`. Khi chạy PostgreSQL qua Docker, luôn dùng image `pgvector/pgvector:pg18`:
   ```bash
   docker run --name teamnexus-pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 -d pgvector/pgvector:pg18
   ```
2. **Đồng bộ Bộ lọc Xóa mềm (Matching Global Query Filters):**
   Mọi thực thể con có quan hệ bắt buộc tới thực thể cha có xóa mềm (`DeletedAt == null`) phải khai báo bộ lọc truy vấn tương ứng để không phát sinh cảnh báo EF Core `10622`.
3. **Kỷ luật Tạo Migration Mới:**
   Luôn biên dịch toàn bộ Solution (`dotnet build TeamNexus.sln -m:1 -nr:false`) **trước** khi chạy `dotnet ef migrations add` và kiểm tra độ khớp mô hình:
   ```bash
   dotnet ef migrations add <TênMigration> --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api
   dotnet ef migrations has-pending-model-changes --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api
   dotnet ef database update --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api
   ```
