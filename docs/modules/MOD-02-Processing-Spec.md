# Processing Module Specification

**文件名稱：** Processing Module Specification  
**模組編號：** MOD-02  
**模組名稱：** Processing Module  
**對應系統：** Photo Processing & Intelligent Image Analysis Platform  
**架構：** Clean Architecture  
**技術：** ASP.NET Core / C# / EF Core  
**文件用途：** AI Agent Development Specification  
**文件版本：** V1.0  
**文件狀態：** Development  

---

# 1. 規格

## 1.1 模組目的

Processing Module 負責管理系統中的背景處理工作。

主要職責：

- 建立 Processing Job
- 將工作加入 Queue
- 由 Background Worker 執行工作
- 管理 Processing State
- 執行指定的 Processing Steps
- 管理 Retry
- 處理 Application Restart 後的 Job Recovery
- 確保 Processing Idempotency
- 控制背景工作並行數量
- 記錄 Processing Log
- 更新 Batch Processing Progress
- 發布 Processing Events

本模組的核心目的，是將：

> HTTP Request

與：

> 長時間背景處理工作

分離，使 API 不需要等待完整影像處理流程完成。

---

## 1.2 模組邊界

### 本模組負責

- Job 建立與管理
- Queue
- Background Worker
- Processing State
- Processing Step 執行控制
- Retry
- Recovery
- Idempotency
- Concurrency Control
- Processing Log
- Batch Progress
- Processing Event

### 本模組不負責

以下功能由其他 Module 負責：

- EXIF Parsing
- GPS Reverse Geocoding
- SHA-256 計算
- Exact Duplicate Detection
- pHash 計算
- Visual Similarity Analysis
- Photo Quality Analysis
- Version Analysis
- Naming Rule Implementation
- File Rename
- CSV Export

Processing Module 僅負責**協調與執行這些處理工作的生命週期**，不實作上述功能本身。

---

## 1.3 System Workflow 責任邊界

本模組**不定義系統整體 Workflow**。

系統層級的：

- Module 執行順序
- 完整 Processing Pipeline
- Upload → Processing → Metadata → Analysis → Naming → Report
- 各 Module 之間的依賴關係

由 **System-Level Specification** 定義。

Processing Module 只負責：

> Job → Queue → Worker → Execute Steps → Persist Result → Update Status → Publish Event

因此，AI Agent 不應在本模組自行新增或修改系統層級 Workflow。

---

## 1.4 Processing Job

Processing Job 代表一個需要背景執行的處理工作。

Job 至少包含：

- JobId
- ImageId
- BatchId
- Workflow
- Status
- RetryCount
- CreatedAt
- StartedAt
- CompletedAt
- ErrorCode
- ErrorMessage

### Workflow

Workflow 是由呼叫端指定的 Workflow Identifier。

Processing Module 不負責定義系統有哪些 Workflow，而是依照系統所提供的 Workflow Definition 執行。

---

## 1.5 Processing State

MVP 使用以下狀態：

- `Pending`
- `Processing`
- `Completed`
- `Failed`

### State Transition

允許：

```text
Pending → Processing
Processing → Completed
Processing → Failed
Failed → Pending
```

其中：

```text
Failed → Pending
```

代表 Retry。

不允許的 State Transition 必須拒絕，並回傳：

```text
INVALID_STATE_TRANSITION
```

---

## 1.6 Processing Steps

Processing Module 提供 Step Execution 機制。

Processing Step 可包含：

```text
VALIDATION
EXIF_PARSING
GEO_LOOKUP
SHA256_CALCULATION
EXACT_DUPLICATE_CHECK
PHASH_GENERATION
VISUAL_SIMILARITY
QUALITY_ANALYSIS
VERSION_ANALYSIS
NAMING
FILE_RENAME
COMPLETED
```

以上僅代表 Processing Module 可以執行的 Step 類型。

實際使用哪些 Step，以及 Step 的組合與順序，由 System-Level Workflow 定義。

Processing Module 不應自行改變 Step 順序。

---

## 1.7 Queue

Processing Module 使用：

```text
System.Threading.Channels
```

作為 MVP Background Processing Queue。

Queue 使用 **Bounded Channel**，避免工作無限制增加造成記憶體持續成長。

Queue Item 至少包含：

- JobId
- ImageId
- BatchId
- Workflow

### Queue Interface

```csharp
public interface IProcessingQueue
{
    Task EnqueueAsync(
        ProcessingJob job,
        CancellationToken cancellationToken);

    ValueTask<ProcessingJob> DequeueAsync(
        CancellationToken cancellationToken);
}
```

Queue 必須透過 Interface 使用。

Application Layer 不應直接依賴 Channel 的具體實作。

---

## 1.8 Background Worker

Background Worker 使用：

```csharp
BackgroundService
```

執行 Queue 中的 Processing Job。

基本執行責任：

1. 從 Queue 取得 Job
2. 驗證 Job State
3. 執行 Workflow
4. 執行 Processing Steps
5. 儲存處理結果
6. 更新 Job Status
7. 記錄 Processing Log
8. 發布 Processing Event

Worker 不應建立無限制的 Task。

---

## 1.9 Controlled Concurrency

Processing Worker 必須限制同時執行的 Job 數量。

例如：

```text
MaxConcurrency = 4
```

實際數值由 Configuration 決定。

目的：

- 避免 CPU 過度使用
- 避免記憶體過度使用
- 避免同時處理大量圖片造成系統不穩定

---

## 1.10 Retry

Processing Module 必須支援 Retry。

### Retryable Error

以下錯誤可以 Retry：

- HTTP 408
- HTTP 429
- HTTP 5xx
- Timeout
- Temporary Storage Error
- Temporary Geo API Error

### Non-Retryable Error

以下錯誤不應 Retry：

- HTTP 400
- HTTP 401
- HTTP 403
- HTTP 404
- Invalid Image
- Invalid Request
- Invalid Configuration
- 不可恢復的 Processing Error

### Retry Strategy

使用 Exponential Backoff：

```text
Retry 1 → 1 second
Retry 2 → 2 seconds
Retry 3 → 4 seconds
```

最大 Retry 次數必須可透過 Configuration 設定。

---

## 1.11 Startup Recovery

Application Restart 後，Processing Module 必須檢查尚未完成的 Job。

若 Job 狀態為：

```text
Processing
```

但 Application 已經重新啟動，則視為可能中斷的 Job。

MVP：

```text
Processing → Pending → Requeue
```

重新加入 Queue。

避免 Job 因 Application Restart 永久停留在 Processing。

---

## 1.12 Idempotency

Processing Module 必須避免相同工作被重複執行。

Logical Identity：

```text
ImageId + ProcessingStep
```

相同 Image 的相同 Processing Step 不應被無限制重複執行。

執行 Step 前應確認：

- 是否已有成功結果
- 是否正在被其他 Worker 執行
- 是否可以安全重新執行

若 Step 已完成且結果可重用，應跳過重複處理。

Idempotency 必須同時考慮：

- Application-level check
- Database constraint / atomic operation
- Worker concurrency

---

## 1.13 Same Image Protection

同一張圖片的相同 Processing Step 不應同時由多個 Worker 執行。

例如：

```text
Image A
 └── QUALITY_ANALYSIS
```

不能同時：

```text
Worker 1 → QUALITY_ANALYSIS
Worker 2 → QUALITY_ANALYSIS
```

應透過 Application Logic 與 Database Concurrency Control 避免。

可使用：

- Optimistic Concurrency
- Atomic Database Update
- Database Lock
- Unique Constraint

實際方案依 Database Design 決定。

---

## 1.14 Processing Log

Processing Module 必須記錄每個 Processing Step 的執行資訊。

至少包含：

- JobId
- ImageId
- Step
- Status
- ErrorCode
- ErrorMessage
- DurationMs
- RetryCount
- Timestamp

Processing Log 用於：

- Debug
- Error Investigation
- Processing Monitoring
- Performance Analysis
- Audit

### Log 原則

Production Environment 不應大量輸出無意義的 Trace / Debug Log。

重要資訊應包含：

- TraceId
- JobId
- BatchId
- ImageId
- Workflow
- ProcessingStep
- Status
- DurationMs
- RetryCount
- ErrorCode

---

## 1.15 Batch Progress

Processing Module 必須支援 Batch Processing Progress。

Batch 至少需要：

- TotalCount
- ProcessedCount
- SuccessCount
- FailedCount
- Status

Progress：

```text
Progress = ProcessedCount / TotalCount × 100
```

Processing Module 負責更新 Processing Progress。

UI 顯示則由 API / Realtime Layer 負責。

---

## 1.16 Processing Events

Processing Module 可以發布 Application Event。

例如：

```text
ProcessingStarted
ProcessingStepCompleted
ProcessingFailed
ProcessingCompleted
BatchProgressUpdated
```

Processing Module 不直接處理 SignalR Connection。

SignalR Hub 屬於 API / Realtime Layer。

Processing Module 只負責發布 Event。

---

## 1.17 Application Interfaces

### IProcessingService

```csharp
public interface IProcessingService
{
    Task<ProcessingJob> CreateJobAsync(
        CreateProcessingJobRequest request,
        CancellationToken cancellationToken);

    Task<ProcessingJob?> GetJobAsync(
        Guid jobId,
        CancellationToken cancellationToken);

    Task RetryJobAsync(
        Guid jobId,
        CancellationToken cancellationToken);

    Task ProcessJobAsync(
        Guid jobId,
        CancellationToken cancellationToken);
}
```

---

### IWorkflowOrchestrator

```csharp
public interface IWorkflowOrchestrator
{
    Task<WorkflowDefinition> ResolveWorkflowAsync(
        string workflow,
        CancellationToken cancellationToken);

    Task ExecuteAsync(
        ProcessingJob job,
        CancellationToken cancellationToken);
}
```

Workflow Orchestrator 負責：

- 取得 Workflow Definition
- 執行 Step
- 處理 Step Result
- 處理 Fatal Error
- 處理 Warning
- 決定是否繼續執行

但不負責定義系統層級 Workflow。

---

### IProcessingQueue

```csharp
public interface IProcessingQueue
{
    Task EnqueueAsync(
        ProcessingJob job,
        CancellationToken cancellationToken);

    ValueTask<ProcessingJob> DequeueAsync(
        CancellationToken cancellationToken);
}
```

---

### IRetryPolicy

```csharp
public interface IRetryPolicy
{
    bool ShouldRetry(
        Exception exception,
        int retryCount);

    TimeSpan GetDelay(
        int retryCount);

    int GetNextRetryCount(
        int retryCount);
}
```

---

# 2. 資料表

本章只定義 Processing Module 所使用的資料結構。

資料表中的 Workflow、State 等欄位屬於資料本身，不在此描述業務流程。

完整 Database Schema 由：

```text
Database-Schema.md
```

統一管理。

---

## 2.1 ProcessingJobs

用途：

> 儲存 Processing Job 的基本資料。

| 欄位 | 型別 | 說明 |
|---|---|---|
| Id | GUID | Job Identifier |
| ImageId | GUID | 對應 Image |
| BatchId | GUID | 對應 Batch |
| Workflow | VARCHAR | Workflow Identifier |
| Status | VARCHAR | Processing Status |
| RetryCount | INT | Retry 次數 |
| ErrorCode | VARCHAR | 錯誤代碼 |
| ErrorMessage | TEXT | 錯誤訊息 |
| TraceId | VARCHAR | Trace Identifier |
| CreatedAt | DATETIME | 建立時間 |
| StartedAt | DATETIME NULL | 開始時間 |
| CompletedAt | DATETIME NULL | 完成時間 |

### Index

建議：

```text
INDEX(ImageId)
INDEX(BatchId)
INDEX(Status)
```

---

## 2.2 ProcessingLogs

用途：

> 儲存 Processing Step 執行紀錄。

| 欄位 | 型別 | 說明 |
|---|---|---|
| Id | GUID | Log Identifier |
| JobId | GUID | Processing Job |
| ImageId | GUID | Image Identifier |
| Step | VARCHAR | Processing Step |
| Status | VARCHAR | Step Status |
| ErrorCode | VARCHAR NULL | 錯誤代碼 |
| ErrorMessage | TEXT NULL | 錯誤訊息 |
| DurationMs | BIGINT | 執行時間 |
| RetryCount | INT | Retry 次數 |
| CreatedAt | DATETIME | 建立時間 |

### Index

建議：

```text
INDEX(JobId)
INDEX(ImageId)
INDEX(Step)
INDEX(CreatedAt)
```

ProcessingLogs 可保留多次執行紀錄，因此不應單純以：

```text
ImageId + Step
```

建立唯一限制，否則 Retry / Execution History 將無法完整保存。

---

## 2.3 Shared Tables

Processing Module 會使用：

```text
Images
Batch
```

但不負責定義其完整 Schema。

這些資料表的所有權與完整結構由各自 Module / Database Schema 文件管理。

Processing Module 僅透過既定 Repository / Application Interface 使用。

---

# 3. API

Processing API 負責提供 Processing Job 的建立、查詢與 Retry。

Base URL：

```text
/api/v1
```

---

## 3.1 Create Processing Job

```http
POST /api/v1/processing/jobs
```

### Request

```json
{
  "imageId": "guid",
  "batchId": "guid",
  "workflow": "default"
}
```

### Response

```http
202 Accepted
```

```json
{
  "jobId": "guid",
  "status": "Pending"
}
```

Processing Job 建立後不等待背景工作完成。

---

## 3.2 Get Processing Job

```http
GET /api/v1/processing/jobs/{jobId}
```

### Response

```json
{
  "jobId": "guid",
  "imageId": "guid",
  "batchId": "guid",
  "workflow": "default",
  "status": "Processing",
  "retryCount": 0,
  "createdAt": "2026-01-01T10:00:00Z",
  "startedAt": "2026-01-01T10:00:01Z",
  "completedAt": null
}
```

---

## 3.3 Retry Processing Job

```http
POST /api/v1/processing/jobs/{jobId}/retry
```

### Response

```http
202 Accepted
```

```json
{
  "jobId": "guid",
  "status": "Pending"
}
```

只有允許 Retry 的 Job 才可以執行。

---

## 3.4 Error Response

### Processing Not Found

```http
404 Not Found
```

```json
{
  "code": "PROCESSING_NOT_FOUND",
  "message": "Processing job was not found."
}
```

### Invalid State Transition

```http
409 Conflict
```

```json
{
  "code": "INVALID_STATE_TRANSITION",
  "message": "The requested state transition is not allowed."
}
```

### Processing Failed

```http
500 Internal Server Error
```

```json
{
  "code": "PROCESSING_FAILED",
  "message": "Processing failed."
}
```

### Queue Error

```http
500 Internal Server Error
```

```json
{
  "code": "QUEUE_ERROR",
  "message": "Unable to enqueue processing job."
}
```

### Worker Unavailable

```http
503 Service Unavailable
```

```json
{
  "code": "WORKER_UNAVAILABLE",
  "message": "Processing worker is unavailable."
}
```

### Internal Error

```http
500 Internal Server Error
```

```json
{
  "code": "INTERNAL_ERROR",
  "message": "An unexpected error occurred."
}
```

---

# 4. 測試

## 4.1 Unit Test

Processing Module 必須建立 Unit Tests。

至少包含：

### State Machine

測試：

- Pending → Processing
- Processing → Completed
- Processing → Failed
- Failed → Pending
- Invalid State Transition

---

### Retry

測試：

- Retryable Error
- Non-Retryable Error
- Retry Count
- Exponential Backoff
- Maximum Retry Count

---

### Workflow Execution

測試：

- Workflow Resolution
- Step Execution
- Step Success
- Step Failure
- Fatal Error
- Warning
- Continue Processing

---

### Idempotency

測試：

- 已完成 Step 不重複執行
- 相同 Image + Step 不重複建立結果
- 重複 Queue Item
- Retry 後不產生錯誤的重複結果

---

### Queue

測試：

- Enqueue
- Dequeue
- Empty Queue
- Cancellation
- Bounded Queue 行為

---

### Concurrency

測試：

- MaxConcurrency
- 同一 Image + Step 不同時執行
- Multiple Jobs 同時執行
- Database Concurrency Conflict

---

### Recovery

測試：

- Processing Job Recovery
- Application Restart 後重新 Queue
- 已完成 Job 不重複 Queue

---

## 4.2 Integration Test

Integration Test 驗證：

```text
API
 ↓
Processing Service
 ↓
EF Core
 ↓
Database
 ↓
Queue
 ↓
Background Worker
```

至少測試：

1. Create Job
2. Job Enqueue
3. Worker Process
4. Job Completed
5. Job Failed
6. Retry
7. Startup Recovery
8. Batch Progress

---

## 4.3 E2E Test

使用 Playwright 驗證使用者實際操作流程。

### Successful Processing

```text
Upload Photos
↓
Select Workflow
↓
Start Processing
↓
Processing Page
↓
Progress Update
↓
Processing Completed
```

### Failed Processing

```text
Upload Invalid Image
↓
Processing
↓
Processing Failed
↓
Error Display
```

### Retry

```text
Processing Failed
↓
Retry
↓
Processing
↓
Completed
```

---

# 5. Acceptance Criteria

Processing Module 必須符合以下條件：

### Queue

- 使用 Bounded Channel
- 不允許無限制 Queue Growth
- Queue 可正常 Enqueue / Dequeue

### Background Worker

- 使用 BackgroundService
- 可持續處理 Queue
- Application Shutdown 時可正常停止

### Processing State

- State Transition 符合規格
- Invalid Transition 必須拒絕

### Retry

- Retryable Error 可以 Retry
- Non-Retryable Error 不 Retry
- 使用 Exponential Backoff
- Retry 次數可設定

### Recovery

- Application Restart 後 Processing Job 不會永久卡住
- Interrupted Job 可以重新 Queue

### Idempotency

- 相同 Image + Step 不會被無限制重複處理
- Retry 不會產生錯誤的重複結果

### Concurrency

- Worker 數量受到 Configuration 控制
- 同一 Image + Step 不應同時執行

### Logging

- 每個 Processing Step 都能留下必要 Log
- Error 包含 ErrorCode
- 可以透過 JobId / ImageId 追蹤處理紀錄

### Batch Progress

- 能正確計算 Batch Progress
- Success / Failed 數量正確

### Realtime Event

- Processing Module 可以發布 Processing Events
- 不直接依賴 SignalR Hub

### Testing

- Unit Test 通過
- Integration Test 通過
- E2E Test 通過

---

# 6. AI Agent Development Rules

AI Agent 實作本模組時必須遵守以下規則。

## 6.1 Architecture

必須維持：

```text
API
 ↓
Application
 ↓
Domain
 ↓
Infrastructure
```

不得讓 Domain / Application 直接依賴：

- EF Core DbContext
- Channel
- SignalR Hub
- File System
- Cloud Storage Implementation

---

## 6.2 Queue

Queue 必須透過：

```text
IProcessingQueue
```

使用。

不得在 Application Layer 直接建立 Channel。

---

## 6.3 Worker

Background Processing 必須使用：

```text
BackgroundService
```

不得使用無限制：

```csharp
Task.Run(...)
```

建立大量背景工作。

---

## 6.4 Retry

不得對所有 Exception 無條件 Retry。

必須根據：

```text
Retryable
Non-Retryable
```

分類。

---

## 6.5 Idempotency

任何 Processing Step 都必須考慮 Idempotency。

不得假設：

```text
一個 Job 只會執行一次。
```

---

## 6.6 Workflow Boundary

AI Agent 不得自行新增：

- 新 Workflow
- Workflow Step 順序
- Module 執行順序
- System-Level Pipeline

若需求需要修改 System Workflow，必須先修改 System-Level Specification。

---

## 6.7 Database Boundary

不得在本模組自行修改其他 Module 所擁有的資料表 Schema。

若需要新增欄位或資料表：

```text
Identify Requirement
↓
Explain Impact
↓
Propose Change
↓
Wait for Approval
```

---

## 6.8 API Contract

不得自行修改：

- API Route
- HTTP Method
- Request Schema
- Response Schema
- Error Code

若需要修改 API Contract，必須先提出變更。

---

## 6.9 Test Requirement

新增 Processing Logic 時，必須同步新增適當測試。

至少考慮：

```text
Happy Path
Failure
Retry
Concurrency
Idempotency
Cancellation
```

---

# 7. Definition of Done

Processing Module 完成時必須確認：

- [ ] Processing Job 完成
- [ ] Queue 完成
- [ ] Background Worker 完成
- [ ] Processing State Machine 完成
- [ ] Processing Step Execution 完成
- [ ] Retry Policy 完成
- [ ] Startup Recovery 完成
- [ ] Idempotency 完成
- [ ] Concurrency Control 完成
- [ ] Processing Log 完成
- [ ] Batch Progress 完成
- [ ] Processing Events 完成
- [ ] API 完成
- [ ] Unit Tests 完成
- [ ] Integration Tests 完成
- [ ] E2E Tests 完成
- [ ] Acceptance Criteria 全部通過

---

# 8. Module Completion

Processing Module 的最終責任可以簡化為：

```text
Job Created
    ↓
Pending
    ↓
Queue
    ↓
Background Worker
    ↓
Processing
    ↓
Execute Processing Steps
    ↓
Persist Result
    ↓
Update Status
    ↓
Publish Event
    ↓
Completed
```

失敗：

```text
Processing
    ↓
Failed
    ↓
Retry
    ↓
Pending
    ↓
Queue
```

Application Restart：

```text
Processing
    ↓
Application Restart
    ↓
Recovery
    ↓
Pending
    ↓
Queue
```

以上僅描述 Processing Module 的內部執行機制。

**系統整體 Workflow、Module 之間的執行順序與功能組合，不屬於本文件。**