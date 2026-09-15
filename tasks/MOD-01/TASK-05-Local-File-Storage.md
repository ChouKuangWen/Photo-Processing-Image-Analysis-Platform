# TASK-05 — Local File Storage Implementation

**Module:** MOD-01 Upload  
**Task:** TASK-05  
**Title:** Local File Storage Implementation  
**Architecture:** Clean Architecture  
**Target:** .NET 10 / C#  
**Status:** Development Specification  

---

# 1. Purpose

實作 MOD-01 所需的本機檔案儲存能力。

本 Task 應實作既有 `IFileStorageService` Contract，使 Application Layer 能透過抽象介面保存及刪除上傳檔案，而不需要知道實際使用的是 Local Storage、Google Cloud Storage 或其他 Storage Provider。

本 Task 僅實作 **Local File Storage**。

不得修改 TASK-03 已建立的 `IFileStorageService` Contract。

---

# 2. Scope

本 Task 僅負責：

- 實作 `IFileStorageService`
- 將來源 Stream 寫入本機檔案系統
- 建立必要的 Storage Directory
- 回傳既有 Contract 定義的 Storage Result / Stored Path
- 支援檔案刪除
- 支援 Cancellation
- 避免覆寫既有檔案
- 正確管理由 Storage Service 自己建立的 File Stream
- 不 Dispose 呼叫端提供的 Source Stream
- 建立對應 Unit Tests

---

# 3. Expected Files

預計新增：

```text
src/
└─ PhotoPlatform.Infrastructure/
   └─ Storage/
      └─ LocalFileStorageService.cs

tests/
└─ PhotoPlatform.UnitTests/
   └─ Infrastructure/
      └─ LocalFileStorageServiceTests.cs
```

必要時允許修改：

```text
tests/PhotoPlatform.UnitTests/PhotoPlatform.UnitTests.csproj
```

用途僅限：

> 加入 `PhotoPlatform.Infrastructure` Project Reference。

不得新增 NuGet Package。

---

# 4. Existing Contract

必須使用 TASK-03 已建立的：

```text
IFileStorageService
```

其現有 method signature 為唯一 authoritative contract。

Agent：

- 不得修改 method name
- 不得修改 parameter
- 不得修改 return type
- 不得新增新的 Application Contract
- 不得新增新的 Storage DTO，除非先取得批准

如果 Task 規格與現有 Interface 發生衝突：

```text
Stop
→ Report Conflict
→ Explain Impact
→ Propose Change
→ Wait for Approval
```

---

# 5. Storage Root

`LocalFileStorageService` 必須由外部提供 Storage Root Directory。

例如概念：

```csharp
new LocalFileStorageService(storageRoot);
```

不得將絕對路徑硬編碼在 Service 內。

例如不得：

```csharp
"C:\\PhotoPlatform\\Uploads"
```

也不得：

```csharp
"/var/photo-platform/uploads"
```

直接寫死在 Service。

Constructor 必須拒絕：

- `null`
- empty
- whitespace-only storage root

---

# 6. Directory Handling

儲存檔案前，如果必要目錄不存在，Service 應建立目錄。

例如：

```text
storageRoot
└─ ...
```

Directory 建立應具備 idempotent 行為：

> Directory 已存在時不得視為錯誤。

不得在本 Task 建立 Batch / Image DB Record。

---

# 7. Storage Path Safety

Client 提供的原始檔名：

```text
OriginalFileName
```

不得直接當成本機 Storage Path。

例如不得直接：

```text
storageRoot + file.FileName
```

原因是原始檔名屬於不可信輸入。

本 Task 不負責 Filename Sanitization。

若既有 `IFileStorageService` Contract 已提供：

```text
storedFileName
storageKey
relativePath
```

等安全 Storage Identifier，必須以該值為準。

若現有 Contract **只有原始檔案，卻沒有任何安全 Storage Identifier 可以使用**，Agent 不得自行發明命名策略，必須：

```text
Stop
→ Report Contract Gap
→ Propose Storage Naming Strategy
→ Wait for Approval
```

---

# 8. File Write Behavior

儲存流程必須遵守：

```text
收到來源 Stream
→ Resolve safe destination path
→ 建立必要 Directory
→ 建立 destination FileStream
→ Copy source stream to destination
→ Flush / complete write
→ Return existing contract result
```

Service 可以 Dispose：

```text
自己建立的 destination FileStream
```

但不得 Dispose：

```text
呼叫端提供的 source Stream
```

---

# 9. Source Stream Ownership

Source Stream 的生命週期由 Caller 負責。

因此不得：

```csharp
using var source = ...
```

如果 Source Stream 是由 `IUploadFile.OpenReadStream()` 取得，而現有 Contract 明確規定 caller ownership，Storage Service 不得 Dispose 該 Stream。

Service 自己建立的：

```text
FileStream
```

則必須正確 Dispose。

---

# 10. Stream Position

不得假設 Source Stream 一定從：

```text
Position = 0
```

開始，除非既有 Contract 已明確保證。

如果既有 Storage Contract 要求儲存「完整檔案」，而 Source Stream：

```text
CanSeek == true
```

則應依既有 MOD-01 / Contract 規則處理 Position。

若現有文件未定義 Storage Service 應：

- 從目前 Position 開始 Copy
- 或強制回到 Position 0

Agent 不得自行決定。

必須先：

```text
Report Ambiguity
→ Propose Behavior
→ Wait for Approval
```

---

# 11. Existing File / Collision

Local Storage 不得靜默覆寫既有檔案。

如果目的路徑已存在：

> 必須依現有 Storage Contract / MOD-01 Collision Rule 處理。

如果目前 Contract 沒有定義 Collision：

```text
Agent must not invent numeric suffix,
GUID rename,
overwrite,
or skip behavior.
```

必須回報規格缺口。

---

# 12. Delete Behavior

使用既有：

```text
IFileStorageService.DeleteAsync(...)
```

Contract。

Delete 只能操作 Storage Root 允許範圍內的檔案。

不得允許：

```text
../
absolute path
root escape
```

導致刪除 Storage Root 外的檔案。

如果目標檔案不存在，其行為必須依既有 Contract。

若 Contract 未定義：

> Agent 必須先回報，不得自行決定「忽略」或「throw」。

---

# 13. Path Traversal Protection

任何用來產生實際 Storage Path 的值都必須防止：

```text
../
..\
absolute path
drive path
UNC path
```

最終 Resolve 的完整路徑必須位於：

```text
Storage Root
```

之下。

例如不得讓：

```text
../../system-file
```

逃出 Upload Storage Directory。

---

# 14. Cancellation

所有支援 `CancellationToken` 的操作都必須傳遞 Token。

例如：

```csharp
CopyToAsync(..., cancellationToken)
```

取消時：

```text
OperationCanceledException
```

不得被轉成一般 Storage Failure。

必須向外傳遞。

---

# 15. Partial File Cleanup

如果寫檔過程中發生：

```text
IOException
Cancellation
Other supported write failure
```

且 Destination File 已被建立但尚未完成：

> 不得留下明確的 incomplete / partial file。

Service 應嘗試移除本次操作建立的 partial destination file。

Cleanup 只能處理：

> 本次 Save 操作所建立的 destination。

不得誤刪既存檔案。

如果 Cleanup 本身失敗：

> 不得掩蓋原始 cancellation 或 write failure。

---

# 16. Exception Handling

不得使用：

```csharp
catch (Exception)
{
    ...
}
```

把所有程式錯誤轉成同一種結果。

應保留：

```text
OperationCanceledException
```

向外傳遞。

對實際 I/O Error 的處理必須符合既有 `IFileStorageService` Contract。

不得自行建立新的 ErrorCode，除非 Module Spec 已定義。

---

# 17. File Content

Local Storage Service：

> 只負責保存 bytes。

不得：

- 驗證 Extension
- 驗證 MIME
- 驗證 Signature
- 解碼圖片
- 解析 EXIF
- 計算 SHA256
- 計算 pHash
- Sanitization 原始檔名
- 做 Duplicate Detection
- 做 Quality Analysis

這些屬於其他 Service / Module。

TASK-04 的：

```text
FileValidationService
```

不應被複製到 Storage Service。

---

# 18. Unit Tests

至少涵蓋以下行為。

## 18.1 Constructor

驗證：

- invalid storage root 被拒絕
- valid storage root 可建立 Service

## 18.2 Save Success

驗證：

- 可以儲存檔案
- Directory 不存在時可建立
- 儲存後內容與 Source bytes 相同
- 回傳值符合既有 Contract
- Source Stream 沒有被 Dispose

## 18.3 Existing Directory

Directory 已存在時：

```text
Save 仍可正常執行
```

## 18.4 Cancellation

驗證：

- Cancellation 正確向外傳遞
- 不轉成一般 Storage Error
- partial file 不應殘留

## 18.5 Write Failure

可控制的 I/O Failure 情況：

- 不留下 partial file
- 不 Dispose Caller-owned Source Stream

## 18.6 Path Safety

驗證：

- traversal path 被拒絕
- absolute path 被拒絕
- destination 無法逃離 Storage Root

## 18.7 Delete

驗證：

- 合法 Storage File 可以刪除
- 不允許刪除 Root 外檔案
- missing file 行為符合既有 Contract

## 18.8 Collision

如果現有 Contract 已定義 Collision：

> 測試對應規則。

如果沒有定義：

> 不得自行新增 Collision 行為或測試假設。

---

# 19. Test File System

Unit Test 應使用：

> Test-specific temporary directory。

例如每個測試建立自己的 temp folder。

不得依賴：

```text
C:\Users\...
Desktop
Downloads
實際 Production Upload Directory
```

測試完成後應清理自己建立的測試檔案及資料夾。

---

# 20. Out of Scope

TASK-05 不得實作：

```text
UploadController
UploadService orchestration
Batch persistence
Image persistence
ProcessingJob persistence
EF Core configuration
SQL Server
Processing Queue
Background Worker
Google Cloud Storage
SignalR
Metadata
SHA256
Duplicate Detection
Similarity
Quality Analysis
Naming Engine
```

也不得修改：

```text
MOD-02 Processing
```

---

# 21. NuGet / Architecture Constraints

不得新增未核准 NuGet Package。

應優先使用：

```text
System.IO
Stream
FileStream
Directory
Path
```

等 .NET 內建能力。

Infrastructure 可以依賴：

```text
Application
Domain
```

但不得造成：

```text
Application → Infrastructure
Domain → Infrastructure
```

的 project dependency。

---

# 22. Definition of Done

TASK-05 完成條件：

```text
□ LocalFileStorageService 實作 IFileStorageService
□ 未修改 TASK-03 Contract
□ Storage Root 不硬編碼
□ Client Filename 未直接作為 Storage Path
□ Path Traversal 無法逃離 Storage Root
□ Source Stream 未被 Dispose
□ Destination Stream 正確 Dispose
□ Cancellation 正確向外傳遞
□ Partial file cleanup 正確
□ Unit Tests 完成
□ Existing tests 全部通過
□ dotnet build 成功
□ dotnet test 成功
□ 無新增未核准 NuGet Package
□ 無 Controller / DB / Queue / Worker 修改
□ git diff 僅包含 TASK-05 範圍
```

---

# 23. Agent Pre-Implementation Review

Agent 開始實作前，必須先進行：

```text
Read-Only Pre-Implementation Review
```

確認：

1. `IFileStorageService` 現有 method signature。
2. TASK-03 Contract 是否足以完成 TASK-05。
3. MOD-01 是否已定義 Storage Path / Collision / Delete semantics。
4. Infrastructure Project 是否已有必要 Application reference。
5. UnitTests 是否已有 Infrastructure Project reference。
6. 是否需要修改任何未列入 Scope 的檔案。

如果發現以下任何一項規格不足：

```text
Storage naming
Collision
Delete missing-file behavior
Stream starting position
Return value
```

Agent 不得自行決定。

必須：

```text
Stop
→ Report Conflict / Ambiguity
→ Explain Impact
→ Propose Minimal Change
→ Wait for Approval
```

---

# 24. Agent Implementation Constraints

Agent 僅能依照本 Task、MOD-01、System-Level Specification、Requirements 與 AGENTS.md 進行實作。

不得：

- 自行新增 Architecture Pattern
- 自行修改 Application Contract
- 自行修改 Domain Entity
- 自行新增 Database Schema
- 自行新增 API Endpoint
- 自行新增 NuGet Package
- 自行實作 GCS
- 自行修改其他 Module
- 自行擴大 TASK-05 Scope

若發現規格衝突或缺口，必須停止實作並回報。

---

# 25. Recommended Implementation Order

建議 Agent 依以下順序執行：

```text
1. Read-Only Review
2. 確認 IFileStorageService Contract
3. 確認 Storage Path / Collision / Delete 規則是否完整
4. 建立 LocalFileStorageService
5. 實作 Save
6. 實作 Delete
7. 加入 Path Safety
8. 加入 Cancellation / Partial File Cleanup
9. 建立 Unit Tests
10. dotnet build
11. dotnet test
12. git diff review
13. 等待人工 Review
```

---

# 26. Completion Rule

即使：

```text
dotnet build
dotnet test
```

全部成功，Agent 也不得自行 Commit。

完成實作後應回報：

- Modified files
- New files
- Build result
- Test result
- Known warnings
- Contract changes
- Scope deviations
- Remaining concerns

最後停止並等待人工 Code Review。

只有在人工 Review 完成後，才進入 Commit 階段。

---

# End of TASK-05 Specification
