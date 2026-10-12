# 04 — Naming Module

> **規格修訂草稿（2026-10-12）**：以 develop `8ff1dd983af2ffa68dfc93893f9cca61fadb2c90` 為基準。
> MOD-01 feature `a1bb29e1d7cf704a202adbe1c14741b6441575be` 僅為交接證據，未合併進 develop。
> 已核准與待決策內容分開記錄於 [規格審閱與決策紀錄](../reviews/Specification-Revision-Review.md)。
> F-01～F-08已核准並同步至最終契約章；同題舊待決文字由最終章取代。引擎/精確版本N-01與實作驗收仍未完成；不是TASK、實作或PASS授權。


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
| Preview | 預覽 Artifact Creation 結果 |
| Apply | 正式執行 Artifact Creation |
| Artifact Creation Result | 回傳每張圖片的處理結果 |
| ProcessingLog | 記錄 Naming / Artifact Creation 結果 |

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
Artifact Creation Apply
Artifact Creation Result
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
| `TakenAt` | `Image.CreatedAt（UploadCreatedAt fallback）` |
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
Do not Artifact Creation
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

Artifact Creation 不得覆蓋已存在的檔案。

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

Preview 用於在正式 Artifact Creation 前讓使用者確認結果。

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

Apply 是正式執行 Artifact Creation。

Apply 不得直接使用 Preview 的舊結果。

因為 Preview 與 Apply 之間可能發生：

```text
File Deleted
File Changed
Target Filename Occupied
Storage Changed
Concurrent Artifact Creation
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
Artifact Creation
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
Create Naming Artifact
 ↓
Update Database
 ↓
ProcessingLog
```

Preview 結果不能被視為 Apply 一定成功。

---

## 1.20 Artifact Creation Result

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
| `RENAME_FAILED` | 500 | Artifact Creation 執行失敗 |
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

## 1.22 Artifact Creation Reliability

原始物件與StoredPath保持不變。artifact建立與Database Update不是同一Atomic Transaction；Storage成功但結果保存前中斷須用OperationId/Inspect核對，不建立第二份artifact。確認receipt/來源/目標一致後才補T3保存並通知；無法確認先阻擋。
reapply、artifact保留與清理政策仍待決，未核准前不得自動清理（包括失敗、取消、孤立artifact）；不能以Compensation為由刪原檔或artifact，也沒有「改名回原檔」操作。receipt mapping與未知副作用處置依F-02最終章/共用Schema，不隨便標Success或消耗新Step retry。

---

## 1.23 Concurrency

Naming Module 必須考慮並行 Artifact Creation。

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
Attempt Artifact Creation
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

Naming Apply 必須避免已完成的 Job 被重複 Artifact Creation。

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
Artifact Creation Result
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
Naming Artifact Creation
   ↓
Database Update
   ↓
ProcessingLog
```

---

## 與 MOD-02 的已核准執行契約

- MVP 單 Instance、多 Worker，MOD-02 以 SQL Server 原子 Claim 取得 Job 執行權；本模組不建立 Queue、Worker 或平行重試排程器。
- 每Run的每個transient Step由MOD-02管理四個邏輯槽位0～3，重試等待1／2／4秒；Shutdown Resume沿用槽位而另記invocation，本模組不疊加retry loop。
- host shutdown cancellation 向上傳播、不算 Retry；中斷由 MOD-02 處理，不能偽裝成 module failure 或 fallback。已提交成功結果需核對可重用性，不重做副作用。
- Job Status、Step Result、Log Status 不混用；失敗由 MOD-02 協調持久化 Job transition，本模組不直接改其 enum。
- D-05／D-06已核准MOD-02單一History writer、Run／checkpoint／invocation、提交後best-effort事件；Step從checkpoint映射、Timestamp為Log建立時間，schema見共用文件§3～§6。子模組回報Outcome，不重複寫History。
- 保留每次 attempt History，重要 operation 安全保留 JobId／ImageId／TraceId；沿用 ILogger<T>，不輸出 raw exception／SQL／path／secrets，不重複 MOD-01 failure event。
- 本模組的新增契約缺口與決策選項見 `docs/reviews/Specification-Revision-Review.md`；未決事項不能以一般實作細節自行補足。


### MOD-04 獨立審閱缺口（未核准）

- M04-01／Major：Naming／Full Upload Workflow 只有 Workflow，沒有 Template、image order、sequenceStart／collisionPolicy 的持久化 Context。§3.4 與 §4.3 的 background Request／Batch identity 如何接到既有 Job 待 D-09，不能從 Queue 以默認 Template 補完。
- M04-02／Major：MOD-01 StoredPath = original/{guid} 且無副檔名；§1.12 原始副檔名必須由 OriginalFileName 取得，不從 StoredPath 猜。本批D-09核准保留original＋建立artifact，StoredPath不改；獨立read/artifact abstraction，具體簽章與mapping待確認。
- M04-03／Major：§1.13 Sanitization 具體替換／reserved basename／空結果政策未定義；必須保留 §1.14 traversal Reject，不能先 sanitize 後把危險 path 轉為可接受。
- M04-04／Major：§1.16 NumericSuffix／Sequence 的 retry 上限、同一次 Request 內 collision、重複 imageIds、sequence overflow 與 length limits 未定。不得用無限 loop 或自行挑選上限。
- M04-05／Major：{OriginalExtension} 與 §1.12 自動補副檔名可能重複；§1.24 已完成 Job skip 與再次 Apply 新 Template 的重新分析語意未定，見 D-04／D-09。
- M04-06／Major（部分解除）：D-06已批准History Skipped、D-07以PrimaryJob計數；Naming Collision Skip按F-02/F-07明確Item終態可完成對應Step，但不是artifact成功；其他必要Step仍須完成。

已確認：Preview 不寫檔案、Image、Job 或 Log；Apply 重新驗證，原子 Storage no-overwrite 作最後防線。重要驗收包含 Crash after artifact creation before DB、compensation failure、同 target Race、原始檔案保留以及 retry 不二次 Artifact Creation；具體驗收預期需先完成 D-09。

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
15. Artifact Creation 不得覆蓋既有檔案。
16. Preview 不得修改檔案或 Database。
17. Apply 必須重新驗證。
18. Apply 必須重新進行 Collision Check。
19. File Operation 與 Database Transaction 不視為同一 Atomic Transaction。
20. 已完成 Job 不得重複 Artifact Creation。
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
       └── Naming Artifact Creation
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

### D-05～D-07 同步限制

只有binding／版本／Context可確認且T1／T2已原子提交Running Log後才開始本模組。結果／History保存重試不重做模組操作，也不消耗business retry budget。
程序在T3前中斷時memory可能遺失；只讀計算需可重現Input／RuleVersion才能安全重算；已有SQL／Group／Storage副作用先核對，不因未保存checkpoint盲目重做。
Unknown副作用／Context先阻擋。PrimaryJob才更新Image／BatchStatus，secondary不改Uploadaggregate。F-01～F-08的結果/版本/receipt已同步最終章；Analysis/Full尚有N-01/N-02，實作與驗收未完成。

---

# 2. 資料表結構

## 2.1 Table Overview

Naming Module 不建立專用 Naming Table。

使用既有：

| Table | 用途 |
|---|---|
| `Images` | 取得圖片、原始檔名、Metadata、Storage Path，並保存新檔名 |
| `ProcessingJobs` | 建立與追蹤 Naming Job |
| `ProcessingLogs` | 記錄 Naming / Artifact Creation 結果 |

`Batches` 由其他 Module 管理。

Naming Module 可以透過：

```text
Images.BatchId
```

取得 Batch 關聯，但不管理 Batch Schema。

---

## 共用 Schema 與交接基線

physical mapping 以 `docs/Database-Schema.md` 已確認部分為共同依據；尚未定案的欄位、索引與關係明列為待決策，不假設不存在的原始 Database-Schema.md 已提供答案。
沿用 MOD-01 Image.Id BIGINT、BatchId uniqueidentifier、DateTimeOffset／datetimeoffset(7) 與 Unicode mapping。
SQL Server timestamp／rowversion 不是日期時間；日期不得以 TIMESTAMP mapping 實作。
MOD-01 feature 尚未合併；TASK-11／12 最新驗收維持待確認，不能把後續模組未完成判作 MOD-01 Backend 缺陷。

---

## 2.2 Images

Naming Module 使用：

| 欄位 | 型別 | Nullable | Key / Index | 用途 |
|---|---|---|---|---|
| `Id` | BIGINT | No | PK | Image ID |
| `BatchId` | UNIQUEIDENTIFIER | No | FK / Index | Batch |
| `OriginalFileName` | NVARCHAR(255) | No | — | 原始檔名 |
| `StoredPath` | VARCHAR(500) | No | — | Storage Path |
| `NewFileName` | NVARCHAR(255) | Yes | — | Artifact Creation 後檔名 |
| `TakenAt` | DATETIMEOFFSET(7) | Yes | Index | 拍攝時間 |
| `CameraModel` | NVARCHAR(100) | Yes | — | 相機型號 |
| `LocationName` | NVARCHAR(150) | Yes | — | 地點 |
| `Status` | VARCHAR(30) | No | Index | Image Status |
| `UpdatedAt` | DATETIMEOFFSET(7) | No | — | 更新時間 |

Naming Module 不得修改未列出的 Schema 定義。

---

## 2.3 ProcessingJobs

Naming Apply 若採 Background Processing，使用既有 `ProcessingJobs`。

| 欄位 | 型別 | Nullable | Key / Index | 用途 |
|---|---|---|---|---|
| `Id` | BIGINT | No | PK | Job ID |
| `ImageId` | BIGINT | No | FK / Index | Image |
| `BatchId` | UNIQUEIDENTIFIER | No | FK / Index | Batch |
| `Workflow` | VARCHAR(30) | No | Unique / Index | Workflow |
| `Status` | VARCHAR(30) | No | Index | Job Status |
| `RetryCount` | INT | No | — | Retry 次數 |
| `CreatedAt` | DATETIMEOFFSET(7) | No | — | 建立時間 |
| `StartedAt` | DATETIMEOFFSET(7) | Yes | — | 開始時間 |
| `CompletedAt` | DATETIMEOFFSET(7) | Yes | — | 完成時間 |
| `ErrorCode` | VARCHAR(50) | Yes | — | 錯誤代碼 |
| `ErrorMessage` | NVARCHAR(MAX) | Yes | — | 錯誤訊息 |

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

使用共用 `docs/Database-Schema.md` §5，不在Naming重建History表；Step由checkpoint關聯取得。
Log.Id為BIGINT identity，JobId／ImageId、checkpoint關聯的Step、Status、Error、DurationMs、RetryOrdinal／ResumeNo／AttemptNo、TraceId、Timestamp依共用契約；舊Query retryCount映射RetryOrdinal，不是physical欄位。
本模組使用 NAMING／FILE_RENAME 的 Step Result；Log Status 和 Job Status 分開，不自行新增 Log enum／索引。
Preview為read-only不寫ProcessingLogs；background attempt的單一writer／提交順序依D-06。Naming RequestContext／Create/Inspect reconciliation依F-02最終章，未知副作用仍阻擋。

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
ProcessingStepCheckpoints.Step（Query join，不是新增Standalone Index批准）
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
| POST | `/api/v1/images/naming/preview` | 預覽 Artifact Creation |
| POST | `/api/v1/images/naming/apply` | 正式 Artifact Creation |

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
不得 Artifact Creation
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

> 正式執行圖片 Artifact Creation。

若使用 Background Processing：

```http
202 Accepted
```

API 建立 Job 後立即返回，不等待整個 Artifact Creation Workflow 完成。

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
| `500` | Artifact Creation / Storage / Internal Error |

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
→ Image.CreatedAt（UploadCreatedAt fallback）
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
No Artifact Creation
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
Naming Artifact Creation
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

確保不會發生二次 Artifact Creation。

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
最多一個 Artifact Creation 直接成功
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
Storage Artifact Creation
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
Storage Artifact Creation Failure
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
Naming Artifact Creation Success
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

## E2E-10 Artifact Creation Failure

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
File artifact creationd
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
error.traceId
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
- [ ] Artifact Creation Result
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
- [ ] Playwright Artifact Creation Failure E2E

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
11. Storage Artifact Creation
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
Storage Artifact Creation
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
Artifact Creation Result
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
Artifact Creation
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

# 最終核准契約 — F-02（文件審核版）

命名是建立artifact，不是rename/move原檔；original bytes與StoredPath不變。只有PrimaryJob可投影NewFileName。舊FILE_RENAME／RENAME_FAILED是兼容識別，不表示搬原檔。

## Application與持久輸入

IImageContentReader.OpenOriginalAsync(ImageId,token)回ContentHandle(Stream,Length,ContentVersion)，caller dispose、不承諾seek；Infrastructure核對reference／原內容SHA256，client不傳path。INamingArtifactStorage.CreateAsync(OperationId,ImageId,ExpectedContentVersion,TargetName,token)回ArtifactReceipt；InspectAsync(OperationId,token)回Missing/Complete/Unknown與receipt。相同operation同payload返回同結果，不同payload拒絕；provider做conditional no-overwrite。
NamingRequest含BatchId、Template、RuleVersion、CollisionPolicy、InputFingerprint；Item綁對應Image/Job、固定Ordinal/Sequence、OriginalExtension與ResolvedInput，Run引用Item。Template無來源時不Claim、不生成默認Template，不修改MOD-01 Upload input。
Start預設1、>=0，Increment沿用MVP固定1，checked BIGINT；digits只padding、不截斷。重複ImageId/跨Batch/overflow/非法空或reserved名/path traversal拒絕。extension來自OriginalFileName，已含同extension不再加，矛盾extension拒絕。Preview非强制且零寫入；Apply重新校驗凍結input。
collision候選最多100（原名第一個、99穩定suffix），可設定、未效能驗證；request內衝突同樣reservation/no-overwrite。Skip是顯式Item終態，不是artifact Success；NAMING/FILE_RENAME checkpoint以Skipped完成，不將任意Skip當Job完成。100耗盡FILENAME_COLLISION Permanent Failed，RENAME_FAILED安全message寫artifact creation failure。

## 操作與crash恢復

保存Prepared與target reservation→Create artifact→Inspect核對receipt/source/target→T3保存result/checkpoint/History/Primary投影→commit後通知。Storage與DB不在同一transaction；未知結果NeedsReconciliation且不執行。已建立但未保存SQL結果須補T3、不重建。shutdown同Run/Input/OperationId恢復，人工Retry新Run沿用原Naming input，結果重用須Inspect，不新增第二份artifact。
reservation對scope內provider-normalized TargetKey唯一；Local實際case-sensitivity/Unicode與GCS策略由adapter契約核對，不能只用memory Exists→Create。receipt須可證明本operation與content，不以單純目標存在當成功。
reapply、Completed重分析、保留／清理延後；未核准不得自動清理artifact/reservation/孤立物件，不以Compensation刪原檔或artifact。部署須承擔storage增長，沒有修復/unblock/cleanup寫API。本輪只規格，不建TASK/實作/遷移。
