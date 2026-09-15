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
- 回傳既有 Contract 的 string StoredPath：original/{guid}
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

為設定 §7.2 的 Internal Test Seam Visibility，另允許以下兩種方式擇一：

- Assembly-level attribute 方式：允許新增 `src/PhotoPlatform.Infrastructure/Properties/AssemblyInfo.cs`。
- Project-level / csproj 方式：允許對 `src/PhotoPlatform.Infrastructure/PhotoPlatform.Infrastructure.csproj` 進行必要的最小修改。

上述額外允許項目僅限設定 `PhotoPlatform.UnitTests` 對 Infrastructure internal members 的測試可見性，不得進行其他 project configuration 變更。實作時只能選擇其中一種方式，不得重複設定。

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

`LocalFileStorageService` 必須由 Constructor 注入 Storage Root Directory（`storageRoot`），原始上傳檔案儲存於其 `original` 子目錄。

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
└─ original
```

Directory 建立應具備 idempotent 行為：

> Directory 已存在時不得視為錯誤。

不得在本 Task 建立 Batch / Image DB Record。

---

# 7. Storage Path Safety / Naming

`LocalFileStorageService` 自行產生 `Guid.NewGuid().ToString("N")` 作為 Storage Identifier。

- 不使用 OriginalFileName 作為 Storage Path。
- 不保留原始副檔名。
- 不進行 Filename Sanitization。
- 不新增 `IFileStorageService` 參數。

目的檔案位於 Constructor 注入的 `storageRoot` 下的 `original` 子目錄。

`SaveAsync` 回傳：

```text
original/{guid}
```

其中 `{guid}` 為上述 Identifier。StoredPath 為跨平台相對路徑，固定使用 `/` 作為邏輯路徑分隔符，不回傳 OS absolute path。

## 7.1 Deterministic Storage Identifier Test Seam

- Public production constructor 維持只接受 `string storageRoot`。
- Production 預設 identifier factory 必須使用 `Guid.NewGuid().ToString("N")`。
- 為了讓 Unit Test 能穩定重現 Collision，允許在 Infrastructure implementation 內提供 `internal` constructor 或等價的 internal injection mechanism，注入 `Func<string> identifierFactory`。
- 此 test seam 僅供 Infrastructure 內部測試能力使用，不得成為 public production API，也不得暴露到 API / Application Layer。
- 不得修改 `IFileStorageService`、新增 Application Contract、Storage DTO 或 NuGet Package。

## 7.2 Internal Test Seam Visibility

`PhotoPlatform.Infrastructure` 與 `PhotoPlatform.UnitTests` 為不同 assembly。允許 Infrastructure 將 internal members 對 `PhotoPlatform.UnitTests` 開放測試可見性，以存取 §7.1 的 identifier factory test seam。

建議使用 .NET 內建的 `InternalsVisibleTo`，實作時以下方式只能擇一，不得重複設定：

1. Assembly-level `InternalsVisibleTo` attribute，例如：

   ```csharp
   [assembly: InternalsVisibleTo("PhotoPlatform.UnitTests")]
   ```

2. 等價的 project-level / csproj `InternalsVisibleTo` 設定。

此規則僅用於 Unit Test 存取 Infrastructure internal test seam，不得因此：

- 將 test seam 改為 public，或新增 public constructor 供測試使用。
- 將 identifier factory 暴露到 API / Application Layer。
- 修改 `IFileStorageService` 或 Application Contract。
- 新增 Production-facing API、Application-level abstraction 或 NuGet Package。
- 使用 Reflection 繞過 internal 存取限制。

---

# 8. File Write Behavior

儲存流程必須遵守：

```text
收到來源 Stream
→ Resolve safe destination path
→ 建立必要 Directory
→ 以原子式 CreateNew semantics 建立 destination FileStream
→ Copy source stream to destination
→ Flush / complete write
→ 回傳 original/{guid} 相對 StoredPath
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

由 `IUploadFile.OpenReadStream()` 取得的 Source Stream 明確屬於 caller-owned resource，Storage Service 不得 Dispose 該 Stream。

Service 自己建立的：

```text
FileStream
```

則必須正確 Dispose。

---

# 10. Stream Position

`IUploadFile.OpenReadStream()` 的已核准語意：

- 每次呼叫都必須提供完整檔案內容，並從檔案開頭開始讀取。
- 不要求每次回傳相同 Stream instance。
- 對 Non-seekable Stream，每次呼叫仍必須取得從完整內容開頭開始的來源，不得接續已消耗的內容。
- Consumer 不負責 Dispose caller-owned source Stream。

若 Source Stream 可 Seek，Storage 必須：

```text
記錄原本 Position
→ 從 Position 0 複製完整內容
→ 完成或失敗後恢復原本 Position
```

Non-seekable Stream 不拒絕，依 OpenReadStream Contract 從目前來源開頭開始複製，不要求 Position Restore。

---

# 11. Existing File / Collision

Destination file 必須使用原子式 CreateNew semantics。

- 不允許覆寫既有檔案。
- 不自動加 Numeric Suffix。
- 不自行重新命名或 Retry。
- 發生 Collision 時，讓 `IOException` 原樣向外傳遞。
- 未成功建立 destination 時，不得將既存檔案視為本次 Save 的 partial file 刪除。

---

# 12. Delete Behavior

使用既有 `IFileStorageService.DeleteAsync(string storedPath, CancellationToken cancellationToken)` Contract。

- DeleteAsync 僅允許操作 `storageRoot` 範圍內的 StoredPath。
- 目標檔案不存在時視為成功，使 Delete 具備 idempotent semantics。
- `null`、empty、whitespace 或非法／逃離 root 的 storedPath 視為 Argument validation failure。
- 使用 `ArgumentException` family，不建立自訂 Exception。
- 不得允許 traversal、absolute path、drive path 或 UNC path 導致刪除 Storage Root 外的檔案。

本 Task 的 DeleteAsync 僅接受 LocalFileStorageService 所產生的 StoredPath 格式：

```text
original/{identifier}
```

- `identifier` 必須符合 Guid `N` format：32 個 hexadecimal characters，不包含 dash。
- 不包含副檔名或其他 path segment；邏輯路徑分隔符固定為 `/`。
- 合法例：`original/550e8400e29b41d4a716446655440000`。
- 即使仍位於 storageRoot 內，`temp/...`、`processed/...`、`foo/bar`、`original/hello`、`original/550e8400-e29b-41d4-a716-446655440000`、`original/file.jpg` 也必須拒絕。
- 上述不合法格式使用 `ArgumentException` family，不建立自訂 Exception。
- 僅合法 StoredPath 指向的檔案不存在時視為成功；其他實際 I/O failure 原樣向外傳遞。

---

# 13. Path Traversal Protection

DeleteAsync 除了檢查 Root 邊界，也必須符合 §12 的 `original/{identifier}` 格式，不得將 Root 內其他目錄或任意檔案視為合法刪除目標。

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

如果 Save 過程失敗或取消，例如：

```text
IOException
Cancellation
Other supported write failure
```

且本次操作已建立 Destination File：

> 不得留下明確的 incomplete / partial file。

Service 應嘗試移除本次操作建立的 partial destination file。

Cleanup 只能處理：

> 本次 Save 操作所建立的 destination。

不得誤刪既存檔案。

如果 Cleanup 本身失敗：

> 不得掩蓋原始 exception，包括 cancellation 或 write failure。

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

IOException 與其他既有 I/O exception 不包裝、不轉 ErrorCode，原樣向外傳遞。

本 Task 不建立新的 ErrorCode 或自訂 Exception；HTTP Error Mapping 留給後續 API Task。

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
- 回傳值為 original/{guid}，guid 為 N 格式，使用 `/` 且不含原始檔名或副檔名
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
- missing file 視為成功，重複 Delete 具備 idempotent semantics
- null、empty、whitespace、非法或逃離 root 的 storedPath 拋出 ArgumentException family
- 僅接受 `original/` 加上 Guid N format 的 32 個 hexadecimal characters，且沒有副檔名或其他 path segment
- 拒絕 `temp/...`、`processed/...`、`foo/bar`、`original/hello`、帶 dash 的 Guid 與 `original/file.jpg`
- 拒絕 traversal、absolute path、drive path、UNC path 與 root escape
- 其他實際 I/O failure 原樣向外傳遞

## 18.8 Collision

驗證原子式 CreateNew semantics：

- `PhotoPlatform.UnitTests` 必須透過 §7.2 核准的 internal visibility mechanism 存取 internal constructor 或等價的 identifierFactory test seam。
- 不得使用 Reflection 繞過 internal，不得為此建立 public constructor 或新的 Application-level abstraction。
- 透過 §7.1 的 internal test seam 注入固定 identifier，例如 `550e8400e29b41d4a716446655440000`。
- 在測試專用 storageRoot 下預先建立 `original/550e8400e29b41d4a716446655440000`，再呼叫 SaveAsync，穩定製造 Collision，不依賴隨機 GUID 碰撞。
- Collision 時 IOException 原樣向外傳遞。
- 既有檔案內容不被覆寫，亦不被 Cleanup 刪除。
- 不自動加 Numeric Suffix、重新命名或 Retry。
- 確認不重新產生 identifier，identifier factory 不因 Collision 被重複呼叫。

## 18.9 Stream Position / Ownership

- 可 Seek 的來源從 Position 0 複製，完成或失敗後恢復原本 Position。
- Non-seekable 來源依 OpenReadStream Contract 提供完整內容，不被拒絕。
- 呼叫端負責提供符合每次從頭讀取語意的來源；Storage 不 Dispose source Stream。

## 18.10 Cleanup Failure

- Save 失敗或取消後，僅嘗試清理本次建立的 destination。
- Cleanup 自身失敗不得掩蓋原始 exception。
- 上述 Production Behavior 為必要要求，不因測試方式而放寬。
- 若能在不新增 Production abstraction、NuGet Package 或額外 File System abstraction 的前提下 deterministic 模擬 Cleanup Failure，應建立對應 Unit Test。
- 不得單純為此測試新增 `IFileSystem`、FileSystem wrapper、Mock filesystem framework 或新的 Production-level abstraction。
- 若目前 Task Scope 內無法 deterministic 模擬，可透過 code review / integration behavior 驗證，不得為了測試擴大 Scope。

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
□ Cleanup failure 不掩蓋原始 exception，依 §18.10 驗證
□ Public production constructor 僅接受 string storageRoot，預設使用 GUID N identifier
□ 透過 internal test seam 完成 deterministic Collision Test
□ Internal test seam 不成為 public production API
□ PhotoPlatform.UnitTests 可透過 InternalsVisibleTo 或等價核准方式存取 test seam，僅設定一次
□ 不使用 Reflection 繞過 internal
□ 未修改 IFileStorageService，未新增 NuGet Package
□ Delete 僅接受 original/{identifier}，identifier 符合 Guid N format
□ Unit Tests 完成（Cleanup Failure 的驗證方式依 §18.10）
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

§7.1 核准的 identifier factory test seam 僅限 Infrastructure internal implementation，不改變 public production constructor 或 Application Contract；不得為 Cleanup Failure Test 引入額外 File System abstraction。

跨 assembly 的測試存取僅使用 §7.2 核准方式，相關檔案變更限定於 §3 所列範圍，不得將 test seam 公開或使用 Reflection 繞過存取限制。

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
