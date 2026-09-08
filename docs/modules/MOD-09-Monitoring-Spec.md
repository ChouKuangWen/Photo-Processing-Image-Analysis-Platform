# Monitoring Module Specification

**文件名稱：** Monitoring Module Specification  
**模組編號：** MOD-09  
**模組名稱：** Monitoring Module  
**文件版本：** V2.0  
**對應系統：** Photo Processing & Intelligent Image Analysis Platform  
**架構：** Clean Architecture  
**技術：** ASP.NET Core / C# / EF Core / SignalR / Serilog  
**文件用途：** AI Agent Development Specification  
**文件狀態：** Development  

---

# 1. 規格

## 1.1 模組用途

Monitoring Module 負責提供系統處理流程的 **Observability & Realtime Monitoring**。

主要讓使用者與開發者可以掌握：

- Batch 目前處理進度
- Processing Job 狀態
- Processing Step 狀態
- Processing History / Logs
- Processing Failure
- Worker 狀態
- Queue Metrics
- Realtime Processing Events
- Application Health
- TraceId 關聯

本模組的核心目的：

> **讓使用者可以即時掌握 Batch 與圖片處理狀態，讓開發者可以透過 Log、TraceId 與 Processing History 快速定位問題。**

---

## 1.2 模組責任

Monitoring Module 負責：

- Batch Status 查詢
- Batch Progress 計算
- Processing Job 查詢
- Processing Step Status 查詢
- Processing Log 查詢
- Monitoring Event
- SignalR Realtime Event
- Worker Status Monitoring
- Queue Metrics
- Health Check
- TraceId 關聯
- Logging Integration
- Monitoring API

---

## 1.3 模組邊界

Monitoring Module **不負責執行任何實際影像處理工作**。

不負責：

- Background Job 執行
- Image Processing
- EXIF Parsing
- SHA-256 Calculation
- pHash Calculation
- Visual Similarity Analysis
- Quality Analysis
- File Rename
- Recommendation
- File Storage

上述功能由其他 Module 負責。

Monitoring Module 只負責：

```text
Processing State
      ↓
Monitoring
      ↓
Processing Log
      ↓
Realtime Event
      ↓
Dashboard
```

---

## 1.4 與 Processing Module 的關係

Processing Module 負責：

> **執行 Job 與 Processing Workflow**

Monitoring Module 負責：

> **觀察 Processing 的執行狀態**

因此兩者責任不同：

```text
Processing Module
        │
        │ Status / Log / Event
        ▼
Monitoring Module
        │
        ├── REST API
        ├── SignalR
        ├── Processing Logs
        └── Dashboard
```

Monitoring Module 不應接管 Processing Module 的 Job Execution。

---

## 1.5 Module 內部處理機制

Monitoring 本身的處理流程：

```text
Processing State Change
        ↓
Update Monitoring Data
        ↓
Create ProcessingLog
        ↓
Publish Monitoring Event
        ↓
SignalR
        ↓
Dashboard
```

例如：

```text
PHASH_GENERATION
        ↓
Success
        ↓
ProcessingLog
        ↓
ProcessingCompleted
        ↓
SignalR
        ↓
Dashboard Update
```

此流程是 **Monitoring Module 的內部處理機制**，不是系統層級 Workflow。

---

## 1.6 Batch Monitoring

Batch 用於表示一批圖片的整體處理狀態。

例如：

```text
Batch #100

Total       = 100
Processed   = 80
Success     = 78
Failed      = 2
Progress    = 80%
```

Progress 計算：

```text
ProgressPercentage =
    ProcessedCount / TotalCount × 100
```

結果必須限制於：

```text
0–100
```

---

## 1.7 Batch Status

MVP 支援：

```text
Pending
Processing
Completed
Failed
```

基本狀態：

```text
Pending
   ↓
Processing
   ↓
Completed
```

失敗：

```text
Processing
   ↓
Failed
```

### 部分失敗

單一圖片失敗不代表整個 Batch 必須 Failed。

例如：

```text
100 Images
│
├── 98 Success
└── 2 Failed
```

Batch 可以：

```text
Status = Completed
FailedCount = 2
```

因此 Batch Status 與 FailedCount 必須分開表示。

---

## 1.8 Processing Job Monitoring

Monitoring 必須可以查詢：

```text
JobId
ImageId
BatchId
Status
RetryCount
CreatedAt
StartedAt
CompletedAt
ErrorCode
ErrorMessage
```

Job Status：

```text
Pending
Processing
Completed
Failed
```

---

## 1.9 Processing Step Monitoring

每個 Processing Step 都必須可以被追蹤。

例如：

```text
Image 101

EXIF_PARSING
    Success

SHA256
    Success

PHASH_GENERATION
    Success

QUALITY_ANALYSIS
    Processing

NAMING
    Pending
```

Monitoring Module 不執行 Step，只負責呈現 Step 狀態。

---

## 1.10 Processing Log

ProcessingLog 用於記錄 Processing History。

主要資訊：

```text
Image
Processing Step
Status
Duration
Error
Retry Count
Timestamp
TraceId
```

Log Status：

```text
Success
Warning
Failed
```

例如：

```text
ImageId      = 101
Step         = PHASH_GENERATION
Status       = Success
DurationMs   = 183
RetryCount   = 0
```

---

## 1.11 TraceId

重要 Processing Operation 必須保留 TraceId。

TraceId 用於串聯：

```text
API Request
    ↓
Batch
    ↓
Image
    ↓
Job
    ↓
Processing Step
    ↓
ProcessingLog
    ↓
Error
```

例如：

```text
TraceId = 00-abc123
BatchId = batch-001
ImageId = 102
JobId   = 1001
Step    = PHASH_GENERATION
```

---

## 1.12 Monitoring Events

MVP 支援：

```text
ProcessingStarted
ProcessingCompleted
ProcessingFailed
BatchProgressUpdated
BatchCompleted
```

### ProcessingStarted

```json
{
  "event": "ProcessingStarted",
  "batchId": "batch-001",
  "imageId": 102,
  "step": "PHASH_GENERATION",
  "timestamp": "2026-08-31T10:00:00Z"
}
```

### ProcessingCompleted

```json
{
  "event": "ProcessingCompleted",
  "batchId": "batch-001",
  "imageId": 102,
  "step": "PHASH_GENERATION",
  "durationMs": 183,
  "timestamp": "2026-08-31T10:00:01Z"
}
```

### ProcessingFailed

```json
{
  "event": "ProcessingFailed",
  "batchId": "batch-001",
  "imageId": 102,
  "step": "PHASH_GENERATION",
  "errorCode": "IMAGE_DECODE_FAILED",
  "traceId": "00-abc123",
  "timestamp": "2026-08-31T10:00:01Z"
}
```

### BatchProgressUpdated

```json
{
  "event": "BatchProgressUpdated",
  "batchId": "batch-001",
  "totalCount": 100,
  "processedCount": 80,
  "successCount": 78,
  "failedCount": 2,
  "progressPercentage": 80
}
```

### BatchCompleted

```json
{
  "event": "BatchCompleted",
  "batchId": "batch-001",
  "totalCount": 100,
  "successCount": 98,
  "failedCount": 2,
  "progressPercentage": 100
}
```

---

## 1.13 SignalR

使用：

> **ASP.NET Core SignalR**

主要用途：

```text
Server
   ↓
SignalR
   ↓
Vue Dashboard
```

用於即時更新 Processing 狀態，避免 Frontend 持續 Polling API。

Monitoring Hub：

```text
/api/v1/hubs/monitoring
```

基本 Hub：

```csharp
public class MonitoringHub : Hub
{
}
```

---

## 1.14 SignalR Batch Group

可以依 Batch 建立 SignalR Group：

```text
batch:{batchId}
```

使用者開啟 Batch Monitoring 時：

```text
Client
   ↓
Join Batch Group
   ↓
Receive Monitoring Events
```

Event Flow：

```text
Worker
   ↓
Processing Completed
   ↓
Update Database
   ↓
Publish Event
   ↓
SignalR Hub
   ↓
Batch Group
   ↓
Vue Dashboard
```

---

## 1.15 Event Ordering

Event 應盡可能依照：

```text
CreatedAt
+
Sequence
```

維持合理順序。

但 Client **不得假設 Network Event 一定按照發送順序抵達**。

因此 Dashboard 必須可以透過：

```text
GET Batch Status
```

重新取得目前正確狀態。

---

## 1.16 Worker Monitoring

Monitoring 可以提供 Worker 基本狀態：

```text
Running
Stopped
Degraded
```

可追蹤：

```text
Worker StartedAt
LastProcessedAt
ProcessedJobs
FailedJobs
```

Monitoring Module 不負責 Worker 執行本身。

---

## 1.17 Queue Monitoring

MVP 使用：

```text
System.Threading.Channels
```

可提供：

```text
Queued Jobs
Active Jobs
Completed Jobs
Failed Jobs
```

由於 `System.Threading.Channels` 本身不提供完整持久化 Queue Monitoring，因此 Queue Metrics 必須由 Application 自行維護。

---

## 1.18 Health Check

提供：

```text
GET /health
GET /health/live
GET /health/ready
```

### Liveness

用途：

> 確認 Application Process 是否正常運作。

Liveness 不應檢查：

```text
Database
External API
Storage
```

避免外部依賴故障造成 Container 被錯誤重啟。

### Readiness

用途：

> 確認 Application 是否準備接受工作。

可以檢查：

```text
Database
Storage
Required Dependencies
```

---

## 1.19 Logging

使用：

> **Serilog**

重要 Operation 應記錄：

```text
TraceId
BatchId
ImageId
JobId
ProcessingStep
DurationMs
Status
ErrorCode
RetryCount
```

Production 不應大量使用：

```text
Trace
Debug
```

重要事件使用：

```text
Information
Warning
Error
```

### Sensitive Information

Log 不得記錄：

```text
Connection String
API Key
Password
Access Token
Secret
```

也不得直接記錄：

```text
完整檔案內容
完整圖片 Binary
```

---

## 1.20 Performance

Monitoring API 必須避免一次載入大量 Processing Logs。

需要使用 Pagination：

```text
page=1
pageSize=50
```

建議：

```text
pageSize <= 500
```

---

## 1.21 Application Interface

主要介面：

```csharp
public interface IMonitoringService
{
    Task<BatchStatusResult> GetBatchStatusAsync(
        Guid batchId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ProcessingLogResult>>
        GetProcessingLogsAsync(
            long imageId,
            CancellationToken cancellationToken);
}
```

Event Publisher：

```csharp
public interface IMonitoringEventPublisher
{
    Task PublishProcessingStartedAsync(
        ProcessingEvent eventData,
        CancellationToken cancellationToken);

    Task PublishProcessingCompletedAsync(
        ProcessingEvent eventData,
        CancellationToken cancellationToken);

    Task PublishProcessingFailedAsync(
        ProcessingEvent eventData,
        CancellationToken cancellationToken);

    Task PublishBatchProgressAsync(
        BatchProgressEvent eventData,
        CancellationToken cancellationToken);
}
```

---

## 1.22 Dependency Rules

Monitoring Module 可以依賴：

```text
Domain
Application
```

資料取得應透過 Application Interface。

不得在 Application Layer 直接依賴：

```text
EF Core
SQL Connection
GCS SDK
Image Library
```

SignalR 的具體實作位於 API / Infrastructure 邊界。

---

## 1.23 Error Handling

Monitoring API 發生未處理錯誤時：

```text
Exception
   ↓
Global Exception Handler
   ↓
Serilog
   ↓
TraceId
   ↓
Standard Error Response
```

不得直接將以下資訊回傳 Client：

```text
SQL Exception
StackTrace
Connection String
Internal Path
```

---

## 1.24 Agent Development Rules

AI Agent 開發本模組時必須遵守：

1. 先閱讀 System-Level Specification。
2. 再閱讀 Database Schema。
3. 再閱讀 Processing Module Contract。
4. 最後依本 Monitoring Module Specification 實作。
5. 不得修改其他 Module 的 Processing 邏輯。
6. 不得讓 Monitoring Module 執行 Image Processing。
7. 不得自行修改 API Contract。
8. 不得自行修改 Database Schema。
9. 不得自行新增 Monitoring Event。
10. 不得自行修改 Event Payload。
11. 不得自行修改 Batch Status 定義。
12. 不得把 SignalR 當成唯一狀態來源，Dashboard 必須能透過 REST API 取得目前狀態。
13. 不得將敏感資訊寫入 Log。
14. 新增功能時必須同步增加對應測試。

### 發生規格衝突時

```text
Identify Conflict
      ↓
Explain Impact
      ↓
Propose Change
      ↓
Wait for Approval
```

Agent 不得自行選擇一個規格並修改其他規格來配合。

---

# 2. 資料表

Monitoring Module 主要使用既有 Processing 資料：

```text
Batches
Images
ProcessingJobs
ProcessingLogs
```

Monitoring Module 主要負責查詢與呈現這些資料，不應為了產生基本 Monitoring 功能而重複建立 Processing 狀態資料。

---

## 2.1 Batches

用途：

> 儲存 Batch 基本資料與 Batch 狀態。

Monitoring 主要使用：

```text
BatchId
Status
TotalCount
ProcessedCount
SuccessCount
FailedCount
```

實際欄位與型別以 Database-Schema.md 為準。

---

## 2.2 Images

用途：

> 儲存圖片與 Batch 的關聯。

Monitoring 主要透過：

```text
ImageId
BatchId
```

取得圖片與 Batch 關係。

---

## 2.3 ProcessingJobs

用途：

> 儲存 Background Processing Job 狀態。

Monitoring 主要使用：

```text
JobId
ImageId
BatchId
Status
RetryCount
CreatedAt
StartedAt
CompletedAt
ErrorCode
ErrorMessage
```

---

## 2.4 ProcessingLogs

用途：

> 儲存圖片 Processing History。

主要欄位：

```text
ImageId
ProcessingStep
Status
DurationMs
ErrorCode
ErrorMessage
RetryCount
Timestamp
TraceId
```

---

## 2.5 Database Index

Monitoring 查詢需要支援：

```text
Batch Status
Job Status
Processing History
Failure Records
```

建議索引：

```text
Images.BatchId

ProcessingJobs.BatchId
ProcessingJobs.Status
ProcessingJobs.ImageId

ProcessingLogs.ImageId
ProcessingLogs.Timestamp
ProcessingLogs.Status
```

Processing History 建議：

```text
INDEX(
    ProcessingLogs.ImageId,
    ProcessingLogs.Timestamp
)
```

實際 Schema、Index 名稱與資料型別以 **Database-Schema.md** 為唯一依據。

---

# 3. API

## 3.1 API Base

```text
/api/v1
```

---

## 3.2 Get Batch Status

```http
GET /api/v1/images/batches/{batchId}/status
```

用途：

> 查詢 Batch 最新處理狀態。

Request Body：

```text
None
```

Response：

```json
{
  "success": true,
  "data": {
    "batchId": "batch-001",
    "totalCount": 100,
    "processedCount": 80,
    "successCount": 78,
    "failedCount": 2,
    "progressPercentage": 80,
    "status": "Processing"
  }
}
```

---

## 3.3 Get Processing Jobs

```http
GET /api/v1/batches/{batchId}/jobs
```

用途：

> 查詢 Batch 所有 Background Jobs。

Query Parameters：

```text
page
pageSize
status
```

例如：

```text
?page=1&pageSize=50&status=Failed
```

---

## 3.4 Get Processing Logs

```http
GET /api/v1/images/{imageId}/processing-logs
```

用途：

> 查詢指定圖片完整 Processing History。

Response：

```json
{
  "success": true,
  "data": [
    {
      "imageId": 102,
      "step": "EXIF_PARSING",
      "status": "Success",
      "durationMs": 25,
      "retryCount": 0,
      "timestamp": "2026-08-31T10:00:01Z"
    },
    {
      "imageId": 102,
      "step": "PHASH_GENERATION",
      "status": "Success",
      "durationMs": 183,
      "retryCount": 0,
      "timestamp": "2026-08-31T10:00:02Z"
    }
  ]
}
```

---

## 3.5 Get Batch Processing Logs

```http
GET /api/v1/batches/{batchId}/processing-logs
```

用途：

> 查詢指定 Batch 的 Processing Logs。

Query Parameters：

```text
page
pageSize
status
step
```

例如：

```text
?page=1&pageSize=100&status=Failed&step=PHASH_GENERATION
```

---

## 3.6 Get Job Detail

```http
GET /api/v1/jobs/{jobId}
```

用途：

> 查詢單一 Background Job 詳細狀態。

Response：

```json
{
  "success": true,
  "data": {
    "jobId": 1001,
    "imageId": 102,
    "batchId": "batch-001",
    "status": "Failed",
    "retryCount": 3,
    "errorCode": "GEO_SERVICE_ERROR",
    "createdAt": "2026-08-31T10:00:00Z",
    "startedAt": "2026-08-31T10:00:01Z",
    "completedAt": "2026-08-31T10:00:08Z"
  }
}
```

---

## 3.7 Health Check API

### Application Health

```http
GET /health
```

### Liveness

```http
GET /health/live
```

### Readiness

```http
GET /health/ready
```

---

## 3.8 SignalR Hub

```text
/api/v1/hubs/monitoring
```

支援：

```text
ProcessingStarted
ProcessingCompleted
ProcessingFailed
BatchProgressUpdated
BatchCompleted
```

Batch Group：

```text
batch:{batchId}
```

---

## 3.9 Unified Error Response

所有 Monitoring API 使用統一格式：

```json
{
  "success": false,
  "error": {
    "code": "BATCH_NOT_FOUND",
    "message": "Batch was not found.",
    "traceId": "00-abc123"
  }
}
```

不得回傳：

```text
Exception
StackTrace
Connection String
Internal Path
Database Error Details
```

---

## 3.10 Error Codes

| Code | HTTP | 說明 |
|---|---:|---|
| BATCH_NOT_FOUND | 404 | 找不到 Batch |
| IMAGE_NOT_FOUND | 404 | 找不到圖片 |
| JOB_NOT_FOUND | 404 | 找不到 Job |
| LOG_NOT_FOUND | 404 | 找不到 Processing Log |
| MONITORING_UNAVAILABLE | 503 | Monitoring 暫時不可用 |
| INTERNAL_ERROR | 500 | 系統內部錯誤 |

---

# 4. 測試

## 4.1 Unit Test

至少測試：

- Progress Calculation
- Progress 0–100 Boundary
- Batch Status Mapping
- Job Status Mapping
- Processing Log Mapping
- Event Mapping
- Pagination
- Health Status
- Worker Status
- Queue Metrics

### Progress

必須驗證：

```text
0 / 100 → 0%
50 / 100 → 50%
100 / 100 → 100%
```

且不得超過：

```text
100%
```

---

## 4.2 Integration Test

測試：

```text
API
 ↓
Monitoring Service
 ↓
Repository
 ↓
EF Core
 ↓
Database
```

驗證：

- Batch Status
- Job Query
- Job Detail
- Processing Log Query
- Pagination
- Error Handling

---

## 4.3 SignalR Integration Test

測試：

```text
Worker
 ↓
Monitoring Event
 ↓
SignalR
 ↓
Client
```

至少驗證：

- ProcessingStarted
- ProcessingCompleted
- ProcessingFailed
- BatchProgressUpdated
- BatchCompleted
- Batch Group

---

## 4.4 Playwright E2E

主要流程：

```text
Upload
   ↓
Processing
   ↓
Monitoring Dashboard
   ↓
Realtime Progress
   ↓
Completed
```

### E2E-MON-01 — Batch Status

```text
Given:
使用者已上傳 Batch

When:
開啟 Processing Dashboard

Then:
顯示 Batch TotalCount
```

### E2E-MON-02 — Realtime Progress

```text
Given:
Batch 正在 Processing

When:
Worker 處理圖片

Then:
Dashboard Progress 即時更新
```

### E2E-MON-03 — Processing Failure

```text
Given:
圖片 Processing Failed

When:
Failure Event 發生

Then:
Dashboard 顯示 Failed 狀態
```

### E2E-MON-04 — Batch Completed

```text
Given:
Batch 正在 Processing

When:
所有圖片處理結束

Then:
Dashboard 顯示 Completed
```

### E2E-MON-05 — Processing History

```text
Given:
圖片存在 Processing History

When:
使用者開啟 Image Detail

Then:
顯示 Processing Logs
```

### E2E-MON-06 — Partial Failure

```text
Given:
Batch 有部分圖片失敗

When:
Batch 完成

Then:
顯示 SuccessCount
並顯示 FailedCount
```

---

## 4.5 Acceptance Criteria

- [ ] 可以查詢 Batch Status
- [ ] 可以查詢 Batch Progress
- [ ] Progress 介於 0–100
- [ ] 可以查詢 Processing Jobs
- [ ] 可以查詢 Job Detail
- [ ] 可以查詢 Processing Logs
- [ ] 支援 Processing Step Status
- [ ] 支援 ProcessingStarted Event
- [ ] 支援 ProcessingCompleted Event
- [ ] 支援 ProcessingFailed Event
- [ ] 支援 BatchProgressUpdated Event
- [ ] 支援 BatchCompleted Event
- [ ] 支援 SignalR
- [ ] 支援 Batch SignalR Group
- [ ] 支援 TraceId
- [ ] 支援 Health Check
- [ ] 支援 Liveness
- [ ] 支援 Readiness
- [ ] 支援 Worker Status
- [ ] 支援 Queue Metrics
- [ ] Processing Logs 支援 Pagination
- [ ] API 使用統一 Error Response
- [ ] 敏感資訊不得寫入 Log
- [ ] Unit Test 完成
- [ ] Integration Test 完成
- [ ] SignalR Test 完成
- [ ] Playwright E2E Test 完成

---

## 4.6 Definition of Done

Monitoring Module 完成前必須確認：

```text
Monitoring Service
      ↓
Batch Status
      ↓
Job Monitoring
      ↓
Processing Logs
      ↓
SignalR
      ↓
Health Check
      ↓
Logging
      ↓
Unit Test
      ↓
Integration Test
      ↓
SignalR Test
      ↓
Playwright E2E
      ↓
API Test
      ↓
Code Review
      ↓
Completed
```

必須確認：

```text
Build                    ✓
Unit Test                ✓
Integration Test         ✓
SignalR Test             ✓
Playwright E2E           ✓
API Test                 ✓
Health Check             ✓
Logging                  ✓
```

---

## 4.7 Agent 測試規則

AI Agent 不得為了讓測試通過而修改：

- API Contract
- Event Contract
- Batch Status
- Processing Log 結構
- Database Schema
- TraceId 規則
- SignalR Group 規則

如果測試與規格衝突：

```text
Stop
 ↓
Report Conflict
 ↓
Explain Impact
 ↓
Wait for Approval
```

---

## 4.8 Future Extension

未來可以擴充：

```text
OpenTelemetry
      ↓
Distributed Tracing
      ↓
Metrics
      ↓
Logs
      ↓
Cloud Monitoring
```

或：

```text
Prometheus
Grafana
```

但：

> **MVP 不要求導入完整 Observability Stack。**

Future extension 不得破壞現有 Monitoring API、Event Contract 與 Module Boundary。