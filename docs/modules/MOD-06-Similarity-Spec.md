# Similarity Module Specification

**文件名稱：** Similarity Module Specification  
**模組編號：** MOD-06  
**模組名稱：** Similarity Module  
**中文名稱：** 視覺相似度分析模組  
**文件版本：** V2.0  
**對應系統：** Photo Processing & Intelligent Image Analysis Platform  
**架構：** Clean Architecture  
**技術：** ASP.NET Core / C# / EF Core / Microsoft SQL Server  
**文件用途：** Human + AI Agent Development Specification  
**文件狀態：** Development Baseline  

---

# 1. 規格

## 1.1 Module 目的

Similarity Module 負責找出：

> **檔案內容不同，但視覺上高度相似的照片。**

MVP 使用：

```text
pHash
+
Hamming Distance
```

判斷圖片之間的視覺相似程度。

例如：

```text
Original Image
Edited Image
Resized Image
Compressed Image
```

即使 Binary Content 不同，仍可能因視覺結構接近而被判定為相似圖片。

---

# 1.2 Module Responsibility

Similarity Module 負責：

- Image Preprocessing
- Perceptual Hash（pHash）
- Hamming Distance
- Similarity Score
- Similarity Threshold
- Candidate Filtering
- Visual Similarity Detection
- VISUAL Group
- Similarity Result Persistence
- Similarity Query

Similarity Module 不負責：

- Exact Duplicate Detection
- SHA-256 Duplicate Rule
- Photo Quality Analysis
- Recommendation Ranking
- Metadata Parsing
- File Rename
- Upload
- Processing Queue
- SignalR

以上功能由其他 Module 負責。

---

# 1.3 Module Boundary

## Input

Similarity Module 可以取得：

```text
ImageId
Image Stream
Existing PerceptualHash
Similarity Threshold
```

圖片來源必須透過：

```text
IFileStorageService
```

取得。

Client 不得直接指定任意：

```text
File System Path
Storage Path
```

---

## Processing

```text
Image
 ↓
Decode
 ↓
Resize
 ↓
Grayscale
 ↓
Generate pHash
 ↓
Find Candidate Images
 ↓
Calculate Hamming Distance
 ↓
Calculate Similarity Score
 ↓
Apply Threshold
 ↓
Create / Update VISUAL Group
```

---

## Output

Similarity Module 可以產生：

```text
PerceptualHash
Similarity Result
VISUAL Group
Group Members
Processing Result
```

Processing Log 由系統 Processing / Monitoring 機制記錄。

---

# 1.4 Module Dependencies

Similarity Module 可以依賴 Application Contract，例如：

```text
IFileStorageService
IImageRepository
IImageAnalysisRepository
IDuplicateGroupRepository
IPerceptualHashService
```

Similarity Module 不應直接依賴：

```text
Google Cloud Storage SDK
EF Core DbContext
Concrete Image Library
Controller
SignalR Hub
```

Infrastructure Implementation 必須透過 Interface 注入。

---

# 1.5 Processing Workflow

Similarity Module 可由：

```text
Analysis Workflow
Full Workflow
```

呼叫。

主要流程：

```text
Processing Job
      ↓
Similarity Analysis
      ↓
Get Image
      ↓
Generate / Read pHash
      ↓
Find Candidates
      ↓
Hamming Distance
      ↓
Similarity Score
      ↓
Threshold
      ↓
Similar?
 ┌────┴────┐
 No        Yes
 │          │
Finish   VISUAL Group
            ↓
         Persist
```

Similarity Module 不自行決定整個 Processing Workflow。

Workflow 執行順序由：

> **MOD-02 Processing Module**

負責協調。

---

# 1.6 Image Preprocessing

產生 pHash 前，圖片必須進行標準化處理：

```text
Image
 ↓
Decode
 ↓
Resize
 ↓
Grayscale
 ↓
pHash
```

目的：

> 降低圖片尺寸、解析度與色彩差異對 Perceptual Hash 的影響。

Implementation 應避免不必要地長時間保留完整 Image Buffer。

圖片處理完成後應釋放相關資源。

---

# 1.7 Perceptual Hash

Similarity Module 使用：

```text
pHash
```

將圖片視覺結構轉換為固定長度 Hash。

pHash 用於：

> **Visual Similarity**

而不是：

> **Binary Equality**

因此：

```text
Same SHA-256
```

屬於 MOD-05 Duplicate Module。

```text
Similar pHash
```

屬於 MOD-06 Similarity Module。

兩者不得混用。

---

# 1.8 Hamming Distance

兩個 pHash 透過：

> **Hamming Distance**

比較。

例如：

```text
Hash A
10110101

Hash B
10100101
   ↑
Difference
```

Distance 越小，代表兩個 Hash 越接近。

因此通常代表：

> 圖片視覺結構越接近。

---

# 1.9 Similarity Score

Similarity Module 可以將 Hamming Distance 轉換為：

```text
SimilarityScore
```

MVP Formula：

```text
SimilarityScore =
1 - (HammingDistance / HashBitLength)
```

範圍：

```text
0 <= SimilarityScore <= 1
```

例如：

```text
HashBitLength = 64
HammingDistance = 4

SimilarityScore
= 1 - (4 / 64)
= 0.9375
```

即：

```text
93.75%
```

Similarity Score 是：

> **數學上的影像相似度指標**

不是：

> **AI Model Confidence Probability**

---

# 1.10 Similarity Threshold

本模組原始規格同時存在：

1. Hamming Distance 的參考區間
2. Similarity Score 的實際 Threshold

本版將兩者分開描述，避免 Agent 混淆。

---

## 1.10.1 Hamming Distance Reference

MVP 初始參考值：

| Hamming Distance | Interpretation |
|---|---|
| 0 | Hash 相同 |
| 1–5 | 極高度相似 |
| 6–10 | 高度相似 |
| 11–15 | 可能相似 |
| > 15 | 通常不相似 |

以上屬於：

> **Initial Reference**

正式判定值仍必須透過測試圖片資料集進行校準。

---

## 1.10.2 Similarity Score Threshold

MVP 預設：

```text
DefaultThreshold = 0.85
```

判定：

```text
SimilarityScore >= Threshold
        ↓
Similar
```

```text
SimilarityScore < Threshold
        ↓
Not Similar
```

Threshold 必須符合：

```text
0 <= Threshold <= 1
```

Threshold 不得散落在程式碼中。

建議透過：

```text
SimilarityOptions
```

管理，例如：

```json
{
  "Similarity": {
    "DefaultThreshold": 0.85
  }
}
```

---

# 1.11 Candidate Search

Similarity Module 不應對所有圖片直接執行完整：

```text
O(N²)
```

比較。

例如：

```text
10,000 Images
```

完整兩兩比較約需要：

```text
10,000 × 9,999 / 2
```

次比較。

因此 MVP 必須先建立：

> **Candidate Filtering**

再進行 Hamming Distance 比較。

Candidate 可以從：

```text
Images with available pHash
+
Compatible image characteristics
```

中產生。

但目前規格沒有進一步固定：

> **Compatible image characteristics 的完整條件**

因此 Agent 不得自行發明複雜 Candidate Rule。

若 Implementation 需要更具體 Candidate Strategy：

```text
Stop
 ↓
Report Missing Rule
 ↓
Propose Candidate Strategy
 ↓
Wait for Approval
```

Future 可以評估：

- Hash Bucketing
- LSH
- Approximate Nearest Neighbor
- Vector Search

以上不屬於 MVP。

---

# 1.12 VISUAL Group Rules

Similarity Module 使用：

```text
DuplicateGroup
```

表示圖片群組。

Visual Similarity Group 必須：

```text
GroupType = VISUAL
```

例如：

```text
VISUAL Group #20
│
├── Image 101
├── Image 205
└── Image 309
```

代表三張照片屬於同一個視覺相似群組。

---

## 1.12.1 Group Creation

當：

```text
SimilarityScore >= Threshold
```

系統可以建立或加入：

```text
VISUAL Group
```

---

## 1.12.2 Existing Group

如果已存在適合的 VISUAL Group：

```text
New Similar Image
        ↓
Existing VISUAL Group
        ↓
Add Member
```

不得無意義地重複建立 Group。

---

## 1.12.3 Group Membership

同一圖片不得重複加入相同 Group。

Database 應保證：

```text
UNIQUE(GroupId, ImageId)
```

---

## 1.12.4 Group Ownership Boundary

Similarity Module 只能建立或管理：

```text
GroupType = VISUAL
```

不得建立：

```text
EXACT
VERSION
```

`EXACT` 屬於 MOD-05 Duplicate Module。

其他 GroupType 必須依對應 Module Specification 處理。

---

# 1.13 Idempotency

Similarity Analysis 必須具備 Idempotent Design。

主要識別：

```text
ImageId
+
ProcessingStep
```

重新執行時：

```text
Check Existing Result
        ↓
Already Completed?
 ├── Yes → Skip / Reuse
 └── No  → Execute
```

必須避免：

- Duplicate pHash Result
- Duplicate VISUAL Group
- Duplicate Group Member

---

# 1.14 Concurrency

Similarity Module 必須考慮：

- Concurrent Workers
- Same Image
- Same pHash
- Same VISUAL Group
- Group Creation Race

例如兩個 Worker 同時發現：

```text
Image A
Image B
SimilarityScore = 0.93
```

不得最後產生不必要的：

```text
VISUAL Group #20
VISUAL Group #21
```

應透過：

```text
Database Constraint
+
Transaction
+
Idempotent Application Logic
```

維持資料一致性。

不得只依靠 Application Memory Lock。

---

# 1.15 Error Handling

## Image Decode Failure

如果圖片無法 Decode：

```text
Decode Failed
 ↓
Processing Failed
```

若確認為 Corrupted Image：

> 不應無限 Retry。

---

## pHash Failure

```text
pHash Calculation Failed
 ↓
Determine Error Type
 ↓
Retry or Failed
```

只有 Transient Failure 才進入 Retry。

---

## Storage Failure

```text
Storage Failure
 ↓
Retry
 ↓
Still Failed?
 ↓
Processing Failed
```

---

# 1.16 Retry

可以 Retry：

- Storage Timeout
- Temporary Storage Failure
- Temporary I/O Failure

MVP Retry：

```text
1 second
 ↓
2 seconds
 ↓
4 seconds
```

不 Retry：

- Invalid Image
- Corrupted Image
- Unsupported Format
- Invalid Configuration

Retry Policy 的最終協調責任仍屬：

> **MOD-02 Processing Module**

Similarity Module 必須正確分類 Error。

---

# 1.17 Performance

Similarity Module 不得：

> 一次載入整個 Batch 的所有圖片。

建議流程：

```text
Image
 ↓
Stream
 ↓
Decode
 ↓
Resize
 ↓
Grayscale
 ↓
pHash
 ↓
Dispose
```

必須考慮：

- Streaming
- Controlled Memory Usage
- Controlled Concurrency
- Bounded Queue

實際 Worker Concurrency 由 Processing Module 管理。

---

# 1.18 Security

Similarity Module 必須遵守：

- Storage Isolation
- Path Validation
- File Validation
- Input Validation

所有 Image 必須透過：

```text
IFileStorageService
```

取得。

API 不得接受任意本機路徑或 Cloud Storage Path 作為圖片存取入口。

---

# 1.19 Application Interfaces

Application Layer 至少定義：

```text
IPerceptualHashService
ISimilarityDetectionService
ISimilarityGroupService
```

---

## IPerceptualHashService

用途：

> 產生圖片 pHash。

概念：

```text
GeneratePHashAsync(Stream)
```

要求：

- Async
- Controlled Memory Usage
- 固定長度 pHash
- 不一次處理整個 Batch

---

## ISimilarityDetectionService

用途：

> 執行單張圖片的 Similarity Detection。

概念流程：

```text
Get Image
 ↓
Generate / Load pHash
 ↓
Find Candidates
 ↓
Calculate Hamming Distance
 ↓
Calculate Similarity Score
 ↓
Apply Threshold
 ↓
Create / Update VISUAL Group
```

---

## ISimilarityGroupService

負責：

- Create VISUAL Group
- Add Member
- Get Group
- Get Group Members

不得負責 EXACT Group。

---

# 1.20 Processing Steps

Similarity Module 使用的主要 Processing Step：

```text
PHASH_GENERATION
VISUAL_SIMILARITY
```

Processing Result 應能讓系統記錄：

- ImageId
- ProcessingStep
- Status
- DurationMs
- ErrorCode
- RetryCount
- TraceId

---

# 1.21 Future Extension

MVP：

```text
pHash
 ↓
Hamming Distance
 ↓
Visual Similarity
```

Future 可以考慮：

- dHash
- aHash
- Multiple Hash Strategy

更遠期才考慮：

```text
CLIP
 ↓
Embedding
 ↓
Vector Database
 ↓
Semantic Similarity
```

MVP 不得提前加入：

- CLIP
- Embedding Service
- Vector Database
- Semantic Search

---

# 1.22 AI Agent Development Rules

AI Agent 實作 MOD-06 時應依序閱讀：

```text
AGENTS.md
 ↓
System-Level Specification
 ↓
MOD-06 Similarity Module Specification
 ↓
Database Schema
 ↓
Related Module Contracts
 ↓
Development Task
 ↓
Existing Code
```

Agent 必須遵守：

1. 不得自行修改 System Architecture。
2. 不得自行修改 Database Schema。
3. 不得自行修改 API Contract。
4. 不得自行修改 Similarity Formula。
5. 不得自行修改 Threshold Definition。
6. 不得將 Exact Duplicate Logic 放入 MOD-06。
7. 不得將 Quality 或 Recommendation Logic 放入 MOD-06。
8. 不得自行加入 CLIP、Vector DB 或其他 Future Scope。
9. 不得自行新增第三方 Package。
10. 不得因實作方便直接依賴 DbContext、GCS SDK 或 Controller。

若規格、Database、API 或 Existing Code 發生衝突：

```text
Stop
 ↓
Report Conflict
 ↓
Explain Impact
 ↓
Propose Change
 ↓
Wait for Approval
```

若規格缺少實作所必要的 Business Rule，也使用相同流程。

Agent 不得自行補規格。

---

# 2. 資料表

## 2.1 Database Boundary

Similarity Module 使用既有資料表：

```text
Images
ImageAnalysis
DuplicateGroups
DuplicateGroupMembers
```

本模組不因 Similarity 功能自行新增新的資料表。

實際 SQL Server：

- Data Type
- Nullability
- PK
- FK
- Index
- Constraint

以正式 Database Schema Specification 為準。

---

# 2.2 Images

Similarity Module 主要讀取 Image 基本資料與 Storage 關聯。

原始規格使用欄位包含：

```text
Id
FileSize
SHA256
```

其中 SHA256 不作為 Visual Similarity 的最終判定依據。

---

# 2.3 ImageAnalysis

Similarity Module 使用：

```text
ImageId
PerceptualHash
```

`PerceptualHash` 保存圖片已計算的 pHash。

如 Existing pHash 可重用，應避免不必要重新計算。

Index Strategy 必須以實際 SQL Server Schema 與 Query Pattern 為準。

不得由 Agent 未經核准自行新增 Index。

---

# 2.4 DuplicateGroups

Similarity Module 使用：

```text
Id
GroupType
CreatedAt
```

Visual Similarity Group：

```text
GroupType = VISUAL
```

---

# 2.5 DuplicateGroupMembers

Similarity Module 使用：

```text
Id
GroupId
ImageId
SimilarityScore
CreatedAt
```

`SimilarityScore`：

```text
0 <= SimilarityScore <= 1
```

---

# 2.6 Constraints

Similarity Module 所使用的 Schema 至少需要維持：

```text
PK
FK
UNIQUE
CHECK
INDEX where approved
```

Group Type 必須符合系統允許值，例如：

```text
EXACT
VISUAL
VERSION
```

Similarity Module 本身只使用：

```text
VISUAL
```

Group Membership 必須保證：

```text
UNIQUE(GroupId, ImageId)
```

避免同一 Image 重複加入相同 Group。

---

# 2.7 Database Rules

Database Section 只負責：

- Table
- Column
- Relationship
- PK / FK
- Unique Constraint
- Check Constraint
- Index

以下不放在 Database Schema：

- Similarity Workflow
- Retry
- Hamming Distance Rule
- Threshold Logic
- Group Creation Flow
- Processing State

以上屬於「規格」。

---

# 3. API

## 3.1 API Base

```text
/api/v1
```

---

# 3.2 Query Similar Images

## Request

```http
GET /api/v1/images/{imageId}/similar
```

用途：

> 查詢與指定圖片視覺上相似的圖片。

---

## Optional Threshold

```http
GET /api/v1/images/{imageId}/similar?threshold=0.85
```

Threshold 必須：

```text
0 <= threshold <= 1
```

只有：

```text
SimilarityScore >= threshold
```

的圖片列入結果。

未提供 threshold 時使用系統 DefaultThreshold。

---

# 3.3 Success Response

```http
200 OK
```

```json
{
  "success": true,
  "data": {
    "imageId": 101,
    "groupId": 20,
    "groupType": "VISUAL",
    "threshold": 0.85,
    "similarImages": [
      {
        "imageId": 205,
        "fileName": "IMG_002.jpg",
        "similarityScore": 0.94
      },
      {
        "imageId": 309,
        "fileName": "IMG_003.jpg",
        "similarityScore": 0.89
      }
    ]
  }
}
```

---

# 3.4 No Similar Images

如果分析已完成，但沒有符合 Threshold 的圖片：

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
    "threshold": 0.85,
    "similarImages": []
  }
}
```

沒有相似圖片：

> **不是 Error。**

---

# 3.5 Error Response

## Image Not Found

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

---

## Similarity Analysis Not Ready

```http
409 Conflict
```

```json
{
  "success": false,
  "error": {
    "code": "SIMILARITY_ANALYSIS_NOT_READY",
    "message": "Similarity analysis is not completed.",
    "traceId": "00-abc123"
  }
}
```

---

## Invalid Threshold

例如：

```text
threshold = 1.5
```

回傳：

```http
400 Bad Request
```

```json
{
  "success": false,
  "error": {
    "code": "INVALID_THRESHOLD",
    "message": "Threshold must be between 0 and 1.",
    "traceId": "00-abc123"
  }
}
```

---

## Internal Error

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

不得回傳：

- Exception
- StackTrace
- Internal File Path
- Database Error Detail
- Connection String

---

# 3.6 API Contract Rule

Agent 不得自行：

- 更改 `/similar`
- 更改 threshold 範圍
- 更改 Success Response Structure
- 更改 Error Code
- 更改 HTTP Status
- 將 Similarity Analysis 改成同步運算 API

若 API Contract 與實作需求發生衝突：

```text
Stop
 ↓
Report
 ↓
Explain Impact
 ↓
Propose Change
 ↓
Wait for Approval
```

---

# 4. 測試

## 4.1 Unit Tests

Unit Test 主要驗證 Module Business Rule。

---

## 4.1.1 pHash

驗證：

```text
Same Image
→
Same pHash
```

以及：

```text
Visually Similar Images
→
Low Hamming Distance
```

---

## 4.1.2 Hamming Distance

例如：

```text
Hash A = 10101010
Hash B = 10101010
```

Expected：

```text
Distance = 0
```

其他不同 Hash 必須驗證 Bit Difference 計算正確。

---

## 4.1.3 Similarity Score

Given：

```text
Distance = 0
```

Expected：

```text
SimilarityScore = 1
```

所有結果必須：

```text
0 <= SimilarityScore <= 1
```

---

## 4.1.4 Threshold

Given：

```text
SimilarityScore >= Threshold
```

Expected：

```text
Similar
```

Given：

```text
SimilarityScore < Threshold
```

Expected：

```text
Not Similar
```

Invalid Threshold：

```text
threshold < 0
threshold > 1
```

必須被拒絕。

---

## 4.1.5 VISUAL Group

第一次建立視覺相似關係：

```text
Similar Images
→
Create VISUAL Group
```

已有 Group：

```text
Existing VISUAL Group
+
New Similar Image
→
Add Member
```

不得重複建立 Member。

---

## 4.1.6 Idempotency

Given：

```text
Process Same Image Twice
```

Expected：

```text
No Duplicate pHash
No Duplicate Group Member
```

---

## 4.1.7 Concurrency

模擬：

```text
Worker A
Worker B
   ↓
Same Similarity Relationship
```

Expected：

> 最終只保留一致的 VISUAL Group State。

---

# 4.2 Integration Tests

Integration Test 必須驗證：

```text
Application
 ↓
EF Core
 ↓
Microsoft SQL Server
 ↓
Storage
```

至少包含：

```text
Create Image
 ↓
Get Image Stream
 ↓
Generate pHash
 ↓
Persist pHash
 ↓
Find Candidate
 ↓
Detect Similarity
 ↓
Create VISUAL Group
 ↓
Create Group Members
 ↓
Query Similar Images
```

必須驗證：

- PerceptualHash Persistence
- Group Persistence
- Group Member Constraint
- UNIQUE(GroupId, ImageId)
- Transaction Behavior
- Idempotency
- Concurrency
- API Query

Integration Test 應使用真正的 SQL Server Test Environment。

---

# 4.3 API Tests

至少測試：

### Success

```http
GET /api/v1/images/{imageId}/similar
```

Expected：

```text
200 OK
```

---

### Custom Threshold

```http
GET /api/v1/images/{imageId}/similar?threshold=0.85
```

Expected：

> 只回傳 SimilarityScore >= 0.85 的結果。

---

### No Similar Images

Expected：

```text
200 OK
similarImages = []
```

---

### Image Not Found

Expected：

```text
404
IMAGE_NOT_FOUND
```

---

### Analysis Not Ready

Expected：

```text
409
SIMILARITY_ANALYSIS_NOT_READY
```

---

### Invalid Threshold

Expected：

```text
400
INVALID_THRESHOLD
```

---

# 4.4 E2E Test

Playwright 驗證使用者完整流程：

```text
Upload Similar Images
       ↓
Start Processing
       ↓
Wait Processing Complete
       ↓
Open Image Detail
       ↓
Open Similar Images
       ↓
Verify Similarity Result
```

UI 至少應能顯示：

- Visual Similarity
- Similarity Group
- Similar Images
- Similarity Score

---

## No Similar Image E2E

```text
Upload Non-Similar Images
       ↓
Process
       ↓
Open Similar Images
```

Expected：

```text
No Similar Images Found
```

---

# 4.5 Reliability Tests

至少驗證：

- Temporary Storage Failure → Retry
- Storage Timeout → Retry
- Corrupted Image → No Infinite Retry
- Invalid Image → Failed
- Same Image Reprocessing → Idempotent
- Concurrent Group Creation → Consistent Result

---

# 4.6 Acceptance Criteria

### AC-01 — pHash

合法圖片完成 Similarity Analysis 後：

```text
PerceptualHash != NULL
```

---

### AC-02 — Hamming Distance

兩張圖片具有 pHash 時：

> Hamming Distance 必須正確計算。

---

### AC-03 — Similarity Score

Similarity Score 必須：

```text
0 <= SimilarityScore <= 1
```

---

### AC-04 — Similar Image

Given：

```text
SimilarityScore >= Threshold
```

Then：

```text
Image can belong to VISUAL Group
```

---

### AC-05 — Non-Similar Image

Given：

```text
SimilarityScore < Threshold
```

Then：

```text
Not Similar
```

---

### AC-06 — VISUAL Group

多張符合相似規則的圖片可以形成：

```text
One VISUAL Group
+
Multiple Group Members
```

且不得產生重複 Membership。

---

### AC-07 — API

Similarity Analysis 完成後：

```http
GET /api/v1/images/{imageId}/similar
```

必須回傳：

```text
200 OK
+
Similar Images
+
Similarity Score
```

---

### AC-08 — Threshold

指定：

```text
threshold = 0.85
```

只回傳：

```text
SimilarityScore >= 0.85
```

---

### AC-09 — Invalid Threshold

如果：

```text
threshold < 0
```

或：

```text
threshold > 1
```

必須回傳：

```text
400
INVALID_THRESHOLD
```

---

### AC-10 — Idempotency

同一 Image 重複執行：

```text
No Duplicate pHash
No Duplicate Group Member
```

---

# 4.7 Definition of Done

MOD-06 Similarity Module 完成時必須符合：

### Core Function

- [ ] Image Decode
- [ ] Resize
- [ ] Grayscale
- [ ] pHash
- [ ] Hamming Distance
- [ ] Similarity Score
- [ ] Threshold
- [ ] Candidate Filtering
- [ ] VISUAL Group

### Application

- [ ] IPerceptualHashService
- [ ] ISimilarityDetectionService
- [ ] ISimilarityGroupService
- [ ] Idempotency
- [ ] Concurrency Handling

### Database

- [ ] PerceptualHash Persistence
- [ ] VISUAL Group
- [ ] Group Member
- [ ] PK / FK
- [ ] UNIQUE(GroupId, ImageId)
- [ ] Required Constraints

### API

- [ ] Similarity Query API
- [ ] Threshold Parameter
- [ ] Success Response
- [ ] Empty Result
- [ ] Error Response
- [ ] HTTP Status Code

### Reliability

- [ ] Retryable Error Classification
- [ ] Non-Retryable Error Classification
- [ ] Storage Failure Handling
- [ ] No Infinite Retry

### Testing

- [ ] Unit Tests
- [ ] Integration Tests
- [ ] API Tests
- [ ] Playwright E2E Tests

### Observability Integration

- [ ] PHASH_GENERATION Result
- [ ] VISUAL_SIMILARITY Result
- [ ] TraceId
- [ ] Duration
- [ ] ErrorCode
- [ ] RetryCount

---

# Module Summary

MOD-06 Similarity Module 的核心工作可以簡化成：

```text
Image
 ↓
pHash
 ↓
Candidate Search
 ↓
Hamming Distance
 ↓
Similarity Score
 ↓
Threshold
 ↓
VISUAL Group
```

模組只回答：

> **「哪些圖片視覺上相似？」**

不回答：

```text
「是不是完全相同檔案？」 → MOD-05 Duplicate

「哪一張品質比較好？」 → MOD-07 Quality

「建議保留哪一張？」 → MOD-08 Recommendation

「檔案應該叫什麼名字？」 → MOD-04 Naming
```

AI Agent 實作時，必須維持上述責任邊界。

任何規格缺漏或跨模組衝突：

```text
Stop
 ↓
Report Conflict
 ↓
Explain Impact
 ↓
Propose Change
 ↓
Wait for Approval
```

不得自行擴充 Architecture 或 Business Rule。