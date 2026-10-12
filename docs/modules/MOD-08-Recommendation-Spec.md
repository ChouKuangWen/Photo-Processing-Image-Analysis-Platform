# Recommendation Module Specification

> **規格修訂草稿（2026-10-12）**：以 develop `8ff1dd983af2ffa68dfc93893f9cca61fadb2c90` 為基準。
> MOD-01 feature `a1bb29e1d7cf704a202adbe1c14741b6441575be` 僅為交接證據，未合併進 develop。
> 已核准與待決策內容分開記錄於 [規格審閱與決策紀錄](../reviews/Specification-Revision-Review.md)。
> F-01～F-08已核准並同步至最終契約章；同題舊待決文字由最終章取代。引擎/精確版本N-01與實作驗收仍未完成；不是TASK、實作或PASS授權。


**文件名稱：** Recommendation Module Specification  
**模組編號：** MOD-08  
**模組名稱：** Recommendation Module  
**對應系統：** Photo Processing & Intelligent Image Analysis Platform  
**架構：** Clean Architecture  
**技術：** ASP.NET Core / C# / EF Core  
**文件用途：** AI Agent Development Specification  
**文件版本：** V2.0  
**文件狀態：** Development  

---

# 1. 規格

## 1.1 模組用途

Recommendation Module 負責根據既有的：

- Visual Similarity Result
- Quality Analysis Result
- Resolution Score
- Sharpness Score
- Exposure Score
- Metadata Completeness

對視覺相似的照片建立候選群組並進行排序，最後提供：

```text
Version Group
    ↓
Candidate Ranking
    ↓
Recommendation Score
    ↓
Recommended Image
    ↓
Recommendation Reasons
```

本模組的定位為：

> **Decision Support（決策輔助）**

而不是：

> **AI Automatic Decision（AI 自動決策）**

系統只能提供推薦結果與可解釋原因。

**是否保留、刪除或修改照片，最終由使用者決定。**

---

## 1.2 模組責任

### 本模組負責

1. 建立或管理 Version Group。
2. 取得 Visual Similarity 候選圖片。
3. 取得 Quality Analysis 結果。
4. 取得必要的 Metadata。
5. 計算 Recommendation Score。
6. Candidate Ranking。
7. 選出 Recommended Image。
8. 產生 Recommendation Reasons。
9. 計算 Rule-based Confidence。
10. 儲存 Recommendation Result。
11. 提供 Recommendation API。
12. 支援重新計算。

### 本模組不負責

以下功能屬於其他 Module：

- pHash 計算
- Hamming Distance 計算
- Visual Similarity 計算
- Quality Score 計算
- EXIF Parsing
- Reverse Geocoding
- SHA-256
- Image Upload
- File Storage
- File Rename
- File Delete
- Image Editing

**Recommendation Module 只能使用其他 Module 已產生的結果，不得重新實作上述分析邏輯。**

---

## 1.3 Module Boundary

Recommendation Module 的核心輸入與輸出：

```text
Visual Similarity Result
        +
Quality Result
        +
Metadata Result
        ↓
Candidate Selection
        ↓
Recommendation Score
        ↓
Ranking
        ↓
Recommendation
        ↓
Explanation
```

因此本模組的核心職責可以簡化為：

> **從視覺相似的照片中，根據既定規則找出較值得優先查看的照片。**

---

## 1.4 內部處理流程

以下僅描述 Recommendation Module 的**內部處理流程**，不是整個系統 Workflow。

```text
Load Visual Similarity Group
        ↓
Load Group Members
        ↓
Load Quality Analysis
        ↓
Load Metadata
        ↓
Build Candidates
        ↓
Calculate Recommendation Score
        ↓
Rank Candidates
        ↓
Select Recommended Image
        ↓
Generate Reasons
        ↓
Calculate Confidence
        ↓
Persist Recommendation Result
```

系統層級的 Upload → Processing → Analysis → Recommendation 執行順序，仍由 System-Level Specification 定義。

---

## 1.5 Version Group

Version Group 表示：

> **視覺上高度相似、可能屬於同一照片版本或連拍照片的圖片集合。**

例如：

```text
Version Group #100

├── IMG_001.jpg
├── IMG_002.jpg
├── IMG_003.jpg
└── IMG_004.jpg
```

Version Group 的候選來源主要為 Visual Similarity Group。

但：

> Visual Similarity 不代表一定是同一張照片的不同版本。

因此 Recommendation Module 只能將這些圖片視為：

> **Candidate Recommendation**

不得宣稱它們一定是同一照片的不同版本。

---

## 1.6 Candidate Selection

只有屬於同一 Version Group 的圖片才能進行 Recommendation Ranking。

例如：

```text
Version Group #10

Image A
Image B
Image C
Image D
```

系統取得：

```text
A → Score 78
B → Score 91
C → Score 84
D → Score 73
```

結果：

```text
Recommended Image = B
```

如果候選資料尚未完整，則不得進行最終推薦。

---

## 1.7 Recommendation Score

MVP 使用 **Rule-based Recommendation Model**。

公式固定為：

```text
Recommendation Score =
    Quality Score      × 50%
  + Similarity Score   × 20%
  + Resolution Score   × 10%
  + Sharpness Score    × 10%
  + Metadata Score     × 10%
```

所有輸入分數必須先正規化至：

```text
0–100
```

Recommendation Score 也必須介於：

```text
0–100
```

### 範例

```text
Quality Score       = 90
Similarity Score    = 95
Resolution Score    = 88
Sharpness Score     = 92
Metadata Score      = 100
```

計算：

```text
90 × 0.50
+ 95 × 0.20
+ 88 × 0.10
+ 92 × 0.10
+ 100 × 0.10
= 92.0
```

因此：

```text
Recommendation Score = 92.0
```

**此公式屬於產品規則，AI Agent 不得自行調整權重。**

---

## 1.8 Similarity Score

Similarity Score 由 Similarity Module 提供。

```text
pHash / Hamming Distance
        ↓
Similarity Module
        ↓
Similarity Score
        ↓
Recommendation Module
```

Recommendation Module：

- 不重新計算 pHash。
- 不重新計算 Hamming Distance。
- 不修改 Similarity Score。

只使用 Similarity Module 已產生的結果。

---

## 1.9 Candidate Ranking

候選圖片依：

```text
Recommendation Score DESC
```

排序。

例如：

```text
IMG_003.jpg   92.6
IMG_002.jpg   87.4
IMG_004.jpg   81.2
IMG_001.jpg   74.8
```

因此：

```text
Recommended Image = IMG_003.jpg
```

---

## 1.10 Tie Breaking

當 Recommendation Score 相同時，依以下順序決定排名：

```text
1. Recommendation Score
2. Quality Score
3. Resolution
4. Sharpness
5. Metadata Completeness
6. ImageId
```

ImageId 只作為最後的 deterministic ordering，確保相同輸入下每次結果一致。

---

## 1.11 Recommendation Reasons

Recommendation 必須提供可解釋原因。

例如：

```text
Higher quality score
Higher sharpness score
Higher resolution
Complete metadata
Better exposure
```

Reason 必須描述「為什麼這張照片排名較高」，不得產生沒有資料依據的自然語言推論。

### Reason 基本規則

| 條件 | Reason |
|---|---|
| QualityScore 明顯高於群組平均 | High quality score |
| SharpnessScore 高於群組平均 | Higher sharpness score |
| ResolutionScore 高於群組平均 | Higher resolution |
| MetadataScore 為高分 | Complete metadata |
| ExposureScore 高於群組平均 | Better exposure |

---

## 1.12 Recommendation Confidence

MVP 可以提供：

```text
Recommendation Confidence
```

但此欄位：

> **不是 AI Model Probability。**

MVP 採用：

> **Rule-based Confidence**

主要依照第一名與第二名的分數差距：

```text
Score Gap =
First Score - Second Score
```

規則：

```text
Score Gap >= 15
→ High

5 <= Score Gap < 15
→ Medium

Score Gap < 5
→ Low
```

例如：

```text
Image A = 92
Image B = 72

Gap = 20
→ High
```

```text
Image A = 86
Image B = 85

Gap = 1
→ Low
```

此 Confidence 屬於產品層級 Decision Support，不代表統計機率。

---

## 1.13 無法推薦的情況

### 單一圖片

如果 Version Group 只有一張圖片：

```text
Version Group
└── Image A
```

可以：

```text
Recommended Image = Image A
```

但 UI 應標示：

```text
No comparison available
```

因為沒有其他候選圖片可比較。

### Analysis 尚未完成

如果 Quality Analysis 尚未完成：

```text
RecommendationStatus = Pending
```

不得使用不完整資料產生最終推薦。

Quality Analysis 完成後，再重新計算 Recommendation。

---

## 1.14 Recommendation State

狀態：

```text
Pending
   ↓
Calculating
   ↓
Completed
```

失敗：

```text
Calculating
   ↓
Failed
```

重新計算：

```text
Failed
   ↓
Pending
```

---

## 1.15 Idempotency

Recommendation Calculation 必須具備 Idempotent Design。

邏輯識別：

```text
VersionGroupId
+
Analysis Version
```

如果相同群組、相同分析版本已完成：

```text
Recommendation Completed
```

再次執行應：

```text
Skip
```

除非明確要求：

```text
Force Recalculate
```

---

## 1.16 Recalculation

以下資料發生變化時，可以重新計算：

- Quality Score
- Similarity Score
- Metadata
- Recommendation Rule Version

流程：

```text
Existing Recommendation
        ↓
Invalidate
        ↓
Recalculate
        ↓
Persist New Result
```

Recommendation Module 不應直接修改來源分析結果。

---

## 1.17 Application Interface

Application Layer 定義：

```csharp
public interface IRecommendationService
{
    Task<RecommendationResult> GetRecommendationAsync(
        long imageId,
        CancellationToken cancellationToken);
}
```

Recommendation Calculator：

```csharp
public interface IRecommendationCalculator
{
    RecommendationResult Calculate(
        IReadOnlyCollection<RecommendationCandidate> candidates);
}
```

Calculator 應保持為可測試的純計算邏輯，不直接依賴 Database、EF Core 或外部服務。

---

## 1.18 Domain Rules

必須遵守：

```text
Recommendation Score 必須介於 0–100

Rank 必須唯一

Recommended Image 必須存在於 Version Group

Recommendation 不得修改原始圖片

Recommendation 不得自動刪除圖片

Recommendation 不得直接執行 Rename
```

---

## 1.19 Dependency Rules

Recommendation Module 可以依賴：

```text
Domain
Application
```

需要其他模組資料時，透過 Application Interface 取得：

```text
Similarity
Quality
Metadata
Repository
```

Application Layer 不得直接依賴 Infrastructure 技術細節。

例如不得在 Application / Domain 直接依賴：

```text
EF Core
SQL Connection
GCS SDK
Image Processing Library
```

Infrastructure 負責實作相關 Interface。

---

## 1.20 Performance

Recommendation 計算不得逐張圖片查詢 Database。

避免：

```text
N + 1 Query
```

建議：

```text
Load Version Group
        ↓
Batch Load Images
        ↓
Batch Load Analysis
        ↓
Calculate In Memory
        ↓
Persist Result
```

---

## 1.21 Logging

重要 Recommendation Operation 應記錄：

```text
TraceId
ImageId
VersionGroupId
RecommendationId
RecommendationScore
Confidence
DurationMs
Status
ErrorCode
```

Logging 用於：

- 問題追蹤
- Recommendation Result 追蹤
- 效能分析
- 錯誤診斷

不得記錄敏感資訊或內部連線資訊。

---

## 1.22 Agent Development Rules

AI Agent 開發本模組時，必須遵守以下順序：

```text
System-Level Specification
        ↓
Database Schema
        ↓
Recommendation Module Specification
        ↓
Existing Implementation
        ↓
Tests
```

### Agent 不得自行修改

- System-Level Workflow
- Database Contract
- API Contract
- Recommendation Score Formula
- Confidence Rule
- Domain Rules
- Module Boundary
- Clean Architecture Dependency Rule

### 發現規格衝突時

Agent 不應自行決定。

必須：

```text
Identify Conflict
      ↓
Explain Impact
      ↓
Propose Change
      ↓
Wait for Approval
```

### 開發原則

新增功能時：

```text
Specification
    ↓
Implementation
    ↓
Unit Test
    ↓
Integration Test
    ↓
E2E Test
```

不得為了讓 Test Pass 而修改既有產品規則。

---

## 1.23 Future Extension

MVP 使用 Rule-based Recommendation。

未來可以擴充：

```text
Rule-based
    ↓
Hybrid Recommendation
    ↓
Machine Learning
    ↓
Vision Model
```

例如：

```text
Image
 ↓
CLIP / Vision Model
 ↓
Feature Embedding
 ↓
Recommendation Model
 ↓
Recommendation
```

但 MVP 不實作。

未來若導入 AI，不得破壞目前的 `IRecommendationService` Contract，除非經過規格變更。

---

## 與 MOD-02 的已核准執行契約

- MVP 單 Instance、多 Worker，MOD-02 以 SQL Server 原子 Claim 取得 Job 執行權；本模組不建立 Queue、Worker 或平行重試排程器。
- 每Run的每個transient Step由MOD-02管理四個邏輯槽位0～3，重試等待1／2／4秒；Shutdown Resume沿用槽位而另記invocation，本模組不疊加retry loop。
- host shutdown cancellation 向上傳播、不算 Retry；中斷由 MOD-02 處理，不能偽裝成 module failure 或 fallback。已提交成功結果需核對可重用性，不重做副作用。
- Job Status、Step Result、Log Status 不混用；失敗由 MOD-02 協調持久化 Job transition，本模組不直接改其 enum。
- D-05／D-06已核准MOD-02單一History writer、Run／checkpoint／invocation、提交後best-effort事件；Step從checkpoint映射、Timestamp為Log建立時間，schema見共用文件§3～§6。子模組回報Outcome，不重複寫History。
- 保留每次 attempt History，重要 operation 安全保留 JobId／ImageId／TraceId；沿用 ILogger<T>，不輸出 raw exception／SQL／path／secrets，不重複 MOD-01 failure event。
- 本模組的新增契約缺口與決策選項見 `docs/reviews/Specification-Revision-Review.md`；未決事項不能以一般實作細節自行補足。


### MOD-08 獨立審閱與待決策

- M08-01／Minor（已修正）：§1.7／§4.1 同輸入公式結果為 92.0，不是 92.6。§3.5 範例 runner-up 87.4 的 gap 為 4.6，Confidence = Low；§1.12 互斥區間表達已澄清，權重／5、15 閾值未改。
- M08-02／Major：MOD-06 score 0～1，而 §1.7 要求 0～100；計算前只做尺度轉換 `normalizedSimilarity = similarityScore * 100`，不得把 source persistent score 改為百分制或重新算 pHash。group 的每 candidate source score 相對何者仍待 D-10／D-12。
- M08-03／Major：§1.2 管理 VersionGroup，但與 VISUAL group 的一對一／派生／membership／ownership 未定；single／empty／multiple group 查詢及單候選 Confidence 也未定，待 D-12。
- M08-04／Major：§1.14～§1.16 的 AnalysisVersion、RuleVersion、State、Invalidate／ForceRecalculate 沒有在 §2 Schema／接口完整呈現。shared group 的 Quality 尚未完成、永久 Failed、晚到 member 的喚醒／終止策略待D-12；D-05已定義Run／checkpoint，不代替GroupDependency政策。
- M08-05／Major：§1.10 tie-break 的 ImageId ASC／DESC 未定；§1.11「明顯高於平均」「高分」未有數值定義；§3.4／§3.5 對同 Endpoint 的不同 payload 是否合併／選項待 D-12。
- M08-06／Major：§1.2 要保存結果但 §2.2 為條件式持久化；保留條件文字作待決事項，不以本草稿批准新表／Migration。pending decision 必須先確定保存範圍與 Group+Version 的 concurrency protection。

跨圖片 Job 可能共同計算同一 Group；Image+Step Claim 不足以取代 Group+AnalysisVersion 的冪等與併發保護。
新增驗收：score 0～1 尺度轉換、沒有第二名、同分 stable ranking、group member 變動、Quality failure／pending、同版本 concurrent calculation／失效舊結果；具體預期先核准 D-12。

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

## 2.1 資料來源

Recommendation Module 主要讀取：

```text
Images
ImageAnalysis
DuplicateGroups
DuplicateGroupMembers
```

其中：

- `Images`：圖片基本資料。
- `ImageAnalysis`：Quality Analysis 結果。
- `DuplicateGroups`：Visual Similarity / Version Group 的群組資訊。
- `DuplicateGroupMembers`：群組與圖片的關聯。

Recommendation Module 不重複儲存上述分析資料。

---

## 2.2 Recommendations

若 MVP 需要永久保存 Recommendation Result，建立：

### `Recommendations`

| 欄位 | 型別 | 必填 | 說明 |
|---|---|---|---|
| Id | BIGINT | ✓ | Recommendation ID |
| GroupId | BIGINT | ✓ | Version Group ID |
| RecommendedImageId | BIGINT | ✓ | 推薦圖片 |
| RecommendationScore | DECIMAL | ✓ | 推薦分數 |
| Confidence | VARCHAR(20) | ✓ | Confidence 等級 |
| CreatedAt | DATETIMEOFFSET(7) | ✓ | 建立時間 |
| UpdatedAt | DATETIMEOFFSET(7) | ✓ | 更新時間 |

---

## 2.3 RecommendationCandidates

用於保存群組內候選圖片的排名。

### `RecommendationCandidates`

| 欄位 | 型別 | 必填 | 說明 |
|---|---|---|---|
| Id | BIGINT | ✓ | Candidate ID |
| RecommendationId | BIGINT | ✓ | Recommendation ID |
| ImageId | BIGINT | ✓ | 圖片 ID |
| Score | DECIMAL | ✓ | Recommendation Score |
| Rank | INT | ✓ | 排名 |
| CreatedAt | DATETIMEOFFSET(7) | ✓ | 建立時間 |

---

## 2.4 Database Constraints

資料表至少應考慮：

```text
PK
FK
INDEX
UNIQUE
CHECK
```

例如：

```text
Recommendations.GroupId
→ INDEX
```

Candidate：

```text
UNIQUE(
    RecommendationId,
    ImageId
)
```

避免同一 Recommendation Result 重複加入相同圖片。

Recommendation Score 應具備適當的 Database Constraint，確保資料符合：

```text
0 <= Score <= 100
```

實際資料型別、命名與索引策略應以系統的 Database Schema 為準。

---

## 2.5 資料表職責限制

本節只定義：

- Table
- Column
- Type
- Nullable
- PK
- FK
- Index
- Unique
- Constraint

以下內容不放入資料表規格：

- Processing Workflow
- Recommendation State Flow
- API 行為
- Score Calculation
- Retry Policy
- UI 行為

上述內容分別由本文件的「規格」與「API」負責。

---

# 3. API

## 3.1 API Base

```text
/api/v1
```

---

## 3.2 Get Recommendation

### Endpoint

```http
GET /api/v1/images/{imageId}/recommendation
```

用途：

> 查詢指定圖片所在 Version Group 的 Recommendation Result。

---

## 3.3 Request

例如：

```http
GET /api/v1/images/205/recommendation
```

不需要 Request Body。

---

## 3.4 Response

```json
{
  "success": true,
  "data": {
    "imageId": 205,
    "versionGroupId": 10,
    "recommendedImageId": 205,
    "recommendationScore": 92.0,
    "confidence": "High",
    "reasons": [
      "Higher quality score",
      "Higher sharpness score",
      "Higher resolution",
      "Complete metadata"
    ]
  }
}
```

---

## 3.5 Candidate Ranking Response

API 應提供群組內候選圖片排名。

```json
{
  "success": true,
  "data": {
    "versionGroupId": 10,
    "recommendedImageId": 205,
    "recommendationScore": 92.0,
    "confidence": "Low",
    "candidates": [
      {
        "imageId": 205,
        "score": 92.0,
        "rank": 1
      },
      {
        "imageId": 203,
        "score": 87.4,
        "rank": 2
      },
      {
        "imageId": 208,
        "score": 81.2,
        "rank": 3
      }
    ],
    "reasons": [
      "Higher quality score",
      "Higher sharpness score"
    ]
  }
}
```

---

## 3.6 Error Response

所有 API 錯誤使用統一格式：

```json
{
  "success": false,
  "error": {
    "code": "RECOMMENDATION_NOT_READY",
    "message": "Recommendation is not ready.",
    "traceId": "00-abc123"
  }
}
```

不得回傳：

```text
Exception
StackTrace
Internal Path
Connection String
Database Error Details
```

---

## 3.7 Error Codes

| Code | HTTP | 說明 |
|---|---:|---|
| IMAGE_NOT_FOUND | 404 | 找不到圖片 |
| VERSION_GROUP_NOT_FOUND | 404 | 找不到版本群組 |
| RECOMMENDATION_NOT_READY | 409 | Recommendation 尚未完成 |
| RECOMMENDATION_FAILED | 500 | Recommendation 計算失敗 |
| INTERNAL_ERROR | 500 | 系統內部錯誤 |

---

## 3.8 API Contract Rules

AI Agent 不得自行：

- 修改 Endpoint。
- 修改 HTTP Method。
- 修改 Response Structure。
- 修改 Error Code。
- 修改 HTTP Status。
- 新增破壞既有 Client 的 Required Field。

若 API 必須變更：

```text
Identify Impact
    ↓
Update Specification
    ↓
Approval
    ↓
Implementation
    ↓
Tests
```

---

# 4. 測試

## 4.1 Unit Test

Recommendation Calculator 必須測試：

- Recommendation Score
- Score Normalization
- Ranking
- Tie Breaking
- Confidence
- Reason Generation
- Empty Candidate
- Single Candidate
- Multiple Candidates
- Invalid Score
- Score Range

### Score 測試

必須驗證既定公式，例如：

```text
90 × 0.50
+ 95 × 0.20
+ 88 × 0.10
+ 92 × 0.10
+ 100 × 0.10
= 92.0
```

### Ranking 測試

確認：

```text
Higher Score → Higher Rank
```

### Tie Breaking 測試

確認：

```text
Score
→ Quality
→ Resolution
→ Sharpness
→ Metadata
→ ImageId
```

依序生效。

---

## 4.2 Confidence Test

測試：

```text
Gap >= 15 → High
5 <= Gap < 15 → Medium
Gap < 5   → Low
```

也必須測試：

```text
Single Candidate
```

此情況不得誤判為正常比較結果。

---

## 4.3 Reason Generation Test

測試系統能依據實際資料產生：

```text
High quality score
Higher sharpness score
Higher resolution
Complete metadata
Better exposure
```

不得產生資料中不存在的理由。

---

## 4.4 State Test

測試：

```text
Pending → Calculating
Calculating → Completed
Calculating → Failed
Failed → Pending
```

並確認：

- Completed 不會被無意義重複計算。
- Failed 可以重新計算。
- Analysis 未完成時 Recommendation 維持 Pending。

---

## 4.5 Idempotency Test

相同：

```text
VersionGroupId
+
Analysis Version
```

重複執行時：

```text
First execution
→ Calculate

Second execution
→ Skip
```

除非明確要求 Force Recalculate。

---

## 4.6 Integration Test

驗證：

```text
RecommendationService
        ↓
Repository
        ↓
EF Core
        ↓
Database
```

至少測試：

- Version Group 查詢
- Candidate 查詢
- Quality Analysis 查詢
- Recommendation Persistence
- Recommendation Retrieval
- Candidate Persistence
- Idempotency
- Recalculation

---

## 4.7 API Test

測試：

```text
GET /api/v1/images/{imageId}/recommendation
```

至少包含：

### 成功

```text
200 OK
```

### 圖片不存在

```text
404 IMAGE_NOT_FOUND
```

### Version Group 不存在

```text
404 VERSION_GROUP_NOT_FOUND
```

### Analysis 尚未完成

```text
409 RECOMMENDATION_NOT_READY
```

### Recommendation 計算失敗

```text
500 RECOMMENDATION_FAILED
```

確認所有錯誤均符合統一 Error Response。

---

## 4.8 Playwright E2E

使用 Playwright 驗證實際使用流程：

```text
Upload Images
      ↓
Processing Complete
      ↓
Open Similarity Group
      ↓
Open Recommendation
      ↓
Display Recommended Image
      ↓
Display Score
      ↓
Display Confidence
      ↓
Display Reasons
```

---

## 4.9 E2E Test Cases

### E2E-REC-01 — Recommendation

**Given**

存在至少兩張視覺相似照片。

**When**

使用者開啟 Recommendation。

**Then**

顯示 Recommended Image。

---

### E2E-REC-02 — Score

**Given**

Recommendation 已完成。

**When**

使用者查看推薦結果。

**Then**

顯示 Recommendation Score。

---

### E2E-REC-03 — Reasons

**Given**

Recommendation 已完成。

**When**

使用者查看推薦結果。

**Then**

顯示 Recommendation Reasons。

---

### E2E-REC-04 — Pending

**Given**

Quality Analysis 尚未完成。

**When**

使用者查看 Recommendation。

**Then**

顯示 Recommendation Pending。

---

### E2E-REC-05 — Ranking

**Given**

群組內存在多張候選圖片。

**When**

使用者查看 Recommendation。

**Then**

候選圖片依 Recommendation Score 正確排序。

---

## 4.10 Acceptance Criteria

- [ ] 可以取得 Visual Similarity Group。
- [ ] 可以建立或取得 Version Group。
- [ ] 可以取得 Version Group Candidates。
- [ ] 可以取得 Quality Analysis。
- [ ] 可以取得 Similarity Score。
- [ ] 可以計算 Recommendation Score。
- [ ] Recommendation Score 介於 0–100。
- [ ] 可以進行 Candidate Ranking。
- [ ] 可以選出 Recommended Image。
- [ ] 可以產生 Recommendation Reasons。
- [ ] 可以產生 Rule-based Confidence。
- [ ] Confidence 不宣稱為 AI 機率。
- [ ] 支援 Tie Breaking。
- [ ] 支援 Pending。
- [ ] 支援 Failed。
- [ ] 支援 Idempotency。
- [ ] 支援 Recalculation。
- [ ] 提供 REST API。
- [ ] API 使用統一 Error Response。
- [ ] Unit Test 完成。
- [ ] Integration Test 完成。
- [ ] API Test 完成。
- [ ] Playwright E2E Test 完成。
- [ ] 不自動刪除圖片。
- [ ] 不修改原始圖片。
- [ ] 不直接執行 Rename。
- [ ] Recommendation Result 可追蹤。

---

## 4.11 Definition of Done

Recommendation Module 完成前，必須確認：

```text
Specification
    ✓

Domain Rules
    ✓

Recommendation Calculator
    ✓

Recommendation Service
    ✓

Repository
    ✓

Database Migration
    ✓

REST API
    ✓

Unit Test
    ✓

Integration Test
    ✓

API Test
    ✓

Playwright E2E
    ✓

Error Handling
    ✓

Logging
    ✓

Idempotency
    ✓

Recalculation
    ✓

Code Review
    ✓
```

最終確認：

```text
Build
✓

All Tests
✓

Database Migration
✓

API Contract
✓

Recommendation Formula
✓

Module Boundary
✓
```

只有以上條件全部符合，Recommendation Module 才視為完成。

# 最終核准契約 — F-06（文件審核版）

MOD08對明確VISUAL membership snapshot計算，不新增平行VERSION group；VERSION_ANALYSIS兼容Step adapter。所有member Quality須同rule/version且Completed；永久Failed使本Step/Run/Job Permanent Failed，不把缺分當0、不無限Waiting。

## Dependency等待／执行互斥

120秒是可設定MVP預設，未效能驗證。第一次在VERSION_ANALYSIS invocation前查得未就緒時，短SQL transaction以DB UTC保存DependencyWaitStartedAt與DependencyWaitDeadlineAt=start+本次配置、DependencyWaitStatus=Waiting，checkpoint.Pending、Run.Active/Job.Processing；不新增Running Log、不消耗Retry槽位，不占Worker。只有此前Step已完成且本Step無Running才能建立wait。
依賴scanner每5秒檢查；先鎖Job/Run/checkpoint、核對deadline/依賴及沒有Running，再在同T2 transaction把Waiting→Resolved、checkpoint→Processing、建立唯一Running Log/Attempt identity，commit後才調adapter。依賴與deadline在同交易核對；DB now>=deadline先Expired並Failed，不因剛就緒而延長。到期管理交易關checkpoint/Run/Job Failed，DEPENDENCY_NOT_READY Permanent；沒有實際invocation不捏造一筆Running/Failed History，安全SystemLog/ProcessingFailed事件commit後通知。
StartedAt/DeadlineAt不因補調度、scanner、重啟、配置變動、queue重複通知重設；wall-clock downtime計入120秒。重啟待舊程序確認停止後，無Running的Waiting Run保持明確等待資格，不當舊執行者回收，不建立新Run/邏輯Retry；若到期立即Expired，未到期照原deadline。等待與執行不能並存：Waiting iff checkpoint.Pending/無Running；Resolved才能Processing/Running；transaction條件更新與唯一索引防多Worker同時wake。
Resolved/Expired與原start/deadline保留歷史。Waiting checkpoint不是任意Processing reclaim許可；unknown副作用仍阻擋。host shutdown不計Retry；管理性等待超時不等同Geo transient或Step invocation timeout。

## 分數、版本與API

每candidate對其餘snapshot members的真實同Profile pHash分數取均值（臨時計算，不保存所有pair）；same集合作完整計算，遵循1000/比較併發2，不能用缺edge當0。singleton推薦自己，SimilarityScore=100、confidence=null/NoRunnerUp；空snapshot Empty/無推薦、Step顯式Skipped。固定Score=Quality*.5+Similarity(0..1)*100*.2+Resolution*.1+Sharpness*.1+Metadata*.1。
排序Score/Quality/Resolution/Sharpness/Metadata DESC、ImageId ASC；confidence沿用gap>=15 High、>=5 Medium、其餘Low。reasons固定code、有資料依據：Quality>=mean+5，其餘sharpness/resolution/exposure>mean，Metadata=100才CompleteMetadata。
唯一(SnapshotId,QualityRuleVersion,RecommendationRuleVersion)持久化ranked result與inputs/resultVersion/Completed；snapshot merge/input版本改則Stale，不偷偷覆寫或自動重分析。既有GET入口包含snapshot/version/ranking/singleton/empty，不新增獨立rankingAPI。single-link同群不是每pair達標/可删保證。

## 最後一致性檢查發現：N-02候選pHash就緒的調度契約

正式VISUAL_SIMILARITY要求完整同版本候選snapshot，Workflow將其放在Quality前；其他Image仍可能在Pending或尚未pHash。F-06核准的T2-W dependency wait目前只限VERSION_ANALYSIS，沒有定義VISUAL候選未齊時的等待／釋放Worker／恢復／到期。不能使用partial hash集合冒充完整snapshot，也不能占滿所有Worker等待其他Job。
這是規格缺口而非已證實runtime deadlock。保持Analysis/Full完整實作准入BLOCKED，核心TASK規劃與Naming不因本缺口阻擋。推薦下一次針對N-02核准把VISUAL候選等待納入既有T2-W（讓出Worker、持久deadline、版本核對、無Running/不消耗Retry、ready後原子開始）；VISUAL等待budget與逾時政策需集中決定，不能擅自挪用Recommendation120秒或改Step順序。本輪不實作、不建立TASK、不改已核准checkpoint wait範圍。
