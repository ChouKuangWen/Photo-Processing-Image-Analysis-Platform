# Upload Module Specification

**Module ID:** MOD-01  
**Module Name:** Upload Module  
**Version:** V1.0  
**Architecture:** Clean Architecture  
**Technology:** ASP.NET Core / C# / EF Core  
**Document Purpose:** Module-Level Development Specification  
**Status:** Development Baseline

---

# 1. 規格

## 1.1 模組目的

Upload Module 負責圖片進入系統的完整入口流程。

使用者可一次上傳一張或多張圖片。Upload Module 必須完成：

```text
接收圖片
    ↓
驗證 Request / File
    ↓
建立 Batch
    ↓
儲存 Original File
    ↓
建立 Image
    ↓
建立 Processing Job
    ↓
加入 Background Queue
    ↓
回傳 HTTP 202
```

Upload API **不等待後續圖片分析完成**。

後續 EXIF、Hash、pHash、Quality、Naming 等處理由 Background Processing 與其他 Module 負責。

---

## 1.2 Module 職責

Upload Module 負責：

- 單張 / 多張圖片上傳
- Request Validation
- Extension Validation
- MIME Type Validation
- File Size Validation
- Empty File Validation
- Filename Validation
- Path Traversal Prevention
- Batch 建立
- Image 建立
- Original File 儲存
- Processing Job 建立
- Background Queue Enqueue
- Batch Status 查詢
- 標準化 Error Response
- TraceId / Logging

### 核心原則

> Upload Module 只負責「接受圖片並建立後續 Processing 所需的資料與 Job」，不負責實際圖片分析。

---

## 1.3 Module Boundary

以下功能不屬於 Upload Module：

```text
EXIF Parsing
GPS Reverse Geocoding
SHA-256 Duplicate Detection
pHash Generation
Visual Similarity
Quality Analysis
Version Recommendation
Naming
CSV Export
SignalR Dashboard
```

這些功能由其他 Module / Processing Component 負責。

Upload Module 不應直接執行上述 Processing。

---

## 1.4 Architecture

Upload Module 遵循 Clean Architecture：

```text
API
 │
 ▼
Application
 │
 ├── Upload Use Case
 ├── Validation
 └── DTO
 │
 ▼
Domain
 │
 ├── Batch
 ├── Image
 └── ProcessingJob
 │
 ▼
Infrastructure
 │
 ├── EF Core
 ├── File Storage
 └── Processing Queue
```

Application Layer 不得直接依賴 Infrastructure 的具體實作。

例如：

```text
Application
    ↓
IFileStorageService
    ↓
Infrastructure
    ├── LocalFileStorage
    └── GcsFileStorage
```

不得：

```text
Application
    ↓
LocalFileStorage / GcsFileStorage
```

Queue 同樣必須透過抽象介面使用。

---

## 1.5 Upload Processing Flow

```text
Client
  ↓
POST /api/v1/images/upload
  ↓
UploadController
  ↓
Upload Application Service
  ↓
Request Validation
  ↓
File Validation
  ↓
Create Batch
  ↓
Store Original Files
  ↓
Create Image Records
  ↓
Create Processing Jobs
  ↓
Commit Database Transaction
  ↓
Enqueue Jobs
  ↓
Return 202 Accepted
```

Upload API 不得等待：

```text
EXIF Parsing
SHA-256
pHash
Visual Similarity
Quality Analysis
Naming
```

完成後才回應。

---

## 1.6 Functional Requirements

### FR-01 Single File Upload

系統必須支援單張圖片上傳。

成功接受：

```text
1 Batch
1 Image
1 Processing Job
```

---

### FR-02 Multiple File Upload

系統必須支援一次上傳多張圖片。

例如：

```text
IMG_001.jpg
IMG_002.jpg
IMG_003.jpg
```

成功接受：

```text
1 Batch
3 Images
3 Processing Jobs
```

---

### FR-03 Batch Creation

每一次 Upload Request 建立一個 Batch。

Batch 用於識別此次上傳所包含的圖片。

```text
Batch
 ├── Image
 ├── Image
 └── Image
```

Batch ID 使用 UUID / GUID。

Batch 建立時：

- Status = Pending
- ProcessedCount = 0
- SuccessCount = 0
- FailedCount = 0
- CompletedAt = null

---

### FR-04 Image Creation

每個成功接受的圖片必須建立 Image Record。

Upload Module 建立：

```text
Id
BatchId
OriginalFileName
StoredPath
FileSize
MimeType
Status
CreatedAt
UpdatedAt
```

Image 建立時：

- Status = Pending

其他 Metadata 由後續 Processing Module 補充。

---

### FR-05 Processing Job Creation

Upload Module 必須為需要處理的圖片建立 Processing Job。

MVP Workflow：

```text
Naming
Analysis
DuplicateDetection
Full
```

其中：

```text
Full
```

代表完整 MVP Processing Workflow。

Processing Job 只代表「待處理工作」，不代表工作已完成。

---

## 1.7 Business Rules

### BR-01 Asynchronous Upload

完成必要的資料建立與 Job 排程後，Upload API 應立即回傳：

```text
HTTP 202 Accepted
```

不得等待完整 Processing。

---

### BR-02 Preserve Original File

Original File 在 Processing 尚未確認完成前不得刪除。

Original File 是後續：

```text
EXIF
SHA-256
pHash
Quality Analysis
Naming
```

等功能的重要來源。

---

### BR-03 Storage / Database Separation

Storage 與 Database 不屬於同一 Transaction。

不得假設：

```text
Database Transaction
=
File Storage Transaction
```

因此兩者操作失敗時，必須依規格進行 Compensation，以維持資料一致性。

---

### BR-04 Image Binary 不存 Database

Database 僅儲存圖片 Metadata 與 Storage Reference：

```text
StoredPath
FileSize
MimeType
```

圖片 Binary 儲存於 Storage。

Development：

```text
LocalFileStorage
```

Production：

```text
GcsFileStorage
```

Application Layer 不直接依賴 GCS API。

---

### BR-05 Filename 不可信任

Client Filename 不得直接作為 Storage Path。

必須執行：

```text
Filename Sanitization
Path Traversal Prevention
Invalid Character Validation
Filename Length Validation
```

例如：

```text
../../../file.jpg
```

不得直接寫入 Storage。

---

### BR-06 Database 是持久化狀態來源

Batch、Image、Processing Job 的持久化狀態由 Database 保存。

Development MVP 的：

```text
System.Threading.Channels
```

僅負責 Runtime 工作傳遞，不取代 Database Job State。

---

### BR-07 Workflow 必須明確

Request 必須指定 Workflow，且只能使用系統支援的 Workflow：

```text
Naming
Analysis
DuplicateDetection
Full
```

未知 Workflow 必須拒絕。

---

## 1.8 Transaction & Consistency

Upload 流程涉及：

```text
Database
Storage
Queue
```

Database Transaction 涵蓋：

```text
Batch Creation
Image Creation
Processing Job Creation
```

Storage 不屬於 Database Transaction。

主要流程：

```text
Validate
   ↓
Create Batch
   ↓
Store File
   ↓
Create Image
   ↓
Create Job
   ↓
Commit Database
   ↓
Enqueue Job
```

### Storage Failure

如果 Storage 寫入失敗：

```text
Upload Failed
```

不得建立無效的 Image / Processing Job。

### Database Failure

如果 Database Commit 失敗，而 File 已成功寫入 Storage：

```text
Database Failure
      ↓
Compensation
      ↓
Delete Stored File
```

避免留下孤兒檔案。

### Partial Upload

多檔案 Upload 發生部分失敗時，處理策略由 System-Level Specification 定義。

Upload Module 不得自行定義與 System-Level Specification 衝突的行為。

---

## 1.9 Application Services

### IUploadService

主要操作：

```text
UploadAsync()
```

負責協調：

```text
Request Validation
File Validation
Batch Creation
Image Creation
Storage
Processing Job Creation
Queue
```

---

### IFileValidationService

負責：

```text
Extension Validation
MIME Validation
File Size Validation
File Integrity Validation
Filename Validation
```

---

### IFileStorageService

Application Layer 只依賴：

```text
IFileStorageService
```

Infrastructure 實作：

```text
LocalFileStorage
GcsFileStorage
```

---

### IProcessingQueue

建立 Processing Job 後，透過 Queue 將工作交給 Background Processing。

Development MVP：

```text
System.Threading.Channels
```

Upload Module 不直接執行 Processing。

---

## 1.10 Infrastructure

### Database

使用：

```text
Entity Framework Core
```

Upload Module 主要操作：

```text
Batches
Images
ProcessingJobs
```

### Storage

Development：

```text
LocalFileStorage
```

Production：

```text
GcsFileStorage
```

### Storage Structure

Development：

```text
/storage
 ├── original
 ├── processed
 └── temp
```

Upload Module 的 Original File 儲存於：

```text
/storage/original
```

Production 使用對應 Cloud Storage 結構，但 Application Layer 不直接依賴 GCS API。

---

## 1.11 Logging & Observability

Upload Module 必須記錄：

```text
TraceId
BatchId
ImageId
OriginalFileName
FileSize
Workflow
Status
DurationMs
ErrorCode
```

不得記錄：

```text
Password
API Key
Connection String
Secret
```

Client 不得取得：

```text
SQL Exception
Stack Trace
Connection String
Internal File Path
Internal Exception Details
```

---

## 1.12 Error Handling

使用統一 Error Contract：

```json
{
  "success": false,
  "error": {
    "code": "INVALID_FILE",
    "message": "The uploaded file is invalid.",
    "traceId": "00-abc123"
  }
}
```

錯誤處理：

```text
Exception
   ↓
Global Exception Middleware
   ↓
Logging
   ↓
TraceId
   ↓
Standard API Error Response
```

Application Service 不得將 Infrastructure Exception 原樣傳回 Client。

---

## 1.13 Edge Cases

Upload Module 必須處理：

| Case | Expected Behavior |
|---|---|
| Empty Request | `INVALID_FILE` |
| Empty File | Reject |
| Unsupported Extension | Reject |
| MIME Mismatch | Reject |
| Oversized File | `FILE_TOO_LARGE` |
| Invalid Filename | Reject / 依既定 Sanitization Rule |
| Path Traversal | Reject |
| Duplicate Filename | 允許；以 Image ID 區分 |
| Partial Upload Failure | 遵循 System-Level Specification |

支援格式：

```text
.jpg
.jpeg
.png
```

檔案驗證不得只依賴副檔名。

---

# 2. 資料表結構

## 2.1 Table Overview

Upload Module 直接使用：

| Table | 用途 |
|---|---|
| `Batches` | 一次批次上傳的基本資訊 |
| `Images` | 圖片基本資訊與 Storage Reference |
| `ProcessingJobs` | 圖片待處理工作的持久化狀態 |

Upload Module 不負責建立：

```text
ImageAnalyses
DuplicateGroups
DuplicateGroupMembers
ProcessingLogs
```

---

## 2.2 Batches

### Table: `batches`

| 欄位 | 型別 | Nullable | Key / Index | Default | 說明 |
|---|---|---:|---|---|---|
| Id | UUID | No | PK | — | Batch ID |
| TotalCount | INT | No | — | 0 | 圖片總數 |
| ProcessedCount | INT | No | — | 0 | 已處理數量 |
| SuccessCount | INT | No | — | 0 | 成功數量 |
| FailedCount | INT | No | — | 0 | 失敗數量 |
| Status | VARCHAR(30) | No | Index | — | Batch 狀態 |
| CreatedAt | TIMESTAMP | No | — | — | 建立時間 |
| CompletedAt | TIMESTAMP | Yes | — | NULL | 完成時間 |

Relationship：

```text
Batches 1 ─── N Images
```

---

## 2.3 Images

### Table: `images`

| 欄位 | 型別 | Nullable | Key / Index | Default | 說明 |
|---|---|---:|---|---|---|
| Id | BIGINT | No | PK | Identity | Image ID |
| BatchId | UUID | No | FK, Index | — | 所屬 Batch |
| OriginalFileName | VARCHAR(255) | No | — | — | 原始檔名 |
| StoredPath | VARCHAR(500) | No | — | — | Storage Reference |
| NewFileName | VARCHAR(255) | Yes | — | NULL | 新檔名 |
| FileSize | BIGINT | No | — | — | 檔案大小 |
| MimeType | VARCHAR(100) | No | — | — | MIME Type |
| SHA256 | VARCHAR(64) | Yes | Index | NULL | SHA-256 |
| TakenAt | TIMESTAMP | Yes | Index | NULL | 拍攝時間 |
| CameraModel | VARCHAR(100) | Yes | — | NULL | 相機型號 |
| ISO | INT | Yes | — | NULL | ISO |
| ShutterSpeed | VARCHAR(50) | Yes | — | NULL | 快門速度 |
| Aperture | VARCHAR(50) | Yes | — | NULL | 光圈 |
| Latitude | DECIMAL(10,7) | Yes | — | NULL | 緯度 |
| Longitude | DECIMAL(10,7) | Yes | — | NULL | 經度 |
| LocationName | VARCHAR(150) | Yes | — | NULL | 地點名稱 |
| Status | VARCHAR(30) | No | Index | — | Image 狀態 |
| CreatedAt | TIMESTAMP | No | — | — | 建立時間 |
| UpdatedAt | TIMESTAMP | No | — | — | 更新時間 |

Relationship：

```text
Images N ─── 1 Batches
```

---

## 2.4 ProcessingJobs

### Table: `processing_jobs`

| 欄位 | 型別 | Nullable | Key / Index | Default | 說明 |
|---|---|---:|---|---|---|
| Id | BIGINT | No | PK | Identity | Job ID |
| ImageId | BIGINT | No | FK, Index | — | Image ID |
| BatchId | UUID | No | FK, Index | — | Batch ID |
| Workflow | VARCHAR(30) | No | — | — | Workflow |
| Status | VARCHAR(30) | No | Index | — | Job 狀態 |
| RetryCount | INT | No | — | 0 | 重試次數 |
| CreatedAt | TIMESTAMP | No | — | — | 建立時間 |
| StartedAt | TIMESTAMP | Yes | — | NULL | 開始時間 |
| CompletedAt | TIMESTAMP | Yes | — | NULL | 完成時間 |
| ErrorCode | VARCHAR(50) | Yes | — | NULL | 錯誤代碼 |
| ErrorMessage | TEXT | Yes | — | NULL | 錯誤訊息 |

Relationships：

```text
ProcessingJobs N ─── 1 Images
ProcessingJobs N ─── 1 Batches
```

---

## 2.5 Keys / Indexes / Constraints

### Batches

```text
PK:
Batches.Id
```

### Images

```text
PK:
Images.Id

FK:
Images.BatchId → Batches.Id

INDEX:
Images.BatchId
Images.SHA256
Images.Status
Images.TakenAt
```

### ProcessingJobs

```text
PK:
ProcessingJobs.Id

FK:
ProcessingJobs.ImageId → Images.Id

FK:
ProcessingJobs.BatchId → Batches.Id

UNIQUE:
ProcessingJobs.ImageId + ProcessingJobs.Workflow

INDEX:
ProcessingJobs.ImageId
ProcessingJobs.BatchId
ProcessingJobs.Status
```

---

## 2.6 EF Core Mapping

Database Mapping 使用：

```text
IEntityTypeConfiguration<T>
```

建議：

```text
BatchConfiguration
ImageConfiguration
ProcessingJobConfiguration
```

Mapping 不應將大量 Database Configuration 塞入 Entity Class。

Schema 變更流程：

```text
Specification
     ↓
Entity
     ↓
EF Core Configuration
     ↓
Migration
     ↓
Integration Test
```

Agent 不得未經規格變更自行修改：

```text
Column
Data Type
PK
FK
Index
Unique Constraint
Relationship
```

---

# 3. API 文件

## 3.1 API Overview

| Method | Endpoint | Purpose |
|---|---|---|
| POST | `/api/v1/images/upload` | 上傳一張或多張圖片 |
| GET | `/api/v1/images/batches/{batchId}/status` | 查詢 Batch Processing Status |

API Base：

```text
/api/v1
```

所有 API 使用系統統一 Response / Error Contract。

---

## 3.2 Upload Images

### Endpoint

```text
POST /api/v1/images/upload
```

### Purpose

接收一張或多張圖片，完成：

```text
Validation
Batch Creation
Image Creation
Storage
Processing Job Creation
Queue
```

不等待完整 Processing。

---

## 3.3 Request

Content-Type：

```text
multipart/form-data
```

| Field | Type | Required | 說明 |
|---|---|---:|---|
| files | File[] | Yes | 一個或多個圖片 |
| workflow | String | Yes | Processing Workflow |

支援：

```text
.jpg
.jpeg
.png
```

Workflow：

```text
Naming
Analysis
DuplicateDetection
Full
```

Example：

```text
files = IMG_001.jpg
files = IMG_002.jpg
files = IMG_003.jpg

workflow = Full
```

---

## 3.4 Request Validation

必須驗證：

```text
Files Exists
File Count
Extension
MIME Type
File Size
File Integrity
Filename
Path Traversal
Workflow
```

不合法 Request 不得建立無效 Processing Job。

---

## 3.5 Upload Response

成功：

```text
HTTP 202 Accepted
```

```json
{
  "success": true,
  "data": {
    "batchId": "550e8400-e29b-41d4-a716-446655440000",
    "totalCount": 3,
    "status": "Pending"
  }
}
```

Response：

| 欄位 | Type | 說明 |
|---|---|---|
| success | Boolean | Request 是否成功 |
| batchId | UUID | Batch ID |
| totalCount | Integer | 圖片數量 |
| status | String | Batch 初始狀態 |

---

## 3.6 Batch Status

### Endpoint

```text
GET /api/v1/images/batches/{batchId}/status
```

### Purpose

取得指定 Batch 的 Processing Status。

### Parameter

| Parameter | Type | Required | 說明 |
|---|---|---:|---|
| batchId | UUID | Yes | Batch ID |

### Response

```text
HTTP 200 OK
```

```json
{
  "success": true,
  "data": {
    "batchId": "550e8400-e29b-41d4-a716-446655440000",
    "totalCount": 100,
    "processedCount": 80,
    "successCount": 78,
    "failedCount": 2,
    "progressPercentage": 80,
    "status": "Processing"
  }
}
```

Response：

| 欄位 | Type | 說明 |
|---|---|---|
| batchId | UUID | Batch ID |
| totalCount | Integer | 圖片總數 |
| processedCount | Integer | 已處理數量 |
| successCount | Integer | 成功數量 |
| failedCount | Integer | 失敗數量 |
| progressPercentage | Decimal | 處理進度 |
| status | String | Batch 狀態 |

---

## 3.7 Error Response

統一格式：

```json
{
  "success": false,
  "error": {
    "code": "INVALID_FILE",
    "message": "The uploaded file is invalid.",
    "traceId": "00-abc123"
  }
}
```

Client 不得取得：

```text
SQL Exception
Stack Trace
Connection String
Internal File Path
Internal Exception Details
```

---

## 3.8 HTTP Status Codes

| HTTP Status | Error Code / Purpose |
|---:|---|
| 200 | Batch Status 查詢成功 |
| 202 | Upload 已接受並進入非同步處理 |
| 400 | `INVALID_FILE` |
| 400 | `UNSUPPORTED_FORMAT` |
| 400 | `FILE_TOO_LARGE` |
| 400 | `INVALID_WORKFLOW` |
| 404 | `BATCH_NOT_FOUND` |
| 500 | `STORAGE_ERROR` |
| 500 | `INTERNAL_ERROR` |

---

## 3.9 API Acceptance Criteria

### Upload Success

Given：

```text
一張合法 JPG
```

When：

```text
POST /api/v1/images/upload
```

Then：

```text
HTTP 202
Batch Created
Image Created
Processing Job Created
```

---

### Multiple Upload

Given：

```text
100 張合法圖片
```

Then：

```text
HTTP 202
Batch.TotalCount = 100
Image Count = 100
Processing Job Count = 100
```

---

### Invalid File

Given：

```text
test.exe
```

Then：

```text
HTTP 400
ErrorCode = UNSUPPORTED_FORMAT
```

---

### File Too Large

Given：

```text
File > configured maximum size
```

Then：

```text
HTTP 400
ErrorCode = FILE_TOO_LARGE
```

---

### Batch Not Found

Given：

```text
Unknown Batch ID
```

Then：

```text
HTTP 404
ErrorCode = BATCH_NOT_FOUND
```

---

### Storage Failure

Given：

```text
Storage unavailable
```

Then：

```text
HTTP 500
ErrorCode = STORAGE_ERROR
```

且不得留下無效 Processing Job。

---

# 4. 測試規劃

## 4.1 Unit Test

Unit Test 驗證 Upload Module 的 Domain / Application 邏輯。

### File Validation

必須測試：

```text
Valid JPG
Valid JPEG
Valid PNG
Invalid Extension
Invalid MIME Type
Empty File
File Too Large
Invalid Filename
Path Traversal
```

### Upload Service

必須測試：

```text
Single File Upload
Multiple File Upload
Batch Creation
Image Creation
Processing Job Creation
Workflow Assignment
```

### Failure Handling

必須測試：

```text
Storage Failure
Database Failure
Queue Failure
Invalid File
```

### Transaction / Compensation

驗證：

```text
Storage Success
+
Database Failure
```

是否觸發：

```text
Compensation
    ↓
Delete Stored File
```

避免產生孤兒檔案。

### Example

Given：

```text
3 valid image files
```

When：

```text
UploadAsync()
```

Then：

```text
Batch.TotalCount = 3
Image Count = 3
Job Count = 3
```

---

## 4.2 Integration Test

Integration Test 驗證：

```text
API
 ↓
Application
 ↓
EF Core
 ↓
Database
 ↓
Storage
 ↓
Queue
```

### Database

測試：

```text
Batch Insert
Image Insert
Processing Job Insert
Foreign Key
Unique Constraint
Transaction Rollback
```

### Storage

測試：

```text
File Write
File Exists
File Delete
Storage Failure
```

### Queue

測試：

```text
Job Enqueue
Workflow Preservation
Queue Failure
```

### Complete Upload

驗證：

```text
Upload Request
 ↓
Batch Created
 ↓
Image Created
 ↓
File Stored
 ↓
Job Created
 ↓
Job Enqueued
 ↓
202 Response
```

必須確認流程中的資料一致性。

EF Core Integration Test 不建議 Mock EF Core，應使用實際 Test Database 驗證：

```text
EF Core
 ↓
Database
```

---

## 4.3 E2E / Playwright Test

UI 完成後使用 Playwright 驗證實際使用者流程。

### E2E-01 Upload Success

```text
Open Upload Page
 ↓
Select Multiple Images
 ↓
Select Workflow
 ↓
Click Upload
 ↓
Wait API Response
 ↓
Verify HTTP 202
 ↓
Verify Batch ID
 ↓
Navigate Dashboard
 ↓
Verify Batch Exists
```

### E2E-02 Upload Validation

```text
Select Unsupported File
 ↓
Upload
 ↓
Verify Error Message
```

### E2E-03 Oversized File

```text
Select oversized file
 ↓
Upload
 ↓
Verify FILE_TOO_LARGE
```

### E2E-04 Multiple Upload

```text
Select 3 Images
 ↓
Upload
 ↓
Verify Batch
 ↓
Verify Total Count = 3
```

### E2E-05 Workflow Selection

```text
Select Workflow
 ↓
Upload
 ↓
Verify Processing Job
 ↓
Verify Workflow
```

### E2E-06 Processing Start

```text
Upload
 ↓
Batch Created
 ↓
Processing Started
 ↓
Dashboard
 ↓
Verify Batch Status
```

---

# 5. 驗收條件

Upload Module 必須至少符合：

### Upload

```text
✓ 支援單張圖片
✓ 支援多張圖片
✓ 支援 JPG / JPEG / PNG
✓ 正確建立 Batch
✓ 正確建立 Image
✓ 正確建立 Processing Job
✓ Original File 正確保存
✓ Upload API 回傳 202
```

### Validation

```text
✓ Extension Validation
✓ MIME Validation
✓ File Size Validation
✓ Empty File Validation
✓ Filename Validation
✓ Path Traversal Prevention
✓ Workflow Validation
```

### Processing

```text
✓ Job 正確建立
✓ Job 正確進入 Queue
✓ Upload 不直接執行後續 Processing
```

### Consistency

```text
✓ Database Transaction 正確
✓ Storage Failure 可正確處理
✓ Database Failure 可觸發 Compensation
✓ 不產生無效 Image / Job
✓ 不留下孤兒檔案
```

### API

```text
✓ Upload Endpoint
✓ Batch Status Endpoint
✓ Response Contract
✓ Error Contract
✓ HTTP Status Code
✓ TraceId
```

### Testing

```text
✓ Unit Test Pass
✓ Integration Test Pass
✓ E2E Test Pass
```

---

# 6. Definition of Done

## Specification

- [ ] Module Purpose
- [ ] Module Responsibility
- [ ] Functional Requirements
- [ ] Processing Flow
- [ ] Business Rules
- [ ] Error Handling
- [ ] Edge Cases

## Database

- [ ] Batches Schema
- [ ] Images Schema
- [ ] ProcessingJobs Schema
- [ ] PK / FK
- [ ] Index
- [ ] Unique Constraint
- [ ] EF Core Mapping
- [ ] EF Core Migration

## Application

- [ ] Upload Service
- [ ] File Validation Service
- [ ] Storage Interface
- [ ] Processing Queue Interface
- [ ] Batch Creation
- [ ] Image Creation
- [ ] Processing Job Creation

## API

- [ ] `POST /api/v1/images/upload`
- [ ] `GET /api/v1/images/batches/{batchId}/status`
- [ ] Request Validation
- [ ] Response Contract
- [ ] Error Contract
- [ ] HTTP Status Code

## Testing

- [ ] Unit Test
- [ ] Integration Test
- [ ] Database Constraint Test
- [ ] Storage Integration Test
- [ ] Queue Integration Test
- [ ] Playwright Upload E2E
- [ ] Playwright Validation E2E
- [ ] Playwright Batch Status E2E

## Observability

- [ ] TraceId
- [ ] BatchId
- [ ] ImageId
- [ ] ErrorCode
- [ ] Duration
- [ ] Structured Logging

---

# 7. AI Agent Implementation Guidance

本文件是 Upload Module 的 **Module-Level Contract**。

Agent 開始實作前，必須先閱讀：

```text
AGENTS.md
System-Level Specification
Upload Module Specification
```

並依照以下順序理解：

```text
Module Boundary
      ↓
Functional Requirements
      ↓
Business Rules
      ↓
Database Schema
      ↓
API Contract
      ↓
Testing Requirements
      ↓
Implementation
```

## 7.1 Agent 必須遵守

Agent 必須：

- 依照本文件實作 Upload Module。
- 遵守 System-Level Specification。
- 遵守 Clean Architecture。
- Application Layer 使用 Interface / Abstraction。
- 使用 EF Core 實作 Database Persistence。
- 使用 `IFileStorageService` 抽象 Storage。
- 使用 Queue Interface 抽象 Background Queue。
- 實作 Unit / Integration Test。
- 在 UI 已完成時實作對應 Playwright E2E Test。
- 完成後執行 Build / Test。

---

## 7.2 Agent 不得自行修改

除非 Specification 明確要求，Agent 不得自行修改：

```text
Module Boundary

API Endpoint
HTTP Method
Request Contract
Response Contract
Error Code

Database Column
Database Type
Primary Key
Foreign Key
Index
Unique Constraint
Relationship

Workflow Definition
Storage Architecture
Queue Architecture
Global Error Contract
```

Agent 不得自行新增：

```text
新的 Workflow
新的 File Format
新的 Database Table
新的 API
新的 Business Rule
```

---

## 7.3 發現規格衝突時

如果發現：

```text
System-Level Specification
        ↕
Upload Module Specification
        ↕
Existing Implementation
```

存在衝突：

```text
STOP
 ↓
Identify Conflict
 ↓
Report Conflict
 ↓
Request Specification Decision
```

不得自行猜測規則或修改 Specification。

---

## 7.4 Module Completion

Upload Module 完成後，應能提供：

```text
User
 ↓
Select Images
 ↓
Upload
 ↓
Validate
 ↓
Create Batch
 ↓
Store Original Files
 ↓
Create Image Records
 ↓
Create Processing Jobs
 ↓
Enqueue Jobs
 ↓
HTTP 202
 ↓
Background Processing
```

並符合：

```text
Unit Tests        = Pass
Integration Tests = Pass
E2E Tests         = Pass
```

完成後才進入後續 Metadata / Analysis / Processing Module 開發。