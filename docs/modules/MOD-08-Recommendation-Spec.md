# Recommendation Module Specification

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
= 92.6
```

因此：

```text
Recommendation Score = 92.6
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

Score Gap >= 5
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

# 2. 資料表

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
| CreatedAt | TIMESTAMP | ✓ | 建立時間 |
| UpdatedAt | TIMESTAMP | ✓ | 更新時間 |

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
| CreatedAt | TIMESTAMP | ✓ | 建立時間 |

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
    "recommendationScore": 92.6,
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
    "recommendationScore": 92.6,
    "confidence": "High",
    "candidates": [
      {
        "imageId": 205,
        "score": 92.6,
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
= 92.6
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
Gap >= 5  → Medium
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