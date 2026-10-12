# Processing Module Specification

> **規格修訂草稿（2026-10-12）**：以 develop `8ff1dd983af2ffa68dfc93893f9cca61fadb2c90` 為基準。
> MOD-01 feature `a1bb29e1d7cf704a202adbe1c14741b6441575be` 僅為交接證據，未合併進 develop。
> 已核准與待決策內容分開記錄於 [規格審閱與決策紀錄](../reviews/Specification-Revision-Review.md)。
> F-01～F-08已核准並同步至最終契約章；同題舊待決文字由最終章取代。引擎/精確版本N-01與實作驗收仍未完成；不是TASK、實作或PASS授權。


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
- Naming Artifact Creation
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

MVP Job 狀態維持 `Pending`、`Processing`、`Completed`、`Failed`。

| 類型 | 允許轉換 | 條件／責任 |
|---|---|---|
| Claim | Pending → Processing | MOD-02 透過 SQL Server 原子條件更新取得執行權；只有成功者可執行 |
| 正常完成 | Processing → Completed | 本次有效執行的所有必要 Step 已完成、結果已提交；Warning fallback 依模組契約繼續 |
| 終止失敗 | Processing → Failed | 不可重試錯誤或必要 Step 重試耗盡且無核准 fallback；不含 Geo Warning fallback 或 host shutdown cancellation |
| 人工 Retry | Failed → Pending | §1.10 的狀態及錯誤分類資格通過，沿用同一 Job Id |
| 中斷恢復例外 | Processing → Pending | 僅 §1.11 的單 Instance 啟動恢復，在確認舊 Instance 已停止後執行 |

Recovery transition 是明確的管理例外，不是一般 Client 或子模組可要求的狀態轉換。
Pending 的重新排程不改變狀態；Completed 不因重複 Queue Item、Retry API 或 Recovery 回到 Pending。
未列出的轉換必須拒絕並使用 `INVALID_STATE_TRANSITION`；人工 Retry 與 Claim 的狀態檢查須在 DB 原子操作內再次核對。

單次 Step transient retry 仍在同一個 `Processing` Job 內進行，不先將整個 Job 改為 Failed／Pending。
Step 結果、Job 狀態與 ProcessingLog Status 是不同契約；不能將 Step Warning／Success 當成 Job enum。
StartedAt／CompletedAt／Error／RetryCount 更新依 §1.10、§1.18～§1.22 的已核准 D-05～D-07 契約；只保留當前 Run 的 Job 投影，歷史保存在 Run／checkpoint／Log，不清空歷史。

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

### Capacity / Backpressure

- Capacity 由設定提供，預設為 `100`。
- Capacity 必須大於 `0`；不合法的設定必須拒絕。
- 使用 `BoundedChannelFullMode.Wait`。
- Queue 滿載時，Producer 必須非同步等待可用空間，等待期間支援 `CancellationToken`，取消必須向上傳遞。
- 不使用 `DropWrite`、`DropOldest` 或 `DropNewest`，不得因 Queue 滿載而丟棄工作。

### Queue Item

沿用現有 `ProcessingJob` Contract，不新增 `ProcessingQueueItem` DTO，不修改既有 `IProcessingQueue` signature。

Queue 僅負責傳遞 `ProcessingJob`，不得修改其狀態、`RetryCount` 或 Error 資訊。

Consumer 所需的識別資訊由既有 `ProcessingJob` 提供：

- JobId（`ProcessingJob.Id`）
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

### Queue Notification 與 Persistence

沿用 MOD-01 的 `IProcessingQueue` 與 `ProcessingJob` signature。Queue Item 是排程通知，DB 是最新狀態來源。
Enqueue 成功只代表進入 runtime Channel，不代表取得 Claim 或執行成功。重複通知不能導致重複取得執行權。
Producer 必須在 Job 所需資料已 Commit 後 Enqueue；Queue failure／取消不得刪除已接受 DB／Storage。

---

## 1.8 Background Worker

使用 `BackgroundService`；MVP 限定單 Instance、多 Worker／Consumer。

1. Dequeue 後使用 `ProcessingJob.Id` 定位 Job，不信任 Queue Entity 的 Status、Error 或 RetryCount 快照。
2. 為本次 Job 執行建立獨立 DI scope，透過 Application abstraction 取得最新持久化狀態。
3. 由 Infrastructure 執行 SQL Server 原子 Claim：條件至少為相同 Id 且 Status = Pending，成功才改為 Processing。
4. 必須判斷條件更新是否取得該筆 Job；已 Processing／Completed／Failed 的重複通知不得執行分析或覆寫狀態。
5. Resolve 已核准 Workflow／必要 Context、執行 Step、保存可重用結果，再更新 Job／Batch 狀態與發出事件。
6. 所有失敗依 §1.10～§1.11 分類，不得以通知或 logger 失敗重做已成功的外部副作用。

Claim 不能使用一般 read-check-write 或只使用 Memory Lock。`UNIQUE(ImageId, Workflow)` 防止重複建立，不能取代執行 Claim。
SQL／EF Core 留在 Infrastructure，不將 DbContext、SQL connection 或 raw SQL 加入 Domain／Application。
各 Job 不共享 DbContext；同一 Job 的平行 Step 亦不得共用非 thread-safe DbContext。
Claim 交易須短，不在等待 Queue、外部 API、Storage、Retry delay 或完整 Workflow 時持有長 SQL Transaction。
Job Claim 不等於 Image+Step／Group 的全部 concurrency protection，仍須依 §1.12～§1.13 與各模組防止結果 Race。

原子交易與 checkpoint 依 §1.18～§1.22；具體服務 method signatures 待實作前契約審查。MOD-01 既有 Mapping 不變，沒有 RowVersion／owner／lease／Outbox 欄位擴充。

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

## 1.10 Retry、Timeout、Run 與 Cancellation（D-05 已核准）

### 邏輯 attempt 與實際 invocation

每 Step／每 Run 有四個邏輯槽位：RetryOrdinal = 0（初次）、1、2、3，等待為 0／1／2／4 秒。
子模組不另做 transient retry。四次上限是邏輯槽位，不是跨 Shutdown 的實際 invocation 筆數。
同槽位因 Shutdown 恢復可新增 invocation：AttemptNo 在 checkpoint 內遞增，ResumeNo 在同槽位由 0 起遞增，RetryOrdinal 不增加。
每筆實際 invocation 都有獨立 History；同一 Running invocation 不和恢復 invocation 同時執行。

ProcessingJobs.RetryCount 是 Job 跨 Step／跨 Run 累計「首次開始的邏輯重試槽位」數。
T1／T2 首次開始 RetryOrdinal > 0 且 ResumeNo = 0 時加一，與 Running Log 同交易；重複通知、保存結果重試、同槽位 Resume 不加一。
若 transient 已造成重試槽位開始，稍後 Shutdown 不撤銷原增量；Shutdown 本身不分配新槽位／Run，也不額外增加 RetryCount。
舊 ProcessingLog DTO 的 retryCount 如仍使用，映射 Step RetryOrdinal，不代表 Job 的累計 RetryCount。

### Step transient retry 與 Geo fallback

HTTP 408／429／5xx、operation timeout、temporary I/O／Storage／Geo failure 可由 MOD-02 重試。
HTTP 400／401／403／404、invalid/corrupted image、永久 missing file、invalid request/configuration 不做自動 Step retry。
每次 transient failure 的 Log／checkpoint 與 NextRetryAt 持久化；Job／Run 維持 Processing／Active，checkpoint 進入 RetryWaiting。
NextRetryAt 由該次失敗完成時間加 1／2／4 秒得出，重啟不重設 budget 或期限。
GEO_LOOKUP 四個槽位耗盡時 LocationName = UnknownLoc、checkpoint ResultStatus = Warning，保留其餘 Metadata 並繼續下一 Step；不是 Job Failed。
Shutdown cancellation 不冒充 Geo failure／fallback。永久 Geo error 與 metadata 輸入分類仍待 D-08。

### 人工 Retry

只接受 Failed 且最後必要 Step 的終止分類為 Transient 的 Job；Pending／Processing／Completed、Permanent 或無法確認分類者為 409／INVALID_STATE_TRANSITION。
最多三個新增 ManualRetry Run：初始 RunNo=1，人工 RunNo=2／3／4；達上限仍為同一拒絕契約。不是新建 Job，不更改 Workflow，也不等同重新分析。
在單一短交易鎖定 Job／Run／binding：確認舊 Run Failed 已關閉，再建立下一 Pending Run、Job Failed → Pending、套用主 Job aggregate 差額，Commit 後才 Enqueue。
正常 failure 已在 T3 原子關閉舊 Active Run。若既有資料出現 Failed Job 但舊 Run 仍 Active，只能在核對持久化 Failed checkpoint／Log 證據後修復為 Failed；缺證據或矛盾時拒絕並回報，不建立第二個未關閉 Run。
新 Run checkpoint 從本次輸入與版本定義準備；可重用已提交結果的判定依 D-08～D-14，不以 row 存在或先前 Job Failed 盲目重做所有 Step。
Job CreatedAt／Workflow／RetryCount 不重設；新 Run 等待時 Job.StartedAt／CompletedAt／ErrorCode／ErrorMessage = NULL。Run／checkpoint／Log 保留舊錯誤與歷史。
首次 Claim 新 Run 設 Job.StartedAt = Run.StartedAt；同 Run Resume 保留首次 StartedAt。Job Completed 或 Failed 時設定 CompletedAt，只有 Failed 投影受控安全 ErrorCode／Message。

### MVP 可設定預設值（沒有效能驗證宣稱）

| 設定 | 預設 | 範圍／語意 |
|---|---:|---|
| PendingReconciliationInterval | 5 秒 | >0；runtime 排程通知掃描 |
| PendingReconciliationBatchSize | 100 | >0；每批上限 |
| GeoStepAttemptTimeout | 15 秒 | >0；每個 invocation |
| DefaultStepAttemptTimeout | 120 秒 | >0；其他 Step invocation |
| HostShutdownGracePeriod | 30 秒 | >0；等待已取消 operation 停止 |
| ReconciliationEnqueueTimeout | 5 秒 | >0；僅 MOD-02 補排程 producer |

以上設定不改核准的四個槽位或 1／2／4 秒 business backoff，不是已校準 Production performance 限制。
Queue 5 秒只適用 MOD-02 Pending 補排程；MOD-01 Upload 的 Enqueue signature、Bounded Wait、Request token、HTTP／error mapping 全部維持原契約。

### Timeout／Shutdown

operation timeout 只有在舊 operation 已停止或副作用可確認後才進下一槽位；不合作取消的 operation 尚活躍時不得再執行同 Step。
Host shutdown 停止新 Claim，取消 Queue／delay／Step，等待 operation 停止；不將該取消分類 Permanent Failed。
已提交 Completed 不退回；未成功保存的記憶體結果不保證保留。恢復按 §1.21 的安全重算／副作用核對分類。
grace period 超過後不等於證明旧 process 已停止；新部署依 §1.11 的外部停止前提，不能在舊 operation 還可能執行時回收 Job。
必要 cleanup 不因原 token 取消而全部跳過、不覆蓋原始 exception；Processing 不執行 MOD-01 已 Commit 圖片的 Upload compensation。

---

## 1.11 Startup、部署前提與 Pending 補排程（D-05）

### 單 Instance 停止證據

同一 DB／工作集合只允許一個執行 Instance。部署／啟動控制必須確認舊 worker process 已停止且不会再執行副作用，才允許 Startup 回收 Processing Job。
Rolling overlap／另起容器、Readiness false、網路 timeout、舊 PID 檔、單次 health failure 或 grace period 到期，都不能單獨作為舊程序已停止證據。
停止證據應由管理舊執行實例的部署控制確認該實例已Terminated／其受管理process已退出，並確認同工作集合沒有其他執行Instance；不要求或假稱目前已有自動偵測API。已停止也不等於舊外部請求／Storage副作用已確認，仍須§1.21核對。
若停止前提未確認，Startup 不回收 Processing、不開放新 Claim／補排程，回報 deployment prerequisite 阻擋；不自行 kill 舊程序、不靠 DB 狀態猜死亡。
此為明列的部署前提，不是目前已有 liveness detection。多 Instance ownership／失效判定／fencing 保留後續。

### Startup 順序

1. 確認單 Instance／舊程序停止與 DB 可讀寫。
2. 不啟動新 Claim，核對 Primary binding、Job／Run／checkpoint／History，依 §1.18～§1.22 分類 crash window。
3. 安全恢復才在 T4 原子 Processing → Pending、Active Run → Pending；保留同 RunNo、RetryOrdinal、NextRetryAt 與成功結果。
4. 無法確認 binding 的相關 Image 所有 Job 都不 Claim、不執行 Step／副作用。未知外部副作用的 Job 保留阻擋，不偽裝成安全可重算。
5. 完成掃描分類，先啟動可消費的 Worker，再啟動分批 Producer；超 capacity 的恢復不阻塞 host 啟動。

| 狀態 | 行為 |
|---|---|
| Pending／無 Run | 綁定与輸入契約有效後可補排程，T1 首次建立 Run |
| Pending／Pending Run | 依原 checkpoint／NextRetryAt 補排程，T1 恢復同 Run |
| Processing／Active Run | 只有上述 Startup 停止前提與結果核對成立，才 T4 恢復 |
| Completed／Completed Run | 不重設、不執行 |
| Failed／Failed Run | 不自動恢復；人工 Retry 依 §1.10 |
| Job／Run／History 矛盾 | 先回報資料一致性阻擋，不猜測、不新增 Run |

Completed checkpoint 與已提交 ResultReference 不因 Shutdown 丟棄；版本兼容時重用。只有可安全恢復的 Processing checkpoint 回 Pending；RetryWaiting 的期限／ordinal 保留。
中斷 Running Log 若確定完成，可在核對後提交既有 invocation 的終態；若可安全重算則以 Interrupted 關閉，Resume 時建立新 invocation。未知副作用不自动 rerun，見 §1.21。

### Runtime Pending reconciliation

啟動時完整分批掃描；執行期間依可設定預設每 5 秒、每批 100 筆，以 CreatedAt／Id 排序。
只處理已 Commit Pending、binding／Context 有效、NextRetryAt 已到期或 NULL 的 Job；不以 runtime scanner 回收 Processing。
MOD-02 Enqueue 最多等可設定預設 5 秒；滿載／取消／failure 保留 DB／Storage、下輪再通知，不 Drop、不改 MOD-01 Enqueue。
通知可重複，只有 T1 Claim 成功才執行；不得以已 Enqueue 的 Memory marker 作 durable 唯一性。
掃描必須逐批向前遍歷，不能每次只取同一批已在 Channel 的舊 Pending 而永久餓死後續 Job；一輪的 batch/cursor 僅是 runtime 進度，重啟可重掃。
DB unavailable 時不猜狀態／不 Enqueue，Readiness 不接受新工作；控制停止／延後扫描，不建立 unbounded background Task。
每個 Job 獨立 scope／DbContext；Producer 排程与 SQL transaction 分開。所列預設尚未效能驗證。

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
- RetryOrdinal（History；JobRetryCount另記為Job累計）
- Timestamp

Processing Log 用於：

- Debug
- Error Investigation
- Processing Monitoring
- Performance Analysis
- Processing History（不是 Audit Log；MVP 不建立 Audit Log）

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
- RetryOrdinal（History；JobRetryCount另記為Job累計）
- ErrorCode

---

### Log 契約與安全邊界（D-06）

MOD-02 單一 lifecycle writer 管理 Running／terminal ProcessingLogs；子模組回報 Outcome，不各自重複寫 History，MOD-09 只 Query／轉送。
physical mapping 見 `docs/Database-Schema.md` §3～§6。Step／Run／Job 由 checkpoint／Run 關聯取得；API 的 Step 與 Timestamp 名稱保持明確，不建立重複 CreatedAt 時間欄位。
Log.Status = Running／Success／Warning／Failed／Interrupted／Skipped；不是 Job enum。
Job 累計 RetryCount 與 History RetryOrdinal 不混用；舊 DTO retryCount 是 RetryOrdinal 的映射，沒有另一個 Log RetryCount physical column。
每個 invocation Running → 一種 terminal status，terminal 不反覆覆寫；同槽位 Resume 建立新 AttemptNo／ResumeNo、保留舊紀錄。
BIGINT identity 原則已核准；feature 未找到 Log Entity／Migration，實際 DB 未驗證。如有既存 GUID／其他映射先回報遷移影響，本次不執行 Migration。
System Log 沿用 ILogger<T> 的安全 identifier／stage／code，不輸出 raw exception／SQL／絕對路徑／secret，不重複 MOD-01 failure event。
原 Request TraceId 已可用時帶入 Run；未知時建立 Worker correlation，不能假稱原 HTTP trace 已被 MOD-01 持久化。
寫入失敗、提交與通知依 §1.18／§1.21；實際 result identity／Context 内容仍待 D-08～D-14。

---

## 1.15 Primary binding、Image／Batch aggregate（D-07）

Primary Job 是 MOD-01 Upload 為 Image 建立的原始 Job，其 Naming／Analysis／DuplicateDetection／Full 都可作主 Workflow，不強制 Full。
新增 ImageProcessingBindings 保存 PrimaryJobId／PrimaryWorkflow／PrimaryStatus；不修改 MOD-01 Job／Image／Batch mapping，保留 TotalCount = 已接受圖片數。
首次發現只有一筆可由 MOD-01 交接契約確認的原始 Job 時可建立 binding；多筆／不一致／不能確認時不按最小 Id／最早時間猜測。
binding 未確認，相關 Image 的任何 Job 都不得開始執行或副作用；先回報 handoff 阻擋。MOD-02 建立 secondary Job 前也必須確保 binding 已確認。
這不授權自動選主、換主或重建計數；缺證據的 binding 阻擋之外部 API 為 HTTP 503／PROCESSING_PREREQUISITE_UNAVAILABLE（U-01 已核准），依 §3.6 使用安全共用錯誤格式。
PrimaryJobId 綁定後不因 Retry／secondary Job 改變。Secondary 不修改 Upload aggregate 或 Image 的全域 Processing Status。

| Primary Job | Image.Status／Binding.PrimaryStatus | Processed 貢獻 | Success 貢獻 | Failed 貢獻 |
|---|---|---:|---:|---:|
| Pending | Pending | 0 | 0 | 0 |
| Processing | Processing | 0 | 0 | 0 |
| Completed（含 Warning fallback） | Completed | 1 | 1 | 0 |
| Failed | Failed | 1 | 0 | 1 |

鎖定 Job／binding／Batch，按持久化舊貢獻計算 delta；Job、Run、binding、Image、Batch 更新在同一 SQL transaction，不讀完後用 memory delta 無條件寫回。
重複 completion 是無變更，不再加計。Failed → Pending 使 ProcessedCount 與 FailedCount 各減一；再次 Completed 使 ProcessedCount／SuccessCount 各加一。
Job.RetryCount 是 execution 數，不是 Batch delta；secondary 的任何執行／Retry 皆不加減 Upload counts。

| Batch 條件 | Status／CompletedAt |
|---|---|
| TotalCount = 0 | 保留既有 Pending，所有 counts／progress = 0；不造 Job，不發 BatchCompleted |
| 非空且全部 Primary Pending | Pending／NULL |
| 未全部終止，存在 Processing 或其他終態圖片 | Processing／NULL |
| 全部終止且 SuccessCount > 0 | Completed／終態提交時間 |
| 全部終止且 FailedCount = TotalCount | Failed／終態提交時間 |

Failed Retry 後若仍有其他 Completed／Failed 圖片，Batch 是 Processing；只有全部 Primary Pending 才是 Pending。
Retry 可使 Batch 終態回 Pending／Processing，清除 CompletedAt；下一次全體終態再設定時間、可發布新的 BatchCompleted transition 事件。
不變量：ProcessedCount = SuccessCount + FailedCount；0 <= ProcessedCount <= TotalCount；TotalCount 不因新 Workflow／Retry 改動。
未知 binding 不當 Pending 或零貢獻猜測，不以 Query clamp 掩蓋資料錯誤；先阻擋相關執行並要求交接確認。
MOD-01 既有 Query DTO／endpoint、Decimal 公式、TotalCount=0 → 0、0～100 clamp 不變；MOD-09 重用，不新建第二套 aggregate。
Step Skipped 對必要 Workflow 完成的判定仍須 D-09／D-14，不能自行把全部 Skip 判為 Completed。

---

### Batch 准入與交接阻擋（U-03 已核准）

Primary Claim、恢復執行與任何 aggregate 更新前，必須核對該 Batch 全部已接受 Image 的 Primary binding 集合；集合涵蓋、Job/Image/Batch/Workflow 一致且主 Job 可由交接證據確認，才可准入。新 Image 加入後，不能沿用舊集合的確認結果；在相關短交易重新確認集合有效性，不能僅依記憶體 ready flag。
未知 binding 的 Image 所有 Job 仍不執行；其他 binding 已確認的 secondary 不因本條新增全 Batch aggregate 門檻，但仍遵守自身 Context／副作用保護。未完成確認的 Batch 不開始 Primary Step／副作用，不將 Job／Image／Batch 偽造為新的阻擋狀態；Job 保留既有持久狀態。阻擋僅限該 Batch，scanner／Worker 必須繼續處理其他已確認 Batch。

阻擋原因須可唯讀查詢，區分缺少 binding、主 Job 證據不唯一、跨表關聯不一致、集合／計數無法核對；呈現安全的 Batch／Image identifier 與原因分類，不公開 SQL／路徑／原始 exception。MOD-09 讀取診斷，不能由 Query 選主、修復或排程。具體診斷 DTO／路由／原因代碼及是否持久化仍待確認，不修改 MOD-01 Query 契約。
解除方式：補齊可核對交接證據／由另行授權程序修正矛盾，MOD-02 重新核對完整集合後解除准入阻擋，再按既有 Pending 補排程規則處理。不得猜最早 Job、偷偷換主、用 Query 修復或強制 unblock；Processing Job 的回收仍須確認舊程序已停止。本次不新增修復 API／資料修復 TASK。

驗收案例（未執行）：同 Batch 任一 Primary binding 未知時全部 Primary 不 Claim；其他已確認 Batch 可執行；證據補齊且重驗成功後 Pending 可補排程；Query 無寫入；新增 Image 使舊集合確認失效；503拒絕無新增／排程副作用。

---

## 1.16 Processing Events（D-06）

Application 事件在相關 SQL commit 確認成功後才發布；Writer 不直接使用 SignalR，MOD-09 負責 Query／轉送。
共同欄位 schemaVersion=1、eventId GUID、occurredAt DateTimeOffset、traceId、batchId、imageId、jobId、runId；Batch 事件以 batchId＋aggregate 為主，沒有單一 Image／Job／Run 時相關欄位為 NULL，不捏造識別。

| 事件 | 額外資料／語意 |
|---|---|
| ProcessingStarted | T1 的 Job Claim／Active Run／首個 Running Log 已提交；同 Run 恢復可重新通知，不等於新 Run |
| ProcessingStepStarted | step／attemptNo／retryOrdinal／resumeNo；Running invocation 已提交 |
| ProcessingStepCompleted | 上述 identity、outcome=Success／Warning／Skipped、durationMs；Step 終態已提交，不是 Job 完成 |
| ProcessingStepFailed | identity、errorCode、willRetry；invocation Failed；willRetry=false 仍須區分 Geo fallback 與 Job fatal |
| ProcessingCompleted | Job／Run Completed 已提交 |
| ProcessingFailed | errorCode；Job／Run 終止 Failed 已提交 |
| BatchProgressUpdated | 原持久化 aggregate 欄位快照 |
| BatchCompleted | status=Completed／Failed＋aggregate；本次進入終態 |

Warning fallback 的最後失敗 invocation 可有 ProcessingStepFailed(willRetry=false)，fallback checkpoint 完成另發 ProcessingStepCompleted(outcome=Warning)；不得發 ProcessingFailed 來表示這種 Warning。
Shutdown 的 Interrupted Log 不發 ProcessingFailed，也不偽裝成新增 Retry；恢復的新 invocation 正常發 StepStarted。
事件 best-effort，不加入 Outbox／Broker／持久事件表、不承諾必達或恰好一次。通知 failure 不回滾 DB、不重跑分析，只記安全 System Log。
Client 用 eventId 去重，不假設跨 Worker、跨重啟有全域 Sequence；REST persisted state 是重新同步來源。完整 payload／範例與 pagination 見 MOD-09 §1.12／§3.4～§3.5。

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
        long jobId,
        CancellationToken cancellationToken);

    Task RetryJobAsync(
        long jobId,
        CancellationToken cancellationToken);

    Task ProcessJobAsync(
        long jobId,
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

## 1.18 原子提交順序與執行不變量

所有 SQL 寫入透過 Application abstraction，由 Infrastructure 同一短 transaction 協調；各 Job 使用獨立 scope／DbContext。
binding／已核准 Workflow、Input／RuleVersion 必須可確認，否則不進 T1；不因缺 D-08～D-14 而填入默認 Context。
多 Worker 以 DB 的 conditional update／鎖定／unique 約束協調，不以 in-memory lock 取代。沒有跨完整 Step 的長 SQL transaction。

| 交易 | 同一 commit 內的順序／內容 | commit 後才允許 |
|---|---|---|
| T1：首個 Step／恢復 Claim | 檢查 binding；鎖定 Job／未關閉 Run；Job Pending→Processing；建立初始 Active Run或原 Pending Run→Active；設 StartedAt 投影；準備／選取 checkpoint→Processing；配置 AttemptNo／ResumeNo；新增 Running Log；首次 retry 槽位才增 Job.RetryCount；套用必要 primary aggregate | Started 通知、開始 Step invocation |
| T2：同 Run 下一 Step／到期重試 | 核對 Job Processing＋Run Active＋checkpoint Eligible；鎖定 checkpoint；處理剩餘 budget／NextRetryAt；新增下一 Running Log與狀態／counter更新 | StepStarted、單一 invocation |
| T3：Step outcome | 保存可用的 SQL 結果與 ResultReference；Running Log→terminal；checkpoint→Completed／RetryWaiting／Failed；如 Job 成功／fatal，同交易關閉 Run、更新 Job／Error／CompletedAt及 primary Image／Batch delta | Step／Job／Batch completion／failure 事件、下一 Step |
| T4：安全恢復 | 僅確認舊程序停止後；核對結果／副作用；可重算 Running→Interrupted、checkpoint→Pending；Processing Job→Pending、原 Active Run→Pending；保持 RunNo／ordinal／NextRetryAt；同步 primary 狀態投影 | 分批 Queue 通知，T1 再 Claim |
| T5：人工 Retry | Failed資格／manual cap／終態證據；確認旧 Active 已關為Failed；插入下一Pending Run；Failed Job→Pending、當前投影清理、primary delta；歷史不刪 | Enqueue；T1 才建立Running invocation |

初次 T1 必須整筆成功，不能先單獨 Commit Job Claim，稍後才建立 Active Run 或 Running Log。
T1／T2 Commit 失敗或不明時不得呼叫模組。用新 DbContext 核對 durable Job／Run／invocation identity；确认已 Commit 才使用既有紀錄，不能靠重複 INSERT／UPDATE猜測。
每个未關閉 Job Run 最多一個；同 checkpoint 不能同時有兩個 Running invocation。配置 AttemptNo／ResumeNo 與 Running Insert 均在鎖定 checkpoint 的 transaction 內，不用 MAX+1 的裸 read-check-write。
Processing/Active 表示尚未終止，並非可靠 liveness 證據。因未知副作用而阻擋的舊 Run 可以保持 Active，但任何 Worker不得將它當新 Pending再 Claim。
T3 最後必要 Step 完成→Job／Run Completed；fatal或耗盡且無 fallback→Job／Run Failed，不保留終止 Job 的 Active Run。
非terminal transient→checkpoint RetryWaiting、Run Active／Job Processing，等待到期後 T2；等待期間沒有 Running invocation。
Checkpoint 已 Completed 只在同input/rule/result與reference可重用時跳過，不以History或共享row存在當成功；各模組判準依最終章。

T1／T2設定本invocation的Log.StartedAt／Timestamp與checkpoint首次StartedAt（若NULL）；Run／Job首次StartedAt只在初次Active設定，Resume不覆蓋。
Running紀錄表示已取得執行準備權、尚未確認Outcome；T1後未呼叫Step就crash也可能留下Running，不能用Log數量證明實際底層呼叫次數。DurationMs只記可量測時間，跨crash無法確定時為NULL，不捏造0。

## 1.19 Run 狀態機

Run.Status = Pending／Active／Completed／Failed；Reason = Initial／ManualRetry，不建立 Recovery Run。

| 起點→終點 | 前提／交易 |
|---|---|
| 無Run→Active | Job 初次 T1，RunNo=1、Reason=Initial，與 Claim／Running Log 同 commit |
| 無新Run→Pending | 合資格人工 Retry T5，RunNo依序2／3／4、Reason=ManualRetry |
| Pending→Active | T1 Claim首個或恢復 invocation |
| Active→Completed | T3 所有必要 checkpoint 完成，Job Completed 同 commit |
| Active→Failed | T3 fatal必要Step或重試耗盡無fallback，Job Failed 同 commit |
| Active→Pending | 僅舊程序已停止且安全恢復 T4，Job Processing→Pending，沿用同Run |

Completed／Failed Run 不重開、不回 Pending；人工 Retry 建立新的 Run，保留全部 terminal Run／Log。
恢復 StartedAt 保留該 Run 首次開始時間；Pending ManualRetry Run StartedAt=NULL，首次 Active 才設定。CompletedAt 只在 terminal transition 寫入。
任何 Job／Run／History 矛盾都先阻擋診斷；只在持久化 terminal 證據足夠時修復舊 Active，不把「人工Retry請求」本身當可關閉活躍Run證據。

## 1.20 Checkpoint／History 狀態機

Checkpoint.Status = Pending／Processing／RetryWaiting／Completed／Failed；ResultStatus = NULL／Success／Warning／Skipped。

| 轉換 | 前提 |
|---|---|
| Pending→Processing | T1／T2，已配置Running invocation |
| Processing→RetryWaiting | transient failure且ordinal<3；該 Log Failed、下一ordinal／NextRetryAt同T3提交 |
| RetryWaiting→Processing | NextRetryAt到期且同Run Active；T2開始新槽位 |
| Processing→Completed | 成功／核准Warning fallback／核准Skip，結果與Log outcome同T3提交 |
| Processing→Failed | permanent或必要Step transient耗盡無fallback，與Job／Run Failed同T3提交 |
| Processing→Pending | 安全Shutdown recovery T4，舊invocation Interrupted、同ordinal恢復，不是普通Step Retry |

Completed／Failed checkpoint 不重開；人工 Retry 的新 Run 使用新 checkpoint，必要時重用已提交結果，不刪旧checkpoint。
RetryWaiting在shutdown未開始新invocation，保留NextRetryAt／ordinal；Run Pending時到期並可確認Context，T1可使其Processing。
Running Log只在T1／T2建立，之後只可→Success／Warning／Failed／Interrupted／Skipped之一；terminal不重寫。全程引用StepCheckpointId＋AttemptNo，Resume另寫新Log。
Geo耗盡的實際失敗invocation維持Failed History，fallback提交後checkpointCompleted／ResultStatusWarning；兩種狀態不同，不將實際錯誤抹成成功。

## 1.21 結果保存失敗與重啟安全性

Result persistence由MOD-02單一Writer協調短交易；模組結果資料的owner仍屬各模組，不能把Domain演算法塞入Writer。
SQL結果／checkpoint／History／必要Job與aggregate須共同提交；具體服務適配與ResultReference依F-01～F-08最終章，不宣稱现有介面已能直接實作。
Storage rename／move、外部資源不屬SQL Transaction，不承諾Rollback可撤銷副作用。

保存結果的獨立重試：初次＋最多三次保存，等待1／2／4秒，不消耗StepRetryOrdinal／JobRetryCount，不呼叫模組重算。
有不明Commit結果時先重新讀取相同invocation identity與terminalcheckpoint，已完成即接受既有提交；未提交才重試同保存内容，不能重複套用delta。
重試期間可持有記憶體Outcome，但程序中斷即可能遺失；不承諾跨重啟保存。
保存持續失敗，不發成功、不執行下一Step／副作用；保持最後可確認的持久化狀態，回報DB／Job保存阻擋，Recovery按下表分類。

| 結果類型／證據 | 中斷後規則 |
|---|---|
| 純計算／只讀（受控原圖／snapshot、版本未變） | 記憶體Hash／pHash／Quality計算／Metadata解析結果可丟棄後在同邏輯槽位重算；須有完整可重現Input／RuleVersion，具体規則仍待各模組 |
| 已提交SQL結果有兼容ResultReference | 讀取與重用，補齊同一checkpoint／History終態，不再次做演算法或套用aggregate |
| SQL commit outcome未知 | 先查DB證據；不能以timeout或缺少memory當未提交 |
| Group／Recommendation／共享結果upsert | 先核對group／version／unique／result證據，不能只看row存在；D-10／D-12未定前不批准盲目重算 |
| Rename／move／建立或刪除Storage物件 | 必須核對來源／目標／receipt或模組冪等結果；不重複rename、不把錯誤刪檔當恢復；細節D-09待決 |
| 無法確認副作用／Input已變／沒有可重用版本 | 保持相關Job阻擋、不Queue、不開始新invocation；回報reconciliation prerequisite，不能猜測Success或自動Failed再Retry |

只有成功核對結果後才完成T3或安全T4；unknown副作用可保留Processing／Active作未關閉狀態，但不是活躍執行、不可被runtimescanner回收。
SystemLogger failure為best-effort，不覆蓋原始失敗；durableHistory failure不能被吞掉後宣稱完成。

### 交易／關閉補充

同Run依核准StepOrder序列執行，不自行平行同Run Step。Job-level Worker可並行；跨Job相同Image+Step／Group protection仍待模組結果契約。
Run Failed時未執行的後續checkpoint可保持Pending，但terminalRun使它們不可執行；不偽造Skipped History。Run Completed必須核對WorkflowVersion的全部必要checkpoint已Completed，必要／可跳過規則依最終F-07章，任意Skip不可算完成。
人工Retry的新Run沿用原Workflow／Input／Rule定義，不藉此更換Template或版本。來源已變或原Context不可確認時拒絕本Retry路徑並回報，重新分析仍另決策。
未知副作用的旧Running Log可保持未確認終態，Running不等於舊process仍活著；核對證據後才完成其終態，不能先改Interrupted再覆寫Success以掩蓋歷史。
只有確定可重新執行的中斷invocation關成Interrupted，T4後T1新Resume建立Running，filtered unique才可放行。
對Primary aggregate更新必須可核對完整Batch主Job集合；未知binding／計數不一致先回報handoff阻擋、不猜Delta或狀態。U-03已核准：完整確認該Batch的Primary binding集合後，才允許Primary Job執行及aggregate更新；僅阻擋該Batch，其他已確認Batch照常執行。

---

## 1.22 Crash window 與必要驗收

| crash位置 | 可確認的DB狀態 | 恢復／禁止事項 |
|---|---|---|
| T1前 | Pending，可能無Run | 正常補排程；無Step副作用 |
| T1中／Commit不明 | 所有T1內容一起提交或未提交 | 查新DbContext核對；不在未確認時開始Step |
| T1後、尚未呼叫Step | Processing／Active／Processing checkpoint／Running Log | 舊程序停止後可Interrupted＋同槽位Resume；不增加Retry |
| Step執行中／計算後、T3前 | Running與checkpoint尚未終態 | memory可能遺失；只讀可按版本安全重算，副作用必須核對 |
| T3中／Commit不明 | 結果＋checkpoint＋History＋終態delta應共同commit | 核對durableidentity，不重複delta或外部操作 |
| T3成功、通知前或通知失敗 | 正確terminal／RetryWaiting已提交 | REST可查；不為缺通知重跑，不承諾重送 |
| StepCompleted後、下一T2前 | 成功checkpoint已保存，Job仍Processing | T4恢復同Run，跳過兼容結果，從下一Step開始 |
| RetryWaiting shutdown | 原NextRetryAt／ordinal已保存，無Running | 保留期限，安全T4後到期才開始該槽位 |
| T5成功、Enqueue前或失敗 | 新PendingRun／PendingJob／已套用Retrydelta | runtime補排程，不再建Run或再減計 |
| terminalJob卻ActiveRun、缺binding或歷史矛盾 | 不符合不變量 | 阻擋／回報，不能關舊Run再新增以掩蓋缺證據 |

必要驗收：每個T1寫入點注入失敗應整筆回滾；Claim後沒有孤立ActiveRun或RunningLog；doubleConsumer僅一invocation；T3commit不明不重複delta；shutdownresume同Run/ordinal；Failed終止關Run、ManualRetry封存舊Run；未知binding／副作用零新執行；舊process停止未確認不回收。
上述是規格驗收要求，尚未執行。D-08～D-14未定的模組結果／副作用核對不能以此表宣稱已解決。

---

# 2. 資料表

本章只定義 Processing Module 所使用的資料結構。

資料表中的 Workflow、State 等欄位屬於資料本身，不在此描述業務流程。

完整 Database Schema 由：

```text
docs/Database-Schema.md
```

統一管理。

---

## 2.1 ProcessingJobs

已由 MOD-01 定義及 feature 實作，沿用 `processing_jobs`，不建立第二套表或 Migration baseline。
正式 physical mapping、PK／FK／Nullability／長度／Default／索引／DeleteBehavior 見 `docs/Database-Schema.md` §2。
包括 BIGINT identity Id、uniqueidentifier BatchId、datetimeoffset(7) 時間、nvarchar(max) NULL ErrorMessage、`UNIQUE(ImageId, Workflow)` 與三條既有 NoAction 關聯。
MOD-01原JobMapping沒有Claim owner／lease／rowversion／Step checkpoint／TraceId欄位；D-05將執行資料放在新Run／checkpoint表，不假裝原Job已加入這些欄位。
若 D-05 的持久化方案需要欄位或 Migration，先提出對 MOD-01 既有映射與資料的影響並取得批准。

---

## 2.2 ProcessingLogs／執行資料

D-05／D-06已核准新增ProcessingRuns、ProcessingStepCheckpoints、ProcessingLogs；D-07新增ImageProcessingBindings。physical mapping見 `docs/Database-Schema.md` §3～§6。
Log.Id BIGINT IDENTITY，StepCheckpointId／JobId／ImageId、AttemptNo／RetryOrdinal／ResumeNo、Status、StartedAt／CompletedAt／DurationMs、Timestamp／TraceId與安全Error。
Step／Run由checkpoint關聯，API欄位明列映射；不另建physical Step／Log.RetryCount欄位，不以UNIQUE(ImageId,Step)限制歷史。
MOD-01既有JobMapping／unique／FK不改；新增表的FK與NoAction不代表批准改原Entity欄位或執行Migration。
實際DB是否已有Log／其他映射仍未確認；如有資料遷移影響須另回報。本次只有文件更新。

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
  "imageId": 1024,
  "batchId": "guid",
  "workflow": "Full"
}
```

### Response

```http
202 Accepted
```

```json
{
  "jobId": 1001,
  "status": "Pending"
}
```

Processing Job 建立後不等待背景工作完成。

---

### Duplicate CreateJob（已核准）

依既有 `UNIQUE(ImageId, Workflow)`，查詢命中或並行 INSERT 觸發該唯一限制，都使用相同 409 契約。
僅將已確認是該唯一限制的衝突映射為 duplicate，其他 SQL error 仍使用安全 INTERNAL_ERROR，不暴露 provider 訊息。

| 既有狀態 | CreateJob 行為 |
|---|---|
| Pending | 409，不新增、不重新 Enqueue |
| Processing | 409，不干擾活躍執行 |
| Completed | 409，不重跑、不將重新分析當 Retry |
| Failed | 409，不建立替代 Job；合資格 Retry 使用原 Id |

```http
409 Conflict
```

```json
{
  "success": false,
  "error": {
    "code": "PROCESSING_JOB_ALREADY_EXISTS",
    "message": "A processing job already exists for the image and workflow.",
    "traceId": "00-abc123"
  }
}
```

此為 MOD-02 CreateJob 的規格契約，不改 MOD-01 Upload 的 INTERNAL_ERROR mapping 或 Request idempotency。
新 Job 的 ImageId／BatchId 必須對應同一Image／Batch；按 §3.6 驗證。新增secondary Job不改Upload aggregate，且須先確認Primary binding；不明binding回HTTP 503／PROCESSING_PREREQUISITE_UNAVAILABLE，不執行任何Job副作用。

---

## 3.2 Get Processing Job

```http
GET /api/v1/processing/jobs/{jobId}
```

### Response

```json
{
  "jobId": 1001,
  "imageId": 1024,
  "batchId": "guid",
  "workflow": "Full",
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
  "jobId": 1001,
  "status": "Pending"
}
```

只有 §1.10 明定的 Failed／transient Job 才可 Retry；保留原 Job Id 與歷史。其他狀態回傳 409／INVALID_STATE_TRANSITION。

---

## 3.4 Error Response

### Processing Not Found

```http
404 Not Found
```

```json
{
  "success": false,
  "error": {
    "code": "PROCESSING_NOT_FOUND",
    "message": "Processing job was not found.",
    "traceId": "00-abc123"
  }
}
```

### Invalid State Transition

```http
409 Conflict
```

```json
{
  "success": false,
  "error": {
    "code": "INVALID_STATE_TRANSITION",
    "message": "The requested state transition is not allowed.",
    "traceId": "00-abc123"
  }
}
```

### Processing Failed

```http
500 Internal Server Error
```

```json
{
  "success": false,
  "error": {
    "code": "PROCESSING_FAILED",
    "message": "Processing failed.",
    "traceId": "00-abc123"
  }
}
```

### Queue Error

```http
500 Internal Server Error
```

```json
{
  "success": false,
  "error": {
    "code": "QUEUE_ERROR",
    "message": "Unable to enqueue processing job.",
    "traceId": "00-abc123"
  }
}
```

### Worker Unavailable

```http
503 Service Unavailable
```

```json
{
  "success": false,
  "error": {
    "code": "WORKER_UNAVAILABLE",
    "message": "Processing worker is unavailable.",
    "traceId": "00-abc123"
  }
}
```

### Internal Error

```http
500 Internal Server Error
```

```json
{
  "success": false,
  "error": {
    "code": "INTERNAL_ERROR",
    "message": "An unexpected error occurred.",
    "traceId": "00-abc123"
  }
}
```

---

## 3.5 既有 HTTP 邊界

全部錯誤使用 SYS §3.4 共用 success:false／error.code／error.message／error.traceId envelope。
MOD-02 CreateJob／Retry 的 Queue error 使用本章既有 QUEUE_ERROR；MOD-01 Upload 的 QueueEnqueue failure 仍為 INTERNAL_ERROR，二者是不同 API boundary，不能互相覆寫。
取得 HTTP 202 不等於背景完成；Error Response 不洩漏 SQL、path、exception 或 secrets。

---

## 3.6 CreateJob validation（D-07）

依序：Request／Workflow／Id合法性→Image存在→Batch存在→Image.BatchId一致→同Image／Workflow重複→確認binding→可建立secondary Job。

| 情況 | HTTP／Code |
|---|---|
| 非法Request／Workflow／Id | 400／INVALID_PROCESSING_JOB_REQUEST |
| Image不存在 | 404／IMAGE_NOT_FOUND |
| Batch不存在 | 404／BATCH_NOT_FOUND |
| Image與Batch不一致 | 409／IMAGE_BATCH_MISMATCH |
| 重複Job（四種狀態） | 409／PROCESSING_JOB_ALREADY_EXISTS |
| 必要交接／binding無法確認 | 503／PROCESSING_PREREQUISITE_UNAVAILABLE |

錯誤統一success:false/error.code/message/traceId；拒絕不新增Run／Job、不Enqueue、不改aggregate，不洩漏其他Batch或provider內容。

```json
{
  "success": false,
  "error": {
    "code": "IMAGE_BATCH_MISMATCH",
    "message": "The image does not belong to the specified batch.",
    "traceId": "00-abc123"
  }
}
```

上述不改MOD-01Uploadvalidation/error mapping。未知binding為handoff阻擋，依U-01回503／PROCESSING_PREREQUISITE_UNAVAILABLE；message使用安全通用說明，traceId供追查，不回傳內部例外、SQL、路徑或交接證據。沿用上述驗證順序，既有duplicate／mismatch維持409；拒絕不新增Job／Run／checkpoint、不Enqueue、不改aggregate。

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

## 4.4 修訂驗收補充（尚未執行）

- Recovery 專用 Processing → Pending 合法，一般呼叫相同轉換被拒絕。
- Pending／中斷 Processing 均可恢復；Completed 不重排；Failed 不繞過人工 Retry 資格。
- 多 Consumer 競爭同一 Id 只有一個 SQL Claim 成功；stale／duplicate Queue Entity 不觸發重做。
- 四種既存 Job 狀態的重複 CreateJob 均為 409，共用 error.traceId；並行 unique violation 使用同一契約。
- 每 Step 初次 + 三次 transient retry，精確 1／2／4 秒；永久錯誤零 retry；子模組無疊加 loop。
- Geo 耗盡產生 UnknownLoc／Warning，已完成 Metadata 保留，後續 Step 繼續。
- Shutdown 不消耗 retry，重啟恢復；timeout 不重疊 operation；先成功 completion 後取消不退回。
- Commit→Enqueue failure／部分 enqueue／取消保留 MOD-01 graph 與 Storage，Pending 可補排程。
- 每 Job 獨立 scope／DbContext；全程維持 MOD-01既有 Queue、Schema、API regression。
- D-05～D-07的crash-window、runtime Pending reconciliation、attempt、History／Event、binding／Batchdelta依 §1.18～§1.22要求驗收；未執行，不標PASS。結果重用／副作用細節依D-08～D-14保持待決。

目前規格修訂、無實作／執行測試證據。正式 Implementation TASK 必須等待待決策契約與 MOD-01 handoff 驗收版本確認。

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

Infrastructure implements Application / Domain abstractions
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

## 6.10 已確認交接與待決策

已核准的單 Instance／Claim／Retry／CreateJob 契約不可被舊圖示或範例反向覆寫。
§1.17 的 IRetryPolicy 是既有概念介面，retryCount參數應表達Step槽位而不是Job累計值；具體Context／結果／Persistence signatures仍需D-08～D-14與Task前契約審查，不因狀態機完成宣稱可直接實作。
WorkflowType 是 MOD-01 的 enum（Naming／Analysis／DuplicateDetection／Full），resolver 可以轉為名稱，但未知 Workflow 不可自行新增或當有效值。
D-01～D-14中本輪F-01～F-08範圍已同步最終契約；MOD-02核心文件層可規劃TASK，完整Analysis/Full仍有N-01/N-02。D-13只延後新Export能力，不能將未驗證功能標READY/PASS。
保留既有 TASK；兩個基準不存在 MOD-02～MOD-10 TASK，不建立正式實作 TASK。本次不修改 MOD-01 TASK，也不以它的歷史 PASS 宣稱 MOD-02 準備完成。

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

本章示意中的 Failed → Retry → Pending 表示 §1.10 的合資格人工 Retry，不表示每個 Step transient failure 都先將 Job 改為 Failed。自動 Step retry 在 Processing 內執行；管理 Recovery 是 §1.5 的受控例外。以下概念圖不是已完成或已通過驗收的證據。

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

# 最終核准契約 — F-07／F-08（核心規劃與Workflow邊界）

## WorkflowVersion與准入

正式組成依SYS最終章：Naming=EXIF_PARSING→GEO_LOOKUP→NAMING→FILE_RENAME；DuplicateDetection=SHA256_CALCULATION→EXACT_DUPLICATE_CHECK；Analysis=EXIF_PARSING→GEO_LOOKUP→SHA256_CALCULATION→EXACT_DUPLICATE_CHECK→PHASH_GENERATION→VISUAL_SIMILARITY→QUALITY_ANALYSIS→VERSION_ANALYSIS；Full=Analysis→NAMING→FILE_RENAME。FILE_RENAME只有Create artifact語意，VERSION_ANALYSIS對接MOD08。VALIDATION是准入，COMPLETED是lifecycle，兩者不建立假checkpoint。
Run.WorkflowVersion以本文件清單版本workflow-mvp-v1凍結，RuleVersion由各MOD結果manifest/input核對；unknown engine/profile/input不填默認。每Run凍結stepOrder/source/inputReferences，Shutdown沿原Run；manualRetry新Run沿原業務input、最多三個新增Run、僅Failed/transient，非更換template/內容/算法。
MissingGPS明確GEO_LOOKUP Skipped、InvalidGPS/Geo耗盡Warning繼續，auth/config Failed；NamingCollision Skip只許明確該Item終態；Recommendation空snapshot可Skip，singleton正常Success。必要Step只能Success/Warning/上述明確Skipped才Job Completed，任意Skip不可算完成。無Naming input不Claim；MOD01 Naming/Full上傳沒有template交接時由prerequisite診斷阻擋，不偷改MOD01或猜template。
跨image-step-version以結果row/unique/conditional-update協調，不僅memoryLock。ResultReference必須指向同input/version已提交result；HashProfile引擎未定阻擋PHASH_GENERATION，不能阻擋所有核心規劃。job層Claim不等於跨Job result完成。

## dependency wait與T2-W

推薦等待已核准，最終規則見MOD08；T2-W是準備／解決／到期等待的短SQL管理交易，不是新增JobStatus。Started/Deadline初次固定、120秒可設定，等待無Running；Resolved與T2的checkpoint Processing/Running同commit。到期Failed關Run、套Primary delta、無invocation不偽造History，commit後通知。NoRunning等待Run重啟先核對舊程序已停，照原deadline續檢，不能按泛用Processing reclaim建立新Run。scanner跨Batch向前處理，不因一Batch阻擋其他。
T1 JobClaim/ActiveRun/可執行checkpoint/RunningLog仍同transaction；不能先Claim未準備好的Step再等依賴。等待發生在已Active Run的下一Step准入，沒有新JobClaim。持久DependencyWaitStatus/Started/Deadline與DB條件更新保證wait/execute互斥，不因反覆enqueue重設。

## 核心範圍准入判定

文件層MOD02核心可拆TASK（本輪不建立）：Run/checkpoint/History、T1～T5/T2-W/SQLClaim、singleInstance recovery/Pending補排程/retry/shutdown、Batchbinding/aggregate、adapter與best-effortevents、最小diagnostic。Export/進階Monitoring與pHash準確率不一概阻擋上述核心。
Naming規格可作後續規劃，但完整執行仍須可核對Primarybinding與持久Naming input、storage adapter/receipt驗收，不把feature尚未合併等同已交接。Analysis/Full的引擎/精確版本N-01與候選就緒调度N-02未定，尚不能凍結完整profile實作准入；holdout/性能是後續验收要求非文件階段已完成能力。核心規劃准入不是原始碼實作授權或runtime READY。

## 最後一致性檢查發現：N-02候選pHash就緒的調度契約

正式VISUAL_SIMILARITY要求完整同版本候選snapshot，Workflow將其放在Quality前；其他Image仍可能在Pending或尚未pHash。F-06核准的T2-W dependency wait目前只限VERSION_ANALYSIS，沒有定義VISUAL候選未齊時的等待／釋放Worker／恢復／到期。不能使用partial hash集合冒充完整snapshot，也不能占滿所有Worker等待其他Job。
這是規格缺口而非已證實runtime deadlock。保持Analysis/Full完整實作准入BLOCKED，核心TASK規劃與Naming不因本缺口阻擋。推薦下一次針對N-02核准把VISUAL候選等待納入既有T2-W（讓出Worker、持久deadline、版本核對、無Running/不消耗Retry、ready後原子開始）；VISUAL等待budget與逾時政策需集中決定，不能擅自挪用Recommendation120秒或改Step順序。本輪不實作、不建立TASK、不改已核准checkpoint wait範圍。
