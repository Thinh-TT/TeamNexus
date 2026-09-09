# TeamNexus.Persistence

Sở hữu **một DbContext duy nhất** (`TeamNexusDbContext`) cùng toàn bộ domain entities,
EF configurations và **một chuỗi migration duy nhất** cho toàn bộ modular monolith
(theo `Project-Documents/04-database-design.md` §1.1).

- `Data/Entities/` – entities (Identity + Workspace/Board/RefreshToken ở Giai đoạn 1).
- `Data/Configurations/` – `IEntityTypeConfiguration` (snake_case tên bảng, FK, index,
  CHECK constraint, soft-delete query filters).
- `Data/IdentityRoles.cs` – 3 role Admin/Manager/Member (seed qua migration, Guid cố định).
- `Migrations/` – migration chain (sinh bởi `dotnet ef`, áp lên PostgreSQL).

## Lệnh migration (chạy từ thư mục gốc repo)

```bash
dotnet ef migrations add <TênMigration> --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api
dotnet ef database update             --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api
```

Connection string đọc từ `ConnectionStrings:DefaultConnection` (User Secrets của
`TeamNexus.Api` hoặc env var `ConnectionStrings__DefaultConnection`).
