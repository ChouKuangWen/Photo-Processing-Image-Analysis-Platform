# TASK-04 — File Validation

**Task ID:** TASK-04
**Module:** MOD-01 Upload Module
**Task Name:** File Validation
**Task Type:** Application Implementation Task
**Architecture:** Clean Architecture
**Technology:** ASP.NET Core / C#
**Status:** Ready for Implementation

---

## 1. 目的

本 Task 負責實作 Upload Module 的檔案驗證能力。

實作目標為：

```text
IUploadFile
    ↓
IFileValidationService
    ↓
FileValidationService
    ↓
FileValidationResult
```

`FileValidationService` 只負責判斷上傳檔案是否符合 MOD-01 已核准的 File Validation Rules。

本 Task 不負責：

```text
Upload Orchestration
File Storage
Database Persistence
Queue
Background Processing
Controller / HTTP
```

File Integrity 在本 Task 僅實作 **MVP Basic File Signature Validation**。

不要求：

```text
完整 JPEG / PNG Decode
Deep Corruption Detection
Metadata Parsing
Image Quality Analysis
第三方 Image Decoder
```

---

## 2. 前置條件

開始 TASK-04 前，下列項目必須已存在：

```text
TASK-01 Solution / Project Structure
TASK-02 Upload Domain Model
TASK-03 Upload Application Contracts
```

至少包含：

```text
IUploadFile
IFileValidationService
FileValidationResult
WorkflowType
```

TASK-04 不得修改上述 Contract，除非發現明確規格衝突並取得人工核准。

---

## 3. 規格來源

Agent 開始實作前必須重新閱讀：

```text
AGENTS.md
Requirements.md
docs/System-Level-Specification.md
docs/modules/MOD-01-Upload-Spec.md
tasks/MOD-01/TASK-04-File-Validation.md
```

規格優先順序：

```text
System-Level Specification
        ↓
MOD-01 Upload Module Specification
        ↓
TASK-04
        ↓
Implementation
```

TASK-04 不得建立與 MOD-01 不同的第二套 File Validation Rule。

若發現衝突：

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

不得自行猜測。

---

## 4. Scope

本 Task 預計新增：

```text
src/PhotoPlatform.Application/Services/FileValidationService.cs

tests/PhotoPlatform.UnitTests/Application/FileValidationServiceTests.cs

tests/PhotoPlatform.UnitTests/TestDoubles/FakeUploadFile.cs
```

本 Task 允許修改：

```text
tests/PhotoPlatform.UnitTests/PhotoPlatform.UnitTests.csproj
```

僅用於新增：

```text
PhotoPlatform.UnitTests
    ↓
PhotoPlatform.Application
```

Project Reference。

不得新增 NuGet Package。

---

## 5. File Validation 規格

### 5.1 Extension Validation

支援副檔名：

```text
.jpg
.jpeg
.png
```

規則：

- Extension 比較不區分大小寫。
- 使用 Filename 的最後一個副檔名判斷。
- `.JPG`、`.JPEG`、`.PNG` 必須視為合法格式。
- 無副檔名必須拒絕。
- 不支援副檔名必須拒絕。
- 多重副檔名本身不直接拒絕，例如：

```text
photo.backup.jpg
```

以上述最後一個 `.jpg` 判斷。

錯誤：

```text
ErrorCode:
UNSUPPORTED_FORMAT

ErrorMessage:
The uploaded file format is not supported.
```

---

### 5.2 MIME Type Validation

允許對應：

```text
.jpg  → image/jpeg
.jpeg → image/jpeg
.png  → image/png
```

規則：

- MIME Type 比較不區分大小寫。
- 比較前可忽略前後空白。
- 不接受附加 Parameter。
- 不接受未核准 Alias。
- Extension 與 MIME Type 必須一致。

例如：

```text
photo.jpg + image/jpeg
→ Valid

photo.jpg + image/png
→ Invalid

photo.png + image/jpeg
→ Invalid
```

MIME Mismatch：

```text
ErrorCode:
INVALID_FILE

ErrorMessage:
The uploaded file is invalid.
```

---

### 5.3 File Size Validation

`FileValidationService` Constructor 接收：

```csharp
long maxFileSizeBytes
```

規則：

- `maxFileSizeBytes <= 0`
  - Constructor 必須拋出 `ArgumentOutOfRangeException`。
- `file.Length < 0`
  - 視為 `INVALID_FILE`。
- `file.Length == 0`
  - 視為 Empty File。
  - 回傳 `INVALID_FILE`。
- `file.Length > maxFileSizeBytes`
  - 回傳 `FILE_TOO_LARGE`。
- `file.Length == maxFileSizeBytes`
  - Size Validation 必須通過。
- `file.Length < maxFileSizeBytes`
  - Size Validation 必須通過。

TASK-04 不要求驗證：

```text
IUploadFile.Length
==
實際 Stream Total Length
```

`maxFileSizeBytes` 為 Implementation-Level Configuration，不在 MOD-01 內寫死 Production 數值。

Unit Test 可使用例如：

```csharp
20 * 1024 * 1024
```

作為測試設定值，但不得因此將 20 MB 視為正式系統規格。

錯誤：

```text
FILE_TOO_LARGE
The uploaded file exceeds the maximum allowed size.
```

或：

```text
INVALID_FILE
The uploaded file is invalid.
```

---

### 5.4 Filename / Path Traversal Validation

File Validation 階段採：

```text
Reject
```

不得在 `FileValidationService` 內 Sanitization Client Filename。

Filename 必須符合：

- 不得為 `null`。
- 不得為 empty。
- 不得為 whitespace。
- 不得為 `.`。
- 不得為 `..`。
- 長度不得超過 255 characters。
- 255 characters 必須允許。
- 256 characters 必須拒絕。
- 不得包含控制字元。
- 不得包含下列非法字元：

```text
< > : " / \ | ? *
```

- 不得包含 `/`。
- 不得包含 `\`。
- 不得構成 Absolute Path。
- 不得構成 Path Traversal。

例如：

```text
../photo.jpg
..\photo.jpg
folder/photo.jpg
folder\photo.jpg
```

皆必須拒絕。

Duplicate Filename：

```text
允許
```

本 Task 不負責 Duplicate Filename Resolution。

Filename / Path Traversal Failure：

```text
INVALID_FILE
The uploaded file is invalid.
```

---

### 5.5 File Integrity Validation

本 Task 的 File Integrity 定義為：

```text
MVP Basic File Signature Validation
```

JPG / JPEG Signature：

```text
FF D8 FF
```

PNG Signature：

```text
89 50 4E 47 0D 0A 1A 0A
```

規則：

- `.jpg` / `.jpeg` 必須具有 JPG / JPEG Signature。
- `.png` 必須具有 PNG Signature。
- Extension、MIME Type、Signature 必須一致。
- Signature 不符 → `INVALID_FILE`。
- Signature Bytes 不足 → `INVALID_FILE`。
- Signature 驗證通過不代表圖片完整可 Decode。
- 不做完整圖片結構驗證。
- 不做深層損毀檢查。

錯誤：

```text
INVALID_FILE
The uploaded file is invalid.
```

---

## 6. FileValidationResult / Error Mapping

成功：

```csharp
new FileValidationResult(
    true,
    null,
    null);
```

TASK-04 只允許使用以下 File Validation Error：

| Error Code | Error Message | 使用情況 |
|---|---|---|
| `UNSUPPORTED_FORMAT` | `The uploaded file format is not supported.` | Unsupported / Missing Extension |
| `FILE_TOO_LARGE` | `The uploaded file exceeds the maximum allowed size.` | File > configured maximum |
| `INVALID_FILE` | `The uploaded file is invalid.` | Empty / Negative Length、MIME Mismatch、Invalid Filename、Path Traversal、Invalid / Insufficient Signature、Stream Read Failure |

不得自行新增：

```text
INVALID_MIME
INVALID_SIGNATURE
INVALID_FILENAME
EMPTY_FILE
PATH_TRAVERSAL
```

等其他 ErrorCode。

---

### 6.1 Validation Order

`FileValidationService.ValidateAsync()` 依序執行：

```text
1. Cancellation
2. Filename / Path Traversal
3. Extension
4. File Size
5. MIME Type
6. File Signature / Integrity
```

第一個 Failure 發生時立即回傳，不再繼續後續 Validation。

此順序屬於 TASK-04 Implementation Decision，不提升為 MOD-01 Business Rule。

---

## 7. Stream Handling

取得 Stream：

```csharp
IUploadFile.OpenReadStream()
```

規則：

- `FileValidationService` 不得 Dispose Caller-owned Stream。
- 不得 Dispose `IUploadFile` 或其他 Caller-owned Resource。
- 只讀取 Signature 驗證所需最少 Bytes。
- 不得將完整圖片載入 Memory。
- 不要求比對 Stream Length 與 `IUploadFile.Length`。

若：

```csharp
stream.CanSeek == true
```

則：

```text
記錄原始 Position
    ↓
讀取 Signature
    ↓
恢復原始 Position
```

恢復 Position 應確保在 Validation 完成或讀取流程離開時執行。

若：

```csharp
stream.CanSeek == false
```

允許消耗 Signature Bytes，不要求恢復 Position。

`OpenReadStream()` 或 Stream Read 無法正常讀取時：

```text
INVALID_FILE
The uploaded file is invalid.
```

但 `OperationCanceledException` 不得轉成 `INVALID_FILE`。

---

### 7.1 Cancellation

`ValidateAsync()` 必須接受並尊重：

```csharp
CancellationToken cancellationToken
```

規則：

- 方法開始時應檢查 Cancellation。
- 已取消 Token 必須拋出 / 傳遞 `OperationCanceledException`。
- 不得將 Cancellation 轉成：

```text
INVALID_FILE
```

- Stream Async Read 在可行時應傳入同一 CancellationToken。

---

## 8. Unit Test

測試檔案：

```text
tests/PhotoPlatform.UnitTests/Application/FileValidationServiceTests.cs
```

至少涵蓋：

### Valid Cases

```text
Valid JPG
Valid JPEG
Valid PNG
Uppercase JPG
Uppercase JPEG
Uppercase PNG
File Size = max - 1
File Size = max
Filename Length = 255
```

### Extension

```text
Unsupported Extension
Missing Extension
Multiple Extension with valid final extension
```

### MIME

```text
JPG + image/png
PNG + image/jpeg
MIME Case Insensitive
MIME Leading / Trailing Whitespace
MIME Parameter Rejected
```

### File Size

```text
Negative Length
Empty File
File Size = max - 1
File Size = max
File Size = max + 1
```

### Filename

```text
Null Filename
Empty Filename
Whitespace Filename
Filename = "."
Filename = ".."
Filename Length = 255
Filename Length = 256
Invalid Character
Control Character
Forward Slash
Backslash
Path Traversal
Absolute Path
Duplicate Filename Allowed
```

### File Signature

```text
Valid JPG Signature
Valid JPEG Signature
Valid PNG Signature
Invalid JPG Signature
Invalid PNG Signature
Insufficient JPG Signature Bytes
Insufficient PNG Signature Bytes
Extension / MIME / Signature Mismatch
```

### Result Mapping

```text
Success:
IsValid = true
ErrorCode = null
ErrorMessage = null

Unsupported Format:
UNSUPPORTED_FORMAT
exact approved message

Too Large:
FILE_TOO_LARGE
exact approved message

Other File Validation Failure:
INVALID_FILE
exact approved message
```

### Stream

```text
Validator does not dispose caller-owned Stream
Seekable Stream restores original Position
Non-seekable Stream does not require Position restore
```

### Cancellation

```text
Already-cancelled token
→ OperationCanceledException
```

---

## 9. Test Double

新增：

```text
tests/PhotoPlatform.UnitTests/TestDoubles/FakeUploadFile.cs
```

`FakeUploadFile` 實作：

```text
IUploadFile
```

至少可控制：

```text
FileName
MimeType
Length
Stream Content
```

測試可使用：

```text
MemoryStream
```

測試本身負責管理測試用 Stream / Resource Lifetime。

不得讓 `FileValidationService` Dispose Caller-owned Resource。

---

## 10. Out of Scope

TASK-04 不實作：

```text
UploadService
UploadController
HTTP Request / Response
Request File Count Validation
Empty Upload Request Validation
Workflow Validation

Batch Creation
Image Creation
ProcessingJob Creation

File Storage
Storage Compensation
Database
EF Core
DbContext
Migration

Processing Queue Implementation
Background Worker
Retry
Recovery
SignalR

Filename Sanitization
Filename Rename
Collision Resolution

Complete JPEG / PNG Decode
Deep Corruption Detection
EXIF
Metadata
SHA-256
pHash
Similarity
Quality
Recommendation
Naming
```

Queue / Worker Implementation 屬於後續 MOD-02 / Processing 相關工作。

---

## 11. Dependency Rule

`FileValidationService` 位於：

```text
PhotoPlatform.Application
```

因此不得依賴：

```text
PhotoPlatform.Infrastructure
ASP.NET Core IFormFile
EF Core DbContext
SQL Server
GCS SDK
Controller
HTTP Context
```

允許依賴：

```text
PhotoPlatform.Application Contracts
.NET BCL
System.IO
System.Threading
```

Application Layer 必須保持 Infrastructure-independent。

---

## 12. Package Rule

本 Task：

```text
不得新增 NuGet Package
```

尤其不得為 Signature Validation 新增：

```text
ImageSharp
SkiaSharp
System.Drawing
其他 Image Decoder
```

Basic Signature Validation 使用 .NET 既有能力即可。

---

## 13. Definition of Done

TASK-04 完成條件：

```text
[ ] FileValidationService 已實作 IFileValidationService

[ ] JPG / JPEG / PNG Extension Validation 完成
[ ] MIME Mapping Validation 完成
[ ] File Size Validation 完成
[ ] Empty / Negative Length Validation 完成
[ ] Filename Validation 完成
[ ] Path Traversal Prevention 完成
[ ] JPG / PNG Basic Signature Validation 完成

[ ] Error Mapping 完全符合規格
[ ] Success Result 為 ErrorCode / ErrorMessage null

[ ] Cancellation 正確傳遞
[ ] Caller-owned Stream 未被 Dispose
[ ] Seekable Stream Position 恢復
[ ] 不載入完整圖片至 Memory

[ ] UnitTests → Application Project Reference 已加入
[ ] FileValidationServiceTests 完成
[ ] FakeUploadFile 完成

[ ] dotnet restore Pass
[ ] dotnet build Pass
[ ] dotnet test Pass
[ ] git diff --check Pass

[ ] 無新增 NuGet Package
[ ] 無修改 TASK-03 Contract
[ ] 無跨入 Storage / DB / Queue / Controller / MOD-02
```

---

## 14. Agent Execution

本 Task 已完成規格核准，可進入 Implementation。

Agent 開始前必須重新閱讀最新：

```text
AGENTS.md
Requirements.md
System-Level Specification
MOD-01 Upload Module Specification
TASK-04 File Validation
```

實作時：

```text
只處理 TASK-04 Scope
```

不得順手實作後續 TASK。

完成後必須回報：

```text
1. 新增 / 修改檔案
2. FileValidationService 實作摘要
3. Unit Test Cases 摘要
4. dotnet restore 結果
5. dotnet build 結果
6. dotnet test 結果
7. git diff --check 結果
8. 是否修改任何既有 Contract
9. 是否新增任何 Package
10. 是否發現規格衝突
```

完成後：

```text
不要 Commit
```

先等待人工 Review。

若實作期間發現 MOD-01、TASK-03 Contract 或既有程式碼存在衝突：

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

不得自行修改規格或 Contract。