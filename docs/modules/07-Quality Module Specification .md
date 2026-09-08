# Quality Module Specification

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
= 86.5
```

---

## 1.12 Quality Grade

Quality Grade 根據 Quality Score 產生：

| Score | Grade |
|---:|---|
| 0–49 | Poor |
| 50–69 | Fair |
| 70–84 | Good |
| 85–100 | Excellent |

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
Exists?
 ├─ Yes → Skip / Return Existing Result
 └─ No  → Execute Analysis
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
Job Retry / Failed
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

# 2. 資料表

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

同一張圖片目前只應存在一筆 Quality Analysis Result。

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
    "qualityScore": 90.55,
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
QualityScore = 86.5
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

Existing Image：

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