# TASK-06 — EF Core Persistence

**Task ID:** TASK-06  
**Module:** MOD-01 Upload Module  
**Task Name:** EF Core Persistence  
**Status:** Development Specification  
**Architecture:** Clean Architecture  
**Technology:** ASP.NET Core / C# / EF Core  

---

# 1. Task 目的

建立 MOD-01 Upload Module 所需的 EF Core Persistence 基礎，使既有：

```text
Batch
Image
ProcessingJob
```

Domain Entity 可以依照 MOD-01 已核准的 Database Schema 持久化至 Database。

本 Task 負責：

```text
Domain Entity
    ↓
EF Core Mapping
    ↓
DbContext
    ↓
Database Schema
    ↓
Migration
    ↓
Integration Test
```

本 Task **不負責 Upload Application Flow**。

也就是目前只建立：

> 資料可以正確存進 Database，且 Database constraints 與 MOD-01 一致。

MOD-01 定義的主要關係：

```text
Batches
   │
   └── 1:N Images
            │
            └── 1:N ProcessingJobs

Batches
   └────────── 1:N ProcessingJobs
```

`ProcessingJobs` 同時具有 `ImageId` 與 `BatchId` 關聯。

---

# 2. Scope

TASK-06 必須完成：

```text
1. EF Core DbContext
2. Batch EF Configuration
3. Image EF Configuration
4. ProcessingJob EF Configuration
5. PK / FK Mapping
6. Index Mapping
7. Unique Constraint Mapping
8. Column / Nullable / Length Mapping
9. Database Migration
10. Persistence Integration Tests
```

---

# 3. Out of Scope

本 Task 不實作：

```text
UploadService
IUploadService orchestration
File Validation
Local File Storage
Processing Queue
Background Worker
Transaction / Compensation orchestration
Upload Controller
Batch Status Controller
HTTP Error Mapping
Logging / TraceId
GCS
```

其中：

```text
File Validation
Local File Storage
```

已由前面 TASK 完成。

Upload 主流程之後才會協調 Database、Storage 與 Queue。

---

# 4. Architecture Boundary

EF Core 屬於：

```text
PhotoPlatform.Infrastructure
```

Domain 不得依賴 EF Core。

Application 不得直接依賴 Infrastructure concrete implementation。

維持：

```text
PhotoPlatform.Domain
        ↑
PhotoPlatform.Application
        ↑
PhotoPlatform.Api

PhotoPlatform.Infrastructure
        ↓
implements persistence concerns
```

不得把以下內容加入 Domain：

```csharp
using Microsoft.EntityFrameworkCore;
```

也不得把：

```text
DbContext
EntityTypeConfiguration
Migration
SQL-specific configuration
```

放入 Domain Project。

---

# 5. Database Schema

本 Task **不得重新設計 Schema**。

必須依 MOD-01 現有 Schema 實作。

- 使用 Microsoft SQL Server；Guid / UUID 明確對應 `uniqueidentifier`。
- 所有 DateTimeOffset / DateTimeOffset? 對應 `datetimeoffset(7)`，不得使用 SQL Server `timestamp` 保存日期時間。
- Unicode 欄位依 MOD-01 使用 `nvarchar`，既有長度限制不變；ProcessingJobs.ErrorMessage 的 SQL Server physical mapping 為 `nvarchar(max) NULL`，不新增業務長度限制。

## 5.1 Batch

對應：

```text
batches
```

欄位：

| Property | Database |
|---|---|
| Id | UNIQUEIDENTIFIER / PK |
| TotalCount | INT / NOT NULL |
| ProcessedCount | INT / NOT NULL |
| SuccessCount | INT / NOT NULL |
| FailedCount | INT / NOT NULL |
| Status | VARCHAR(30) / NOT NULL |
| CreatedAt | DATETIMEOFFSET(7) / NOT NULL |
| CompletedAt | DATETIMEOFFSET(7) / NULL |

預設：

```text
TotalCount      = 0
ProcessedCount  = 0
SuccessCount    = 0
FailedCount     = 0
CompletedAt     = null
```

---

# 6. Image Mapping

對應：

```text
images
```

必須符合既有 Schema：

```text
Id
BatchId
OriginalFileName
StoredPath
NewFileName
FileSize
MimeType
SHA256
TakenAt
CameraModel
ISO
ShutterSpeed
Aperture
Latitude
Longitude
LocationName
Status
CreatedAt
UpdatedAt
```

特別注意：

```text
OriginalFileName NVARCHAR(255) NOT NULL
StoredPath       VARCHAR(500) NOT NULL
NewFileName      NVARCHAR(255) NULL
MimeType         VARCHAR(100) NOT NULL
SHA256           VARCHAR(64) NULL
CameraModel      NVARCHAR(100) NULL
ShutterSpeed     VARCHAR(50) NULL
Aperture         VARCHAR(50) NULL
LocationName     NVARCHAR(150) NULL
```

不得自行增加、刪除或重新命名 Column。

---

## 6.1 Approved Image Domain Alignment

TASK-06 允許在既有 Image Entity 補充以下 nullable properties，使 Domain 與 MOD-01 既有 Schema 對齊：

```csharp
string? NewFileName
string? SHA256
DateTimeOffset? TakenAt
string? CameraModel
int? ISO
string? ShutterSpeed
string? Aperture
decimal? Latitude
decimal? Longitude
string? LocationName
```

此為 TASK-02 延後欄位的明確授權，不增加 Database Column，也不改變既有建構子 Contract。
不得新增 Metadata parsing、SHA-256 calculation、Naming behavior、Analysis behavior 或新 Business Rule。
Domain 不得因此依賴 EF Core。

---

# 7. ProcessingJob Mapping

對應：

```text
processing_jobs
```

必須包含：

```text
Id
ImageId
BatchId
Workflow
Status
RetryCount
CreatedAt
StartedAt
CompletedAt
ErrorCode
ErrorMessage
```

其中：

```text
Workflow     VARCHAR(30)
Status       VARCHAR(30)
ErrorCode    VARCHAR(50)
ErrorMessage NVARCHAR(MAX) NULL
```

以及：

```text
RetryCount default = 0
```

不得在本 Task 修改 Workflow Definition。

---

# 8. Relationships

必須建立：

```text
Batch 1 ───── N Image
```

FK：

```text
Images.BatchId
    →
Batches.Id
```

以及：

```text
Image 1 ───── N ProcessingJob
```

FK：

```text
ProcessingJobs.ImageId
    →
Images.Id
```

另外：

```text
Batch 1 ───── N ProcessingJob
```

FK：

```text
ProcessingJobs.BatchId
    →
Batches.Id
```

以下三條關聯全部明確設定 `DeleteBehavior.NoAction`：

- Batch → Images。
- Image → ProcessingJobs。
- Batch → ProcessingJobs。

不得使用 EF Core default behavior 代替明確設定，也不得自行改為 Cascade、Restrict 或 SetNull。

---

# 9. Indexes

依 MOD-01 建立：

## Batch

```text
Status
```

## Image

```text
BatchId
SHA256
Status
TakenAt
```

## ProcessingJob

```text
ImageId
BatchId
Status
```

不得自行新增「覺得可能有效能幫助」的額外 Index。

---

# 10. Unique Constraint

Processing Job 必須有：

```text
UNIQUE (
    ImageId,
    Workflow
)
```

目的：

> 同一個 Image 不得存在兩個相同 Workflow 的 ProcessingJob。

例如：

```text
ImageId = 10
Workflow = Full
```

已存在後，再建立：

```text
ImageId = 10
Workflow = Full
```

必須由 Database Constraint 拒絕。

但：

```text
ImageId = 10
Workflow = Naming
```

可以是另一筆 Job。

---

# 11. EF Core Configuration

建議檔案：

```text
PhotoPlatform.Infrastructure
└─ Persistence
   ├─ PhotoPlatformDbContext.cs
   │
   └─ Configurations
      ├─ BatchConfiguration.cs
      ├─ ImageConfiguration.cs
      └─ ProcessingJobConfiguration.cs
```

實際目錄應先由 Agent Pre-review 檢查 Repository 現況。

如果目前已有：

```text
DbContext
Persistence/
Data/
Configurations/
```

則沿用既有結構。

**不得為了符合本 Task 範例而建立第二套 Persistence architecture。**

MOD-01 建議使用：

```csharp
IEntityTypeConfiguration<T>
```

並避免大量 Database Mapping 塞進 Entity。

例如概念：

```csharp
internal sealed class BatchConfiguration
    : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        ...
    }
}
```

實際 property 名稱必須與現有 Domain Entity 一致。

---

# 12. DbContext

DbContext 必須能管理：

```csharp
DbSet<Batch>
DbSet<Image>
DbSet<ProcessingJob>
```

實際命名應遵循現有 project convention。

例如：

```csharp
public DbSet<Batch> Batches { get; set; }
public DbSet<Image> Images { get; set; }
public DbSet<ProcessingJob> ProcessingJobs { get; set; }
```

DbContext 應於：

```csharp
OnModelCreating(...)
```

套用 Configuration。

例如：

```csharp
modelBuilder.ApplyConfigurationsFromAssembly(...);
```

或現有專案採用的等價方式。

不要為了 TASK-06 自行建立另一套 configuration discovery strategy。

---

## 12.1 Design-time DbContext

核准在 Infrastructure 建立 `IDesignTimeDbContextFactory<PhotoPlatformDbContext>`，供 EF Migration tooling 使用。
預期檔案為 `Persistence/PhotoPlatformDbContextFactory.cs`。
不得因此修改 API architecture、提前實作 Upload DI，或將 Credential hardcode 至 Repository。

---

# 13. Database Provider

TASK-06 **不得自行選擇或更換 Database Provider**。

Agent 必須在 Pre-review 確認：

```text
System-Level Specification
Existing csproj packages
Existing configuration
Existing DbContext
```

所指定的 provider。

如果目前已核准：

```text
SQL Server
```

就使用現有 SQL Server EF Core Provider。

如果 Repository 與 System-Level 對 Provider 不一致：

```text
STOP
→ Report Conflict
```

不得自行改成：

```text
SQLite
PostgreSQL
InMemory
```

---

# 14. Migration

Mapping 完成後必須建立對應 Migration。

Migration 必須反映：

```text
batches
images
processing_jobs
```

以及：

```text
PK
FK
Index
Unique Constraint
Nullable
Column Length
Database Type
```

Migration 內容不得出現未在 MOD-01 核准的新 Schema。

如果 Migration 產生：

```text
多餘 Column
錯誤 FK
多餘 Index
錯誤 Nullable
錯誤 Table Name
```

必須先修正 Mapping，不得手動把 Migration 改成另一套未對應 Model 的 Schema。

---

# 15. Integration Tests

這個 TASK 不能只有 Unit Test。

TASK-06 至少測：

## TEST-01 Batch Persistence

```text
Create Batch
↓
SaveChanges
↓
Query Database
↓
資料正確存在
```

## TEST-02 Image FK

```text
Batch
↓
Image.BatchId
↓
Save
↓
Relationship 正確
```

## TEST-03 ProcessingJob Relationships

確認：

```text
ProcessingJob.ImageId
ProcessingJob.BatchId
```

都正確指向現有 Entity。

## TEST-04 Unique Constraint

先建立：

```text
ImageId = X
Workflow = Full
```

再建立相同：

```text
ImageId = X
Workflow = Full
```

Database 必須拒絕。

這必須真正驗證 Database Constraint。

## TEST-05 Different Workflow Allowed

例如：

```text
ImageId = X
Workflow = Full

ImageId = X
Workflow = Naming
```

若 Domain / Workflow contract 允許，Database unique constraint 本身不得因 ImageId 相同而拒絕。

## TEST-06 Foreign Key Constraint

不存在的：

```text
BatchId
ImageId
```

不得建立合法關聯資料。

## TEST-07 Transaction Rollback

在實際 Transaction：

```text
Insert Batch
Insert Image
Insert Job
↓
Rollback
```

之後確認：

```text
Batch 不存在
Image 不存在
Job 不存在
```

這裡只驗證 **Database Transaction capability**。

不要在 TASK-06 做：

```text
Storage compensation
```

那是後續 Upload orchestration Task。

---

# 16. Test Database

不得使用 EF Core InMemory Provider 來宣稱：

```text
FK
Unique Constraint
Transaction
```

已被正確驗證。

TASK-06 的重點之一就是實際 relational database behavior。

已核准的 Local Integration Test strategy 為 Docker SQL Server，必須使用真實 Microsoft SQL Server。

Connection String 從環境變數取得：

```text
PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING
```

不得將正式或測試 Credential hardcode 至 Repository。
不得使用 EF Core InMemory、SQLite、Testcontainers 或 Mock Database。

TASK-06 acceptance 時必須實際啟動 SQL Server，套用並驗證 Migration、FK、Unique Constraint、Transaction Rollback。
僅有 Build 成功、測試略過或未連線的模型檢查，不符合此驗收要求。

---

# 17. Expected Files

Pre-review 後依現有 repo 調整，但預期主要可能涉及：

```text
src/PhotoPlatform.Infrastructure/
└─ Persistence/
   ├─ PhotoPlatformDbContext.cs
   └─ Configurations/
      ├─ BatchConfiguration.cs
      ├─ ImageConfiguration.cs
      └─ ProcessingJobConfiguration.cs

src/PhotoPlatform.Infrastructure/
└─ Migrations/
   └─ ...

tests/PhotoPlatform.IntegrationTests/
└─ Persistence/
   └─ UploadPersistenceTests.cs
```

可能還需要：

```text
Infrastructure.csproj
IntegrationTests.csproj
```

但只能在：

> 現有 EF Provider / ProjectReference 不足，而且符合既有 System-Level 決策

時修改。

---

## 17.1 Approved Additional Files / Changes

除上述 Configuration、DbContext、Migration 與 Integration Test 外，核准範圍包含：

- 修改 `src/PhotoPlatform.Domain/Entities/Image.cs`：僅補充 §6.1 的 nullable properties。
- 新增 `src/PhotoPlatform.Infrastructure/Persistence/PhotoPlatformDbContextFactory.cs`：僅供 design-time tooling。
- 新增 `.config/dotnet-tools.json`：repository-local `dotnet-ef` 10.0.12 manifest。
- 修改 `src/PhotoPlatform.Infrastructure/PhotoPlatform.Infrastructure.csproj`：加入 §19 核准套件。
- 修改 `tests/PhotoPlatform.IntegrationTests/PhotoPlatform.IntegrationTests.csproj`：加入 Infrastructure ProjectReference。

不得因此修改 API、Application Contracts、Storage 或 Queue。

---

# 18. Prohibited Changes

Agent 不得自行修改：

```text
Domain Entity business meaning
Database table list
Column list
Column type
PK
FK
Index
Unique constraint
Relationship
Workflow
API
Storage
Queue
Error contract
```

不得新增：

```text
Repository pattern
Unit of Work abstraction
Generic Repository
New Database Table
Audit Table
Outbox Table
Soft Delete
CreatedBy / UpdatedBy
RowVersion
```

除非已有 System-Level / existing approved contract 明確要求。

> **TASK-06 是把既有 Schema 落實成 EF Core，不是重新設計 Data Access Architecture。**

---

# 19. NuGet / Dependency Rule

已核准以下固定版本：

| Dependency | Version | Location / Purpose |
|---|---|---|
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.12 | Infrastructure SQL Server Provider |
| Microsoft.EntityFrameworkCore.Design | 10.0.12 | Infrastructure Migration design-time support |
| dotnet-ef | 10.0.12 | Repository-local tool manifest |

EF Core packages 必須維持相同版本。
`dotnet-ef` 必須使用 repository-local tool manifest，不依賴 global tool。
除上述核准項目外，不得自行新增其他 NuGet Package；若需要額外依賴，必須停止並回報取得批准。

---

# 20. Acceptance Criteria

TASK-06 完成後必須滿足：

```text
[ ] Image 僅補充核准的 nullable properties，無新增 Processing behavior
[ ] Guid = uniqueidentifier；DateTimeOffset = datetimeoffset(7)
[ ] Unicode 欄位依 MOD-01 明確 Mapping
[ ] 三條 FK 明確使用 DeleteBehavior.NoAction
[ ] EF 套件與 repository-local dotnet-ef 均為 10.0.12
[ ] Design-time factory 不改變 API architecture 或 Upload DI
[ ] 真實 Docker SQL Server 驗收，連線來源為核准環境變數
[ ] DbContext 可以管理 Batch / Image / ProcessingJob
[ ] 三個 Entity 有獨立 EF Configuration
[ ] Table Name 符合 MOD-01
[ ] Column Mapping 符合 MOD-01
[ ] Required / Nullable 符合 MOD-01
[ ] String Length 符合 MOD-01
[ ] PK 正確
[ ] FK 正確
[ ] Index 正確
[ ] ProcessingJob(ImageId, Workflow) Unique 正確
[ ] Migration 正確產生
[ ] Migration 無額外 Schema
[ ] 真實 relational integration test pass
[ ] FK constraint test pass
[ ] Unique constraint test pass
[ ] Transaction rollback test pass
[ ] Domain 無 EF Core dependency
[ ] Application 無 Infrastructure dependency
[ ] Build pass
[ ] Unit tests pass
[ ] Integration tests pass
[ ] git diff --check pass
```

---

# 21. Definition of Done

TASK-06 完成流程：

```text
Specification Review
        ↓
Pre-Implementation Review
        ↓
READY
        ↓
User Approval
        ↓
Implementation
        ↓
Build
        ↓
Unit Tests
        ↓
Integration Tests
        ↓
Migration Review
        ↓
git diff --check
        ↓
Human Review
        ↓
Commit
```

Agent **不得自行 commit / push**。

---

# 22. Agent Conflict Rule

若發現：

```text
TASK-06
    ↕
MOD-01
    ↕
System-Level
    ↕
Existing Domain Entity
    ↕
Existing Database code
```

有任何衝突：

```text
STOP
↓
Report Conflict
↓
Explain Impact
↓
Propose Minimal Change
↓
Wait for Approval
```

不得：

```text
自行修改 Domain
自行修改 Schema
自行新增 FK
自行選 DeleteBehavior
自行加入 NuGet
自行改 Database Provider
```

---

# 23. TASK-06 完成後的位置

```text
TASK-04
FileValidationService
        ✅

TASK-05
LocalFileStorageService
        ✅

TASK-06
EF Core Persistence
        ⬅ 本 Task
```

TASK-06 完成後，MOD-01 會具備三個主要 Infrastructure 基礎：

```text
Validation
     ✅
Storage
     ✅
Database
     ✅
```

下一步預計：

```text
TASK-07
Processing Queue
```

之後 Upload Application Service 再協調：

```text
Validation
+
Storage
+
Database
+
Queue
```
