# 10. Export Module Specification

**文件名稱：** Export Module Specification  
**模組編號：** MOD-10  
**模組名稱：** Export Module  
**文件版本：** V2.0  
**對應系統：** Photo Processing & Intelligent Image Analysis Platform  
**架構：** Clean Architecture  
**技術：** ASP.NET Core / C# / EF Core  
**文件用途：** AI Agent Development Specification  
**文件狀態：** Development

---

# 1. 規格

## 1.1 模組目的

Export Module 負責將系統中已存在的圖片、Metadata、Analysis、Naming 與 Processing 結果整理成可下載的 CSV 報表。

本模組的核心責任：

> **讀取既有資料 → 整理成 Export DTO → 轉換成 CSV → 提供下載。**

MVP 支援：

- CSV Export
- UTF-8 BOM
- Image Metadata 匯出
- Quality Analysis 匯出
- Processing Status 匯出
- Naming Result 匯出
- Duplicate / Similarity 結果匯出
- Batch / Status / Date Filter
- CSV Streaming / 大量資料處理

---

## 1.2 模組邊界

### 本模組負責

- Export Request
- Export Query
- Filter Validation
- Database Data Query
- Data Projection / Mapping
- Export DTO
- CSV Generation
- UTF-8 BOM
- CSV Escaping
- Null Handling
- HTTP File Download
- Export Error Handling

### 本模組不負責

- Image Upload
- EXIF Parsing
- GPS / Reverse Geocoding
- SHA-256 Calculation
- Duplicate Detection
- pHash / Similarity Detection
- Quality Analysis
- Naming Processing
- Processing Workflow
- 修改圖片
- 修改 Analysis Result
- 修改 Processing Status

Export Module 是**讀取與輸出模組**，不應成為其他模組資料的修改者。

---

## 1.3 模組內部流程

Export Module 內部處理流程：

```text
Export Request
      ↓
Validate Filter
      ↓
Query Existing Data
      ↓
Project to Export DTO
      ↓
Generate CSV
      ↓
Add UTF-8 BOM
      ↓
Return File Response
```

這是 Export Module 的**內部處理流程**，不代表系統層級 Workflow。

系統完整 Workflow 由 System-Level Specification 定義。

---

## 1.4 Export Data

MVP 匯出資料主要包含：

```text
Image Information
Metadata
Analysis Result
Naming Result
Processing Status
Duplicate / Similarity Result
```

主要欄位：

```text
ImageId
BatchId
OriginalFileName
NewFileName

TakenAt
CameraModel
ISO
ShutterSpeed
Aperture
Latitude
Longitude
LocationName

SHA256

QualityScore
SharpnessScore
ExposureScore
ResolutionScore
MetadataScore

Status
CreatedAt
```

實際可匯出欄位應以目前 Database Schema 與各 Module Contract 為準。

Agent 不得自行增加尚未定義的資料來源或欄位。

---

## 1.5 Export DTO

Application Layer 建立：

```text
ImageExportDto
```

用途：

> 將系統內部資料轉換成 Export 所需的資料模型。

範例：

```csharp
public sealed class ImageExportDto
{
    public long ImageId { get; init; }
    public Guid BatchId { get; init; }

    public string OriginalFileName { get; init; } = string.Empty;
    public string? NewFileName { get; init; }

    public DateTime? TakenAt { get; init; }
    public string? CameraModel { get; init; }
    public int? Iso { get; init; }
    public string? ShutterSpeed { get; init; }
    public string? Aperture { get; init; }

    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public string? LocationName { get; init; }

    public string? Sha256 { get; init; }

    public decimal? QualityScore { get; init; }
    public decimal? SharpnessScore { get; init; }
    public decimal? ExposureScore { get; init; }
    public decimal? ResolutionScore { get; init; }
    public decimal? MetadataScore { get; init; }

    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
```

Export DTO 不應被其他模組當作 Domain Entity 使用。

---

## 1.6 Export Service

Application Layer：

```csharp
public interface IExportService
{
    Task<ExportResult> ExportImagesAsync(
        ExportImagesRequest request,
        CancellationToken cancellationToken);
}
```

Export Service 負責：

```text
Validate Request
      ↓
Query Data
      ↓
Map Export DTO
      ↓
Generate CSV
      ↓
Return Export Result
```

Export Service 不負責：

- HTTP Response
- Controller Routing
- CSV 低階格式處理
- 修改資料庫資料

---

## 1.7 CSV Generator

建立：

```text
ICsvGenerator
```

介面：

```csharp
public interface ICsvGenerator
{
    byte[] Generate<T>(IEnumerable<T> data);
}
```

實作：

```text
CsvGenerator
```

責任：

```text
Export DTO
    ↓
CSV Header
    ↓
CSV Rows
    ↓
CSV Escaping
    ↓
UTF-8 BOM
    ↓
CSV Output
```

CSV Generator：

- 不查詢 Database
- 不依賴 Controller
- 不修改 Domain Entity
- 不處理 Business Rule

---

## 1.8 CSV Format

MVP CSV 必須使用：

```text
UTF-8 BOM
```

目的：

> 確保 Windows / Excel 開啟 CSV 時中文資料能正常顯示。

CSV 欄位順序必須固定。

例如：

```text
ImageId,BatchId,OriginalFileName,NewFileName,QualityScore,Status
1,xxx,IMG_001.jpg,Tokyo_001.jpg,85.5,Completed
2,xxx,IMG_002.jpg,Tokyo_002.jpg,72.3,Completed
```

---

## 1.9 CSV Escaping

CSV 欄位可能包含：

```text
,
"
newline
```

因此 Generator 必須正確處理 CSV Escape。

例如：

```text
Tokyo, Japan
```

應輸出：

```text
"Tokyo, Japan"
```

包含雙引號：

```text
Camera "Canon EOS R6"
```

應輸出：

```text
"Camera ""Canon EOS R6"""
```

避免產生無效 CSV。

---

## 1.10 Null Handling

資料庫中的 Nullable 欄位：

```text
TakenAt
CameraModel
GPS
QualityScore
NewFileName
```

若不存在資料，CSV 應輸出：

```text
空白欄位
```

例如：

```text
1,xxx,IMG_001.jpg,,2026-08-23,,,85.2,Completed
```

MVP 不自行產生：

```text
NULL
undefined
N/A
```

除非未來 Specification 明確定義。

---

## 1.11 Large Dataset

Export 不應無限制地將所有資料載入 Memory。

避免：

```text
Database
   ↓
ToList()
   ↓
全部資料進入 Memory
   ↓
Generate CSV
```

優先採用：

```text
Database
   ↓
Projection
   ↓
Async / Streaming
   ↓
CSV Writer
   ↓
HTTP Response
```

MVP 可使用：

```text
IAsyncEnumerable
```

或：

```text
Batch Query / Pagination
```

實際實作方式由資料量與技術設計決定。

核心要求：

> **不得因一次匯出大量資料而造成不必要的 Memory Pressure。**

---

## 1.12 Database Query

Export 應優先使用：

```text
Projection
+
Async Query
+
必要的 JOIN
```

避免 N+1 Query。

例如：

```csharp
Select(x => new ImageExportDto
{
    ImageId = x.Id,
    OriginalFileName = x.OriginalFileName,
    QualityScore = x.Analysis.QualityScore
});
```

Export 不應為了取得單筆資料而逐筆重新查詢 Database。

---

## 1.13 File Download

MVP 不需要將 CSV 永久儲存在 Storage。

處理方式：

```text
Generate CSV
      ↓
HTTP Response
      ↓
Browser Download
```

Response：

```text
Content-Type:
text/csv; charset=utf-8
```

並設定：

```text
Content-Disposition:
attachment
```

---

## 1.14 File Name

預設檔名：

```text
image-report.csv
```

指定 Batch 時：

```text
image-report-{batchId}.csv
```

檔名必須經過 Filename Sanitization。

目的：

> 防止 Path Traversal 與非法檔名。

Export 不得讓使用者直接控制任意 Server File Path。

---

## 1.15 Empty Result

若 Filter 沒有符合任何資料：

```text
HTTP 200
```

CSV 仍應正常產生：

```text
UTF-8 BOM
CSV Header
```

沒有 Data Rows。

「沒有資料」不是系統錯誤。

---

## 1.16 Storage Strategy

### MVP

```text
Export Request
      ↓
Generate CSV
      ↓
HTTP Download
```

不建立永久 Export File。

### Future

若未來資料量大幅增加，可以改為：

```text
Export Request
      ↓
Background Job
      ↓
CSV Generation
      ↓
Cloud Storage
      ↓
Download URL
```

此功能不屬於 MVP。

---

## 1.17 Error Handling

Export API 使用系統統一 Error Response。

MVP Error Code：

| Error Code | HTTP | 說明 |
|---|---:|---|
| INVALID_EXPORT_FILTER | 400 | 匯出條件錯誤 |
| BATCH_NOT_FOUND | 404 | Batch 不存在 |
| EXPORT_FAILED | 500 | 匯出處理失敗 |
| INTERNAL_ERROR | 500 | 未預期系統錯誤 |

不建議將底層：

```text
DATABASE_ERROR
```

直接暴露給 Client。

Database Exception 應由 Application / Global Exception Handler 轉換成統一的：

```text
INTERNAL_ERROR
```

API 不得暴露：

```text
Exception
StackTrace
SQL
Connection String
Internal Path
Database Details
```

---

## 1.18 Logging

Export 是讀取型操作。

MVP：

- 不建立 ProcessingLog
- 不建立 ExportLog
- 不建立 AuditLog

原因：

> Export 並非 Image Processing Step，而且目前系統沒有 User / Authentication，因此沒有 Audit Log 的必要。

Export 發生系統錯誤時，仍應遵循 System Logging Specification 記錄必要的 System Log。

若未來加入會員、權限或管理操作追蹤，再由 System-Level Specification 定義 Audit Log。

---

## 1.19 Security Rules

Export 必須：

- 驗證 Query Parameters
- 使用 Parameterized Query
- 透過 EF Core / Repository 查詢資料
- 防止 SQL Injection
- Filename Sanitization
- 不暴露 Internal Storage Path
- 不暴露 Database Exception
- 不回傳 Connection String
- 不回傳 Server File System Path

目前系統沒有 Authentication，因此 MVP 不實作：

```text
User Authentication
Authorization
Resource Ownership
```

未來加入 Authentication 後，Export 才需要加入相應的 Authorization 規則。

---

## 1.20 Module Dependencies

Export Module 依賴其他模組提供的**既有資料**：

```text
Export Module
      │
      ├── Image Data
      ├── Metadata Result
      ├── Quality Result
      ├── Naming Result
      ├── Duplicate / Similarity Result
      ├── Processing Status
      └── Database
```

Export Module：

- 可以讀取上述資料
- 不負責產生上述分析結果
- 不修改上述資料
- 不改變其他 Module 的 Domain Rules

---

## 1.21 Dependency Rules

Export Module 必須遵循 Clean Architecture。

### Application Layer

負責：

```text
Export Use Case
Export Request
Export DTO
Export Service
```

### Infrastructure Layer

負責：

```text
CSV Formatting
Encoding
Streaming Implementation
```

### API Layer

負責：

```text
HTTP Request
Request Binding
Application Service
HTTP File Response
```

Controller 不負責：

```text
Database Query
CSV Formatting
Business Logic
```

---

## 1.22 Application Structure

建議：

```text
Application
└── Export
    ├── DTO
    │   └── ImageExportDto.cs
    │
    ├── Requests
    │   └── ExportImagesRequest.cs
    │
    ├── Services
    │   ├── IExportService.cs
    │   └── ExportService.cs
    │
    └── Interfaces
        └── ICsvGenerator.cs
```

Infrastructure：

```text
Infrastructure
└── Export
    └── CsvGenerator.cs
```

API：

```text
API
└── Controllers
    └── ExportController.cs
```

實際專案目錄仍以現有 Architecture 為準。

---

## 1.23 Agent Development Rules

AI Agent 實作 Export Module 時，必須依序閱讀：

```text
1. System-Level Specification
        ↓
2. Database-Schema.md
        ↓
3. Related Module Specifications
        ↓
4. Export Module Specification
        ↓
5. Existing Project Architecture
        ↓
6. Existing Implementation
```

Agent 不得自行修改：

```text
System Architecture
Database Schema
Existing Module Contract
API Contract
Domain Rules
```

Agent 不得：

- 重新實作其他 Module 的分析邏輯
- 在 Export Module 重新解析 EXIF
- 在 Export Module 重新計算 Quality Score
- 在 Export Module 重新計算 SHA-256
- 在 Export Module 重新計算 Similarity
- 修改 Image / Analysis / Processing Status
- 為了通過測試而修改既有規格

若發現規格衝突：

```text
Stop
 ↓
Identify Conflict
 ↓
Explain Impact
 ↓
Propose Change
 ↓
Wait for Approval
```

---

# 2. 資料表

## 2.1 資料表原則

Export Module **MVP 不建立專用資料表**。

原因：

> Export 是讀取與格式轉換功能，不需要保存 Export Result。

資料來源使用既有系統資料。

---

## 2.2 主要資料來源

Export 可能讀取：

```text
Images
Batches
ImageAnalysis
ProcessingJobs
ProcessingLogs
DuplicateGroups
DuplicateGroupMembers
```

實際使用哪些資料表，依匯出欄位需求決定。

---

## 2.3 Data Ownership

Export Module：

```text
Read
  ↓
Transform
  ↓
Output
```

不得：

```text
Export
  ↓
Update Image
```

或：

```text
Export
  ↓
Update Analysis
```

或：

```text
Export
  ↓
Update Processing Status
```

Export 不擁有上述資料表的 Domain Ownership。

實際 Schema 以：

```text
Database-Schema.md
```

為準。

---

## 2.4 未來 Export Job

若未來需要支援：

- 非同步 Export
- 大型報表
- Cloud Storage 暫存
- Export History
- Export Status

可以新增 Export Job 機制。

但此功能：

> **不屬於 MVP。**

目前不建立：

```text
ExportJobs
ExportLogs
AuditLogs
```

---

# 3. API

## 3.1 API Base

```text
/api/v1
```

---

## 3.2 Export Images

```http
GET /api/v1/images/export
```

用途：

> 匯出圖片處理與分析結果。

---

## 3.3 Query Parameters

MVP 支援：

```text
batchId
status
from
to
```

範例：

```http
GET /api/v1/images/export?batchId={batchId}
```

```http
GET /api/v1/images/export?status=Completed
```

多條件：

```http
GET /api/v1/images/export?batchId={batchId}&status=Completed
```

---

## 3.4 Export Request

Application Request：

```csharp
public sealed class ExportImagesRequest
{
    public Guid? BatchId { get; init; }

    public string? Status { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }
}
```

所有欄位皆為 Optional。

若沒有 Filter：

```text
Export all eligible image records
```

實際可匯出範圍仍受系統資料與未來 Authorization 規則限制。

---

## 3.5 Filter Rules

MVP：

```text
batchId
status
from
to
```

Filter 可以單獨使用，也可以組合。

例如：

```text
batchId + status
```

或：

```text
from + to + status
```

日期條件必須驗證：

```text
From <= To
```

不合法條件：

```text
HTTP 400
INVALID_EXPORT_FILTER
```

---

## 3.6 API Success Response

成功：

```http
200 OK
```

Response：

```text
Content-Type:
text/csv; charset=utf-8
```

並包含：

```text
Content-Disposition:
attachment; filename="image-report.csv"
```

Response Body：

```text
UTF-8 BOM
ImageId,BatchId,OriginalFileName,NewFileName,...
1,xxx,IMG_001.jpg,Tokyo_001.jpg,...
2,xxx,IMG_002.jpg,Tokyo_002.jpg,...
```

API 不回傳 JSON。

---

## 3.7 Empty Result

沒有符合條件的資料：

```http
200 OK
```

Response：

```text
UTF-8 BOM
ImageId,BatchId,OriginalFileName,NewFileName,...
```

只包含 Header。

---

## 3.8 File Name

無 Filter：

```text
image-report.csv
```

指定 Batch：

```text
image-report-{batchId}.csv
```

實際檔名必須經過：

```text
Filename Sanitization
```

不得包含：

```text
Path Traversal
Invalid File Characters
Server Path
```

---

## 3.9 API Error Response

API 使用統一錯誤格式：

```json
{
  "success": false,
  "error": {
    "code": "INVALID_EXPORT_FILTER",
    "message": "The export filter is invalid."
  },
  "traceId": "abc123"
}
```

不得回傳：

```text
Exception
StackTrace
SQL
Connection String
Internal Path
```

---

## 3.10 HTTP Status

| HTTP | 情境 |
|---:|---|
| 200 | Export 成功 |
| 400 | Filter 不合法 |
| 404 | 指定 Batch 不存在 |
| 500 | Export / Internal Error |

---

# 4. 測試

## 4.1 測試策略

Export Module 測試分為：

```text
Unit Test
    ↓
Integration Test
    ↓
Playwright E2E
```

測試重點：

- CSV 正確性
- Filter
- Encoding
- Null Handling
- Error Handling
- Security
- Large Dataset
- 實際下載流程

---

## 4.2 Unit Test

### CSV Generation

- [ ] Header 正確
- [ ] Column Order 正確
- [ ] UTF-8 BOM 正確
- [ ] Data Row 正確
- [ ] Comma Escape
- [ ] Quote Escape
- [ ] Newline Escape

### Null Handling

- [ ] Null Metadata
- [ ] Null GPS
- [ ] Null Quality Score
- [ ] Null NewFileName

### Filtering

- [ ] Batch Filter
- [ ] Status Filter
- [ ] From / To Filter
- [ ] Multiple Filters
- [ ] Invalid Date Range

### Service

- [ ] Export Success
- [ ] Empty Result
- [ ] Invalid Filter
- [ ] Batch Not Found
- [ ] Export Failure

### Security

- [ ] Filename Sanitization
- [ ] Invalid Parameter
- [ ] SQL Injection Input

---

## 4.3 Unit Test Examples

### UTF-8 BOM

```csharp
[Fact]
public void Generate_ShouldIncludeUtf8Bom()
{
    // Arrange
    var data = CreateTestData();

    // Act
    var result = _generator.Generate(data);

    // Assert
    Assert.Equal(0xEF, result[0]);
    Assert.Equal(0xBB, result[1]);
    Assert.Equal(0xBF, result[2]);
}
```

---

### Empty Data

```csharp
[Fact]
public void Generate_ShouldReturnHeader_WhenDataIsEmpty()
{
    // Arrange
    var data = Enumerable.Empty<ImageExportDto>();

    // Act
    var result = _generator.Generate(data);

    // Assert
    Assert.NotEmpty(result);
}
```

---

### CSV Escaping

```csharp
[Fact]
public void Generate_ShouldEscapeCommaAndQuote()
{
    // Arrange
    var data = new[]
    {
        new ImageExportDto
        {
            ImageId = 1,
            OriginalFileName = "Tokyo, Trip.jpg"
        }
    };

    // Act
    var result = _generator.Generate(data);

    // Assert
    var csv = Encoding.UTF8.GetString(result);

    Assert.Contains("\"Tokyo, Trip.jpg\"", csv);
}
```

---

## 4.4 Integration Test

Integration Test 驗證：

```text
API
 ↓
Export Service
 ↓
EF Core
 ↓
Database
 ↓
Export DTO
 ↓
CSV
```

至少驗證：

- [ ] Export Endpoint
- [ ] Batch Filter
- [ ] Status Filter
- [ ] Date Filter
- [ ] Multiple Filters
- [ ] Empty Result
- [ ] Batch Not Found
- [ ] Database Failure
- [ ] CSV Content
- [ ] Response Headers

---

## 4.5 Large Dataset Test

驗證大量資料 Export：

```text
Database
 ↓
Query
 ↓
Projection
 ↓
CSV
```

確認：

- [ ] 不產生 N+1 Query
- [ ] 不一次建立不必要的大型 List
- [ ] 不產生異常 Memory Growth
- [ ] CSV 所有資料均可正確輸出

MVP 不要求特定秒數，除非 System-Level Performance Requirement 另有定義。

---

## 4.6 Playwright E2E

Export Module 必須透過 Playwright 驗證實際使用者流程：

```text
Dashboard
    ↓
Prepare / Process Images
    ↓
Processing Completed
    ↓
Open Export
    ↓
Select Filter
    ↓
Click Export
    ↓
Download CSV
    ↓
Validate File
```

---

## 4.7 Playwright Test Cases

### E2E-EXPORT-01 — Basic Export

Given：

> 系統存在已完成處理的圖片。

When：

> 使用者點擊 Export。

Then：

```text
CSV successfully downloaded
```

---

### E2E-EXPORT-02 — Batch Filter

Given：

```text
Batch A
Batch B
```

When：

> 使用者選擇 Batch A。

Then：

```text
Downloaded CSV
    ↓
Only contains Batch A
```

---

### E2E-EXPORT-03 — Chinese Content

Given：

> 圖片包含中文檔名或 Location。

When：

> 使用者匯出 CSV。

Then：

```text
UTF-8 BOM
    ↓
Chinese text displays correctly
```

---

### E2E-EXPORT-04 — Empty Result

Given：

> 沒有符合 Filter 的圖片。

When：

> 使用者執行 Export。

Then：

```text
HTTP 200
CSV Download
Header exists
No data rows
```

---

## 4.8 Playwright Example

```typescript
import { test, expect } from '@playwright/test';

test('should export image report', async ({ page }) => {
    await page.goto('/dashboard');

    const downloadPromise = page.waitForEvent('download');

    await page.getByRole('button', {
        name: 'Export'
    }).click();

    const download = await downloadPromise;

    expect(download.suggestedFilename())
        .toMatch(/image-report.*\.csv$/);
});
```

---

## 4.9 Acceptance Criteria

### AC-01 — CSV Export

Given：

> 系統存在可匯出的圖片資料。

When：

> 使用者執行 Export。

Then：

```text
HTTP 200
CSV Download
```

---

### AC-02 — UTF-8 BOM

Given：

> CSV 包含中文資料。

When：

> 使用者開啟 CSV。

Then：

```text
中文正常顯示
```

---

### AC-03 — Batch Filter

Given：

```text
Batch A
Batch B
```

When：

> 使用者指定 Batch A。

Then：

```text
Export Result
    ↓
Only Batch A
```

---

### AC-04 — Empty Result

Given：

> Filter 沒有符合資料。

When：

> 使用者執行 Export。

Then：

```text
HTTP 200
CSV Header
No Data Rows
```

---

### AC-05 — CSV Safety

Given：

> Filename 或 Location 包含逗號、引號或換行。

When：

> 產生 CSV。

Then：

> CSV 格式保持正確。

---

### AC-06 — Large Dataset

Given：

> 大量圖片資料。

When：

> 使用者執行 Export。

Then：

> 系統不得因一次載入全部資料而產生不必要的 Memory Pressure。

---

### AC-07 — Data Integrity

Given：

> 系統存在 Metadata、Quality、Naming 與 Processing Result。

When：

> 使用者執行 Export。

Then：

> CSV 應正確反映系統目前已儲存的資料，不重新計算或修改分析結果。

---

## 4.10 Definition of Done

### Specification

- [ ] Module Boundary 明確
- [ ] Export Data 定義
- [ ] CSV Rules 定義
- [ ] Null Handling 定義
- [ ] Large Dataset Strategy 定義
- [ ] Security Rules 定義

### Application

- [ ] ExportImagesRequest
- [ ] ImageExportDto
- [ ] IExportService
- [ ] ExportService
- [ ] ICsvGenerator

### Infrastructure

- [ ] CsvGenerator
- [ ] UTF-8 BOM
- [ ] CSV Escaping
- [ ] Async / Streaming Strategy

### API

- [ ] Export Endpoint
- [ ] Query Filter
- [ ] HTTP Response
- [ ] Content-Type
- [ ] Content-Disposition
- [ ] Error Response

### Security

- [ ] Input Validation
- [ ] Filename Sanitization
- [ ] Parameterized Query
- [ ] Exception Sanitization
- [ ] Internal Path Protection

### Testing

- [ ] Unit Tests
- [ ] Integration Tests
- [ ] Playwright E2E
- [ ] Empty Result Test
- [ ] UTF-8 BOM Test
- [ ] CSV Escape Test
- [ ] Filter Test
- [ ] Large Dataset Test

### Acceptance

- [ ] CSV 可以成功下載
- [ ] Excel 中文正常顯示
- [ ] Filter 正確
- [ ] 空資料正常處理
- [ ] CSV Escape 正確
- [ ] 不修改既有資料
- [ ] 不暴露內部錯誤資訊
- [ ] 符合 System-Level Specification

---

## 4.11 Module Completion

完成 Export Module 後：

```text
Existing Image Data
        ↓
Export Request
        ↓
Filter Validation
        ↓
Database Projection
        ↓
ImageExportDto
        ↓
CSV Generator
        ↓
UTF-8 BOM CSV
        ↓
HTTP Download
```

使用者可以：

```text
Dashboard
    ↓
Export
    ↓
選擇條件
    ↓
Download CSV
    ↓
Excel / Data Analysis
```

Export Module 最終定位：

> **將系統內部已存在的影像處理與分析結果，以穩定、安全且可供使用者使用的 CSV 報表形式輸出。**