# Duplicate Module Specification

> **規格修訂草稿（2026-10-12）**：以 develop `8ff1dd983af2ffa68dfc93893f9cca61fadb2c90` 為基準。
> MOD-01 feature `a1bb29e1d7cf704a202adbe1c14741b6441575be` 僅為交接證據，未合併進 develop。
> 已核准與待決策內容分開記錄於 [規格審閱與決策紀錄](../reviews/Specification-Revision-Review.md)。
> F-01～F-08已核准並同步至最終契約章；同題舊待決文字由最終章取代。引擎/精確版本N-01與實作驗收仍未完成；不是TASK、實作或PASS授權。


**文件名稱：** Duplicate Module Specification  
**模組編號：** MOD-05
**模組名稱：** Duplicate Module  
**中文名稱：** 重複照片偵測模組  
**文件版本：** V2.0  
**對應系統：** Photo Processing & Intelligent Image Analysis Platform  
**架構：** Clean Architecture  
**技術：** ASP.NET Core / C# / EF Core  
**文件用途：** AI Agent Development Specification  
**文件狀態：** Development

---

# 1. 規格

## 1.1 模組目的

Duplicate Module 負責判斷照片是否為 **完全相同內容的檔案（Exact Duplicate）**。

MVP 使用：

```text
File Size
+
SHA-256
```

判斷兩個檔案是否具有完全相同的 Binary Content。

### MVP 範圍

```text
File Size Comparison
        ↓
SHA-256 Calculation
        ↓
Exact Duplicate Detection
        ↓
Duplicate Group Management
        ↓
Duplicate Query
```

---

## 1.2 模組責任

### 本模組負責

- File Size 比較
- SHA-256 計算
- Exact Duplicate Detection
- Duplicate Group 建立與管理
- Duplicate Group Member 管理
- Duplicate 結果查詢
- Processing Log
- Idempotency
- Concurrent Processing 一致性

### 本模組不負責

以下功能由其他 Module 負責：

- Visual Similarity
- Photo Quality Analysis
- Version Recommendation
- File Rename
- Image Upload
- EXIF Parsing
- pHash Generation

> **Agent Rule：**
> 不得因實作 Duplicate Module 而自行加入上述功能。

---

## 1.3 Module Boundary

Duplicate Module 的輸入與輸出：

```text
Input
│
├── ImageId
├── FileSize
└── File Content
        │
        ▼
Duplicate Module
        │
        ├── SHA-256
        ├── Exact Duplicate Detection
        └── Duplicate Group
        │
        ▼
Output
│
├── Duplicate Result
├── Duplicate Group
└── Processing Log
```

本模組透過 Application Interface 取得資料：

```text
IFileStorageService
IImageRepository
IDuplicateGroupRepository
IHashService
```

### 不得直接依賴

```text
GCS SDK
EF Core DbContext
HttpClient
Controller
SignalR Hub
```

Infrastructure 實作必須透過介面注入。

---

## 1.4 Exact Duplicate 判定規則

兩個檔案必須同時符合：

```text
FileSize(A) = FileSize(B)
AND
SHA256(A) = SHA256(B)
```

才能判定：

```text
Exact Duplicate = true
```

只符合其中一項，不得判定為 Exact Duplicate。

---

## 1.5 Duplicate Detection 策略

採用兩階段判定：

```text
File
 ↓
File Size
 ↓
Size Different?
 ├── Yes → Not Duplicate
 │
 └── No
      ↓
   SHA-256
      ↓
   Hash Comparison
      ↓
   Hash Different?
    ├── Yes → Not Duplicate
    └── No  → Exact Duplicate
```

目的：

> 先為本Workflow圖片完整計算並保存SHA256，即使沒有同size候選；FileSize只縮小EXACT比較候選，不取消hash，以支持晚到候選。

---

## 1.6 SHA-256 計算規則

SHA-256 必須針對：

```text
Original File Binary Content
```

進行計算。

輸出格式：

```text
64-character hexadecimal string
```

例如：

```text
a3f1...9c20
```

### 效能要求

Hash 計算必須：

- 使用 Async
- 使用 Streaming
- 不將大型圖片完整載入 Memory
- 由 File Stream 逐步計算 Hash

概念：

```text
File Stream
    ↓
SHA-256
    ↓
Hash Result
```

Application Interface：

```text
IHashService
```

主要操作：

```text
CalculateSha256Async(Stream)
```

---

## 1.7 Duplicate Group

當多張圖片具有相同：

```text
FileSize
+
SHA256
```

時，建立或加入：

```text
DuplicateGroup
```

MVP Group Type：

```text
EXACT
```

例如：

```text
DuplicateGroup #10
│
├── Image 101
├── Image 205
└── Image 309
```

代表三張圖片具有完全相同的檔案內容。

---

## 1.8 Duplicate Group 規則

### 建立 Group

當發現相同 SHA-256：

```text
SHA256(A) = SHA256(B)
```

系統必須建立或取得：

```text
EXACT DuplicateGroup
```

### 已存在 Group

如果相同 SHA-256 已經存在：

```text
New Image
    ↓
Same SHA256
    ↓
Existing EXACT Group
    ↓
Add Member
```

不得建立新的 Duplicate Group。

因此相同 SHA-256 最終只能對應到同一個 EXACT Group。

---

## 1.9 Group Type

Domain Model 預留：

```text
EXACT
VISUAL
VERSION
```

但本模組的 MVP 僅實作：

```text
EXACT
```

未來可以擴充：

```text
SHA-256
    ↓
EXACT

pHash
    ↓
VISUAL

Similarity + Quality
    ↓
VERSION
```

> **Agent Rule：**
> MOD-05 不實作 Visual Similarity 或 Version Recommendation；系統 MVP 的這些能力由 MOD-06／MOD-08 負責。

---

## 1.10 Application Services

Application Layer 定義：

```text
IHashService
IDuplicateDetectionService
IDuplicateGroupService
```

### IHashService

負責：

```text
Calculate SHA-256
```

要求：

- Async
- Streaming
- 不完整載入大型檔案
- 回傳 SHA-256

### IDuplicateDetectionService

負責：

```text
取得 File Size
      ↓
尋找 Candidate
      ↓
計算 SHA-256
      ↓
比較 Hash
      ↓
建立或更新 Duplicate Group
```

### IDuplicateGroupService

負責：

```text
Create Group
Add Member
Get Group
Get Group Members
```

---

## 1.11 Processing Integration

Duplicate Module 可被 Processing Module 的相關 Workflow 呼叫。

本模組只定義**自身執行方式**，不重新定義整個系統 Workflow。

內部處理概念：

```text
Processing Job
      ↓
Duplicate Detection
      ↓
Get File Size
      ↓
Query Candidate Files
      ↓
Calculate SHA-256
      ↓
Compare
      ↓
Exact Duplicate?
   ┌──┴──┐
  No    Yes
  │      │
  ↓      ↓
Finish  Group
          ↓
       Persist
          ↓
         Log
```

System-Level Specification 負責定義：

- 哪個 Workflow 呼叫 Duplicate Module
- Duplicate Module 與其他 Module 的執行順序
- 整體 Processing Pipeline

---

## 1.12 Idempotency

Duplicate Detection 必須具備 Idempotent Design。

邏輯識別：

```text
ImageId + ProcessingStep
```

再次執行時：

```text
Check Processing Result
        ↓
Already Completed?
 ├── Yes → Skip
 └── No  → Execute
```

必須避免：

- 重複 Hash 計算
- 重複建立 Duplicate Group
- 重複建立 Duplicate Group Member

---

## 1.13 Concurrency

Duplicate Module 必須處理多個 Worker 同時處理相同圖片或相同 SHA-256 的情況。

例如：

```text
Worker A ──┐
           ├── SHA256 = ABC123
Worker B ──┘
```

不得產生：

```text
Group #10
Group #11
```

而應確保最終只有：

```text
One EXACT Group
```

建議透過：

```text
Database Unique Constraint
+
Transaction
+
Idempotent Application Logic
```

確保一致性。

---

## 1.14 Database Consistency Rule

同一圖片不得重複加入同一 Group。

必須具備：

```text
UNIQUE(GroupId, ImageId)
```

此 Constraint 是資料一致性的最後一道保護。

Application Layer 仍必須實作 Idempotency，不得只依賴 Database Constraint。

---

## 1.15 Error Handling

### File Not Found

```text
File Not Found
    ↓
ProcessingLog
    ↓
Processing Failed（永久不存在不 Retry；中斷 Job 由 MOD-02 Recovery）
```

### Storage Temporary Failure

```text
Storage Error
    ↓
Retry
    ↓
Retry Failed
    ↓
Processing Failed
```

### SHA-256 Calculation Failure

```text
SHA256 Failed
    ↓
安全分類／ProcessingLog
    ↓
只有 transient 交由 MOD-02 Retry；永久錯誤 Failed
```

---

## 1.16 Retry Strategy

適用於暫時性錯誤：

```text
Storage Timeout
Storage Temporary Failure
IO Temporary Failure
```

Retry：

```text
1 sec
 ↓
2 sec
 ↓
4 sec
```

不適用：

```text
Invalid File
File Not Found
Unsupported Format
```

Retry 不得用於無法透過重試解決的永久性錯誤。

---

## 1.17 Processing Log

Duplicate Module 必須記錄重要 Processing Step。

使用：

```text
SHA256_CALCULATION
EXACT_DUPLICATE_CHECK
```

例如：

```text
ImageId = 101
Step = SHA256_CALCULATION
Status = Success
DurationMs = 120
```

以及：

```text
ImageId = 101
Step = EXACT_DUPLICATE_CHECK
Status = Success
DurationMs = 15
```

Log 至少應保留：

```text
ImageId
ProcessingStep
Status
ErrorCode
DurationMs
RetryCount
Timestamp
TraceId
```

---

## 1.18 Performance

Duplicate Module 不得一次將所有圖片載入 Memory。

必須使用：

```text
Streaming
+
Controlled Concurrency
```

Hash 計算：

```text
File Stream
    ↓
SHA-256
    ↓
Hash Result
```

Controlled Concurrency 應由 Processing Layer 控制 Worker 數量，Duplicate Module 不應自行建立無限制的平行工作。

---

## 1.19 Security

Duplicate Module 必須遵循：

```text
Storage Isolation
Path Validation
File Validation
Access Validation
```

不得直接接受 Client 提供的任意 Storage Path。

取得檔案必須透過：

```text
IFileStorageService
```

避免 Module 直接操作實際 Storage Provider。

---

## 1.20 Agent Development Rules

AI Agent 開發本模組時，應依序讀取：

```text
AGENTS.md
    ↓
System-Level Specification
    ↓
docs/Database-Schema.md
    ↓
Duplicate Module Specification
    ↓
Existing Code
    ↓
Implementation
    ↓
Unit Test
    ↓
Integration Test
    ↓
Playwright Test
    ↓
Review
```

### Agent 不得自行修改

```text
System Architecture
Database Contract
API Contract
Domain Rules
```

也不得為了通過測試而修改 Specification。

### 發現規格衝突

```text
STOP
 ↓
Report Conflict
 ↓
Explain Impact
 ↓
Propose Change
 ↓
Wait for Decision
```

Agent 不得自行選擇一個規則覆蓋另一個規則。

---

## 與 MOD-02 的已核准執行契約

- MVP 單 Instance、多 Worker，MOD-02 以 SQL Server 原子 Claim 取得 Job 執行權；本模組不建立 Queue、Worker 或平行重試排程器。
- 每Run的每個transient Step由MOD-02管理四個邏輯槽位0～3，重試等待1／2／4秒；Shutdown Resume沿用槽位而另記invocation，本模組不疊加retry loop。
- host shutdown cancellation 向上傳播、不算 Retry；中斷由 MOD-02 處理，不能偽裝成 module failure 或 fallback。已提交成功結果需核對可重用性，不重做副作用。
- Job Status、Step Result、Log Status 不混用；失敗由 MOD-02 協調持久化 Job transition，本模組不直接改其 enum。
- D-05／D-06已核准MOD-02單一History writer、Run／checkpoint／invocation、提交後best-effort事件；Step從checkpoint映射、Timestamp為Log建立時間，schema見共用文件§3～§6。子模組回報Outcome，不重複寫History。
- 保留每次 attempt History，重要 operation 安全保留 JobId／ImageId／TraceId；沿用 ILogger<T>，不輸出 raw exception／SQL／path／secrets，不重複 MOD-01 failure event。
- 本模組的新增契約缺口與決策選項見 `docs/reviews/Specification-Revision-Review.md`；未決事項不能以一般實作細節自行補足。


### MOD-05 獨立審閱缺口（未核准）

- M05-01／Major：§1.8 要求同 hash 只一個 EXACT Group，但 §2.3 只有 Id／GroupType／CreatedAt，缺少可持久化的 FileSize+SHA256 group key 與唯一策略。GroupMember unique 不能解決 Group creation race；見 D-10。
- M05-02／Major：§1.5 避免無候選 hash 計算，但查詢 NotReady／NoDuplicate 與後續新候選加入需要完成證據／補比對策略；不能以 SHA256 null 當已完成或 group absence 當未完成。候選範圍、完成／更新語意待 D-10。
- M05-03／Minor：SHA256 64-character hex 大小寫／比較正規化未定，不能依 DB default collation 猜測。
- M05-04／Major：Read Stream／lifetime 與完整 IHashService signature 未接上既有 Storage Save／Delete，見 D-09；候選可讀取範圍需同 D-10，不能默認全庫或 Batch-only。

已確認 Step 名稱統一為 SHA256_CALCULATION／EXACT_DUPLICATE_CHECK；永久 FileNotFound 不 Retry，Hash error 先分類，Job Recovery 不等於修復缺失檔案。
新增驗收：同 key concurrent group creation、晚到候選、NoDuplicate completion 與 NotReady 區分、無候選之後有候選、Hash encoding 正規化；期待結果待 D-10，不標記 PASS。

---

### D-05～D-07 同步限制

只有binding／版本／Context可確認且T1／T2已原子提交Running Log後才開始本模組。結果／History保存重試不重做模組操作，也不消耗business retry budget。
程序在T3前中斷時memory可能遺失；只讀計算需可重現Input／RuleVersion才能安全重算；已有SQL／Group／Storage副作用先核對，不因未保存checkpoint盲目重做。
Unknown副作用／Context先阻擋。PrimaryJob才更新Image／BatchStatus，secondary不改Uploadaggregate。F-01～F-08的結果/版本/receipt已同步最終章；Analysis/Full尚有N-01/N-02，實作與驗收未完成。

---

# 2. 資料表

## 2.1 使用的資料表

Duplicate Module 使用：

```text
Images
DuplicateGroups
DuplicateGroupMembers
```

---

## 共用 Schema 與交接基線

physical mapping 以 `docs/Database-Schema.md` 已確認部分為共同依據；尚未定案的欄位、索引與關係明列為待決策，不假設不存在的原始 Database-Schema.md 已提供答案。
沿用 MOD-01 Image.Id BIGINT、BatchId uniqueidentifier、DateTimeOffset／datetimeoffset(7) 與 Unicode mapping。
SQL Server timestamp／rowversion 不是日期時間；日期不得以 TIMESTAMP mapping 實作。
MOD-01 feature 尚未合併；TASK-11／12 最新驗收維持待確認，不能把後續模組未完成判作 MOD-01 Backend 缺陷。

---

## 2.2 Images

Duplicate Module 主要使用：

| 欄位 | 用途 |
|---|---|
| Id | Image 識別 |
| FileSize | 第一階段候選篩選 |
| SHA256 | Exact Duplicate 判定 |

`SHA256` 應建立 Index。

建議：

```text
INDEX IX_Images_FileSize
INDEX IX_Images_SHA256
```

實際資料型別依：

```text
docs/Database-Schema.md
```

為準。

---

## 2.3 DuplicateGroups

| 欄位 | 說明 |
|---|---|
| Id | Group 識別 |
| GroupType | Group 類型 |
| CreatedAt | 建立時間 |

MVP：

```text
GroupType = EXACT
```

其他模組可使用的 GroupType（MOD-05 不負責）：

```text
EXACT
VISUAL
VERSION
```

---

## 2.4 DuplicateGroupMembers

| 欄位 | 說明 |
|---|---|
| Id | Member 識別 |
| GroupId | Duplicate Group |
| ImageId | 圖片 |
| SimilarityScore | 相似度，MVP Exact Duplicate 為 NULL |
| CreatedAt | 建立時間 |

Exact Duplicate 不依賴 Similarity Score：

```text
SimilarityScore = NULL
```

---

## 2.5 Database Constraints

必須具備：

```text
Primary Key
Foreign Key
Unique Constraint
Index
Check Constraint
```

### GroupType

建議：

```text
CHECK GroupType IN
(
    'EXACT',
    'VISUAL',
    'VERSION'
)
```

### Group Member

必須：

```text
UNIQUE(GroupId, ImageId)
```

### SHA-256 Index

必須建立：

```text
INDEX IX_Images_SHA256
```

File Size 建議建立：

```text
INDEX IX_Images_FileSize
```

> **資料表文件只描述 Schema、Constraint、Index。**
>
> Workflow、Retry、Idempotency、Processing State 等業務邏輯不放在本區塊。

---

# 3. API

## 3.1 API Base

```text
/api/v1
```

---

## 3.2 Query Exact Duplicates

### Endpoint

```http
GET /api/v1/images/{imageId}/duplicates
```

### Purpose

查詢指定圖片的 Exact Duplicate。

### Request

Path Parameter：

```text
imageId
```

Request Body：

```text
None
```

---

## 3.3 Success Response

```http
200 OK
```

範例：

```json
{
  "success": true,
  "data": {
    "imageId": 101,
    "groupId": 10,
    "groupType": "EXACT",
    "duplicates": [
      {
        "imageId": 205,
        "fileName": "IMG_001_copy.jpg",
        "fileSize": 5242880,
        "sha256": "a3f1...9c20"
      },
      {
        "imageId": 309,
        "fileName": "backup_IMG_001.jpg",
        "fileSize": 5242880,
        "sha256": "a3f1...9c20"
      }
    ]
  }
}
```

---

## 3.4 No Duplicate

沒有 Exact Duplicate 時仍回傳：

```http
200 OK
```

```json
{
  "success": true,
  "data": {
    "imageId": 101,
    "groupId": null,
    "groupType": null,
    "duplicates": []
  }
}
```

---

## 3.5 API Error Response

統一格式：

```json
{
  "success": false,
  "error": {
    "code": "ERROR_CODE",
    "message": "Error message.",
    "traceId": "00-abc123"
  }
}
```

不得回傳：

```text
Exception
StackTrace
Internal Path
Database Error
Connection String
```

---

## 3.6 HTTP Status

### Image 不存在

```http
404 Not Found
```

```json
{
  "success": false,
  "error": {
    "code": "IMAGE_NOT_FOUND",
    "message": "Image was not found.",
    "traceId": "00-abc123"
  }
}
```

### Duplicate Analysis 尚未完成

```http
409 Conflict
```

```json
{
  "success": false,
  "error": {
    "code": "DUPLICATE_ANALYSIS_NOT_READY",
    "message": "Duplicate analysis is not completed.",
    "traceId": "00-abc123"
  }
}
```

### Internal Error

```http
500 Internal Server Error
```

```json
{
  "success": false,
  "error": {
    "code": "INTERNAL_ERROR",
    "message": "An internal error occurred.",
    "traceId": "00-abc123"
  }
}
```

---

## 3.7 API Contract Rules

AI Agent 實作 API 時：

- 必須遵循本文件 Endpoint
- 不得自行修改 Request / Response 結構
- 不得新增未定義的 HTTP Status 作為替代方案
- Error Response 必須包含 `traceId`
- 不得將 Infrastructure Exception 直接暴露給 Client

若現有系統 API 與本文件衝突：

```text
STOP
→ Report Conflict
→ Wait for Decision
```

---

# 4. 測試

## 4.1 Unit Test

Duplicate Module 必須建立 Unit Tests。

測試範圍：

```text
Hash
File Size
Exact Duplicate
Non-Duplicate
Duplicate Group
Idempotency
Concurrency
```

---

## 4.2 SHA-256 Tests

### Same File

```text
Same Binary Content
        ↓
Same SHA256
```

### Different File

```text
Different Binary Content
        ↓
Different SHA256
```

---

## 4.3 File Size Tests

不同大小的檔案：

```text
FileSize(A) != FileSize(B)
```

結果：

```text
Not Exact Duplicate
```

此情況不應進入不必要的 SHA-256 比較流程。

---

## 4.4 Exact Duplicate Tests

```text
Same FileSize
+
Same SHA256
        ↓
Exact Duplicate
```

預期：

```text
Exact Duplicate = true
```

---

## 4.5 Non-Duplicate Tests

```text
Same FileSize
+
Different SHA256
        ↓
Not Exact Duplicate
```

預期：

```text
Exact Duplicate = false
```

---

## 4.6 Duplicate Group Tests

### First Duplicate

```text
First Duplicate
      ↓
Create EXACT Group
```

### Existing Group

```text
Existing EXACT Group
        +
New Duplicate Image
        ↓
Add Member
```

不得建立新的 Group。

---

## 4.7 Idempotency Tests

同一圖片執行 Duplicate Detection 多次：

```text
Process Image
      ↓
Process Same Image Again
```

預期：

```text
One EXACT Group
One Group Membership
```

不得產生：

```text
Duplicate Group
Duplicate Group Member
```

---

## 4.8 Concurrency Tests

模擬兩個 Worker 同時處理相同 SHA-256：

```text
Worker A ──┐
           ├── Same SHA256
Worker B ──┘
```

預期：

```text
One EXACT Group
```

不得產生：

```text
Group #10
Group #11
```

也不得產生重複的：

```text
GroupMember
```

---

## 4.9 Integration Test

Integration Test 必須驗證：

```text
Application
    ↓
EF Core
    ↓
Database
    ↓
Storage
```

至少包含：

```text
Create Image
    ↓
Calculate SHA256
    ↓
Detect Duplicate
    ↓
Create Group
    ↓
Create Member
    ↓
Query Duplicate
```

必須確認資料實際正確寫入 Database。

---

## 4.10 API Integration Test

至少測試：

### Duplicate Exists

```http
GET /api/v1/images/{imageId}/duplicates
```

預期：

```text
HTTP 200
```

並包含：

```text
groupId
groupType = EXACT
duplicates
```

### No Duplicate

預期：

```text
HTTP 200
duplicates = []
```

### Image Not Found

預期：

```text
HTTP 404
error.code = IMAGE_NOT_FOUND
traceId exists
```

### Analysis Not Ready

預期：

```text
HTTP 409
error.code = DUPLICATE_ANALYSIS_NOT_READY
```

---

## 4.11 Playwright E2E

Playwright 驗證使用者從 UI 操作到 Duplicate 結果顯示的完整流程。

### Exact Duplicate

```text
Open Upload Page
      ↓
Upload Duplicate Images
      ↓
Start Processing
      ↓
Wait Processing Complete
      ↓
Open Image Detail
      ↓
Open Duplicate Section
      ↓
Verify Duplicate Images
```

UI 預期顯示：

```text
Exact Duplicate
Duplicate Group
Duplicate Images
```

---

## 4.12 No Duplicate E2E

上傳沒有重複的圖片：

```text
Upload
  ↓
Process
  ↓
Image Detail
  ↓
Duplicate Section
```

預期：

```text
No Exact Duplicate Found
```

---

## 4.13 Acceptance Criteria

### AC-01 — SHA-256

Given：

```text
Two files have identical Binary Content
```

When：

```text
Calculate SHA-256
```

Then：

```text
SHA256(A) = SHA256(B)
```

---

### AC-02 — Exact Duplicate

Given：

```text
FileSize(A) = FileSize(B)
SHA256(A) = SHA256(B)
```

Then：

```text
GroupType = EXACT
```

---

### AC-03 — Different Hash

Given：

```text
FileSize(A) = FileSize(B)
SHA256(A) != SHA256(B)
```

Then：

```text
Not Exact Duplicate
```

---

### AC-04 — Duplicate Group

Given：

```text
Three identical files
```

Then：

```text
One EXACT Group
+
Three Group Members
```

---

### AC-05 — Idempotency

Given：

```text
Same Image processed multiple times
```

Then：

```text
No Duplicate Group Duplication
No Duplicate Member Duplication
```

---

### AC-06 — Duplicate API

Given：

```text
Duplicate Analysis completed
```

When：

```http
GET /api/v1/images/{imageId}/duplicates
```

Then：

```text
HTTP 200
```

並回傳 Duplicate Group 與成員。

---

### AC-07 — API Error

Given：

```text
Image does not exist
```

Then：

```text
HTTP 404
IMAGE_NOT_FOUND
traceId
```

---

## 4.14 Definition of Done

### Detection

- [ ] File Size Comparison
- [ ] SHA-256
- [ ] Exact Duplicate Detection
- [ ] Duplicate Group
- [ ] Duplicate Group Member

### Database

- [ ] Images SHA-256 Index
- [ ] File Size Index 需求經 Schema 決策確認（建議項目，不視為已核准新增）
- [ ] DuplicateGroups
- [ ] DuplicateGroupMembers
- [ ] PK / FK
- [ ] UNIQUE
- [ ] CHECK

### Application

- [ ] IHashService
- [ ] IDuplicateDetectionService
- [ ] IDuplicateGroupService
- [ ] Idempotency
- [ ] Concurrency Handling

### API

- [ ] Duplicate Query API
- [ ] Request / Response
- [ ] Error Response
- [ ] HTTP Status Code

### Reliability

- [ ] Retry
- [ ] Recovery
- [ ] Error Logging
- [ ] Transaction Handling

### Performance

- [ ] Streaming Hash
- [ ] Controlled Concurrency
- [ ] No Full Image Loading

### Testing

- [ ] Unit Test
- [ ] Integration Test
- [ ] API Test
- [ ] Playwright E2E Test

### Observability

- [ ] ProcessingLog
- [ ] TraceId
- [ ] Duration
- [ ] ErrorCode
- [ ] RetryCount

---

## 4.15 Agent Completion Rule

AI Agent 完成本模組前，必須確認：

```text
Specification
    ↓
Database
    ↓
Application
    ↓
API
    ↓
Unit Tests
    ↓
Integration Tests
    ↓
E2E Tests
    ↓
Review
```

只有所有必要項目完成，Duplicate Module 才視為完成。

# 最終核准契約 — F-03（EXACT與hash結果）

單部署/DB/Storage單管理者確認集合，非技術隔離，不支持多owner/tenant。同集合跨Batch才可候選，unique EXACT(FileSize,SHA256)依此部署前提成立。
本Workflow圖片完整SHA256，無候選也計算，取代舊無同size才不hash的優化。64字元小寫hex/ordinal，版本化hash唯一(ImageId,ContentVersion,HashRuleVersion)，ContentVersion由原始內容核對；獨立Pending/Processing/Completed/Failed，Images.SHA256只是Primary相容投影，不改mapping。HashRuleVersion/NoMatch/candidate版本結果authority，不以null/group不存在當已完成。
group/member唯一與DBtransaction處理並行race；member UNIQUE(GroupId,ImageId)，單content可重用versionhash，候選晚到建立新membership snapshot，不覆寫舊CompletedJob/History。SHA256與FileSize只判EXACT，不冒充pHash/VISUAL，也不自動刪圖。
Application只協調，Infrastructure透過獨立reader，NoMatch有明確已分析證據。缺檔/損壞/不支持永久錯誤，不Retry；transient由MOD02四槽位，不內部重試。T3保存result/group/reference/checkpoint/History同commit，commit不明先核DB。
