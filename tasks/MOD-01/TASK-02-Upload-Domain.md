# TASK-02 — Upload Domain Model

**Module:** MOD-01 Upload Module  
**Target Project:** `PhotoPlatform.Domain`

---

## Goal

建立 Upload Module 所需的核心 Domain Model：

- `Batch`
- `Image`
- `ProcessingJob`

只實作 Specification 已明確定義的 Domain Entity、Relationship、Initial State 與基本 Domain Rule。

---

## Required Reading

依序閱讀：

1. `AGENTS.md`
2. `Requirements.md`
3. `docs/System-Level-Specification.md`
4. `docs/modules/MOD-01-Upload-Spec.md`
5. `docs/modules/MOD-02-Processing-Spec.md`
6. 本 Task

並檢查：

- `src/PhotoPlatform.Domain`
- `tests/PhotoPlatform.UnitTests`

---

## Scope

建立：

```text
Batch
Image
ProcessingJob
```

依 Specification 確認：

- Identity
- Required Properties
- Optional Properties
- Entity Relationships
- Initial State
- 已明確定義的 Domain Rules

如果規格沒有定義：

> 不得自行補 Business Rule。

---

## Boundary

本 Task 不處理：

- Upload API
- Upload Service
- File Validation
- EF Core / DbContext / Migration
- SQL Server Mapping
- Storage
- Queue / Channel
- Background Worker
- Retry / Recovery
- ProcessingLog
- 其他 Module Business Logic

`ProcessingJob` 僅建立基本 Domain Representation。

完整 Processing State Machine 屬 `MOD-02 Processing`。

---

## Expected Files

預期新增：

```text
src/PhotoPlatform.Domain/
└── Entities/
    ├── Batch.cs
    ├── Image.cs
    └── ProcessingJob.cs
```

若需要 Enum / Value Object，Agent 必須先說明理由。

---

## Unit Tests

在：

```text
tests/PhotoPlatform.UnitTests
```

建立 Domain Unit Tests。

允許新增：

```text
PhotoPlatform.UnitTests
→ PhotoPlatform.Domain
```

測試依實際 Domain Rule至少涵蓋：

- Entity 正常建立
- Required Data
- Entity Relationship
- Initial State

不得依賴：

- EF Core
- SQL Server
- File System
- Network
- API Host

---

## Known Issues

以下問題本 Task 不處理：

- `Workflow` Unique Constraint 疑義
- `UUID / TIMESTAMP` SQL Server Mapping
- Partial Upload Failure
- Upload 規格中的 `Domain → Infrastructure` 圖示疑義

如上述問題實際阻塞 Domain Design，依 `AGENTS.md` 停止並回報。

---

## Acceptance Criteria

- [ ] `Batch` 已建立
- [ ] `Image` 已建立
- [ ] `ProcessingJob` 基本 Domain Model 已建立
- [ ] Entity Relationship 符合 Specification
- [ ] 未實作 MOD-02 State Machine
- [ ] Domain 無 Infrastructure / EF Core / ASP.NET Core Dependency
- [ ] Unit Tests 通過
- [ ] `dotnet restore` 成功
- [ ] `dotnet build` 成功
- [ ] `dotnet test` 成功

---

## Planning Phase

第一次收到本 Task 時先不要修改檔案。

先回報：

1. 預計建立哪些 Entity
2. 每個 Entity 的 Property / Behavior
3. 是否需要 Enum / Value Object
4. 預計修改哪些檔案
5. Unit Test Plan
6. 是否存在 Missing Rule / Conflict

等待核准後再 Implementation。

---

## Conflict Rule

如規格不足：

```text
STOP
→ Report
→ Explain Impact
→ Propose
→ Wait for Approval
```