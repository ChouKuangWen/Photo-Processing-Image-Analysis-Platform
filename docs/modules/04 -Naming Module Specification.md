# 04 — Naming Module

> Module-Level Specification  
> 本文件定義 Naming Module 的功能、業務規則、資料依賴、API Contract 與測試要求。  
> AI Agent 實作本模組時，應以本文件為主要依據；若與 System-Level Specification 衝突，以 System-Level Specification 為準。

---

# 1. 規格

## 1.1 Module 目的

Naming Module 負責依照使用者提供的 Naming Template，將圖片原始檔名轉換為新的檔名。

本模組的核心概念很簡單：

```text
User-defined Template
        +
Image Metadata
        +
Sequence
        ↓
New Filename
```

Naming Module **負責產生與套用檔名**，但不負責分析圖片內容。

其他 Module 已產生的 Metadata 可以作為 Naming Input，例如：

- TakenAt
- LocationName
- CameraModel
- ImageId

Naming Module 只使用這些資料，不重新執行 EXIF、GPS、Hash、pHash 或圖片分析。

---

## 1.2 Module 職責

Naming Module 必須提供：

| 功能 | 說明 |
|---|---|
| Template Parsing | 解析 Naming Template |
| Token Resolution | 將 Token 轉換為實際值 |
| Sequence Generation | 產生批次序號 |
| Sequence Formatting | 將序號套用固定寬度格式 |
| Metadata Fallback | Metadata 缺失時使用既定備援值 |
| Filename Construction | 組合固定文字與 Token |
| Filename Sanitization | 處理非法檔名字元 |
| Filename Validation | 驗證檔名是否合法 |
| Collision Detection | 檢查目標檔名是否已存在 |
| Preview | 預覽 Rename 結果 |
| Apply | 正式執行 Rename |
| Rename Result | 回傳每張圖片的處理結果 |
| ProcessingLog | 記錄 Naming / Rename 結果 |

---

## 1.3 Module Boundary

### 1.3.1 Naming Module 負責

```text
Template Parsing
Token Resolution
Sequence Generation
Sequence Formatting
Metadata Fallback
Filename Construction
Filename Sanitization
Filename Validation
Collision Detection
Preview
Rename Apply
Rename Result
Naming ProcessingLog
Naming API Contract
```

### 1.3.2 Naming Module 不負責

```text
EXIF Parsing
GPS Reverse Geocoding
SHA-256 Calculation
pHash Calculation
Visual Similarity Analysis
Photo Quality Analysis
Image Compression
Image Editing
Image Generation
OCR
Face Recognition
Semantic Search
AI-based Filename Generation
```

這些功能由其他 Module 負責。

Naming Module 可以：

```text
Read existing metadata
        ↓
Use metadata in naming
```

但不得：

```text
Read metadata
        ↓
重新執行 EXIF / GPS / Image Analysis
```

---

## 1.4 Naming Template

Naming Template 是一個描述「新檔名如何產生」的字串。

Template 由兩種內容組成：

```text
Fixed Text
+
Naming Token
```

例如：

```text
日本_{Sequence:000}_東京
```

可解析為：

```text
FixedText("日本")
Token("Sequence", "000")
FixedText("東京")
```

---

## 1.5 Template 使用規則

### 1.5.1 固定文字

使用者可以自由輸入固定文字。

例如：

```text
日本東京之旅_{Sequence:000}
```

固定文字為：

```text
日本東京之旅_
```

系統不得限制固定文字只能使用英文或特定格式。

---

### 1.5.2 Token 位置

Token 可以出現在 Template 的任意位置。

以下皆為合法：

```text
{Sequence:000}_日本東京
```

```text
日本_{Sequence:000}_東京
```

```text
日本東京_{Sequence:000}
```

Token 順序不得由系統硬編碼。

---

### 1.5.3 自訂分隔符號

分隔符號本身就是 Fixed Text。

因此不需要額外定義 Separator 欄位。

以下皆可支援：

```text
日本_{Sequence:000}_東京
```

```text
日本-{Sequence:000}-東京
```

```text
日本 {Sequence:000} 東京
```

```text
日本・{Sequence:000}・東京
```

```text
日本~{Sequence:000}~東京
```

系統不得假設 `_` 是唯一合法分隔符號。

---

## 1.6 MVP Naming Token

MVP 支援以下 Token：

| Token | 說明 | 範例 |
|---|---|---|
| `{OriginalFileName}` | 原始檔名，不含副檔名 | `IMG_0001` |
| `{OriginalExtension}` | 原始副檔名 | `jpg` |
| `{TakenAt}` | 拍攝日期時間 | `20260823143025` |
| `{TakenDate}` | 拍攝日期 | `20260823` |
| `{TakenTime}` | 拍攝時間 | `143025` |
| `{Year}` | 年 | `2026` |
| `{Month}` | 月 | `08` |
| `{Day}` | 日 | `23` |
| `{LocationName}` | 地點名稱 | `Taoyuan` |
| `{CameraModel}` | 相機型號 | `CanonEOSR6` |
| `{ImageId}` | Database Image ID | `1024` |
| `{Sequence}` | 批次序號 | `001` |

---

## 1.7 Token Resolution

Token Resolution 的責任是：

```text
Token
 ↓
取得既有資料
 ↓
回傳字串值
```

例如：

```text
{TakenDate}
        ↓
Image.TakenAt
        ↓
20260823
```

Naming Module 不負責產生 Metadata。

---

## 1.8 Metadata Fallback

Metadata 缺失時使用既定 Fallback：

| Metadata | Fallback |
|---|---|
| `TakenAt` | `FileCreatedAt` |
| `LocationName` | `UnknownLoc` |
| `CameraModel` | `UnknownCamera` |
| `OriginalFileName` | 不允許 Fallback |

例如：

```text
Template:
{TakenDate}_{LocationName}_{OriginalFileName}
```

GPS 不存在時：

```text
20260823_UnknownLoc_IMG_0001.jpg
```

如果 Fallback 後仍無法取得必要值，該圖片必須進入 Failed。

---

## 1.9 OriginalFileName

`OriginalFileName` 是 Naming Context 的必要資料。

如果不存在：

```text
Naming Request
    ↓
Failed
```

不得自行猜測或產生替代檔名。

---

## 1.10 Sequence

Sequence 用於在同一次 Naming Request 中產生連續編號。

預設：

```text
Start = 1
Increment = 1
```

例如：

```text
Image A → 1
Image B → 2
Image C → 3
```

---

### 1.10.1 Sequence Start

使用者可以指定起始值。

例如：

```json
{
  "sequenceStart": 100
}
```

結果：

```text
100
101
102
```

未提供時：

```text
sequenceStart = 1
```

Sequence Start 是 Request 設定，不是 Metadata。

---

### 1.10.2 Sequence Increment

MVP 固定：

```text
Increment = 1
```

因此目前不提供使用者自訂 Increment。

未來若需要：

```text
Start = 10
Increment = 5

10
15
20
25
```

再擴充。

---

### 1.10.3 Image Order

Sequence 必須依照 Request 提供的圖片順序產生。

例如：

```text
imageIds = [103, 101, 102]
```

則：

```text
103 → 001
101 → 002
102 → 003
```

Naming Module 不得自行重新排序。

若系統未來需要排序策略，排序規則應由 Application / System Layer 定義。

---

### 1.10.4 Deterministic Sequence

相同輸入：

```text
Same Image Set
+
Same Image Order
+
Same Sequence Start
+
Same Template
```

必須產生相同結果。

---

## 1.11 Sequence Formatting

Sequence Format 使用：

```text
{Sequence:00}
{Sequence:000}
{Sequence:0000}
```

例如：

```text
{Sequence:00}

01
02
03
```

```text
{Sequence:000}

001
002
003
```

```text
{Sequence:0000}

0001
0002
0003
```

---

### 1.11.1 Start 與 Format 必須分離

例如：

```text
Start = 98
Format = 000
```

結果：

```text
098
099
100
```

Format 只負責格式化。

Start 只負責決定起始值。

---

### 1.11.2 不得截斷 Sequence

例如：

```text
Start = 999
Format = 000
```

結果：

```text
999
1000
1001
```

不得產生：

```text
999
000
001
```

---

### 1.11.3 Multiple Sequence Token

同一 Template 可以出現多個 Sequence Token。

例如：

```text
日本_{Sequence:000}_東京_{Sequence:000}
```

同一張圖片：

```text
Sequence = 1
```

必須產生：

```text
日本_001_東京_001
```

不得產生：

```text
日本_001_東京_002
```

規則：

> Sequence Value 以「圖片」為單位產生一次，同一圖片中的所有 Sequence Token 共用該值。

---

## 1.12 Filename Construction

Template Resolution 完成後：

```text
Fixed Text
+
Resolved Token
+
Sequence
        ↓
Raw Filename
```

例如：

```text
Template:
{TakenDate}_{LocationName}_{Sequence:000}
```

得到：

```text
20260823_Taoyuan_001
```

副檔名應保留原始圖片副檔名：

```text
20260823_Taoyuan_001.jpg
```

---

## 1.13 Filename Sanitization

產生 Raw Filename 後，必須進行 Sanitization。

至少處理：

```text
Invalid Characters
Path Separator
Reserved Characters
Whitespace
Filename Length
```

例如：

```text
Taoyuan/City
```

若 `/` 屬於一般非法檔名字元，可依既定 Sanitization Policy 轉換為：

```text
Taoyuan_City
```

Sanitization 不得由 Controller 自行實作。

應位於 Domain / Application 的既定服務中。

---

## 1.14 Path Traversal Prevention

Filename 不得成為檔案系統路徑。

以下皆必須拒絕：

```text
../../../file.jpg
```

```text
..\..\file.jpg
```

```text
/var/data/file.jpg
```

```text
C:\Windows\file.jpg
```

此類輸入不能單純依賴一般字元替換處理。

規則：

```text
Path Traversal detected
        ↓
Reject Request
        ↓
Do not Rename
```

---

## 1.15 Filename Length Validation

產生 Filename 後必須驗證長度。

若超過系統既定限制：

```text
FILENAME_TOO_LONG
```

Naming Module 不得建立一套與 System-Level Validation Policy 衝突的長度限制。

---

## 1.16 Filename Collision

Rename 不得覆蓋已存在的檔案。

MVP 支援：

```text
NumericSuffix
Sequence
Skip
Fail
```

---

### 1.16.1 NumericSuffix

例如：

```text
IMG.jpg
```

已存在：

```text
IMG.jpg
```

則可以產生：

```text
IMG_01.jpg
```

若仍存在：

```text
IMG_02.jpg
```

直到取得可用名稱。

---

### 1.16.2 Sequence

使用 Sequence 重新產生唯一檔名。

最終結果仍必須通過實際 Collision Check。

---

### 1.16.3 Skip

如果依照 Collision Policy 無法產生可用名稱：

```text
Status = Skipped
```

並建立 ProcessingLog。

---

### 1.16.4 Fail

若 Policy 為：

```text
Fail
```

發生 Collision：

```text
Status = Failed
ErrorCode = FILENAME_COLLISION
```

原始檔案不得修改。

---

## 1.17 Preview

Preview 用於在正式 Rename 前讓使用者確認結果。

Preview：

```text
Read Database
Read Storage State
Resolve Template
Generate Filename
Check Collision
Return Result
```

Preview **不得修改**：

```text
Physical File
Images.NewFileName
Images.StoredPath
ProcessingJobs
```

---

## 1.18 Apply

Apply 是正式執行 Rename。

Apply 不得直接使用 Preview 的舊結果。

因為 Preview 與 Apply 之間可能發生：

```text
File Deleted
File Changed
Target Filename Occupied
Storage Changed
Concurrent Rename
```

因此 Apply 必須重新：

```text
Validate Request
        ↓
Resolve Template
        ↓
Resolve Metadata
        ↓
Generate Sequence
        ↓
Sanitize
        ↓
Validate
        ↓
Collision Check
        ↓
Rename
```

---

## 1.19 Preview 與 Apply 的關係

### Preview

```text
Request
 ↓
Validate
 ↓
Resolve
 ↓
Generate
 ↓
Collision Check
 ↓
Return Preview
```

不修改任何持久化資料。

### Apply

```text
Request
 ↓
Re-validate
 ↓
Resolve
 ↓
Generate
 ↓
Collision Check
 ↓
Rename File
 ↓
Update Database
 ↓
ProcessingLog
```

Preview 結果不能被視為 Apply 一定成功。

---

## 1.20 Rename Result

每張圖片至少回傳：

```text
ImageId
OriginalFileName
NewFileName
Collision
Status
```

例如：

```json
{
  "imageId": 101,
  "originalFileName": "IMG_001.jpg",
  "newFileName": "20260823_Taoyuan_001.jpg",
  "collision": false,
  "status": "Ready"
}
```

Apply 最終狀態至少：

```text
Success
Skipped
Failed
```

---

## 1.21 Error Handling

Naming Module 使用既有 Global Error Contract。

Naming 相關錯誤：

| Error Code | HTTP | 說明 |
|---|---:|---|
| `INVALID_NAMING_TEMPLATE` | 400 | Template 無效 |
| `UNKNOWN_NAMING_TOKEN` | 400 | Token 不支援 |
| `INVALID_SEQUENCE_FORMAT` | 400 | Sequence Format 無效 |
| `INVALID_SEQUENCE_START` | 400 | Sequence Start 無效 |
| `INVALID_FILENAME` | 400 | Filename 無效 |
| `FILENAME_TOO_LONG` | 400 | Filename 超過限制 |
| `IMAGE_NOT_FOUND` | 404 | 圖片不存在 |
| `FILENAME_COLLISION` | 409 | 無法解決檔名衝突 |
| `RENAME_FAILED` | 500 | Rename 執行失敗 |
| `STORAGE_ERROR` | 500 | Storage 操作失敗 |
| `INTERNAL_ERROR` | 500 | 未預期錯誤 |

API 不得暴露：

```text
StackTrace
SQL Exception
Internal File Path
Connection String
Secret
Internal Exception Details
```

---

## 1.22 Rename Reliability

File Rename 與 Database Update 不屬於同一 Atomic Transaction。

可能發生：

```text
File Rename
    ↓
Success
    ↓
Database Update
    ↓
Failure
```

因此 Application Layer 必須依 System-Level 定義的 Compensation Strategy 處理。

必要時：

```text
New File
   ↓
Compensation
   ↓
Original File
```

如果 Compensation 也失敗：

```text
Status = Failed
ProcessingLog = Failed
TraceId = Preserved
```

Naming Module 不自行建立新的跨系統 Transaction 機制。

---

## 1.23 Concurrency

Naming Module 必須考慮並行 Rename。

例如：

```text
Worker A → IMG.jpg
Worker B → IMG.jpg
```

兩個 Worker 不得都依賴同一次：

```text
Check Exists
```

就認定檔名安全。

實際流程：

```text
Check Exists
      ↓
Attempt Rename
      ↓
Actual Storage Result
```

最終以 Storage / File System 的實際操作結果為準。

若發生 Collision：

```text
重新執行 Collision Handling
```

---

## 1.24 Idempotency

Naming Apply 必須避免已完成的 Job 被重複 Rename。

以：

```text
ImageId + Workflow
```

作為既有 Processing Job Identity。

流程：

```text
Check Processing State
        ↓
Already Completed?
     /       \
   Yes        No
   ↓           ↓
 Skip        Execute
```

例如：

```text
IMG.jpg
   ↓
Tokyo.jpg
```

再次執行同一 Job 時，不應再次產生：

```text
Tokyo_01.jpg
```

---

## 1.25 ProcessingLog

Naming Module 使用既有 `ProcessingLogs`。

Naming 相關 Step：

```text
NAMING
FILE_RENAME
```

成功：

```text
Status = Success
```

失敗：

```text
Status = Failed
ErrorCode = ...
```

至少記錄：

```text
ImageId
Step
Status
ErrorCode
ErrorMessage
DurationMs
RetryCount
TraceId
Timestamp
```

重要事件應包含：

```text
TraceId
ImageId
BatchId
```

不得記錄：

```text
Connection String
API Key
Secret
Password
```

---

## 1.26 Processing Flow

Naming Module 的標準流程：

```text
Naming Request
      ↓
Request Validation
      ↓
Template Parsing
      ↓
Token Resolution
      ↓
Metadata Fallback
      ↓
Sequence Generation
      ↓
Sequence Formatting
      ↓
Filename Construction
      ↓
Filename Sanitization
      ↓
Filename Validation
      ↓
Path Traversal Validation
      ↓
Collision Detection
      ↓
Preview / Apply
      ↓
Rename Result
      ↓
ProcessingLog
```

Apply 額外：

```text
Preview
   ↓
User Confirmation
   ↓
Re-validation
   ↓
File Rename
   ↓
Database Update
   ↓
ProcessingLog
```

---

## 1.27 Application Layer

Application Layer 負責協調 Naming Use Case。

主要 Use Cases：

```text
PreviewNaming
ApplyNaming
ResolveTemplate
ResolveToken
ValidateNamingRequest
```

主要 Service：

```text
NamingApplicationService
```

Application Service 不應把所有邏輯塞在單一 Class。

---

## 1.28 Application Interfaces

建議介面：

```csharp
INamingService
INamingTemplateParser
ITokenResolver
ISequenceGenerator
ISequenceFormatter
IFilenameSanitizer
ICollisionDetector
```

### INamingService

```csharp
public interface INamingService
{
    Task<NamingPreviewResult> PreviewAsync(
        NamingPreviewRequest request,
        CancellationToken cancellationToken);

    Task<NamingApplyResult> ApplyAsync(
        NamingApplyRequest request,
        CancellationToken cancellationToken);
}
```

### INamingTemplateParser

```csharp
public interface INamingTemplateParser
{
    NamingTemplate Parse(string template);
}
```

### ITokenResolver

```csharp
public interface ITokenResolver
{
    string Resolve(
        string token,
        ImageContext context);
}
```

### ISequenceGenerator

```csharp
public interface ISequenceGenerator
{
    int GetSequenceValue(
        int startValue,
        int itemIndex);
}
```

### ISequenceFormatter

```csharp
public interface ISequenceFormatter
{
    string Format(
        int sequenceValue,
        string? format);
}
```

### IFilenameSanitizer

```csharp
public interface IFilenameSanitizer
{
    string Sanitize(string filename);
}
```

### ICollisionDetector

```csharp
public interface ICollisionDetector
{
    Task<bool> ExistsAsync(
        string filename,
        CancellationToken cancellationToken);
}
```

---

## 1.29 核心業務規則

AI Agent 實作時，以下規則視為 **MUST**：

1. Template 不得為空。
2. Unknown Token 必須拒絕。
3. Token 可以位於任意位置。
4. Fixed Text 由使用者決定。
5. Separator 不得硬編碼。
6. OriginalFileName 必須存在。
7. Sequence 預設從 1 開始。
8. Sequence Increment 在 MVP 固定為 1。
9. Sequence 必須依 Request 的 Image Order 產生。
10. Sequence Format 與 Sequence Start 必須分離。
11. 同一圖片的 Multiple Sequence Token 必須使用相同 Sequence Value。
12. Filename 必須經過 Sanitization。
13. Path Traversal 必須拒絕。
14. Filename 過長必須拒絕。
15. Rename 不得覆蓋既有檔案。
16. Preview 不得修改檔案或 Database。
17. Apply 必須重新驗證。
18. Apply 必須重新進行 Collision Check。
19. File Operation 與 Database Transaction 不視為同一 Atomic Transaction。
20. 已完成 Job 不得重複 Rename。
21. 不得重新執行其他 Module 的分析工作。
22. API 必須遵守 Global Error Contract。

---

## 1.30 與其他 Module 的關係

```text
Metadata Module
       │
       │ Metadata
       ▼
Naming Module
       │
       ├── Template Parser
       ├── Token Resolver
       ├── Sequence Generator
       ├── Sequence Formatter
       ├── Sanitizer
       └── Collision Detector
       │
       ▼
Storage
       │
       └── File Rename
```

Naming Module 使用：

```text
Images
ProcessingJobs
ProcessingLogs
```

Naming Module 不負責：

```text
EXIF
GPS
SHA-256
pHash
Quality Score
```

---

## 1.31 與 System-Level Specification 的關係

本文件只定義：

```text
Naming Responsibilities
Naming Business Rules
Naming Interfaces
Naming API Contract
Naming Testing Requirements
Naming Module Boundary
```

以下由 System-Level Specification 統一管理：

```text
System Architecture
Database Global Schema
API Versioning Policy
Global Error Contract
Authentication / Authorization
Background Processing Architecture
Storage Architecture
Deployment Architecture
Observability Architecture
SignalR Architecture
Global Retry Policy
```

如果兩份文件出現衝突：

```text
System-Level Specification
        ↓
優先於
        ↓
Module-Level Specification
```

Naming Module 不得自行修改系統級規則。

---

# 2. 資料表結構

## 2.1 Table Overview

Naming Module 不建立專用 Naming Table。

使用既有：

| Table | 用途 |
|---|---|
| `Images` | 取得圖片、原始檔名、Metadata、Storage Path，並保存新檔名 |
| `ProcessingJobs` | 建立與追蹤 Naming Job |
| `ProcessingLogs` | 記錄 Naming / Rename 結果 |

`Batches` 由其他 Module 管理。

Naming Module 可以透過：

```text
Images.BatchId
```

取得 Batch 關聯，但不管理 Batch Schema。

---

## 2.2 Images

Naming Module 使用：

| 欄位 | 型別 | Nullable | Key / Index | 用途 |
|---|---|---|---|---|
| `Id` | BIGINT | No | PK | Image ID |
| `BatchId` | UUID | No | FK / Index | Batch |
| `OriginalFileName` | VARCHAR(255) | No | — | 原始檔名 |
| `StoredPath` | VARCHAR(500) | No | — | Storage Path |
| `NewFileName` | VARCHAR(255) | Yes | — | Rename 後檔名 |
| `TakenAt` | TIMESTAMP | Yes | Index | 拍攝時間 |
| `CameraModel` | VARCHAR(100) | Yes | — | 相機型號 |
| `LocationName` | VARCHAR(150) | Yes | — | 地點 |
| `Status` | VARCHAR(30) | No | Index | Image Status |
| `UpdatedAt` | TIMESTAMP | No | — | 更新時間 |

Naming Module 不得修改未列出的 Schema 定義。

---

## 2.3 ProcessingJobs

Naming Apply 若採 Background Processing，使用既有 `ProcessingJobs`。

| 欄位 | 型別 | Nullable | Key / Index | 用途 |
|---|---|---|---|---|
| `Id` | BIGINT | No | PK | Job ID |
| `ImageId` | BIGINT | No | FK / Index | Image |
| `BatchId` | UUID | No | FK / Index | Batch |
| `Workflow` | VARCHAR(30) | No | Unique / Index | Workflow |
| `Status` | VARCHAR(30) | No | Index | Job Status |
| `RetryCount` | INT | No | — | Retry 次數 |
| `CreatedAt` | TIMESTAMP | No | — | 建立時間 |
| `StartedAt` | TIMESTAMP | Yes | — | 開始時間 |
| `CompletedAt` | TIMESTAMP | Yes | — | 完成時間 |
| `ErrorCode` | VARCHAR(50) | Yes | — | 錯誤代碼 |
| `ErrorMessage` | TEXT | Yes | — | 錯誤訊息 |

既有 Constraint：

```text
UNIQUE(ImageId, Workflow)
```

Naming Workflow：

```text
Workflow = Naming
```

---

## 2.4 ProcessingLogs

| 欄位 | 型別 | Nullable | Key / Index | 用途 |
|---|---|---|---|---|
| `Id` | BIGINT | No | PK | Log ID |
| `ImageId` | BIGINT | No | FK / Index | Image |
| `Step` | VARCHAR(50) | No | Index | Processing Step |
| `Status` | VARCHAR(20) | No | — | 執行狀態 |
| `ErrorCode` | VARCHAR(50) | Yes | — | Error Code |
| `ErrorMessage` | TEXT | Yes | — | Error Message |
| `DurationMs` | BIGINT | Yes | — | 執行時間 |
| `RetryCount` | INT | No | — | Retry 次數 |
| `TraceId` | VARCHAR(100) | Yes | Index | Trace ID |
| `Timestamp` | TIMESTAMP | No | Index | 發生時間 |

Naming 使用：

```text
NAMING
FILE_RENAME
```

---

## 2.5 相關 Index / Constraint

Naming Module 依賴既有 Database Schema。

主要 Index：

```text
Images.BatchId
Images.Status
Images.TakenAt

ProcessingJobs.ImageId
ProcessingJobs.BatchId
ProcessingJobs.Status

ProcessingLogs.ImageId
ProcessingLogs.Step
ProcessingLogs.Timestamp
ProcessingLogs.TraceId
```

主要 Constraint：

```text
Images.Id
Images.BatchId → Batches.Id

ProcessingJobs.Id
ProcessingJobs.ImageId → Images.Id
ProcessingJobs.BatchId → Batches.Id
UNIQUE(ImageId, Workflow)

ProcessingLogs.Id
ProcessingLogs.ImageId → Images.Id
```

Naming Module 不得自行新增、刪除或修改既有 Constraint。

如需變更，必須先修改：

```text
System-Level Specification
+
Database Schema Specification
```

---

# 3. API 文件

## 3.1 API Overview

Naming API 使用系統既有版本：

```text
/api/v1
```

API：

| Method | Endpoint | 用途 |
|---|---|---|
| POST | `/api/v1/images/naming/preview` | 預覽 Rename |
| POST | `/api/v1/images/naming/apply` | 正式 Rename |

API 必須遵守：

```text
Global Error Contract
TraceId
Authentication / Authorization
API Versioning
```

以上規則由 System-Level Specification 管理。

---

## 3.2 Naming Preview

```http
POST /api/v1/images/naming/preview
```

用途：

> 根據圖片、Template、Sequence 與 Collision Policy 產生預覽結果。

Preview：

```text
不得 Rename
不得修改 Database
```

成功：

```http
200 OK
```

---

## 3.3 Naming Apply

```http
POST /api/v1/images/naming/apply
```

用途：

> 正式執行圖片 Rename。

若使用 Background Processing：

```http
202 Accepted
```

API 建立 Job 後立即返回，不等待整個 Rename Workflow 完成。

---

## 3.4 Request

Preview 與 Apply 使用相同 Request 結構：

```json
{
  "imageIds": [101, 102, 103],
  "template": "日本東京之旅_{Sequence:000}",
  "sequenceStart": 1,
  "collisionPolicy": "NumericSuffix"
}
```

---

## 3.5 Request Parameters

| 欄位 | 型別 | 必填 | 預設值 | 說明 |
|---|---|---:|---:|---|
| `imageIds` | `array<long>` | Yes | — | 圖片 ID |
| `template` | `string` | Yes | — | Naming Template |
| `sequenceStart` | `integer` | No | `1` | Sequence 起始值 |
| `collisionPolicy` | `string` | Yes | — | Collision Policy |

支援：

```text
NumericSuffix
Sequence
Skip
Fail
```

---

## 3.6 Request Validation

### imageIds

必須：

```text
Not Empty
Valid Long
Image Exists
```

否則：

```text
IMAGE_NOT_FOUND
```

---

### template

必須：

```text
Not Empty
Within System Request Length Limit
Parsable
```

---

### Token

Unknown Token：

```text
UNKNOWN_NAMING_TOKEN
```

---

### Sequence Format

Invalid Format：

```text
INVALID_SEQUENCE_FORMAT
```

---

### Sequence Start

規則：

```text
未提供 → 1
必須為合法整數
不得為負值
不得超過系統最大值
```

無效：

```text
INVALID_SEQUENCE_START
```

---

### Collision Policy

不支援的 Policy 必須拒絕。

---

# 4. Response

## 4.1 Naming Preview Response

成功：

```http
200 OK
```

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "imageId": 101,
        "originalFileName": "IMG_001.jpg",
        "newFileName": "日本東京之旅_001.jpg",
        "collision": false,
        "status": "Ready"
      }
    ]
  },
  "traceId": "00-abc123"
}
```

---

## 4.2 Preview Item

每個 Item：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `imageId` | long | Image ID |
| `originalFileName` | string | 原始檔名 |
| `newFileName` | string | 預計新檔名 |
| `collision` | boolean | 是否 Collision |
| `status` | string | Preview Status |

Status：

```text
Ready
Skipped
Failed
```

---

## 4.3 Naming Apply Response

Background Processing：

```http
202 Accepted
```

```json
{
  "success": true,
  "data": {
    "batchId": "8d3f4c4e-1d9b-4c72-8d32-123456789abc",
    "jobCount": 3,
    "status": "Pending"
  },
  "traceId": "00-abc123"
}
```

欄位：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `batchId` | UUID | 此次處理 Batch |
| `jobCount` | int | Job 數量 |
| `status` | string | Job 初始狀態 |
| `traceId` | string | Trace ID |

---

# 5. Error Response

所有錯誤遵循 Global Error Contract：

```json
{
  "success": false,
  "error": {
    "code": "INVALID_NAMING_TEMPLATE",
    "message": "The naming template is invalid.",
    "traceId": "00-abc123"
  }
}
```

Client 不得收到：

```text
StackTrace
SQL Exception
Internal File Path
Connection String
Internal Exception Details
```

---

## 5.1 HTTP Status

| HTTP | 用途 |
|---:|---|
| `200` | Preview Success |
| `202` | Apply Job Accepted |
| `400` | Validation Error |
| `404` | Image Not Found |
| `409` | Filename Collision |
| `500` | Rename / Storage / Internal Error |

---

# 6. 測試規劃

## 6.1 Unit Test

Naming 核心業務邏輯必須可以獨立 Unit Test。

---

## 6.2 Template Parser

至少測試：

```text
Valid Template
Invalid Template
Empty Template
Unknown Token
Multiple Tokens
Fixed Text + Token
Token at Beginning
Token in Middle
Token at End
Multiple Sequence Tokens
Invalid Sequence Format
```

例如：

```text
{TakenDate}_{Sequence:000}
```

必須成功。

```text
{UnknownToken}
```

必須失敗。

---

## 6.3 Token Resolution

每個 Token 必須測試：

```text
Normal Value
Missing Value
Fallback Value
```

包含：

```text
OriginalFileName
OriginalExtension
TakenAt
TakenDate
TakenTime
Year
Month
Day
LocationName
CameraModel
ImageId
Sequence
```

---

## 6.4 Sequence Generation

測試：

```text
Start = 1
→ 1, 2, 3
```

```text
Start = 100
→ 100, 101, 102
```

```text
Start = 2026
→ 2026, 2027, 2028
```

並驗證 Image Order。

例如：

```text
ImageIds = [103, 101, 102]
```

必須：

```text
103 → 001
101 → 002
102 → 003
```

---

## 6.5 Sequence Formatting

測試：

```text
{Sequence:00}
→ 01, 02, 03
```

```text
{Sequence:000}
→ 001, 002, 003
```

```text
{Sequence:0000}
→ 0001, 0002, 0003
```

並測試：

```text
Start = 98
Format = 000
→ 098, 099, 100
```

---

## 6.6 Multiple Sequence Token

Template：

```text
日本_{Sequence:000}_東京_{Sequence:000}
```

Sequence：

```text
1
```

預期：

```text
日本_001_東京_001
```

不得：

```text
日本_001_東京_002
```

---

## 6.7 Metadata Fallback

測試：

```text
TakenAt missing
→ FileCreatedAt
```

```text
LocationName missing
→ UnknownLoc
```

```text
CameraModel missing
→ UnknownCamera
```

```text
OriginalFileName missing
→ Error
```

---

## 6.8 Filename Sanitization

測試：

```text
Taoyuan/City
Taoyuan\City
Reserved Characters
Leading Whitespace
Trailing Whitespace
Long Filename
```

確認結果符合既定 Filename Policy。

---

## 6.9 Path Traversal

至少測試：

```text
../../../file.jpg
..\..\file.jpg
/var/data/file.jpg
C:\Windows\file.jpg
```

預期：

```text
Request Rejected
No Rename
```

---

## 6.10 Filename Length

測試：

```text
Within Limit
Exactly At Limit
Exceeds Limit
```

超過限制：

```text
FILENAME_TOO_LONG
```

---

## 6.11 Collision

測試：

```text
No Collision
NumericSuffix
Sequence
Skip
Fail
```

最重要的 invariant：

```text
Existing File
      ↓
Never Overwrite
```

---

## 6.12 Preview / Apply

測試：

```text
Preview
```

不得修改：

```text
Physical File
Images.NewFileName
Images.StoredPath
ProcessingJobs
```

Apply：

```text
File Rename
Database Update
ProcessingLog
```

都必須正確執行。

---

## 6.13 Apply Re-validation

測試：

```text
Preview
 ↓
Target Filename becomes occupied
 ↓
Apply
```

預期：

```text
Collision Check Again
```

不得直接使用 Preview 的舊結果。

---

## 6.14 Idempotency

測試：

```text
Completed Job
 ↓
Execute Again
 ↓
Skip
```

確保不會發生二次 Rename。

---

## 6.15 Concurrency

測試：

```text
Request A
      ↓
Same Target Filename
      ↑
Request B
```

預期：

```text
最多一個 Rename 直接成功
其他 Request 必須重新處理 Collision
```

---

# 7. Integration Test

Integration Test 驗證：

```text
API
 ↓
Application Service
 ↓
Naming Service
 ↓
EF Core
 ↓
Database
 ↓
Storage
```

---

## 7.1 Preview Integration

驗證：

```text
Image Query
Template Resolution
Sequence Generation
Collision Detection
Preview Result
```

並確認：

```text
Database unchanged
File unchanged
```

---

## 7.2 Apply Integration

驗證：

```text
Application Service
 ↓
Storage Rename
 ↓
Images.NewFileName
 ↓
ProcessingLog
```

---

## 7.3 ProcessingJob

驗證 Apply 建立：

```text
Workflow = Naming
Status = Pending
```

並確認：

```text
UNIQUE(ImageId, Workflow)
```

---

## 7.4 ProcessingLog

成功：

```text
NAMING → Success
FILE_RENAME → Success
```

失敗至少包含：

```text
Status
ErrorCode
ErrorMessage
TraceId
DurationMs
RetryCount
```

---

## 7.5 Storage Failure

模擬：

```text
Storage Rename Failure
```

確認能正確轉換為：

```text
RENAME_FAILED
```

或：

```text
STORAGE_ERROR
```

並傳遞至 Application Layer。

---

## 7.6 Database Failure

測試：

```text
File Rename Success
        ↓
Database Update Failure
```

確認：

```text
Compensation Strategy
```

依系統既有規則執行。

---

# 8. E2E / Playwright Test

若系統提供 Naming UI，必須驗證完整流程：

```text
User
 ↓
UI
 ↓
API
 ↓
Naming Module
 ↓
Storage / Database
 ↓
UI Result
```

---

## E2E-01 Naming Preview

```text
Open Upload Page
 ↓
Select Images
 ↓
Open Naming
 ↓
Enter Template
 ↓
Click Preview
 ↓
Verify New Filename
```

確認：

```text
Preview Result Correct
File Not Modified
```

---

## E2E-02 Sequence Position

測試：

```text
{Sequence:000}_日本
```

```text
日本_{Sequence:000}_東京
```

```text
日本_{Sequence:000}
```

全部必須正確產生。

---

## E2E-03 Metadata Template

Template：

```text
{TakenDate}_{LocationName}_{Sequence:000}
```

預期：

```text
20260823_Taoyuan_001.jpg
```

---

## E2E-04 Custom Separator

Template：

```text
日本-{Sequence:000}-東京
```

預期：

```text
日本-001-東京.jpg
```

---

## E2E-05 Custom Sequence Start

```text
Sequence Start = 100
```

結果：

```text
日本東京_100.jpg
日本東京_101.jpg
日本東京_102.jpg
```

---

## E2E-06 Multiple Sequence Token

Template：

```text
日本_{Sequence:000}_東京_{Sequence:000}
```

預期：

```text
日本_001_東京_001.jpg
日本_002_東京_002.jpg
```

---

## E2E-07 Collision

```text
Existing Filename
 ↓
Preview
 ↓
Collision Detected
 ↓
NumericSuffix
 ↓
Apply
 ↓
Verify Unique Filename
```

---

## E2E-08 Invalid Template

Template：

```text
{UnknownToken}
```

預期：

```text
HTTP 400
UNKNOWN_NAMING_TOKEN
```

並在 UI 顯示可理解的錯誤訊息。

---

## E2E-09 Metadata Fallback

圖片：

```text
GPS = null
```

Template：

```text
{LocationName}_{Sequence:000}
```

預期：

```text
UnknownLoc_001.jpg
```

---

## E2E-10 Rename Failure

```text
Naming Apply
 ↓
Storage Failure
 ↓
Processing Failed
 ↓
UI Error
```

---

# 9. 驗收條件

## AC-01 Custom Naming

```text
Template = 日本東京之旅_{Sequence:000}
Start = 1
```

預期：

```text
日本東京之旅_001.jpg
日本東京之旅_002.jpg
```

---

## AC-02 Token Position

以下全部成功：

```text
{Sequence:000}_日本
```

```text
日本_{Sequence:000}_東京
```

```text
日本_{Sequence:000}
```

---

## AC-03 Metadata

```text
{TakenDate}_{LocationName}_{Sequence:000}
```

Metadata：

```text
TakenDate = 20260823
LocationName = Taoyuan
```

結果：

```text
20260823_Taoyuan_001.jpg
```

---

## AC-04 Custom Separator

```text
日本-{Sequence:000}-東京
```

結果：

```text
日本-001-東京.jpg
```

系統不得強制轉成 `_`。

---

## AC-05 Sequence Start

```text
Start = 100
```

結果：

```text
100
101
102
```

---

## AC-06 Sequence Format

```text
Start = 1
Format = 0000
```

結果：

```text
0001
0002
0003
```

---

## AC-07 Multiple Sequence

```text
日本_{Sequence:000}_東京_{Sequence:000}
```

結果：

```text
日本_001_東京_001.jpg
```

---

## AC-08 Metadata Fallback

```text
LocationName = null
```

結果：

```text
UnknownLoc
```

---

## AC-09 Unknown Token

```text
{UnknownToken}
```

預期：

```text
HTTP 400
UNKNOWN_NAMING_TOKEN
```

---

## AC-10 Filename Safety

```text
Taoyuan/City
```

依既定 Sanitization Policy 處理。

例如：

```text
Taoyuan_City
```

---

## AC-11 Path Traversal

```text
../../../file.jpg
```

預期：

```text
Request Rejected
```

---

## AC-12 Collision

Target 已存在時：

```text
Original File
      ↓
Must Not Be Overwritten
```

---

## AC-13 Preview

Preview 後：

```text
File unchanged
Database unchanged
```

---

## AC-14 Apply

Apply 成功後：

```text
File renamed
Database updated
ProcessingLog created
```

---

## AC-15 Apply Re-validation

Preview 後 Target 被其他程序佔用：

```text
Apply
 ↓
Re-check Collision
```

不得直接覆蓋。

---

## AC-16 API Error Contract

錯誤 Response 必須包含：

```text
success = false
error.code
error.message
traceId
```

不得包含：

```text
StackTrace
Internal Path
SQL Exception
Connection String
```

---

# 10. Definition of Done

## 功能

- [ ] Custom Text
- [ ] Naming Template
- [ ] Token 任意位置
- [ ] Metadata Token
- [ ] Metadata Fallback
- [ ] Sequence Generation
- [ ] Sequence Start
- [ ] Sequence Formatting
- [ ] Multiple Sequence Token
- [ ] Custom Fixed Text
- [ ] Custom Separator
- [ ] Filename Sanitization
- [ ] Filename Validation
- [ ] Filename Length Validation
- [ ] Path Traversal Prevention
- [ ] Collision Detection
- [ ] Preview
- [ ] Apply
- [ ] Rename Result
- [ ] Idempotency
- [ ] Concurrency Handling

## Application

- [ ] NamingApplicationService
- [ ] INamingService
- [ ] INamingTemplateParser
- [ ] ITokenResolver
- [ ] ISequenceGenerator
- [ ] ISequenceFormatter
- [ ] IFilenameSanitizer
- [ ] ICollisionDetector
- [ ] Request Validation

## Database

- [ ] Images.NewFileName Update
- [ ] ProcessingJob
- [ ] ProcessingLog
- [ ] Existing Constraint 使用正確
- [ ] No Overwrite
- [ ] Compensation Strategy

## API

- [ ] Naming Preview API
- [ ] Naming Apply API
- [ ] Request DTO
- [ ] Response DTO
- [ ] Sequence Start
- [ ] Collision Policy
- [ ] Validation
- [ ] Error Response
- [ ] Error Codes
- [ ] HTTP Status
- [ ] TraceId
- [ ] Global Error Contract

## Testing

- [ ] Template Parser Unit Test
- [ ] Token Resolution Unit Test
- [ ] Sequence Generation Unit Test
- [ ] Sequence Start Unit Test
- [ ] Sequence Formatting Unit Test
- [ ] Multiple Sequence Unit Test
- [ ] Token Position Unit Test
- [ ] Custom Separator Unit Test
- [ ] Metadata Fallback Unit Test
- [ ] Sanitization Unit Test
- [ ] Filename Length Unit Test
- [ ] Path Traversal Unit Test
- [ ] Collision Unit Test
- [ ] Idempotency Test
- [ ] Concurrency Test
- [ ] Database Integration Test
- [ ] Storage Integration Test
- [ ] API Integration Test
- [ ] Playwright Preview E2E
- [ ] Playwright Apply E2E
- [ ] Playwright Sequence E2E
- [ ] Playwright Collision E2E
- [ ] Playwright Invalid Template E2E
- [ ] Playwright Metadata Fallback E2E
- [ ] Playwright Rename Failure E2E

---

# 11. AI Agent Implementation Guidance

本章不是新的業務規則，而是為了讓 AI Agent 正確理解本 Module 的實作邊界。

## 11.1 實作順序

建議依以下順序實作：

```text
1. Domain Model
       ↓
2. Template Parser
       ↓
3. Token Resolver
       ↓
4. Sequence Generator
       ↓
5. Sequence Formatter
       ↓
6. Filename Sanitizer
       ↓
7. Filename Validator
       ↓
8. Collision Detector
       ↓
9. Naming Application Service
       ↓
10. Database Integration
       ↓
11. Storage Rename
       ↓
12. API
       ↓
13. Unit Test
       ↓
14. Integration Test
       ↓
15. E2E Test
```

---

## 11.2 Agent 不應自行擴充的功能

除非其他 Specification 明確要求，Agent 不應自行加入：

```text
AI Filename Generation
AI Image Understanding
CLIP
OCR
Face Recognition
Semantic Search
New Naming Token
New Collision Policy
New Database Table
New API Version
New Retry Policy
New Storage Architecture
```

---

## 11.3 Agent 修改規則

如果實作時發現：

```text
Specification 不足
Specification 衝突
需要修改 Database Schema
需要修改 Global Error Contract
需要修改 API Version
需要修改 System Architecture
```

Agent 不應自行決定。

應：

```text
停止該項假設
 ↓
提出 Specification Gap
 ↓
等待明確決策
```

---

# 12. Module 完成判定

Naming Module 完成的最低條件：

```text
Template
   ↓
Token Resolution
   ↓
Sequence
   ↓
Filename Construction
   ↓
Sanitization
   ↓
Validation
   ↓
Collision Handling
   ↓
Preview
   ↓
Apply
   ↓
Storage Rename
   ↓
Database Update
   ↓
ProcessingLog
```

並且：

```text
Unit Test
+
Integration Test
+
E2E Test
+
Acceptance Criteria
+
Definition of Done
```

全部符合後，Naming Module 才視為完成。

---

# 13. Module 最終摘要

Naming Module 的核心功能只有一件事：

> **讓使用者自由定義檔名規則，系統安全且可預測地將規則套用到圖片。**

核心輸入：

```text
Template
+
Image Metadata
+
Sequence Settings
+
Collision Policy
```

核心輸出：

```text
New Filename
+
Rename Result
+
ProcessingLog
```

核心流程：

```text
Parse
 ↓
Resolve
 ↓
Generate
 ↓
Format
 ↓
Construct
 ↓
Sanitize
 ↓
Validate
 ↓
Collision Check
 ↓
Preview
 ↓
Re-validate
 ↓
Rename
 ↓
Persist
```

核心設計原則：

```text
Flexible
Safe
Deterministic
Previewable
Idempotent
Testable
Extensible
```

Naming Module **不分析圖片**，只使用其他 Module 已產生的資料。

---

# 14. 文件結構

本 Module Specification 固定維持以下結構：

```text
Naming Module
│
├── 1. 規格
│   ├── Module Purpose
│   ├── Responsibilities
│   ├── Module Boundary
│   ├── Naming Template
│   ├── Naming Token
│   ├── Sequence
│   ├── Metadata Fallback
│   ├── Filename Safety
│   ├── Collision
│   ├── Preview / Apply
│   ├── Error Handling
│   ├── Reliability
│   ├── Concurrency
│   ├── Idempotency
│   ├── Processing Flow
│   └── Application Interfaces
│
├── 2. 資料表結構
│   ├── Table Overview
│   ├── Images
│   ├── ProcessingJobs
│   ├── ProcessingLogs
│   └── Index / Constraint
│
├── 3. API 文件
│   ├── API Overview
│   ├── Preview
│   ├── Apply
│   ├── Request
│   ├── Response
│   └── Error Contract
│
├── 4. 測試規劃
│   ├── Unit Test
│   ├── Integration Test
│   └── E2E / Playwright
│
├── 5. 驗收條件
│
├── 6. Definition of Done
│
└── 7. AI Agent Implementation Guidance
```

這個結構就是本 Module 的標準文件骨架，後續其他 Module 可以沿用相同格式。