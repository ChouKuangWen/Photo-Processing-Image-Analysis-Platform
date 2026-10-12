# Monitoring Module Specification

> **規格修訂草稿（2026-10-12）**：以 develop `8ff1dd983af2ffa68dfc93893f9cca61fadb2c90` 為基準。
> MOD-01 feature `a1bb29e1d7cf704a202adbe1c14741b6441575be` 僅為交接證據，未合併進 develop。
> 已核准與待決策內容分開記錄於 [規格審閱與決策紀錄](../reviews/Specification-Revision-Review.md)。
> F-01～F-08已核准並同步至最終契約章；同題舊待決文字由最終章取代。引擎/精確版本N-01與實作驗收仍未完成；不是TASK、實作或PASS授權。


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
Processing State Change／Step Result
        ↓
由 MOD-02 單一 lifecycle writer 確認 History 已提交
        ↓
Read Persisted State／History
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

TotalCount == 0 時 progressPercentage = 0；沿用 MOD-01 Decimal 與既有公式，不建立新 precision / rounding model。

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

### D-07 aggregate 投影

Primary Job為原UploadJob，binding未確認相關Job不開始執行；secondary不改Uploadcounts／Image.Status。
全Primary Pending才BatchPending；Failed Retry後仍有其他終態圖片則BatchProcessing。全終態且有成功為Completed，全失敗為Failed，空Batch保留Pending／0counts／0progress。
終態重試清除CompletedAt，PrimaryFailed→Pending減Processed／Failed各一；完成再加Processed／Success，所有delta與Job／Run同SQLcommit。
Query只讀，不能以History／Queue counts重算或自行修復binding；完整規則依MOD-02 §1.15。

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

## 1.10 Processing Log（D-06）

History來自MOD-02單一writer，Schema見 `docs/Database-Schema.md` §5。Status為Running／Success／Warning／Failed／Interrupted／Skipped，不是Job enum。
每個實際invocation有AttemptNo／RetryOrdinal／ResumeNo；Shutdown恢復沿用槽位但新增invocation，保留舊Interrupted History。
Step從checkpoint、RunId從Run關聯取得；Timestamp是record建立時間、StartedAt／CompletedAt是invocation時間，DurationMs未完成可NULL。
既有Query DTO retryCount映射RetryOrdinal，Job Query retryCount才是跨Step／Run累計值。
Active Step以checkpoint.Status為準，History的最後一筆不是唯一active source。
Log Query不建立／更新History、不觸發Retry／Recovery、不重複MOD-01Uploadfailure事件。

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

## 1.12 Monitoring Events（D-06）

MOD-02單一writer在commit確認後發布，MOD-09只轉送。共用schemaVersion=1、eventId GUID、occurredAt DateTimeOffset、traceId、batchId、imageId、jobId、runId；Batch事件沒有單一Job時相關id為NULL。

| Event | payload／語意 |
|---|---|
| ProcessingStarted | Job Claim／Active Run／首個Running Log已提交 |
| ProcessingStepStarted | step／attemptNo／retryOrdinal／resumeNo |
| ProcessingStepCompleted | identity＋outcome Success／Warning／Skipped＋durationMs；不是Job完成 |
| ProcessingStepFailed | identity＋safe errorCode＋willRetry；不是必然JobFailed |
| ProcessingCompleted | Job／Run已Completed |
| ProcessingFailed | Job／Run已Failed、safe errorCode |
| BatchProgressUpdated | 原aggregate欄位快照 |
| BatchCompleted | status Completed／Failed＋aggregate；不是所有StepCompleted都發 |

```json
{
  "schemaVersion": 1,
  "eventId": "e0f0d28d-b4cf-4cda-a3ed-594eec640001",
  "occurredAt": "2026-10-12T10:00:01+08:00",
  "traceId": "00-abc123",
  "batchId": "e0f0d28d-b4cf-4cda-a3ed-594eec640002",
  "imageId": 102,
  "jobId": 1001,
  "runId": 2001,
  "event": "ProcessingStepCompleted",
  "step": "PHASH_GENERATION",
  "attemptNo": 2,
  "retryOrdinal": 0,
  "resumeNo": 1,
  "outcome": "Success",
  "durationMs": 183
}
```

```json
{
  "schemaVersion": 1,
  "eventId": "e0f0d28d-b4cf-4cda-a3ed-594eec640003",
  "occurredAt": "2026-10-12T10:00:02+08:00",
  "traceId": "00-abc123",
  "batchId": "e0f0d28d-b4cf-4cda-a3ed-594eec640002",
  "imageId": null,
  "jobId": null,
  "runId": null,
  "event": "BatchCompleted",
  "status": "Completed",
  "totalCount": 100,
  "processedCount": 100,
  "successCount": 98,
  "failedCount": 2,
  "progressPercentage": 100
}
```

Geo耗盡最後Failed invocation與fallback Warning StepCompleted分開，不能因此發Job ProcessingFailed。
ShutdownInterrupted不發JobFailed；T5後若Batch又進入新的終態可再發BatchCompleted，Client以當前REST為準。
best-effort通知不保證必達／恰好一次／重啟重送，沒有Outbox／Broker／持久event表。failure只安全SystemLog，不回滾或重跑已提交分析。

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

## 1.15 Event Ordering 與重新同步

事件可遺失／重複／延遲／亂序。Client可用eventId去重，但不把occurredAt當全域順序，不假設存在未核准Sequence欄位。
REST持久化Job／checkpoint／Batch是當前狀態來源，SignalR只通知變化。斷線／亂序／無法判斷新舊時重新Query，不靠event delta恢復counts。
事件delivery failure不會造成分析重跑；batch snapshot沒有逐通知必達保證。

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

> **Microsoft.Extensions.Logging / ILogger<T> 共用 abstraction**

MOD-01 不安裝或綁定 Serilog provider。Serilog provider 與完整 Monitoring provider integration 留待後續 MOD-09 / composition decision；Application 不引用 Infrastructure。沿用 MOD-01 Logging Ownership / Security，不建立平行 abstraction 或重複 failure events。

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

## 1.21 Application Interface 與映射

BatchStatus query保留MOD-01既有contract。History query需要帶入page／pageSize及可選step、status、jobId、runId，回傳含items與page／pageSize／totalCount的Query DTO，完整規則見§3.4～§3.5。
不得沿用舊無pagination參數的GetProcessingLogsAsync並宣稱符合本契約；具體Application type／method signatures於Task前review確認，不引用EF Entity／SQL／SignalR於Application。
Event Publisher能表達§1.12全部事件，MOD-02發布、MOD-09轉送；不以舊四方法示意遺漏StepStarted／StepFailed／BatchCompleted。
MOD-02 GET /api/v1/processing/jobs/{id}與本模組GET /api/v1/jobs/{id}共用同一持久化Job source，是不同既有Query boundary；各自保留已列payload及PROCESSING_NOT_FOUND／JOB_NOT_FOUND，不宣稱同一API或新增副作用。

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
ILogger<T>（provider integration 另行決策）
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

## 跨模組修訂與 MOD-09 獨立審閱

MOD-02 更新 Job／Image／Batch，MOD-09 只讀持久化 aggregate、History 與 Application Event，不接管 Claim／Retry／Recovery，也不建立平行 Batch API。
沿用 MOD-01 TASK-11 的 endpoint、Decimal、TotalCount=0 → 0 與 0–100 clamp；既有 Upload System Log 由原 producer 擁有，不重複發送。
canonical Log schema 見共用文件；事件抵達順序不保證，REST DB 是重新同步來源。

- M09-01／Major（文件契約已解除）：D-06核准page1／pageSize50、1～500、server pagination、Timestamp+Id排序與一致性讀取；§1.21／§3.4～§3.5已同步。未有runtime證據。
- M09-02／Major（文件契約已解除）：§1.12已統一StepStarted／Completed／Failed與Job／Batch事件，Writer commit後通知，Warning不發JobFailed。
- M09-03／Major（資料來源契約已解除）：active state讀checkpoint；History具Running與terminal Status、每invocation identity，不靠最後Log猜Job狀態。
- M09-04／Major（文件矛盾已解除）：已核准best-effort事件不含全域Sequence，不保證排序／送達；REST resync為狀態來源，§1.15已改正。
- M09-05／Risk：§1.17 Channel metrics 需定義 accepted/dequeued/active/completed、取消、重複通知與重啟歸零，不能把 Queue notification counts 當 DB Job counts；Readiness／部署先核對舊Instance已停止及Worker可消費；Queue counter定義仍待U-02，不把通知數當DB Job數。
- M09-06／Major（來源／boundary歧義已解除）：§1.21明列同DBsource、不同既有Query boundary與各自not-found code／payload，沒有合併route。

新增驗收：log mapping、穩定分頁、retry attempt 不重複寫入、Step completion 不誤發 Job completion、out-of-order event 後 REST resync、Queue counter cancellation／duplicate、partial Batch failure；待相關契約定案後才可判 PASS。

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

## 共用 Schema 與交接基線

physical mapping 以 `docs/Database-Schema.md` 已確認部分為共同依據；尚未定案的欄位、索引與關係明列為待決策，不假設不存在的原始 Database-Schema.md 已提供答案。
沿用 MOD-01 Image.Id BIGINT、BatchId uniqueidentifier、DateTimeOffset／datetimeoffset(7) 與 Unicode mapping。
SQL Server timestamp／rowversion 不是日期時間；日期不得以 TIMESTAMP mapping 實作。
MOD-01 feature 尚未合併；TASK-11／12 最新驗收維持待確認，不能把後續模組未完成判作 MOD-01 Backend 缺陷。

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

## 2.4 ProcessingLogs 與 execution Query

正式設計見 `docs/Database-Schema.md` §3～§6：Run、checkpoint、Log與Primary binding由MOD-02寫入。
Log.Id為BIGINT identity；Step、RunId透過checkpoint關聯，沒有另一個physical Step／Log.RetryCount欄位。
DTO step映射checkpoint.Step，timestamp映射Log.Timestamp，retryCount映射Log.RetryOrdinal；JobQueryretryCount仍映射ProcessingJobs.RetryCount。
Batch／Workflow由已核對Job／Image／binding關聯取得，Query不補寫缺binding／Run、也不判斷舊Instance可否回收。
實際DB未核對；這是規格與Schema設計，不宣稱Migration完成。

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

實際 mapping 以 `docs/Database-Schema.md` 的已確認部分為依據；本節索引為建議，未決索引不得直接加入 Migration。

---

# 3. API

## 3.1 API Base

```text
/api/v1
```

---

## 3.2 Get Batch Status

此 endpoint 由 MOD-01 TASK-11 首次提供；MOD-09 重用 / 擴充同一 endpoint 與 contract，不建立第二套 API。Read-only query 不新增 lifecycle；Processing 狀態更新仍屬 MOD-02。

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

## 3.4 Get Processing Logs（D-06）

```http
GET /api/v1/images/{imageId}/processing-logs
```

| Parameter | Default／限制 |
|---|---|
| page | 1，整數>=1 |
| pageSize | 50，整數1～500 |
| step | optional，核准Step identifier（Registry仍待D-14） |
| status | optional，Running／Success／Warning／Failed／Interrupted／Skipped |
| jobId／runId | optional，正BIGINT |

sort = Timestamp DESC,Id DESC；同一次Query的totalCount與items在一致性讀取範圍取得，DB projection／server-side pagination，不全載入後切頁。
跨不同頁請求不保證同一snapshot；新增History會影響offset，長時間穩定串流/cursor不是本Pagination保證。
既有items欄位保留imageId／step／status／durationMs／retryCount／timestamp；durationMs可NULL，retryCount是Step RetryOrdinal，不是Job累計值。

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "imageId": 102,
        "step": "PHASH_GENERATION",
        "status": "Success",
        "durationMs": 183,
        "retryCount": 0,
        "timestamp": "2026-10-12T10:00:01+08:00"
      }
    ],
    "page": 1,
    "pageSize": 50,
    "totalCount": 1
  }
}
```

Image存在但無History為200／empty items／totalCount0；頁超過現有資料為empty items／保留實際totalCount。不存在Image維持404／IMAGE_NOT_FOUND。
非法pagination／enum／Id filter為400／INVALID_MONITORING_QUERY，共用error.traceId；Query不觸發Step／Retry。

## 3.5 Get Batch Processing Logs

```http
GET /api/v1/batches/{batchId}/processing-logs
```

同§3.4參數、response、sort與限制，以Job.BatchId／一致Image關聯篩選；不得以join重複同Log row。
不存在Batch維持404／BATCH_NOT_FOUND；存在無History為200空items。

```json
{
  "success": false,
  "error": {
    "code": "INVALID_MONITORING_QUERY",
    "message": "The monitoring query is invalid.",
    "traceId": "00-abc123"
  }
}
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
| INVALID_MONITORING_QUERY | 400 | pagination／enum／filter不合法 |
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

# 最終核准契約 — F-08（最小診斷與runtime metrics）

GET /api/v1/processing/batches/{batchId}/prerequisites只讀，MOD01原Query不改。正常blocked回200 data={batchId,checkedAt,ready:false,reasons,resolutionHint,count,truncated}；Batch不存在404/BATCH_NOT_FOUND；provider無法核對503/PROCESSING_PREREQUISITE_UNAVAILABLE，不能假ready。ready依完整集合核對，不以只取100個理由當確認全部；reasons安全分類BINDING_MISSING/BINDING_AMBIGUOUS/RELATIONSHIP_MISMATCH/COLLECTION_UNCONFIRMED/CONTEXT_MISSING/SIDE_EFFECT_UNCONFIRMED/AGGREGATE_INCONSISTENT，最多呈現100筆並count/truncated，identifier僅Batch/Image/Job且不揭內部路徑/SQL/證據。排序reasonCode/ImageId/JobId穩定，checkedAt表示本次核對時間，不是已持久阻擋狀態。
Query不選主/修復/排程/清理；補證據或經另授權處理矛盾後MOD02重驗解除，不提供unblock寫API、不加MOD01Status/診斷持久表。只阻擋該Batch Primary執行；其他已確認Batch正常。CreateJob與管理寫API保留驗證次序，duplicate/mismatch仍409，交接阻擋503與安全envelope，拒絕零新增/排程/aggregate副作用。
runtime按processInstanceId區分、重啟歸零、best-effort而非SQL Job truth：queuedNotifications=已成功enqueue未dequeue通知（重複也算通知）；activeInvocations=已commit Running至terminal/shutdown結束；stepRetryInvocationsTotal=首次開始新retry邏輯槽位，不含resume/保存retry；shutdownResumesTotal=恢复invocation；droppedDuplicateNotificationsTotal=DB核對無執行資格的重複通知。enqueue/dequeue失敗不更新counter，counter採thread-safe，不拿History筆數當Job數。依賴Waiting沒有Running不計activeInvocations。DB counts依ProcessingJob status另查，REST authority/Historywriter不變。
SignalR/event best-effort、commit後通知，無Outbox/事件必達/持久metrics/dashboard/跨重啟counter連續性；health/readiness不是舊process已停證据。具體handler/SQL/metrics驗收尚未實作，不稱PASS。
