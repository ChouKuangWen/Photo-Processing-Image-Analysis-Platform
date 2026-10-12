# Similarity Module Specification

> **規格修訂草稿（2026-10-12）**：以 develop `8ff1dd983af2ffa68dfc93893f9cca61fadb2c90` 為基準。
> MOD-01 feature `a1bb29e1d7cf704a202adbe1c14741b6441575be` 僅為交接證據，未合併進 develop。
> 已核准與待決策內容分開記錄於 [規格審閱與決策紀錄](../reviews/Specification-Revision-Review.md)。
> F-01～F-08已核准並同步至最終契約章；同題舊待決文字由最終章取代。引擎/精確版本N-01與實作驗收仍未完成；不是TASK、實作或PASS授權。


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

MVP使用管理者確認的單資料集合完整snapshot，最多1000張/50000threshold edges/比較併發2，可設定且未效能驗證。合法集合可跨Batch；不得跨集合或默認有technical tenant隔離。1000以上拒絕，不截斷/NoMatch/Completed。
同版本pHash完整後才可發布single-link正式結果。上限內最多499500pair計算，只保存>=0.85 edges，未計算pair未知；低threshold query可唯讀有界計算，不寫結果。候選hash未齊的執行調度為N-02未決，不能直接占Worker無限等待或發partial snapshot。細節依最終F-03章。

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

## 與 MOD-02 的已核准執行契約

- MVP 單 Instance、多 Worker，MOD-02 以 SQL Server 原子 Claim 取得 Job 執行權；本模組不建立 Queue、Worker 或平行重試排程器。
- 每Run的每個transient Step由MOD-02管理四個邏輯槽位0～3，重試等待1／2／4秒；Shutdown Resume沿用槽位而另記invocation，本模組不疊加retry loop。
- host shutdown cancellation 向上傳播、不算 Retry；中斷由 MOD-02 處理，不能偽裝成 module failure 或 fallback。已提交成功結果需核對可重用性，不重做副作用。
- Job Status、Step Result、Log Status 不混用；失敗由 MOD-02 協調持久化 Job transition，本模組不直接改其 enum。
- D-05／D-06已核准MOD-02單一History writer、Run／checkpoint／invocation、提交後best-effort事件；Step從checkpoint映射、Timestamp為Log建立時間，schema見共用文件§3～§6。子模組回報Outcome，不重複寫History。
- 保留每次 attempt History，重要 operation 安全保留 JobId／ImageId／TraceId；沿用 ILogger<T>，不輸出 raw exception／SQL／path／secrets，不重複 MOD-01 failure event。
- 本模組的新增契約缺口與決策選項見 `docs/reviews/Specification-Revision-Review.md`；未決事項不能以一般實作細節自行補足。


### MOD-06 獨立審閱缺口（未核准）

- M06-01／Major：§1.6～§1.9 未固定 pHash 位元長度／preprocessing／DC coefficient／bit ordering／serialization／algorithm version。64-bit 是算例，不視為已核准算法。重用結果必須兼容同一版本，見 D-10。
- M06-02／Major：§1.11 已承認 Candidate Filter 條件未定；filter 不得無證據排除合規相似圖，候選範圍與晚到 image 補比對需決策。
- M06-03／Major：§1.12 A~B／B~C 不代表 A~C；single-link與versioned membership已核准；最終章定義group不代表全部pair與DB原子snapshot發布。
- M06-04／Major：§2.5 每 member 單一 SimilarityScore 未定相對對象，不能代表全部 pair。§3.2 允許 lower custom threshold，若只保存 default threshold group 則可能漏掉合法候選；read-only query 不能藉此重建群組。pair/reference schema 與 query strategy 待 D-10。
- M06-05／Major：§2.3 與 MOD-07 共用 ImageAnalysis，pHash row 存在不代表 Quality 完成；部分欄位／完成版本／並行更新不能互相覆蓋，見共用 Schema 待決策。

SimilarityScore 的公式保持不變，HammingDistance／HashBitLength 必須使用非整數除法；閾值 0～1 與 >= 邊界不變。
新增驗收：閾值 0／1、不同 hash version 不比較、A~B~C 非傳遞、低於 default 的 Query、晚到候選、多 Worker group merge、pHash／Quality 同 Image 並行寫入；需要先核准 D-10。

---

### D-05～D-07 同步限制

只有binding／版本／Context可確認且T1／T2已原子提交Running Log後才開始本模組。結果／History保存重試不重做模組操作，也不消耗business retry budget。
程序在T3前中斷時memory可能遺失；只讀計算需可重現Input／RuleVersion才能安全重算；已有SQL／Group／Storage副作用先核對，不因未保存checkpoint盲目重做。
Unknown副作用／Context先阻擋。PrimaryJob才更新Image／BatchStatus，secondary不改Uploadaggregate。F-01～F-08的結果/版本/receipt已同步最終章；Analysis/Full尚有N-01/N-02，實作與驗收未完成。

---

# 2. 資料表

## 共用 Schema 與交接基線

physical mapping 以 `docs/Database-Schema.md` 已確認部分為共同依據；尚未定案的欄位、索引與關係明列為待決策，不假設不存在的原始 Database-Schema.md 已提供答案。
沿用 MOD-01 Image.Id BIGINT、BatchId uniqueidentifier、DateTimeOffset／datetimeoffset(7) 與 Unicode mapping。
SQL Server timestamp／rowversion 不是日期時間；日期不得以 TIMESTAMP mapping 實作。
MOD-01 feature 尚未合併；TASK-11／12 最新驗收維持待確認，不能把後續模組未完成判作 MOD-01 Backend 缺陷。

---

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

# 最終核准契約 — F-03／F-04（文件審核版）

## 部署、候選、發布與查詢

每部署/DB/Storage僅管理者確認的單集合，非技術隔離；不支持多使用者／租戶，不新增Auth。候選可跨Batch，不能將Batch當owner/tenant或混入其他資料集合。上限是整個目標集合1000張，不藉缺hash或偷偷截斷繞過；同snapshot/Profile必須完整，缺hash/混版本NotReady。
暫定可設定限制：1000圖片、50000 edges、比較併發2，未效能驗證。只保存>=0.85 edges，canonical pair ImageLo<ImageHi、兩端ContentVersion/ProfileVersion/SnapshotId。single-link是達threshold edges的connected components；同群不代表每pair近似，更不是可刪證據。membership/version持久化，歷史snapshot不默改。
候選/edge完整核對後T3原子發布正式snapshot；超限409/SIMILARITY_CAPACITY_EXCEEDED，Permanent Step/Run/Job Failed，不Retry、不NoMatch、不Completed、不發布部分正式edges/membership/Recommendation。可以保留獨立成功hash與非正式staging；舊成功snapshot保留但明確版本與Stale/不是本次成功。容量失敗不能因調高配置而自動取得人工Retry資格；重新執行管理語意延後，不自行清理資料。
低threshold Query只讀既有同版本hash，實際完整計算最多999pair後分頁；threshold0..1，page>=1預設1，pageSize1..100預設50，score DESC/ImageId ASC，同request items/count一致。返回snapshotId/profileVersion/coverage/totalMatched；只有完整比較成功才coverage=Complete。跨page不承諾snapshot穩定。不補hash、不解碼、不寫Job/History/Pair/Group。
2秒可設定Query deadline包含DB讀與計算；共享比較gate2、無無界等待，資源滿503/SIMILARITY_QUERY_BUSY，逾時503/SIMILARITY_QUERY_TIMEOUT，缺hash/混版本409/SIMILARITY_NOT_READY，容量409。取消只結束read，deadline必須傳入DB/計算。單張有有效hash則NoMatch；無hash是NotReady。沒有已存pair可由此次有界hash計算得分，但未計算前不承諾已有可讀score。

## phash-dct64-v1 完整数值定義

本profile是待實作與驗收的規格，不是已證實bit一致或準確率能力。
解碼器／ICC引擎及精確版本目前沒有已核准baseline：feature .csproj僅EF/Logging，MOD-07列ImageSharp/OpenCvSharp候選非決策。必須Architecture核准EngineId/PackageVersion、native dependency version（若有）、RGBA輸出約定與ICC版本後才可凍結ProfileManifest；不能用latest/default填值或自行引入package。這是剩餘必要引擎決策N-01，阻擋Analysis/Full profile freeze，不阻擋核心規劃。
profile適用已核准JPEG/PNG；異常/不支持格式Permanent failure，不能產生Completed hash。decode輸出unpremultiplied RGBA8，沒有ICC假定sRGB；合法embedded ICC轉sRGB，relative colorimetric、black-point compensation off；非法ICC永久失敗。色彩引擎必須記manifest並用golden檢查，不以平台default代替。合法Orientation1..8對decoded pixels套一次（含mirror），沒有則identity、非法identity/Warning；原檔不變。
白底混合每channel C'=floor((a*C+(255-a)*255+127)/255)，a與C皆0..255。gray Y=floor((299R'+587G'+114B'+500)/1000)，範圍0..255。
resize32x32：每destination cell对应source面積區間[i*W/32,(i+1)*W/32)×[j*H/32,(j+1)*H/32)；把邊界以32倍整數座標表示，權重為與source pixel cell相交的整數面積，sum(Y*weight)/sum(weight)做half away rounding。source小於32同樣面積重採樣，不用平台interpolation。alpha混合/gray先做，再resize。
DCT-II basis B[u,x]=roundAway(65536*alpha(u)*cos(pi*(2x+1)*u/64))，alpha(0)=sqrt(1/32)、其餘sqrt(2/32)。由單一reference流程高精度離線生成32x32固定表，table/checksum隨Manifest凍結；不得每平台動態cos。C[u,v]=sum_y(sum_x(Y[x,y]*B[u,x])*B[v,y])，不除Q32（全係數同scale），x/y固定升序、checked Int64；溢出是失敗不是wrap。固定表數值/checksum與golden bytes屬後續實作產物，未產生前不能聲稱profile已發布。
zigzag按s=u+v由0..62；s偶數u降序、奇數u升序，在合法0..31範圍列出(u,v)，跳過(0,0)，取前64。排序係數d[0..63]，以2*C>d[31]+d[32]判bit1，等號bit0，不需float median。第0係數為byte0的MSB；8byte big-endian／16字元lowercase hex。roundAway對負數對稱，精確tie遠離0；gray/resize為非負，使用整数half-up等价。Score=1-popcount(xor)/64，內部精確分數，不先round再threshold；threshold0.85保持。

## Golden與holdout驗收（尚未執行）

golden manifest每fixture保存合法來源/用途、原檔SHA256、format/dimensions/EXIF/ICC、Engine/Profile/table版本、Orientation後RGBA checksum、gray32 checksum、64係數/8byte預期hash/error分類。fixture類別必含：黑/白/中灰平面、checkerboard、gradient、單像素與非整倍數尺寸、RGBA alpha0/128/255、Orientation1..8同視圖等價、missing/invalid EXIF、sRGB/missing/不同合法ICC/非法ICC、baseline/progressive/grayscale JPEG與PNG、截斷檔。視圖等價只能以受控無損fixture指定，JPEG重新編碼不強制hash全同。
expected values由凍結reference人工檢視並保存，不以被測實作當場生成自己的expected。reference/測試fixture檔案未在本輪建立；只有數學可推的預期在設計給出：bit數64、相等median bit0、同原byte距離0、不同Profile拒絕。golden實際bytes/output/checksum與支援OS/runtime matrix需後續TASK完成並驗收，禁止假PASS。
golden與matrix須100%bit一致、並發/重啟一致。independent holdout按原始圖來源分組，變體不跨校準/holdout；初步300圖與positive/hardnegative各100不視充分證據。0.85處observed precision>=95%、recall>=90%、FPR<=5%；95% Wilson precision下界>=90%、recall下界>=85%、FPR上界<=10%，報告樣本/分類/區間及bootstrap。未達須回報重新決策或補獨立樣本，不自行降門檻；調profile須新version/未使用holdout。
性能後續量測1000/499500pair/50000edges/Query999/併發2，測deadline/取消/記憶體/DBvolume/無部分發布。只承諾待驗證要求，不宣稱已效能PASS；本輪不建實作TASK。

## 最後一致性檢查發現：N-02候選pHash就緒的調度契約

正式VISUAL_SIMILARITY要求完整同版本候選snapshot，Workflow將其放在Quality前；其他Image仍可能在Pending或尚未pHash。F-06核准的T2-W dependency wait目前只限VERSION_ANALYSIS，沒有定義VISUAL候選未齊時的等待／釋放Worker／恢復／到期。不能使用partial hash集合冒充完整snapshot，也不能占滿所有Worker等待其他Job。
這是規格缺口而非已證實runtime deadlock。保持Analysis/Full完整實作准入BLOCKED，核心TASK規劃與Naming不因本缺口阻擋。推薦下一次針對N-02核准把VISUAL候選等待納入既有T2-W（讓出Worker、持久deadline、版本核對、無Running/不消耗Retry、ready後原子開始）；VISUAL等待budget與逾時政策需集中決定，不能擅自挪用Recommendation120秒或改Step順序。本輪不實作、不建立TASK、不改已核准checkpoint wait範圍。

### N-01引擎決策的完整資料要求

Architecture決策需一次確認Decoder EngineId/精確PackageVersion、ICC engine/version、native version/OS變體（若有）、package來源/鎖定及支援matrix（至少Linux x64與Windows x64、.NET10相同精確runtime patch，版本寫入manifest）。跨平台bit一致是待驗收要求，不能把只跑Linux說成跨平台PASS。推薦優先單一跨平台managed decoder/ICC provider以減少不同native實作，但ImageSharp與OpenCvSharp都只候選；本輪無已核准package/版本，不虛構pin。若無法達門檻須回報重新決策，不更換引擎/降threshold/改rounding默默通過。
