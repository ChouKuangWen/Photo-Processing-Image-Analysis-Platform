# TASK-03 — Upload Application Contracts

## 1. 目的

建立 MOD-01 Upload Module 在 Application Layer 所需要的 Contracts。

本 Task 只定義 Upload Use Case 與 Infrastructure 之間的介面與必要資料模型，
不實作實際上傳流程。

---

## 2. 前置條件

- TASK-01 Project Foundation 已完成
- TASK-02 Upload Domain Model 已完成
- Batch / Image / ProcessingJob 已存在於 Domain
- 目前開發分支：`feature/mod-01-upload`

---

## 3. 規格來源

實作前必須閱讀：

- `AGENTS.md`
- `Requirements.md`
- `docs/System-Level-Specification.md`
- `docs/modules/MOD-01-Upload-Spec.md`

以 MOD-01 Specification 為主要功能依據。

---

## 4. Scope

在 `PhotoPlatform.Application` 建立 Upload Module 所需要的 Application Contracts。

預期包含 MOD-01 已定義的：

- `IUploadService`
- `IFileValidationService`
- `IFileStorageService`
- `IProcessingQueue`

若 MOD-01 已明確定義 Request / Result / DTO，
則建立對應的 Application DTO。

建議目錄：

```text
PhotoPlatform.Application
├─ Interfaces
└─ DTOs
```

---

## 5. Interface 原則

Interface 只描述：

- Application Layer 需要什麼能力
- Input
- Output
- 非同步 Contract

不得包含 Infrastructure implementation。

例如：

```text
IFileStorageService
→ 描述「保存檔案」能力

IProcessingQueue
→ 描述「加入 Processing Job」能力
```

但本 Task 不建立：

- Local File Storage
- GCP Cloud Storage
- Channel Queue
- Background Worker

---

## 6. 不得自行推導 Contract

若 Specification 沒有明確定義：

- Method Name
- Parameter
- Return Type
- DTO Field
- Error Contract

不得自行新增。

必須遵循：

```text
Stop
→ Report Gap / Conflict
→ Explain Impact
→ Propose Contract
→ Wait for Approval
```

---

## 7. Out of Scope

本 Task 不處理：

- Upload Controller
- HTTP API implementation
- UploadService implementation
- File validation implementation
- EF Core
- DbContext
- Repository
- Migration
- SQL Server mapping
- File Storage implementation
- Channel / Queue implementation
- Background Worker
- Retry
- Recovery
- Processing state machine
- ProcessingLog
- SignalR
- MOD-02 behavior
- 其他 Module

---

## 8. Domain 限制

不得修改 TASK-02 已完成的：

- Batch
- Image
- ProcessingJob
- WorkflowType
- ProcessingJobStatus

若 Application Contract 與 Domain Model 發生衝突，
必須停止並回報。

---

## 9. Dependency Rule

維持 Clean Architecture：

```text
Application
    ↓
Domain
```

Application 可以引用 Domain。

Application 不得依賴：

- Infrastructure
- EF Core
- SQL Server
- File System implementation
- ASP.NET Controller

---

## 10. Package Rule

不得新增 NuGet Package。

若認為需要新 Package：

```text
Stop
→ Report Reason
→ Wait for Approval
```

---

## 11. Testing

本 Task 主要建立 Contracts，因此不要求為純 Interface 撰寫 Unit Test。

完成後必須確認：

```bash
dotnet restore
dotnet build
dotnet test
```

既有 TASK-01 / TASK-02 Tests 必須全部通過。

---

## 12. Definition of Done

- Application Contracts 已建立
- Contract 與 MOD-01 Specification 一致
- 沒有 Infrastructure implementation
- 沒有修改 Domain behavior
- 沒有新增 NuGet Package
- Build 成功
- Existing Tests 全部通過

---

## 13. Agent 執行方式

第一階段只做 Read-Only Review。

請先回報：

1. 預計新增 / 修改哪些檔案
2. 每個 Interface 的責任
3. Specification 中找到的 Method Signature
4. 需要建立哪些 DTO
5. 是否存在規格缺口或衝突

在取得批准前不要修改程式碼。