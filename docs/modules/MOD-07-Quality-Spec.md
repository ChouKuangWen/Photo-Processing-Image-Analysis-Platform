# Quality Module Specification

> **規格修訂草稿（2026-10-12）**：以 develop `8ff1dd983af2ffa68dfc93893f9cca61fadb2c90` 為基準。
> MOD-01 feature `a1bb29e1d7cf704a202adbe1c14741b6441575be` 僅為交接證據，未合併進 develop。
> 已核准與待決策內容分開記錄於 [規格審閱與決策紀錄](../reviews/Specification-Revision-Review.md)。
> F-01～F-08已核准並同步至最終契約章；同題舊待決文字由最終章取代。引擎/精確版本N-01與實作驗收仍未完成；不是TASK、實作或PASS授權。


**文件名稱：** Quality Module Specification  
**模組編號：** MOD-07  
**模組名稱：** Quality Analysis Module  
**對應系統：** Photo Processing & Intelligent Image Analysis Platform  
**架構：** Clean Architecture  
**技術：** ASP.NET Core / C# / EF Core  
**文件版本：** V2.0  
**文件用途：** AI Agent Development Specification  

---

# 1. 規格

## 1.1 模組目的

Quality Module 負責分析照片的基本影像品質，產生以下品質指標：

- Resolution Score
- Sharpness Score
- Exposure Score
- Metadata Score
- Compression Score
- Quality Score
- Quality Grade

本模組的目的：

> 提供照片品質的客觀指標與輔助判斷依據。

Quality Score 屬於產品內部評分模型：

- 不代表 AI 信心機率
- 不代表專業攝影品質判定
- 不代表照片的絕對視覺品質

MVP 採用傳統影像分析與 Rule-based Scoring。

---

## 1.2 模組責任

### 本模組負責

- 取得影像尺寸
- Resolution Analysis
- Sharpness Analysis
- Exposure Analysis
- Metadata Completeness Analysis
- Compression Analysis
- Quality Score 計算
- Quality Grade 判定
- 儲存 Quality Analysis Result
- 提供 Quality API
- Quality Analysis 錯誤處理
- Idempotency
- ProcessingLog

### 本模組不負責

- EXIF 原始解析
- GPS Reverse Geocoding
- SHA-256 Duplicate Detection
- pHash Visual Similarity
- Naming
- Version Recommendation
- AI Image Quality Model
- Face Recognition
- Object Detection
- Semantic Image Evaluation

上述功能由其他 Module 負責。

---

## 1.3 模組邊界

Quality Module 的輸入：

- ImageId
- 原始影像內容
- 已存在的 Metadata

輸出：

- Quality Analysis Result
- Quality Score
- Quality Grade
- ProcessingLog

本模組不得自行修改其他 Module 的資料或業務規則。

---

## 1.4 System Workflow 邊界

Quality Module 不負責定義整個系統的 Workflow。

系統何時執行：

```text
Upload
→ Processing
→ Metadata
→ Duplicate Detection
→ Similarity
→ Quality
→ Naming
→ Report
```

由 System-Level Specification 定義。

Quality Module 只定義自身被呼叫後的處理方式。

---

## 1.5 Quality Analysis 內部處理

模組內部處理概念：

```text
Image
 ↓
Validate Image
 ↓
Decode Image
 ↓
Resolution Analysis
 ↓
Sharpness Analysis
 ↓
Exposure Analysis
 ↓
Metadata Completeness
 ↓
Compression Analysis
 ↓
Quality Score
 ↓
Quality Grade
 ↓
Persist Result
 ↓
ProcessingLog
```

此流程只描述 Quality Module 的內部處理，不代表整個系統 Workflow。

---

## 1.6 Resolution Analysis

取得：

- Width
- Height
- PixelCount
- AspectRatio

計算：

```text
PixelCount = Width × Height
```

例如：

```text
Width  = 6000
Height = 4000

PixelCount = 24,000,000
```

Resolution Score 必須正規化為：

```text
0–100
```

---

## 1.7 Sharpness Analysis

MVP 採用：

```text
Laplacian Variance
```

處理方式：

```text
Image
 ↓
Grayscale
 ↓
Laplacian
 ↓
Variance
 ↓
Normalize
 ↓
Sharpness Score
```

Sharpness Score：

```text
0–100
```

Laplacian Variance 為原始分析值，不直接作為產品 Quality Score，必須先正規化。

---

## 1.8 Exposure Analysis

分析：

- Brightness
- Histogram
- Overexposure
- Underexposure

處理方式：

```text
Image
 ↓
Brightness Analysis
 ↓
Histogram
 ↓
Overexposure Detection
 ↓
Underexposure Detection
 ↓
Exposure Score
```

Exposure Score：

```text
0–100
```

過曝或欠曝比例過高時，應降低 Exposure Score。

---

## 1.9 Metadata Completeness

Metadata Score 用於表示照片 Metadata 的完整程度。

MVP 檢查：

- TakenAt
- CameraModel
- ISO
- Aperture
- ShutterSpeed
- GPS

所有項目存在：

```text
Metadata Score = 100
```

部分存在時，依完成比例計算。

Metadata Score：

```text
0–100
```

Metadata 的原始解析由 Metadata Module 負責，本模組只使用已取得的 Metadata。

---

## 1.10 Compression Analysis

Compression Score 用於評估照片是否可能存在明顯壓縮問題。

MVP 可參考：

- Image Format
- File Size
- Pixel Count
- Compression Characteristics

產生：

```text
Compression Score = 0–100
```

此分數屬於輔助指標，不代表影像實際視覺品質。

---

## 1.11 Quality Score

MVP 使用固定加權模型：

```text
Quality Score =
    Resolution × 25%
  + Sharpness × 30%
  + Exposure × 15%
  + Metadata × 10%
  + Compression × 20%
```

所有 Component Score 必須先正規化為：

```text
0–100
```

最終 Quality Score：

```text
0–100
```

必須符合：

```text
0 <= QualityScore <= 100
```

### 範例

```text
Resolution  = 80
Sharpness   = 90
Exposure    = 70
Metadata    = 100
Compression = 90
```

計算：

```text
80 × 0.25
+ 90 × 0.30
+ 70 × 0.15
+ 100 × 0.10
+ 90 × 0.20
= 85.5
```

---

## 1.12 Quality Grade

Quality Grade 根據 Quality Score 產生：

| Score | Grade |
|---:|---|
| [0,50) | Poor |
| [50,70) | Fair |
| [70,85) | Good |
| [85,100] | Excellent |

Grade 不得由 Agent 自行修改。

---

## 1.13 Application Layer

Application Layer 負責提供 Quality Use Case。

主要介面：

```csharp
IImageQualityService
```

主要方法：

```csharp
AnalyzeAsync(ImageAnalysisRequest request)
```

```csharp
GetQualityAsync(long imageId)
```

Application Layer 不應直接依賴 Image Processing Library。

---

## 1.14 Infrastructure Layer

Infrastructure Layer 負責實際影像分析。

可使用 Image Processing Library，例如：

- ImageSharp
- OpenCvSharp

實際 Library 由 Architecture / ADR 決定。

Infrastructure 實作 Application Layer 定義的介面，不讓 Application Layer 直接依賴第三方影像處理套件。

---

## 1.15 Processing Integration

Quality Module 可由 Processing Module 執行：

```text
Processing Worker
 ↓
QUALITY_ANALYSIS
 ↓
IImageQualityService
 ↓
Quality Analysis
 ↓
Persist Result
 ↓
ProcessingLog
```

Processing Step：

```text
QUALITY_ANALYSIS
```

Processing Module 負責 Job、Queue、Worker 與 Retry；Quality Module 負責實際品質分析。

---

## 1.16 Idempotency

Quality Analysis 必須具備 Idempotent Design。

識別：

```text
ImageId + QUALITY_ANALYSIS
```

再次執行時：

```text
Check Existing Result
 ↓
QUALITY_ANALYSIS 成功且結果可重用?
 ├─ Yes → Skip / Return Existing Result
 └─ No  → Execute Analysis（不能只檢查共享 row 是否存在）
```

目的：

> 同一張圖片的同一個 Quality Analysis Step 不應產生重複結果。

資料庫與 Application Logic 應共同確保此規則。

---

## 1.17 Failure Handling

### Image Decode Failure

圖片無法解析時：

```text
Decode Failed
 ↓
QUALITY_ANALYSIS = Failed
 ↓
ProcessingLog
 ↓
由 MOD-02 分類；Corrupted Image 不 Retry，temporary decode infrastructure error 才依核准政策 Retry
```

錯誤代碼：

```text
IMAGE_DECODE_FAILED
```

### Temporary Infrastructure Error

若錯誤屬於暫時性 Infrastructure Failure，可交由 Processing Module Retry。

Retry：

```text
1s
2s
4s
```

Quality Module 不應自行建立另一套與 Processing Module 衝突的 Retry Workflow。

---

## 1.18 Logging

Quality Analysis 必須產生 ProcessingLog。

至少記錄：

- TraceId
- ImageId
- ProcessingStep
- DurationMs
- Status
- ErrorCode
- RetryCount

例如：

```text
ImageId      = 205
Step         = QUALITY_ANALYSIS
DurationMs   = 183
Status       = Success
```

---

## 1.19 Agent Development Rules

AI Agent 實作本模組時，必須依照：

```text
System-Level Specification
        ↓
Database Schema
        ↓
Quality Module Specification
        ↓
Existing Code
        ↓
Implementation
        ↓
Unit Test
        ↓
Integration Test
        ↓
Playwright E2E
```

Agent 必須遵守：

1. 不修改 System-Level Architecture。
2. 不修改 Database Contract。
3. 不修改 API Contract。
4. 不修改 Quality Score Formula。
5. 不修改 Quality Grade Rules。
6. 不修改 Domain Rules。
7. 不修改其他 Module 的責任。
8. 不直接繞過 Application Layer 使用 Infrastructure。
9. 不為了讓測試通過而修改產品規則。
10. 新增功能時必須同步新增或更新測試。

### 發現規格衝突時

```text
Stop
 ↓
Report Conflict
 ↓
Explain Impact
 ↓
Propose Solution
 ↓
Wait for Human Decision
```

Agent 不得自行改變產品規則。

---

## 1.20 MVP Out of Scope

MVP 不包含：

- AI Image Quality Model
- Deep Learning Quality Model
- CLIP
- Vision LLM
- Face Recognition
- Object Detection
- Semantic Quality Evaluation
- Professional Photography Judgment

MVP 定位：

```text
Traditional Image Analysis
+
Rule-based Scoring
+
Decision Support
```

---

## 1.21 Future Extension

未來可加入：

```text
Quality Analysis
+
AI Quality Model
+
Vision Model
```

或：

```text
Quality Analysis
+
CLIP Embedding
+
Semantic Understanding
```

新增 AI 能力時，不應破壞既有：

```csharp
IImageQualityService
```

Application Contract。

---

## 與 MOD-02 的已核准執行契約

- MVP 單 Instance、多 Worker，MOD-02 以 SQL Server 原子 Claim 取得 Job 執行權；本模組不建立 Queue、Worker 或平行重試排程器。
- 每Run的每個transient Step由MOD-02管理四個邏輯槽位0～3，重試等待1／2／4秒；Shutdown Resume沿用槽位而另記invocation，本模組不疊加retry loop。
- host shutdown cancellation 向上傳播、不算 Retry；中斷由 MOD-02 處理，不能偽裝成 module failure 或 fallback。已提交成功結果需核對可重用性，不重做副作用。
- Job Status、Step Result、Log Status 不混用；失敗由 MOD-02 協調持久化 Job transition，本模組不直接改其 enum。
- D-05／D-06已核准MOD-02單一History writer、Run／checkpoint／invocation、提交後best-effort事件；Step從checkpoint映射、Timestamp為Log建立時間，schema見共用文件§3～§6。子模組回報Outcome，不重複寫History。
- 保留每次 attempt History，重要 operation 安全保留 JobId／ImageId／TraceId；沿用 ILogger<T>，不輸出 raw exception／SQL／path／secrets，不重複 MOD-01 failure event。
- 本模組的新增契約缺口與決策選項見 `docs/reviews/Specification-Revision-Review.md`；未決事項不能以一般實作細節自行補足。


### MOD-07 獨立審閱與未決評分規則

- M07-01／Minor（已修正）：§1.11／§4.4 原公式算例應為 85.5，不是 86.5；§3.3 DTO 的 weighted score 應為 90.95，不是 90.55。固定權重未變。
- M07-02／Major：§1.6～§1.10 缺 Resolution／Laplacian／Exposure／Compression normalization 公式與校準參數；0～100 邊界不能代替算法，不能由選用 Library 默認值決定產品規則，待 D-11。
- M07-03／Major：§1.12 的 0–49／50–69 等整數表示沒有定義 49.5／69.5／84.5 與 rounding／precision；維持整數邊界，連續小數政策待 D-11，不自行取整。
- M07-04／Major：MOD-03 會填 UnknownCamera／FileCreatedAt 等 fallback，Metadata completeness 的「存在」可能把缺 EXIF 算成完整；是否計原始來源／有效值／fallback provenance 待 D-08／D-11。
- M07-05／Major：§1.16 row exists 即 Skip 與 MOD-06 共享 ImageAnalysis 的 partial row 衝突。必須確認 QUALITY_ANALYSIS 成功且可重用，不以僅有 pHash 或 ImageAnalysis row 作成功證據；具體版本／mapping 待 D-10／D-11。

GET Existing Image 只有 Quality 結果完成時才為 200；尚未完成為既有 409／QUALITY_ANALYSIS_NOT_FOUND，Image 不存在為 404。
新增驗收：partial pHash-only row、Quality update 不覆寫 pHash、metadata fallback provenance、小數 grade 邊界、固定算例與重試分類；未決 normalization 不以 range-only tests 宣稱完成。

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

## 2.1 ImageAnalysis

Quality Analysis 結果儲存於：

```text
ImageAnalysis
```

主要欄位：

| 欄位 | 說明 |
|---|---|
| ImageId | 對應圖片 |
| Width | 圖片寬度 |
| Height | 圖片高度 |
| ResolutionScore | Resolution 評分 |
| SharpnessScore | Sharpness 評分 |
| ExposureScore | Exposure 評分 |
| MetadataScore | Metadata 完整度評分 |
| CompressionScore | Compression 評分 |
| QualityScore | 最終品質分數 |
| QualityGrade | 品質等級 |

---

## 2.2 Database Constraints

同一Image/ContentVersion/QualityRuleVersion只能一筆版本化Quality result；不同版本保留，current projection至多一筆。不以單Image唯一限制禁止歷史版本。

建議：

```text
UNIQUE(ImageId)
```

Score 欄位必須符合：

```text
0 <= ResolutionScore <= 100
0 <= SharpnessScore <= 100
0 <= ExposureScore <= 100
0 <= MetadataScore <= 100
0 <= CompressionScore <= 100
0 <= QualityScore <= 100
```

實際 SQL Type、Nullable、Index、FK 等資料庫細節，以 Database Schema 文件為準。

### 資料表職責

Database 文件只定義：

- Table
- Column
- Data Type
- Nullable
- Primary Key
- Foreign Key
- Unique
- Index
- Constraint

不在本節定義 Processing Workflow 或跨 Module 業務流程。

---

# 3. API

## 3.1 API Base

```text
/api/v1
```

---

## 3.2 Get Quality Result

### Endpoint

```http
GET /api/v1/images/{imageId}/quality
```

用途：

> 查詢指定圖片的 Quality Analysis Result。

Request 不需要 Body。

例如：

```http
GET /api/v1/images/205/quality
```

---

## 3.3 Success Response

HTTP：

```text
200 OK
```

Response：

```json
{
  "success": true,
  "data": {
    "imageId": 205,
    "width": 6000,
    "height": 4000,
    "resolutionScore": 95.0,
    "sharpnessScore": 88.5,
    "exposureScore": 91.0,
    "metadataScore": 100.0,
    "compressionScore": 85.0,
    "qualityScore": 90.95,
    "grade": "Excellent"
  }
}
```

---

## 3.4 HTTP Status

| Status | 用途 |
|---:|---|
| 200 | 查詢成功 |
| 404 | Image 不存在 |
| 409 | Quality Analysis 尚未完成 |
| 500 | 系統內部錯誤 |

---

## 3.5 Error Response

統一格式：

```json
{
  "success": false,
  "error": {
    "code": "QUALITY_ANALYSIS_NOT_FOUND",
    "message": "Quality analysis result was not found.",
    "traceId": "00-abc123"
  }
}
```

禁止回傳：

- Exception
- StackTrace
- Database Error
- Internal Path
- Connection String

---

## 3.6 Error Codes

| Error Code | HTTP | 說明 |
|---|---:|---|
| IMAGE_NOT_FOUND | 404 | 圖片不存在 |
| QUALITY_ANALYSIS_NOT_FOUND | 409 | Quality Analysis 尚未完成 |
| IMAGE_DECODE_FAILED | 500 | 圖片無法解析 |
| QUALITY_ANALYSIS_FAILED | 500 | Quality Analysis 失敗 |
| INTERNAL_ERROR | 500 | 系統內部錯誤 |

---

# 4. 測試

## 4.1 Unit Test

Quality Module 必須建立 Unit Tests。

主要測試範圍：

- Resolution
- Sharpness
- Exposure
- Metadata
- Compression
- Quality Score
- Quality Grade
- Score Validation
- Error Handling
- Idempotency

---

## 4.2 Resolution Test

輸入：

```text
Width  = 6000
Height = 4000
```

預期：

```text
PixelCount = 24,000,000
```

---

## 4.3 Score Range Test

所有 Component Score 必須：

```text
>= 0
<= 100
```

測試：

```text
ResolutionScore
SharpnessScore
ExposureScore
MetadataScore
CompressionScore
QualityScore
```

---

## 4.4 Quality Score Test

固定輸入：

```text
Resolution  = 80
Sharpness   = 90
Exposure    = 70
Metadata    = 100
Compression = 90
```

預期：

```text
QualityScore = 85.5
```

---

## 4.5 Quality Grade Test

至少驗證邊界值：

```text
49  → Poor
50  → Fair
69  → Fair
70  → Good
84  → Good
85  → Excellent
100 → Excellent
```

---

## 4.6 Invalid Image Test

輸入損壞圖片：

```text
Corrupted Image
```

預期：

```text
IMAGE_DECODE_FAILED
```

並產生：

```text
ProcessingLog = Failed
```

---

## 4.7 Idempotency Test

相同：

```text
ImageId
+
QUALITY_ANALYSIS
```

重複執行時：

```text
No Duplicate Result
```

---

## 4.8 Integration Test

Integration Test 驗證：

```text
Quality Service
 ↓
Database
 ↓
ImageAnalysis
```

至少確認：

- Analysis Result 可以正確儲存
- ImageId Unique Constraint 生效
- Score 資料正確
- Quality Grade 正確
- 重複執行不會產生重複結果

---

## 4.9 API Test

驗證：

### 成功

```text
GET /api/v1/images/{imageId}/quality
→ 200
```

### Image 不存在

```text
→ 404
IMAGE_NOT_FOUND
```

### Analysis 尚未完成

```text
→ 409
QUALITY_ANALYSIS_NOT_FOUND
```

### 系統錯誤

```text
→ 500
INTERNAL_ERROR
```

並確認 Error Response 不包含：

- Exception
- StackTrace
- Database Error
- Internal Path
- Connection String

---

## 4.10 Playwright E2E Test

Playwright 驗證完整使用者流程：

```text
UI
 ↓
API
 ↓
Processing
 ↓
Quality Analysis
 ↓
Database
 ↓
UI
```

### Quality Result

前置：

```text
Image 已完成 Upload
Quality Analysis 已完成
```

操作：

```text
Open Image Detail
 ↓
Open Quality
```

驗證 UI 正確顯示：

- Resolution Score
- Sharpness Score
- Exposure Score
- Metadata Score
- Compression Score
- Quality Score
- Grade

---

## 4.11 Quality Score Display

驗證：

```text
Quality Score = 0–100
```

且：

```text
Grade
```

必須與 Quality Score 對應。

---

## 4.12 Quality Analysis Failure

建立損壞圖片：

```text
Corrupted Image
```

上傳後：

```text
Processing
 ↓
Quality Analysis Failed
```

UI 必須：

```text
顯示 Analysis Failed
```

且不得顯示不存在的 Quality Score。

---

## 4.13 API Error Display

當 API 回傳：

```text
QUALITY_ANALYSIS_NOT_FOUND
```

UI 應顯示：

```text
Quality analysis is not available yet.
```

而不是直接顯示：

```text
500 Internal Server Error
```

---

## 4.14 Acceptance Criteria

### AC-01 Resolution

Given：

```text
Valid Image
```

When：

```text
Quality Analysis
```

Then：

```text
Width
Height
PixelCount
```

必須正確取得。

---

### AC-02 Sharpness

Valid Image 完成 Sharpness Analysis 後：

```text
0 <= SharpnessScore <= 100
```

---

### AC-03 Exposure

Valid Image 完成 Exposure Analysis 後：

```text
0 <= ExposureScore <= 100
```

---

### AC-04 Metadata

Metadata Analysis 完成後：

```text
MetadataScore
```

必須正確反映 Metadata 完整程度。

---

### AC-05 Quality Score

所有 Component Scores 完成後：

```text
0 <= QualityScore <= 100
```

並依照既定 Formula 計算。

---

### AC-06 Grade

Quality Grade 必須符合：

```text
0–49     → Poor
50–69    → Fair
70–84    → Good
85–100   → Excellent
```

---

### AC-07 API

Existing Image 且 Quality Analysis 已完成：

```http
GET /api/v1/images/{imageId}/quality
```

應回傳：

```text
HTTP 200
```

並包含完整 Quality Analysis。

---

### AC-08 Image Not Found

Invalid ImageId：

```text
HTTP 404
IMAGE_NOT_FOUND
```

---

### AC-09 Decode Failure

Corrupted Image：

```text
Status = Failed
```

並產生：

```text
ProcessingLog
```

---

### AC-10 Idempotency

同一：

```text
ImageId + QUALITY_ANALYSIS
```

重複執行不得產生重複 Analysis Result。

---

### AC-11 Playwright

Quality Analysis Completed 後，使用者開啟 Image Detail：

> Quality Result 必須正確顯示於 UI。

---

## 4.15 Definition of Done

Quality Module 完成前必須確認：

- [ ] Resolution Analysis
- [ ] Sharpness Analysis
- [ ] Exposure Analysis
- [ ] Metadata Completeness
- [ ] Compression Analysis
- [ ] Quality Score
- [ ] Quality Grade
- [ ] Database Persistence
- [ ] API Endpoint
- [ ] API Response
- [ ] API Error Handling
- [ ] Processing Integration
- [ ] Idempotency
- [ ] Retry Handling
- [ ] ProcessingLog
- [ ] Unit Tests
- [ ] Integration Tests
- [ ] Playwright E2E Tests
- [ ] Acceptance Criteria 全部通過
- [ ] Code Review 完成
- [ ] Build 成功
- [ ] All Tests Passed

---

# 5. Agent Implementation Rule

> 本節屬於開發指引，不屬於模組業務規格。

AI Agent 實作 Quality Module 時，應遵循：

```text
Read
 ↓
System-Level Specification
 ↓
Database Schema
 ↓
Quality Module Specification
 ↓
Existing Code
 ↓
Implement
 ↓
Unit Test
 ↓
Integration Test
 ↓
Playwright E2E
 ↓
Review
```

如果 Agent 發現：

```text
System Spec
≠
Module Spec
```

或：

```text
Database Schema
≠
Module Spec
```

不得自行選擇其中一方修改。

應：

```text
Stop
 ↓
Report Conflict
 ↓
Explain Impact
 ↓
Propose Change
 ↓
Wait for Human Approval
```

**Agent 的職責是依照規格實作，而不是自行重新定義產品規則。**

---

# 6. Module Definition of Done

本模組視為完成，必須同時符合：

```text
Functionality
    +
Boundary
    +
Database
    +
API
    +
Processing Integration
    +
Idempotency
    +
Error Handling
    +
Tests
    +
Acceptance Criteria
    +
Build
```

全部完成後才算 Quality Module Done。

# B-1b-02 核准補充 — QUALITY Frozen Input

本章為人工核准的文件契約，優先補足既有 B-1b-01「QUALITY T2 binding 尚待定義」的同題描述（包括下方保留章節）；不修改既有品質公式／權重／錯誤碼，也不代表 Adapter、EF／SQL 已實作或 TASK-04 COMPLETE。

## 來源與綁定

processing_quality_inputs 每 Job 一組不可變輸入，保存 MetadataResultId、QualityRuleVersion、InputFingerprint；欄位與約束依 Database-Schema B-1b-02。原圖內容／長度／Workflow 由 B-1a 原圖綁定取得。
目前 Analysis／Full 都先經 GEO。QUALITY MetadataResultId 必須等於同 Job processing_geo_inputs.MetadataResultId，並與原 Job 核准 EXIF 結果鏈一致；不以相同 ImageId、最新 row 或可變 Image Projection 代替。EXIF 必須已提交且不可變，GEO Completion 與其他前置 Step 須符合 Workflow gate。
有效性、Orientation、provenance 由原 EXIF authority 讀取；GEO LocationName 不計入六項 Metadata 分數。缺來源／版本不符不等於六項皆缺，不猜分。

## QUALITY Canonical Input v1

下列欄位按 Ordinal 輸出，全部非 NULL，沿用既有 CanonicalInputSchema，不改 Public Signature。

| 順序 | 欄位 | Canonical 型別／來源 |
|---|---|---|
| 1 | contentLength | Integer／Int64，非負；原圖綁定的完整內容長度 |
| 2 | contentVersion | String，64字元小寫 SHA256；原圖綁定，核對 EXIF SourceSHA256 |
| 3 | imageId | Integer／Int64，正值；Job／EXIF 一致的 Image |
| 4 | metadataInputFingerprint | String，64字元小寫 SHA256；原不可變 EXIF |
| 5 | metadataResultId | Integer／Int64，正值；QUALITY binding，與 GEO 來源一致 |
| 6 | metadataRuleVersion | String，受控、非空、可解析的原 EXIF 版本 |
| 7 | ruleVersion | String，受控、非空、固定 QualityRuleVersion |
| 8 | schemaVersion | Integer，固定1，由 serializer 自動輸出，不建立 CanonicalField |
| 9 | step | String，固定 QUALITY_ANALYSIS |
| 10 | workflowVersion | String，原圖綁定／Run 的固定版本 |

版本沿既有50字元限制與精確比對，不改成最新設定。ContentLength 用於來源一致性，不新增品質算分因子，不重複加入 FileSize。
Fingerprint=SHA256(Canonical UTF-8 bytes)，64字元小寫 hex；ContentVersion 是原圖完整 bytes 的 SHA256，兩者不可混用。沿用 v1 無 BOM／無排版空白、固定 ASCII-only escaping、Ordinal、Invariant 數值及不做 Unicode Normalize／Case Fold 的規則。
不包含 GEO LocationName、整段 Metadata JSON、Runtime Timestamp、RunId／RunNo、AttemptNo／RetryOrdinal／ResumeNo、TraceId、DbContext、Stream、Storage Path、目前 Image Projection 或未核准 Profile Default。MetadataResultId 與來源版本定位不可變 provenance，不用任意 Dictionary／object Context。

## QualityRuleVersion 與 N-01

QualityRuleVersion 必須唯一解析到核准且不可原地變更的有效分析 Profile；Decoder、ICC、Orientation、Resize、GrayScale 與品質計算規則，必須有一致且可驗證的版本來源。
quality-rules-v1 的公式不代表完整可執行引擎／Profile 已批准。N-01 未解決時可定稿此持久契約，不可自行選引擎或宣稱真實 Adapter 可執行；原版本不可解析時 Fail-Closed。
未來若將 QualityRuleVersion 與 Decoder Profile 分開版本化，先重新審查 Canonical Schema 與 Quality Result 唯一鍵，不默默更換。同 bytes 的 Reader 保證與解碼／分析視圖是不同責任，均須後續交付。

## T2、恢復及結果重用

首次 T2 核對 Job／Run／Checkpoint、原圖綁定、已提交 EXIF、同 Job GEO 來源一致性及 Workflow 前置條件；固定來源／規則／fingerprint，與 Running Invocation 同一短 SQL transaction 提交或回滾。Commit 確認後才執行；不跨完整 Storage I/O 或品質分析。
已有 binding 時，Resume 沿原 Run，ManualRetry 新 Run 沿同 Job 原業務輸入並重新核對 fingerprint，不換最新來源。原 Run 從未開始 QUALITY 時，不虛構不存在的 MetadataResultId，也不直接選最新 QualityRuleVersion；必須依原 Workflow 已核准且可重建的版本選擇規則。若持久版本依據不足，該首次具體化／跨 Run 路徑保持 Implementation Blocker，不用執行當下設定補洞。
Timeout／Cancellation／失聯不證明舊程序已停止。T3 outcome unknown 先核對原 Checkpoint、History、Quality Result、Owner 與提交證據，不盲目重送、建立第二份結果或重複 aggregate；不改 T1～T5、Retry 資格或上限。
保留既有 quality_results 唯一鍵(ImageId,ContentVersion,QualityRuleVersion,MetadataResultId)，它不等於完整 InputFingerprint。Completed 候選須核對來源、版本、原 Owner Checkpoint 及完整輸入證據；不同 WorkflowVersion 未經核准不能視為可重用同輸入。重用不更換原 owner；Binding 不授予寫入權，T3 仍須 owner＋rowversion 與 History／Checkpoint 原子更新。
Failed Result 不冒充完成，同 key Failed 無核准處置時 Fail-Closed，不新增 Result Attempt 表或擅自重開機制。

## Remaining Implementation Blockers

N-01 Decoder／ICC／Profile、N-02 VISUAL 等待、F-04-01 其他 Step、Reader 實際同 bytes、Missing／Mismatch 診斷及未關閉 invocation 處置仍保留。尚未凍結 QualityRuleVersion 的跨 Run 來源選擇、同 key Failed 受控處置亦未批准；本章不新增狀態、ErrorCode、API、套件或版本預設，不開始 B-1b-03／B-1c。

# B-1b-01 核准補充 — QUALITY 的 EXIF Authority

本節補充並優先於同題的共享 Metadata 來源描述（包含下方保留的「最終核准契約 — F-05」章），不改品質公式、API 或已核准錯誤規則。
QUALITY 以已提交且不可變的 image_metadata_results 作為 Metadata 有效性、Orientation、原始 provenance 的來源；quality_results.MetadataResultId 指向此 EXIF authority，不以 ImageAnalysis/current projection 或最新 Metadata 代替。
GEO LocationName 不屬 Metadata 六項有效性分數。GEO Completion 仍須符合既有 Workflow gate；Permanent Failure 不因品質計算不使用地名就允許跳過。來源缺失／版本不符不能當成六項皆缺。
首次 QUALITY T2 固定 MetadataResultId 與來源版本的持久綁定仍屬後續 B-1b 工作，本章不宣稱已完成。無新增 QUALITY Canonical Schema、Mapping 或實作；F-04-01、N-01／N-02 保留。

# 最終核准契約 — F-05（文件審核版）

quality-rules-v1；保留Resolution/Sharpness/Exposure/Metadata/Compression權重25/30/15/10/20。原始解碼/Orientation/gray遵循MOD-06 reference規則，Quality分析視圖長邊縮至<=1024、不放大，area resize同數值定義；尺寸至少能產生有效分析樣本。必需影像資料損壞、有效內部Laplacian樣本不足或JPEG table不可解析時Permanent QUALITY_INPUT_INCOMPLETE/INVALID_IMAGE，不猜分、不把未完成結果寫Completed。

| Component | 完整公式 |
|---|---|
| Resolution | 100*min(originalWidth*originalHeight/12000000,1)，尺寸先Orientation正常化，checked pixelCount |
| Sharpness | interior(x,y)的L=Yleft+Yright+Yup+Ydown-4Ycenter；population variance mean(L^2)-mean(L)^2；100*min(variance/1000,1)，boundary不算樣本 |
| Exposure | 100*(1-(n[Y<=5]+n[Y>=250])/N)，N為分析視圖所有有效像素，不double count |
| Metadata | 100*validOriginalFields/6；六項有效判定完整引用MOD-03最終章，不以fallback/UnknownCamera/UnknownLoc計分，GPS pair一項 |
| Compression JPEG | 找到luminance component實際使用的DQT；去DC後q=(sum63個AC量化值)/63；100*clamp((100-q)/99,0,1)；clamp(x,0,1)=min(max(x,0),1) |
| Compression PNG | 有效PNG無損格式本項=100；只代表此啟發式Compression分數，不代表圖整體品質 |

JPEG量化值依DQT precision還原無符號實際整數（有效非零，8/16bit依JPEG宣告），不靠decoder預估quality。grayscale取唯一component；YCbCr依luma component使用的table；同component跨scan若table重新定義且無唯一可核對表，不能猜平均、不能採最後DQT。CMYK/其他無可靠luma mapping或其他圖像格式不在本profile定義，Permanent UNSUPPORTED_QUALITY_FORMAT，不Retry；JPEG缺/零/損壞DQT為Permanent QUALITY_INPUT_INCOMPLETE。不得補Compression=0或100冒充成功。
可選Metadata缺失/非法欄位只不計那項，分母仍6；Resolved結果引用provenance來源，無Metadata結果/版本不符為依賴未就緒，不當全六項缺失。Job workflow先MOD03，permanent Metadata failure按workflow終止，不產生半成品Quality Completed。
ComponentDecimal，計算保留6位、final輸出2位round half away；weighted sum先按6位計，再用未兩位rounding總分Grade：[0,50)Poor、[50,70)Fair、[70,85)Good、[85,100]Excellent。rawmetrics/component/ContentVersion/MetadataResultReference/QualityRuleVersion與獨立Completed狀態持久化；shared ImageAnalysis若保留只是projection，不作版本result authority。
缺失/非法/不支援情況已由核准的不得猜值/永久錯誤規則推導，沒有新增format支援或替代分數。啟發式閾值與準確性未驗證；PNG100、量化表映射與resolution都不是專業攝影保證。
驗收0/100與小數grade、全fallback Metadata0、單GPS欄不計、不同DQT/DC不影響AC平均、q1得100/q100以上得0、PNG僅本項100、無DQT/不支持不Completed、sameimage並行不lost update。future實作量測，不本輪測試PASS。
